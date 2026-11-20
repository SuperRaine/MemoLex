import AppKit
import CryptoKit
import Darwin
import Foundation
import SwiftUI

@MainActor final class MacUpdates: ObservableObject {
    @Published var busy = false
    @Published var status = "点击“手动检查更新”获取最新版本。"
    @Published var progress: Double = 0
    @Published var release = J.null
    @Published var downloaded: URL?
    let preferences: Preferences
    var onAvailable: (() -> Void)?
    private var timer: Timer?
    private var downloadGeneration = 0
    private var automaticDownloading = false
    private var notified: Set<String> = []
    private let session = URLSession(configuration: .ephemeral)
    private var asset = J.null
    var hasUpdate: Bool { asset != .null }
    var latestVersion: String { release["tag_name"].text.replacingOccurrences(of: "v", with: "") }
    static let sources = [("auto", "自动选择"), ("gh-proxy", "GH-Proxy"), ("ghfast", "GHFast"), ("official", "GitHub 官方")]
    init(preferences: Preferences) { self.preferences = preferences }
    func start() {
        Task { await check(automatic: true) }
        timer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in Task { @MainActor in guard let self, self.preferences.updateFrequency != "startup" else { return }; await self.check(automatic: true) } }
    }
    private var due: Bool {
        guard preferences.automaticUpdates else { return false }
        guard let last = UserDefaults.standard.object(forKey: "lastUpdateCheck") as? Date else { return true }
        let seconds: TimeInterval = preferences.updateFrequency == "6h" ? 21600 : preferences.updateFrequency == "weekly" ? 604800 : preferences.updateFrequency == "startup" ? 0 : 86400
        return Date().timeIntervalSince(last) >= max(60, seconds)
    }
    func check(automatic: Bool = false) async {
        guard !busy, !automatic || due else { return }
        busy = true; status = "正在检查更新…"
        do {
            let value = try await Self.metadata(session)
            guard !value["draft"].flag, !value["prerelease"].flag else { throw CoreFailure(code: "updates", message: "暂未找到正式版本。") }
            UserDefaults.standard.set(Date(), forKey: "lastUpdateCheck")
            if value["tag_name"] != release["tag_name"] { downloaded = nil }
            release = value
            guard compare(latestVersion, AppPaths.version) == .orderedDescending else { asset = .null; status = "当前版本 \(AppPaths.version) 无需更新。"; busy = false; return }
            #if arch(arm64)
            let architecture = "arm64"
            #else
            let architecture = "x64"
            #endif
            asset = value["assets"].list.first { $0["name"].text == "MemoLex-\(latestVersion)-macOS-universal.zip" } ?? value["assets"].list.first { $0["name"].text == "MemoLex-\(latestVersion)-macOS-\(architecture).zip" } ?? .null
            guard asset != .null else { status = "发现新版 \(latestVersion)，请前往下载页面查看 Mac 版。"; busy = false; return }
            status = "发现新版 \(latestVersion)，点击“下载更新”开始下载。"; busy = false
            let automaticallyDownload = automatic && preferences.automaticDownloads
            if automatic, notified.insert(latestVersion).inserted { onAvailable?() }
            if automaticallyDownload && preferences.automaticUpdates && preferences.automaticDownloads { automaticDownloading = true; await download(); automaticDownloading = false }
        } catch { busy = false; status = "暂时无法检查更新，请检查网络后重试。" }
    }
    func download() async {
        guard !busy, asset != .null else { return }
        busy = true; progress = 0; downloadGeneration += 1; let generation = downloadGeneration; let snapshot = asset
        let digest = snapshot["digest"].text; let name = snapshot["name"].text
        guard digest.hasPrefix("sha256:"), digest.count == 71, snapshot["size"].integer > 0, snapshot["size"].integer < 2_000_000_000, name.hasPrefix("MemoLex-"), name.hasSuffix(".zip"), !name.contains("/"), !name.contains("\\"), let original = URL(string: snapshot["browser_download_url"].text), original.scheme == "https", original.host == "github.com", original.path.hasPrefix("/SuperRaine/MemoLex/releases/download/") else { busy = false; status = "更新信息不完整，请前往下载页面手动安装。"; return }
        let destinations = ["gh-proxy": "https://gh-proxy.com/", "ghfast": "https://ghfast.top/", "official": ""]
        let ids = preferences.updateSource == "auto" ? ["gh-proxy", "ghfast", "official"] : [preferences.updateSource] + ["gh-proxy", "ghfast", "official"].filter { $0 != preferences.updateSource }
        for source in ids {
            guard let prefix = destinations[source], let url = URL(string: prefix + original.absoluteString) else { continue }
            do {
                status = "正在下载更新 · \(Self.sources.first { $0.0 == source }?.1 ?? source)…"
                var request = URLRequest(url: url); request.timeoutInterval = 180
                let delegate = DownloadProgressDelegate { [weak self] fraction in Task { @MainActor in if self?.downloadGeneration == generation { self?.progress = fraction } } }
                let (temporary, response) = try await session.download(for: request, delegate: delegate)
                guard generation == downloadGeneration else { return }
                guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw URLError(.badServerResponse) }
                let attributes = try FileManager.default.attributesOfItem(atPath: temporary.path)
                let hash = try await Task.detached { try Self.hash(temporary) }.value
                guard generation == downloadGeneration else { return }
                guard (attributes[.size] as? NSNumber)?.intValue == snapshot["size"].integer, hash == String(digest.dropFirst(7)) else { throw CoreFailure(code: "updates", message: "下载文件校验失败。") }
                let folder = AppPaths.profile.appendingPathComponent("updates", isDirectory: true); try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let destination = folder.appendingPathComponent(name)
                if FileManager.default.fileExists(atPath: destination.path) { try FileManager.default.removeItem(at: destination) }
                try FileManager.default.moveItem(at: temporary, to: destination)
                downloaded = destination; status = "新版已下载完成，点击“更新并重启”安装。"; progress = 1; busy = false; return
            } catch { if generation != downloadGeneration { return }; if Task.isCancelled { break } }
        }
        busy = false; status = "下载未完成，可更换下载源后重试。"
    }
    func cancelDownload() { downloadGeneration += 1; session.getAllTasks { $0.forEach { $0.cancel() } }; busy = false; status = "下载已取消，稍后可重新下载。" }
    func preferencesChanged() { preferences.save(); if automaticDownloading && (!preferences.automaticUpdates || !preferences.automaticDownloads) { cancelDownload() } }
    nonisolated private static func metadata(_ session: URLSession) async throws -> J {
        let original = "https://api.github.com/repos/SuperRaine/MemoLex/releases/latest"
        let sources = [original, "https://gh-proxy.com/" + original, "https://ghfast.top/" + original]
        return try await withThrowingTaskGroup(of: J?.self) { group in
            for (index, source) in sources.enumerated() { group.addTask {
                do {
                    if index > 0 { try await Task.sleep(nanoseconds: UInt64(index) * 350_000_000) }
                    var request = URLRequest(url: URL(string: source)!); request.timeoutInterval = 8; request.setValue("MemoLex/\(AppPaths.version) macOS", forHTTPHeaderField: "User-Agent"); request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
                    let (data, response) = try await session.data(for: request)
                    guard (response as? HTTPURLResponse)?.statusCode == 200, data.count <= 2_000_000 else { return nil }
                    let value = try JSONDecoder().decode(J.self, from: data)
                    return value["tag_name"].text.isEmpty || !value["html_url"].text.hasPrefix(AppPaths.repository + "/releases/tag/") ? nil : value
                } catch { return nil }
            } }
            for try await result in group { if let result { group.cancelAll(); return result } }
            throw CoreFailure(code: "updates", message: "暂时无法检查更新，请稍后重试。")
        }
    }
    func install() async {
        guard let downloaded, !busy else { return }; busy = true
        do { try await MacUpdateInstaller.prepare(downloaded, digest: String(asset["digest"].text.dropFirst(7))); NSApp.terminate(nil) }
        catch { status = error.localizedDescription; busy = false }
    }
    nonisolated static func hash(_ url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url); defer { try? handle.close() }; var digest = SHA256()
        while let bytes = try handle.read(upToCount: 131072), !bytes.isEmpty { digest.update(data: bytes) }
        return digest.finalize().map { String(format: "%02x", $0) }.joined()
    }
    private func compare(_ lhs: String, _ rhs: String) -> ComparisonResult { lhs.compare(rhs, options: .numeric) }
    func stop() { timer?.invalidate(); session.invalidateAndCancel() }
}

private final class DownloadProgressDelegate: NSObject, URLSessionDownloadDelegate {
    var progress: (Double) -> Void
    init(progress: @escaping (Double) -> Void) { self.progress = progress }
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {}
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64, totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) { if totalBytesExpectedToWrite > 0 { progress(Double(totalBytesWritten) / Double(totalBytesExpectedToWrite)) } }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(request.url?.scheme == "https" ? request : nil) }
}

struct UpdatesSettings: View {
    @ObservedObject var updates: MacUpdates
    @ObservedObject private var preferences: Preferences
    init(updates: MacUpdates) { self.updates = updates; self.preferences = updates.preferences }
    var body: some View {
        VStack(alignment: .leading, spacing: 13) {
            Text("当前版本 \(AppPaths.version) · macOS").foregroundStyle(Palette.muted)
            HStack(spacing: 16) { Toggle("自动检查更新", isOn: $preferences.automaticUpdates).toggleStyle(.switch).controlSize(.small); Text("检查更新间隔").foregroundStyle(Palette.muted); Picker("", selection: $preferences.updateFrequency) { Text("每次启动").tag("startup"); Text("每 6 小时").tag("6h"); Text("每天").tag("daily"); Text("每周").tag("weekly") }.labelsHidden().frame(width: 150) }
            HStack(spacing: 13) { Text("下载源").foregroundStyle(Palette.muted); Picker("", selection: $preferences.updateSource) { ForEach(MacUpdates.sources, id: \.0) { id, name in Text(name).tag(id) } }.labelsHidden().frame(width: 180); Toggle("发现新版后自动下载", isOn: $preferences.automaticDownloads).toggleStyle(.switch).controlSize(.small) }
            Text("默认发现新版时只提醒，点击下载后开始下载。连接失败时会切换备用源。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            Text(updates.status).foregroundStyle(Palette.muted).padding(.top, 5)
            if updates.busy { ProgressView(value: updates.progress).tint(Palette.accent) }
            HStack(spacing: 8) {
                Button("手动检查更新") { Task { await updates.check() } }.disabled(updates.busy)
                Button("下载更新") { Task { await updates.download() } }.disabled(updates.busy || !updates.hasUpdate || updates.downloaded != nil)
                Button("更新并重启") { Task { await updates.install() } }.disabled(updates.busy || updates.downloaded == nil)
                Button("前往下载页面") { NSWorkspace.shared.open(URL(string: AppPaths.repository + "/releases/latest")!) }
            }.buttonStyle(MemoButtonStyle(compact: true))
            if updates.busy { Button("取消下载") { updates.cancelDownload() }.buttonStyle(MemoButtonStyle(compact: true)) }
            if !updates.release["body"].text.isEmpty { DisclosureGroup("更新说明") { Text(updates.release["body"].text).font(.system(size: 12)).textSelection(.enabled) } }
        }.onChange(of: preferences.automaticUpdates) { _ in updates.preferencesChanged() }.onChange(of: preferences.automaticDownloads) { _ in updates.preferencesChanged() }.onChange(of: preferences.updateFrequency) { _ in preferences.save() }.onChange(of: preferences.updateSource) { _ in preferences.save() }
    }
}

enum MacUpdateInstaller {
    struct Job: Codable { var process: Int32; var original: String; var staged: String; var backup: String; var originalHash: String; var ready: String }
    @MainActor static func prepare(_ archive: URL, digest: String) async throws {
        guard try MacUpdates.hash(archive) == digest else { throw CoreFailure(code: "updates", message: "更新文件校验失败，请重新下载。") }
        let current = Bundle.main.bundleURL.resolvingSymlinksInPath()
        guard current.pathExtension == "app", Bundle.main.bundleIdentifier == "com.superraine.MemoLex" else { throw CoreFailure(code: "updates", message: "请从完整 App 中更新，或前往下载页面安装新版。") }
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("MemoLex.Update.\(UUID().uuidString)", isDirectory: true); try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let listing = try await run("/usr/bin/zipinfo", ["-1", archive.path])
        let names = listing.components(separatedBy: .newlines).filter { !$0.isEmpty }
        guard names.count < 20000, names.allSatisfy({ name in !name.hasPrefix("/") && !name.split(separator: "/").contains("..") && (name.hasPrefix("MemoLex.app/") || name.hasPrefix("__MACOSX/")) }) else { throw CoreFailure(code: "updates", message: "更新包目录异常，已停止安装。") }
        _ = try await run("/usr/bin/ditto", ["-x", "-k", archive.path, folder.path])
        let candidate = folder.appendingPathComponent("MemoLex.app")
        guard Bundle(url: candidate)?.bundleIdentifier == "com.superraine.MemoLex" else { throw CoreFailure(code: "updates", message: "更新包不属于墨墨搜词。") }
        _ = try await run("/usr/bin/codesign", ["--verify", "--deep", "--strict", candidate.path])
        let parent = current.deletingLastPathComponent(); let staged = parent.appendingPathComponent(".MemoLex-stage-\(UUID().uuidString).app")
        do { try FileManager.default.copyItem(at: candidate, to: staged) } catch { throw CoreFailure(code: "updates", message: "无法写入软件所在目录，请手动将新版拖到应用程序文件夹。") }
        guard let executable = Bundle.main.executableURL else { throw CoreFailure(code: "updates", message: "找不到更新组件。") }
        let helper = folder.appendingPathComponent("MemoLex.Updater"); try FileManager.default.copyItem(at: executable, to: helper)
        let ready = folder.appendingPathComponent("ready")
        let job = Job(process: ProcessInfo.processInfo.processIdentifier, original: current.path, staged: staged.path, backup: parent.appendingPathComponent(".MemoLex-backup-\(UUID().uuidString).app").path, originalHash: try MacUpdates.hash(executable), ready: ready.path)
        let file = folder.appendingPathComponent("job.json"); try JSONEncoder().encode(job).write(to: file, options: .atomic)
        let process = Process(); process.executableURL = helper; process.arguments = ["--apply-update", file.path]; try process.run()
        for _ in 0..<30 { if FileManager.default.fileExists(atPath: ready.path) { return }; try await Task.sleep(nanoseconds: 100_000_000) }
        throw CoreFailure(code: "updates", message: "更新组件未能启动，请手动安装新版。")
    }
    static func apply(_ file: URL) throws {
        let job = try JSONDecoder().decode(Job.self, from: Data(contentsOf: file))
        let original = URL(fileURLWithPath: job.original).standardizedFileURL; let staged = URL(fileURLWithPath: job.staged).standardizedFileURL; let backup = URL(fileURLWithPath: job.backup).standardizedFileURL
        guard original.pathExtension == "app", staged.deletingLastPathComponent() == original.deletingLastPathComponent(), backup.deletingLastPathComponent() == original.deletingLastPathComponent(), staged.lastPathComponent.hasPrefix(".MemoLex-stage-"), backup.lastPathComponent.hasPrefix(".MemoLex-backup-"), Bundle(url: original)?.bundleIdentifier == "com.superraine.MemoLex", Bundle(url: staged)?.bundleIdentifier == "com.superraine.MemoLex", let binary = Bundle(url: original)?.executableURL, try MacUpdates.hash(binary) == job.originalHash else { throw CoreFailure(code: "updates", message: "更新路径校验失败。") }
        try Data().write(to: URL(fileURLWithPath: job.ready), options: .atomic)
        for _ in 0..<120 { if kill(job.process, 0) != 0 { break }; Thread.sleep(forTimeInterval: 0.25) }
        guard kill(job.process, 0) != 0 else { return }
        try FileManager.default.moveItem(at: original, to: backup)
        do { try FileManager.default.moveItem(at: staged, to: original) }
        catch { try? FileManager.default.moveItem(at: backup, to: original); throw error }
        try? FileManager.default.trashItem(at: backup, resultingItemURL: nil)
        let launch = Process(); launch.executableURL = URL(fileURLWithPath: "/usr/bin/open"); launch.arguments = [original.path]; try launch.run()
    }
    static func run(_ executable: String, _ arguments: [String]) async throws -> String {
        try await Task.detached {
            let process = Process(); let pipe = Pipe(); process.executableURL = URL(fileURLWithPath: executable); process.arguments = arguments; process.standardOutput = pipe; process.standardError = FileHandle.nullDevice
            try process.run(); let data = pipe.fileHandleForReading.readDataToEndOfFile(); process.waitUntilExit()
            guard process.terminationStatus == 0, data.count < 2_000_000 else { throw CoreFailure(code: "updates", message: "更新包检查未通过，请重新下载。") }
            return String(data: data, encoding: .utf8) ?? ""
        }.value
    }
}

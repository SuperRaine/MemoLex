import AppKit
import CryptoKit
import Darwin
import Foundation
import SwiftUI

struct MacUpdatePackage: Equatable {
    let url: URL
    let name: String
    let version: String
    let digest: String
    let size: Int

    static func select(from release: J) -> Self? {
        let tag = release["tag_name"].text
        let version = tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
        guard version.range(of: #"^[0-9]+(\.[0-9]+){1,3}$"#, options: .regularExpression) != nil else { return nil }
        #if arch(arm64)
        let architecture = "arm64"
        #else
        let architecture = "x64"
        #endif
        let prefix = "MemoLex-\(version)-macOS-"
        let names = ["universal.dmg", "\(architecture).dmg", "universal.zip", "\(architecture).zip"]
        for suffix in names {
            let name = prefix + suffix
            let expected = AppPaths.repository + "/releases/download/\(tag)/\(name)"
            guard let asset = release["assets"].list.first(where: { $0["name"].text == name }),
                  asset["browser_download_url"].text == expected, let url = URL(string: expected) else { continue }
            let digest = asset["digest"].text
            let size = asset["size"].doubleValue
            guard digest.range(of: #"^sha256:[a-f0-9]{64}$"#, options: .regularExpression) != nil,
                  (1..<2_000_000_000).contains(size), size == size.rounded() else { continue }
            return .init(url: url, name: name, version: version, digest: String(digest.dropFirst(7)), size: Int(size))
        }
        return nil
    }
}

@MainActor final class MacUpdates: ObservableObject {
    @Published var busy = false
    @Published var status = "点击“手动检查更新”获取最新版本。"
    @Published var progress: Double = 0
    @Published var release = J.null
    @Published var downloaded: URL?
    @Published private(set) var downloading = false
    let preferences: Preferences
    var onAvailable: (() -> Void)?
    private var timer: Timer?
    private var downloadGeneration = 0
    private var automaticDownloading = false
    private var notified: Set<String> = []
    private let session = URLSession(configuration: .ephemeral)
    private var package: MacUpdatePackage?
    private var officialMetadata = false
    var hasUpdate: Bool { package != nil }
    var latestVersion: String {
        let tag = release["tag_name"].text
        return tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
    }
    static let sources = [("auto", "自动选择"), ("gh-proxy", "GH-Proxy"), ("ghfast", "GHFast"), ("official", "GitHub 官方")]
    init(preferences: Preferences) { self.preferences = preferences }
    func start() {
        guard timer == nil else { return }
        Task { await check(automatic: true) }
        timer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in Task { @MainActor in guard let self, self.preferences.updateFrequency != "startup" else { return }; await self.check(automatic: true) } }
    }
    private var due: Bool {
        guard preferences.automaticUpdates else { return false }
        if preferences.updateFrequency == "startup" { return true }
        guard let last = UserDefaults.standard.object(forKey: "lastUpdateCheck") as? Date else { return true }
        let seconds: TimeInterval = preferences.updateFrequency == "6h" ? 21600 : preferences.updateFrequency == "weekly" ? 604800 : 86400
        return Date().timeIntervalSince(last) >= seconds
    }
    func check(automatic: Bool = false) async {
        guard !busy, !automatic || due else { return }
        busy = true; status = "正在检查更新…"
        do {
            let (value, official) = try await Self.metadata(session)
            UserDefaults.standard.set(Date(), forKey: "lastUpdateCheck")
            let next = MacUpdatePackage.select(from: value)
            if package != next { downloaded = nil }
            release = value
            officialMetadata = official
            guard latestVersion.compare(AppPaths.version, options: .numeric) == .orderedDescending else {
                package = nil
                downloaded = nil
                status = "当前版本 \(AppPaths.version) 无需更新。"
                busy = false
                return
            }
            package = next
            if downloaded != nil { status = "新版已下载，可更新并重启。" }
            else if next != nil { status = "发现新版 \(latestVersion)，可以下载更新。" }
            else { status = "发现新版 \(latestVersion)，请前往下载页面查看 Mac 版。" }
            busy = false
            if automatic, notified.insert(latestVersion).inserted { onAvailable?() }
            if automatic && next != nil && downloaded == nil && preferences.automaticUpdates && preferences.automaticDownloads {
                automaticDownloading = true
                await download()
                automaticDownloading = false
            }
        } catch { busy = false; status = "暂时无法检查更新，请检查网络后重试。" }
    }
    func download() async {
        guard !busy, let package else { return }
        busy = true; downloading = true; progress = 0; downloadGeneration += 1
        let generation = downloadGeneration
        defer { if generation == downloadGeneration { busy = false; downloading = false } }

        // A mirror can serve the file when its hash was confirmed independently on GitHub.
        var trusted = officialMetadata
        if !trusted {
            status = "正在核对更新文件…"
            if let official = await Self.fetchMetadata(Self.latestRelease, session), MacUpdatePackage.select(from: official) == package { trusted = true }
        }
        guard generation == downloadGeneration else { return }
        let destinations = ["gh-proxy": "https://gh-proxy.com/", "ghfast": "https://ghfast.top/", "official": ""]
        let choices = ["gh-proxy", "ghfast", "official"]
        let preferred = preferences.updateSource
        let ids = !trusted ? ["official"] : preferred == "auto" ? choices : [preferred] + choices.filter { $0 != preferred }
        for source in ids {
            guard let prefix = destinations[source], let url = URL(string: prefix + package.url.absoluteString) else { continue }
            do {
                status = "正在下载更新 · \(Self.sources.first { $0.0 == source }?.1 ?? source)…"
                progress = 0
                var request = URLRequest(url: url); request.timeoutInterval = 180
                let delegate = DownloadProgressDelegate(officialOnly: !trusted) { [weak self] fraction in
                    Task { @MainActor in if self?.downloadGeneration == generation, self?.downloading == true { self?.progress = fraction } }
                }
                let (temporary, response) = try await session.download(for: request, delegate: delegate)
                defer { try? FileManager.default.removeItem(at: temporary) }
                guard generation == downloadGeneration else { return }
                guard (response as? HTTPURLResponse)?.statusCode == 200,
                      trusted || DownloadProgressDelegate.isGitHubDownload(response.url) else { throw URLError(.badServerResponse) }
                let attributes = try FileManager.default.attributesOfItem(atPath: temporary.path)
                let hash = try await Task.detached { try Self.hash(temporary) }.value
                guard generation == downloadGeneration else { return }
                guard (attributes[.size] as? NSNumber)?.intValue == package.size, hash == package.digest else { throw CoreFailure(code: "updates", message: "下载文件校验失败。") }
                let folder = AppPaths.profile.appendingPathComponent("updates", isDirectory: true); try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let destination = folder.appendingPathComponent(package.name)
                let manager = FileManager.default
                if manager.fileExists(atPath: destination.path) { _ = try manager.replaceItemAt(destination, withItemAt: temporary) }
                else { try manager.moveItem(at: temporary, to: destination) }
                downloaded = destination; status = "新版已下载，可更新并重启。"; progress = 1; return
            } catch { if generation != downloadGeneration { return }; if Task.isCancelled { break } }
        }
        status = trusted ? "下载未完成，可更换下载源后重试。" : "暂时无法连接 GitHub 校验新版，请稍后重试或前往下载页面。"
    }
    func cancelDownload() {
        guard downloading else { return }
        downloadGeneration += 1
        session.getAllTasks { $0.forEach { $0.cancel() } }
        busy = false; downloading = false; progress = 0; status = "下载已取消，稍后可重新下载。"
    }
    func preferencesChanged() { preferences.save(); if automaticDownloading && (!preferences.automaticUpdates || !preferences.automaticDownloads) { cancelDownload() } }
    nonisolated private static let latestRelease = "https://api.github.com/repos/SuperRaine/MemoLex/releases/latest"
    nonisolated private static func fetchMetadata(_ address: String, _ session: URLSession) async -> J? {
        guard let url = URL(string: address) else { return nil }
        do {
            var request = URLRequest(url: url)
            request.timeoutInterval = 6
            request.setValue("MemoLex/\(AppPaths.version) macOS", forHTTPHeaderField: "User-Agent")
            request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            let (data, response) = try await session.data(for: request)
            guard (response as? HTTPURLResponse)?.statusCode == 200, data.count <= 2_000_000,
                  address != latestRelease || response.url?.host == "api.github.com" else { return nil }
            let value = try JSONDecoder().decode(J.self, from: data)
            let tag = value["tag_name"].text
            guard tag.range(of: #"^v?[0-9]+(\.[0-9]+){1,3}$"#, options: .regularExpression) != nil,
                  value["html_url"].text == AppPaths.repository + "/releases/tag/" + tag,
                  !value["draft"].flag, !value["prerelease"].flag else { return nil }
            return value
        } catch { return nil }
    }

    nonisolated private static func metadata(_ session: URLSession) async throws -> (J, Bool) {
        let sources = [latestRelease, "https://gh-proxy.com/" + latestRelease, "https://ghfast.top/" + latestRelease]
        return try await withThrowingTaskGroup(of: (J, Bool)?.self) { group in
            for (index, source) in sources.enumerated() { group.addTask {
                if index > 0 { try? await Task.sleep(nanoseconds: UInt64(index) * 350_000_000) }
                guard !Task.isCancelled, let release = await fetchMetadata(source, session) else { return nil }
                return (release, index == 0)
            } }
            for try await result in group { if let result { group.cancelAll(); return result } }
            throw CoreFailure(code: "updates", message: "暂时无法检查更新，请稍后重试。")
        }
    }
    func install() async {
        guard let downloaded, let package, !busy else { return }
        busy = true; status = "正在安装新版…"
        do { try await MacUpdateInstaller.prepare(downloaded, digest: package.digest, version: package.version); NSApp.terminate(nil) }
        catch { status = error.localizedDescription; busy = false }
    }
    nonisolated static func hash(_ url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url); defer { try? handle.close() }; var digest = SHA256()
        while let bytes = try handle.read(upToCount: 131072), !bytes.isEmpty { digest.update(data: bytes) }
        return digest.finalize().map { String(format: "%02x", $0) }.joined()
    }
    func stop() { timer?.invalidate(); session.invalidateAndCancel() }
}

private final class DownloadProgressDelegate: NSObject, URLSessionDownloadDelegate {
    let officialOnly: Bool
    let progress: (Double) -> Void
    init(officialOnly: Bool, progress: @escaping (Double) -> Void) { self.officialOnly = officialOnly; self.progress = progress }
    static func isGitHubDownload(_ url: URL?) -> Bool {
        guard url?.scheme == "https", let host = url?.host else { return false }
        return ["github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"].contains(host)
    }
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {}
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64, totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) { if totalBytesExpectedToWrite > 0 { progress(Double(totalBytesWritten) / Double(totalBytesExpectedToWrite)) } }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        let allowed = request.url?.scheme == "https" && (!officialOnly || Self.isGitHubDownload(request.url))
        completionHandler(allowed ? request : nil)
    }
}

struct UpdatesSettings: View {
    @ObservedObject private var model: AppModel
    @ObservedObject private var updates: MacUpdates
    @ObservedObject private var preferences: Preferences
    @State private var keepData = false

    init(model: AppModel) {
        self.model = model
        self.updates = model.updates
        self.preferences = model.preferences
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            SettingsCard(title: "自动更新") {
                Toggle("自动检查更新", isOn: $preferences.automaticUpdates).toggleStyle(.switch).controlSize(.small)
                Toggle("发现新版后自动下载", isOn: $preferences.automaticDownloads).toggleStyle(.switch).controlSize(.small)
                    .disabled(!preferences.automaticUpdates)
                Grid(alignment: .leading, horizontalSpacing: 14, verticalSpacing: 8) {
                    GridRow {
                        Text("检查更新间隔").foregroundStyle(Palette.muted)
                        MemoPicker(title: "检查更新间隔", selection: $preferences.updateFrequency,
                                   options: [("startup", "每次启动"), ("6h", "每 6 小时"), ("daily", "每天"), ("weekly", "每周")])
                            .frame(width: 155).disabled(!preferences.automaticUpdates)
                    }
                    GridRow {
                        Text("下载源").foregroundStyle(Palette.muted)
                        MemoPicker(title: "下载源", selection: $preferences.updateSource, options: MacUpdates.sources).frame(width: 155)
                    }
                }
            }
            SettingsCard(title: "当前版本 \(AppPaths.version)") {
                Text(updates.status).font(.callout).foregroundStyle(Palette.muted).fixedSize(horizontal: false, vertical: true)
                if updates.downloading { ProgressView(value: updates.progress).tint(Palette.accent) }
                ViewThatFits(in: .horizontal) {
                    HStack(spacing: 8) { updateActions }.fixedSize(horizontal: true, vertical: false)
                    VStack(alignment: .leading, spacing: 8) { updateActions }
                }
                if updates.downloading { Button("取消下载") { updates.cancelDownload() }.buttonStyle(MemoButtonStyle(compact: true)) }
            }.tourTarget("updates")
            if !updates.release["body"].text.isEmpty { DisclosureGroup("更新说明") { Text(updates.release["body"].text).font(.system(size: 12)).textSelection(.enabled) } }
            DisclosureGroup("卸载软件") {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle("保留用户资料", isOn: $keepData)
                    Text("默认删除本机账号、助记、查词历史和设置，云端数据不受影响。").font(.footnote).foregroundStyle(Palette.muted)
                    Button("卸载墨墨搜词", action: confirmUninstall).buttonStyle(MemoButtonStyle())
                }.padding(.top, 10).disabled(updates.busy)
            }
        }.onChange(of: preferences.automaticUpdates) { _ in updates.preferencesChanged() }.onChange(of: preferences.automaticDownloads) { _ in updates.preferencesChanged() }.onChange(of: preferences.updateFrequency) { _ in preferences.save() }.onChange(of: preferences.updateSource) { _ in preferences.save() }
    }

    private func confirmUninstall() {
        let keep = keepData
        model.notice = .init(title: "卸载墨墨搜词？",
                             message: keep ? "软件会移到废纸篓，用户资料会保留。" : "软件会移到废纸篓，并删除本机账号、助记、历史和设置。",
                             confirm: "卸载", destructive: true,
                             action: { MacUninstaller.remove(keepData: keep, model: model) })
    }

    @ViewBuilder private var updateActions: some View {
        Button("手动检查更新") { Task { await updates.check() } }
            .buttonStyle(MemoButtonStyle(compact: true)).disabled(updates.busy)
        Button("下载更新") { Task { await updates.download() } }
            .buttonStyle(MemoButtonStyle(accent: updates.hasUpdate && updates.downloaded == nil, compact: true))
            .disabled(updates.busy || !updates.hasUpdate || updates.downloaded != nil)
        Button("更新并重启") { Task { await updates.install() } }
            .buttonStyle(MemoButtonStyle(accent: updates.downloaded != nil, compact: true))
            .disabled(updates.busy || updates.downloaded == nil)
        Button("前往下载页面") { NSWorkspace.shared.open(URL(string: AppPaths.repository + "/releases/latest")!) }
            .buttonStyle(MemoButtonStyle(compact: true))
    }
}

enum MacUpdateInstaller {
    struct Job: Codable {
        var process: Int32
        var original: String
        var staged: String
        var backup: String
        var originalHash: String
        var version: String
        var ready: String
    }
    private static let bundleID = "com.superraine.MemoLex"

    @MainActor static func prepare(_ archive: URL, digest: String, version: String) async throws {
        let actual = try await Task.detached { try MacUpdates.hash(archive) }.value
        guard actual == digest else { throw CoreFailure(code: "updates", message: "更新文件校验失败，请重新下载。") }
        let current = Bundle.main.bundleURL.resolvingSymlinksInPath()
        guard current.pathExtension == "app", Bundle.main.bundleIdentifier == bundleID,
              let executable = Bundle.main.executableURL else {
            throw CoreFailure(code: "updates", message: "请从完整 App 中更新，或前往下载页面安装新版。")
        }
        let manager = FileManager.default
        let parent = current.deletingLastPathComponent()
        guard manager.isWritableFile(atPath: parent.path) else {
            throw CoreFailure(code: "updates", message: "软件所在目录无法写入。请先把 App 移到应用程序文件夹，再更新。")
        }
        let folder = manager.temporaryDirectory.appendingPathComponent("MemoLex.Update.\(UUID().uuidString)", isDirectory: true)
        try manager.createDirectory(at: folder, withIntermediateDirectories: false)
        let staged = parent.appendingPathComponent(".MemoLex-stage-\(UUID().uuidString).app")
        var readyToReplace = false
        defer { if !readyToReplace { try? manager.removeItem(at: staged); try? manager.removeItem(at: folder) } }
        try await stage(archive, in: folder, at: staged, version: version)
        let helper = folder.appendingPathComponent("MemoLex.Updater")
        try manager.copyItem(at: executable, to: helper)
        let ready = folder.appendingPathComponent("ready")
        let job = Job(process: ProcessInfo.processInfo.processIdentifier, original: current.path, staged: staged.path,
                      backup: parent.appendingPathComponent(".MemoLex-backup-\(UUID().uuidString).app").path,
                      originalHash: try MacUpdates.hash(executable), version: version, ready: ready.path)
        let file = folder.appendingPathComponent("job.json")
        try JSONEncoder().encode(job).write(to: file, options: .atomic)
        let process = Process()
        process.executableURL = helper
        process.arguments = ["--apply-update", file.path]
        try process.run()
        for _ in 0..<30 {
            if manager.fileExists(atPath: ready.path) { readyToReplace = true; return }
            if !process.isRunning { break }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        if process.isRunning { process.terminate() }
        throw CoreFailure(code: "updates", message: "更新组件未能启动，请手动安装新版。")
    }

    static func stage(_ archive: URL, in folder: URL, at staged: URL, version: String) async throws {
        if archive.pathExtension == "dmg" {
            let mount = folder.appendingPathComponent("volume", isDirectory: true)
            try FileManager.default.createDirectory(at: mount, withIntermediateDirectories: false)
            _ = try await run("/usr/bin/hdiutil", ["attach", "-readonly", "-nobrowse", "-noautoopen", "-mountpoint", mount.path, archive.path])
            do {
                try await stageBundle(mount.appendingPathComponent("MemoLex.app"), at: staged, version: version)
                _ = try await run("/usr/bin/hdiutil", ["detach", mount.path])
            } catch {
                _ = try? await run("/usr/bin/hdiutil", ["detach", mount.path])
                throw error
            }
        } else {
            guard archive.pathExtension == "zip" else { throw CoreFailure(code: "updates", message: "更新包格式不受支持。") }
            let listing = try await run("/usr/bin/zipinfo", ["-1", archive.path])
            let names = listing.components(separatedBy: .newlines).filter { !$0.isEmpty }
            guard !names.isEmpty, names.count < 20000, names.allSatisfy({ name in
                !name.split(separator: "/").contains("..") && (name.hasPrefix("MemoLex.app/") || name.hasPrefix("__MACOSX/"))
            }) else { throw CoreFailure(code: "updates", message: "更新包目录异常。") }
            _ = try await run("/usr/bin/ditto", ["-x", "-k", archive.path, folder.path])
            try await stageBundle(folder.appendingPathComponent("MemoLex.app"), at: staged, version: version)
        }
    }

    private static func stageBundle(_ bundle: URL, at staged: URL, version: String) async throws {
        guard bundle.resolvingSymlinksInPath().deletingLastPathComponent() == bundle.deletingLastPathComponent().resolvingSymlinksInPath(),
              isExpectedBundle(bundle, version: version) else { throw CoreFailure(code: "updates", message: "更新包的应用或版本不匹配。") }
        _ = try await run("/usr/bin/ditto", [bundle.path, staged.path])
        _ = try await run("/usr/bin/codesign", ["--verify", "--deep", "--strict", staged.path])
    }

    private static func isExpectedBundle(_ url: URL, version: String) -> Bool {
        guard let bundle = Bundle(url: url), bundle.bundleIdentifier == bundleID else { return false }
        return bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == version
    }

    static func apply(_ file: URL) throws {
        let job = try JSONDecoder().decode(Job.self, from: Data(contentsOf: file))
        let original = URL(fileURLWithPath: job.original).standardizedFileURL; let staged = URL(fileURLWithPath: job.staged).standardizedFileURL; let backup = URL(fileURLWithPath: job.backup).standardizedFileURL
        guard original.pathExtension == "app", staged.deletingLastPathComponent() == original.deletingLastPathComponent(),
              backup.deletingLastPathComponent() == original.deletingLastPathComponent(),
              staged.lastPathComponent.hasPrefix(".MemoLex-stage-"), backup.lastPathComponent.hasPrefix(".MemoLex-backup-"),
              let previous = Bundle(url: original), previous.bundleIdentifier == bundleID, let binary = previous.executableURL,
              isExpectedBundle(staged, version: job.version), try MacUpdates.hash(binary) == job.originalHash else {
            throw CoreFailure(code: "updates", message: "更新路径校验失败。")
        }
        try Data().write(to: URL(fileURLWithPath: job.ready), options: .atomic)
        for _ in 0..<120 { if kill(job.process, 0) != 0 { break }; Thread.sleep(forTimeInterval: 0.25) }
        guard kill(job.process, 0) != 0 else { return }
        let manager = FileManager.default
        try manager.moveItem(at: original, to: backup)
        do {
            try manager.moveItem(at: staged, to: original)
            let launch = Process()
            launch.executableURL = URL(fileURLWithPath: "/usr/bin/open")
            launch.arguments = [original.path]
            try launch.run()
            launch.waitUntilExit()
            guard launch.terminationStatus == 0 else { throw CoreFailure(code: "updates", message: "新版未能启动。") }
        } catch {
            if manager.fileExists(atPath: original.path) { try? manager.moveItem(at: original, to: staged) }
            if !manager.fileExists(atPath: original.path) { try? manager.moveItem(at: backup, to: original) }
            throw error
        }
        try? manager.trashItem(at: backup, resultingItemURL: nil)
        try? manager.removeItem(at: file.deletingLastPathComponent())
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

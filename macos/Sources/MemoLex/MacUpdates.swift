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
    private enum Phase {
        case checking, downloading, preparing, applying
    }

    @Published private var phase: Phase?
    @Published private(set) var cancelling = false
    @Published var status = "点击“手动检查更新”获取最新版本。"
    @Published var progress: Double = 0
    @Published var release = J.null
    @Published var downloaded: URL?
    let preferences: Preferences
    var onAvailable: (() -> Void)?
    private var timer: Timer?
    private var operation: Task<Bool, Never>?
    private var operationID: UUID?
    private var automaticDownloading = false
    private var notified: Set<String> = []
    private let session = URLSession(configuration: .ephemeral)
    private var package: MacUpdatePackage?
    private var officialMetadata = false
    var busy: Bool { phase != nil }
    var downloading: Bool { phase == .downloading }
    var canCancel: Bool { busy && phase != .applying && !cancelling }
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
        let checked = await perform(.checking, message: "正在检查更新…") { [self] _ in
            let (value, official) = try await Self.metadata(session)
            try Task.checkCancellation()
            UserDefaults.standard.set(Date(), forKey: "lastUpdateCheck")
            let next = MacUpdatePackage.select(from: value)
            if package != next { downloaded = nil }
            release = value
            officialMetadata = official
            guard latestVersion.compare(AppPaths.version, options: .numeric) == .orderedDescending else {
                package = nil
                downloaded = nil
                status = "当前版本 \(AppPaths.version) 无需更新。"
                return
            }
            package = next
            if downloaded != nil { status = "新版已下载，可更新并重启。" }
            else if next != nil { status = "发现新版 \(latestVersion)，可以下载更新。" }
            else { status = "发现新版 \(latestVersion)，请前往下载页面查看 Mac 版。" }
        }
        guard checked, automatic, latestVersion.compare(AppPaths.version, options: .numeric) == .orderedDescending else { return }
        if notified.insert(latestVersion).inserted { onAvailable?() }
        if hasUpdate && downloaded == nil && preferences.automaticUpdates && preferences.automaticDownloads {
            automaticDownloading = true
            defer { automaticDownloading = false }
            _ = await download()
        }
    }
    @discardableResult func download() async -> Bool {
        guard !busy, let package else { return false }
        return await perform(.downloading, message: "正在准备下载…") { [self] id in
            try await download(package, operation: id)
        }
    }

    private func download(_ package: MacUpdatePackage, operation id: UUID) async throws {
        // A mirror can serve the file when its hash was confirmed independently on GitHub.
        var trusted = officialMetadata
        if !trusted {
            status = "正在核对更新文件…"
            if let official = await Self.fetchMetadata(Self.latestRelease, session), MacUpdatePackage.select(from: official) == package { trusted = true }
        }
        try Task.checkCancellation()
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
                    Task { @MainActor in
                        guard let self, self.operationID == id, self.downloading, !self.cancelling else { return }
                        self.progress = fraction
                    }
                }
                let (temporary, response) = try await session.download(for: request, delegate: delegate)
                defer { try? FileManager.default.removeItem(at: temporary) }
                try Task.checkCancellation()
                guard (response as? HTTPURLResponse)?.statusCode == 200,
                      trusted || DownloadProgressDelegate.isGitHubDownload(response.url) else { throw URLError(.badServerResponse) }
                let attributes = try FileManager.default.attributesOfItem(atPath: temporary.path)
                let hash = try await Self.hashOffMain(temporary)
                try Task.checkCancellation()
                guard (attributes[.size] as? NSNumber)?.intValue == package.size, hash == package.digest else { throw CoreFailure(code: "updates", message: "下载文件校验失败。") }
                let folder = AppPaths.profile.appendingPathComponent("updates", isDirectory: true); try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let destination = folder.appendingPathComponent(package.name)
                let manager = FileManager.default
                if manager.fileExists(atPath: destination.path) { _ = try manager.replaceItemAt(destination, withItemAt: temporary) }
                else { try manager.moveItem(at: temporary, to: destination) }
                downloaded = destination; status = "新版已下载，可更新并重启。"; progress = 1; return
            } catch { try Task.checkCancellation() }
        }
        throw CoreFailure(code: "updates", message: trusted ? "下载未完成，可更换下载源后重试。" : "暂时无法连接 GitHub 校验新版，请稍后重试或前往下载页面。")
    }

    private func perform(_ phase: Phase, message: String, action: @escaping @MainActor (UUID) async throws -> Void) async -> Bool {
        guard operation == nil else { return false }
        let id = UUID()
        operationID = id
        self.phase = phase
        status = message
        progress = 0
        let work = Task {
            defer {
                operation = nil
                operationID = nil
                self.phase = nil
                cancelling = false
            }
            do {
                try Task.checkCancellation()
                try await action(id)
                return true
            } catch {
                if Task.isCancelled || error is CancellationError {
                    progress = 0
                    switch phase {
                    case .checking: status = "检查更新已取消。"
                    case .downloading: status = "下载已取消，稍后可重新下载。"
                    case .preparing, .applying: status = "安装已取消，可稍后继续。"
                    }
                } else {
                    status = error.localizedDescription
                }
                return false
            }
        }
        operation = work
        return await work.value
    }

    func cancelCurrentOperation() {
        guard canCancel else { return }
        cancelling = true
        status = "正在取消…"
        operation?.cancel()
    }
    func preferencesChanged() {
        preferences.save()
        if automaticDownloading && downloading && (!preferences.automaticUpdates || !preferences.automaticDownloads) {
            cancelCurrentOperation()
        }
    }
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
        guard let package, !busy else { return }
        if downloaded == nil {
            guard await download() else { return }
        }
        guard let downloaded, self.package == package else { return }
        _ = await perform(.preparing, message: "正在准备安装新版…") { [self] _ in
            do {
                try await MacUpdateInstaller.prepare(downloaded, digest: package.digest, version: package.version) {
                    phase = .applying
                    status = "正在安装并重启…"
                }
            } catch let failure as CoreFailure where failure.code == "update_archive" {
                self.downloaded = nil
                throw failure
            }
            NSApp.terminate(nil)
        }
    }
    nonisolated static func hash(_ url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url); defer { try? handle.close() }; var digest = SHA256()
        while let bytes = try handle.read(upToCount: 131072), !bytes.isEmpty {
            try Task.checkCancellation()
            digest.update(data: bytes)
        }
        return digest.finalize().map { String(format: "%02x", $0) }.joined()
    }
    nonisolated static func hashOffMain(_ url: URL) async throws -> String {
        let worker = Task.detached(priority: .utility) { try hash(url) }
        return try await withTaskCancellationHandler { try await worker.value } onCancel: { worker.cancel() }
    }
    func stop() {
        timer?.invalidate()
        if phase != .applying { operation?.cancel() }
        session.invalidateAndCancel()
    }
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
                else if updates.busy { ProgressView().controlSize(.small) }
                ViewThatFits(in: .horizontal) {
                    HStack(spacing: 8) { updateActions }.fixedSize(horizontal: true, vertical: false)
                    VStack(alignment: .leading, spacing: 8) { updateActions }
                }
                if updates.canCancel || updates.cancelling {
                    Button(updates.cancelling ? "正在取消…" : "取消当前操作") { updates.cancelCurrentOperation() }
                        .buttonStyle(MemoButtonStyle(compact: true)).disabled(updates.cancelling)
                }
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
        Button(updates.downloaded == nil ? "下载并安装" : "更新并重启") { Task { await updates.install() } }
            .buttonStyle(MemoButtonStyle(accent: updates.downloaded != nil, compact: true))
            .disabled(updates.busy || !updates.hasUpdate)
        Button("前往下载页面") { NSWorkspace.shared.open(URL(string: AppPaths.repository + "/releases/latest")!) }
            .buttonStyle(MemoButtonStyle(compact: true))
    }
}

enum MacUpdateInstaller {
    private struct Job: Codable {
        let process: Int32
        let original: String
        let originalHash: String
        let version: String
    }
    private static let bundleID = "com.superraine.MemoLex"
    private static let jobPrefix = "MemoLex.Update."

    private struct Paths {
        let folder: URL
        let original: URL
        let id: UUID

        var file: URL { folder.appendingPathComponent("job.json") }
        var ready: URL { folder.appendingPathComponent("ready") }
        var commit: URL { folder.appendingPathComponent("install") }
        var launched: URL { folder.appendingPathComponent("launched") }
        var staged: URL { original.deletingLastPathComponent().appendingPathComponent(".MemoLex-stage-\(id.uuidString).app") }
        var backup: URL { original.deletingLastPathComponent().appendingPathComponent(".MemoLex-backup-\(id.uuidString).app") }
    }

    @MainActor static func prepare(_ archive: URL, digest: String, version: String, onCommit: () -> Void) async throws {
        let actual: String
        do { actual = try await MacUpdates.hashOffMain(archive) }
        catch {
            try Task.checkCancellation()
            throw CoreFailure(code: "update_archive", message: "已下载的更新文件无法读取，请重新下载。")
        }
        guard actual == digest else { throw CoreFailure(code: "update_archive", message: "更新文件校验失败，请重新下载。") }
        let current = Bundle.main.bundleURL.resolvingSymlinksInPath().standardizedFileURL
        guard current.pathExtension == "app", Bundle.main.bundleIdentifier == bundleID,
              let executable = Bundle.main.executableURL else {
            throw CoreFailure(code: "updates", message: "请从完整 App 中更新，或前往下载页面安装新版。")
        }
        let manager = FileManager.default
        let parent = current.deletingLastPathComponent()
        let readOnly = (try? parent.resourceValues(forKeys: [.volumeIsReadOnlyKey]))?.volumeIsReadOnly == true
        guard !readOnly, manager.isWritableFile(atPath: parent.path) else {
            throw CoreFailure(code: "updates", message: "当前安装位置不可写入。请把墨墨搜词移到可写的「应用程序」文件夹，再重试。")
        }
        let id = UUID()
        let folder = manager.temporaryDirectory.resolvingSymlinksInPath()
            .appendingPathComponent(jobPrefix + id.uuidString, isDirectory: true)
        let paths = Paths(folder: folder, original: current, id: id)
        try manager.createDirectory(at: folder, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        var committed = false
        let helper = Process()
        defer {
            if !committed {
                if helper.isRunning { helper.terminate() }
                clean(paths)
            }
        }
        try await stage(archive, in: folder, at: paths.staged, version: version)
        let job = Job(process: ProcessInfo.processInfo.processIdentifier, original: current.path,
                      originalHash: try await MacUpdates.hashOffMain(executable), version: version)
        try JSONEncoder().encode(job).write(to: paths.file, options: .atomic)
        try Task.checkCancellation()

        // Launch the updater from the installed bundle so the executable keeps its signed resources.
        helper.executableURL = executable
        helper.arguments = ["--apply-update", paths.file.path]
        helper.standardInput = FileHandle.nullDevice
        helper.standardOutput = FileHandle.nullDevice
        helper.standardError = FileHandle.nullDevice
        do { try helper.run() }
        catch { throw CoreFailure(code: "updates", message: "未能启动安装程序。请重新打开已安装的软件后再试。") }
        for _ in 0..<150 {
            try Task.checkCancellation()
            if manager.fileExists(atPath: paths.ready.path) {
                try Data().write(to: paths.commit, options: .atomic)
                committed = true
                onCommit()
                return
            }
            if !helper.isRunning { break }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        throw CoreFailure(code: "updates", message: "安装程序未能就绪，当前版本仍可使用。请重新打开软件后再试。")
    }

    static func stage(_ archive: URL, in folder: URL, at staged: URL, version: String) async throws {
        if archive.pathExtension == "dmg" {
            let mount = folder.appendingPathComponent("volume", isDirectory: true)
            try FileManager.default.createDirectory(at: mount, withIntermediateDirectories: false)
            _ = try await run("/usr/bin/hdiutil", ["attach", "-readonly", "-nobrowse", "-noautoopen", "-mountpoint", mount.path, archive.path], failure: "无法打开更新磁盘映像，请重新下载。")
            do {
                try Task.checkCancellation()
                try await stageBundle(mount.appendingPathComponent("MemoLex.app"), at: staged, version: version)
                _ = try await run("/usr/bin/hdiutil", ["detach", mount.path], failure: "暂时无法推出更新磁盘映像，请稍后重试。")
            } catch {
                _ = try? await run("/usr/bin/hdiutil", ["detach", mount.path], failure: "")
                throw error
            }
        } else {
            guard archive.pathExtension == "zip" else { throw CoreFailure(code: "updates", message: "更新包格式不受支持。") }
            let listing = try await run("/usr/bin/zipinfo", ["-1", archive.path], failure: "无法读取更新压缩包，请重新下载。")
            let names = listing.components(separatedBy: .newlines).filter { !$0.isEmpty }
            guard !names.isEmpty, names.count < 20000, names.allSatisfy({ name in
                !name.split(separator: "/").contains("..") && (name.hasPrefix("MemoLex.app/") || name.hasPrefix("__MACOSX/"))
            }) else { throw CoreFailure(code: "updates", message: "更新包目录异常。") }
            try Task.checkCancellation()
            _ = try await run("/usr/bin/ditto", ["-x", "-k", archive.path, folder.path], failure: "无法解压更新文件，请检查磁盘空间后重试。")
            try await stageBundle(folder.appendingPathComponent("MemoLex.app"), at: staged, version: version)
        }
    }

    private static func stageBundle(_ bundle: URL, at staged: URL, version: String) async throws {
        try Task.checkCancellation()
        guard bundle.resolvingSymlinksInPath().deletingLastPathComponent().path == bundle.deletingLastPathComponent().resolvingSymlinksInPath().path,
              isExpectedBundle(bundle, version: version) else { throw CoreFailure(code: "updates", message: "更新包的应用或版本不匹配。") }
        _ = try await run("/usr/bin/ditto", [bundle.path, staged.path], failure: "无法将更新写入当前安装位置，请检查磁盘空间和 App 管理权限。")
        try Task.checkCancellation()
        _ = try await run("/usr/bin/codesign", ["--verify", "--deep", "--strict", staged.path], failure: "更新包签名校验失败，请重新下载。")
    }

    private static func isExpectedBundle(_ url: URL, version: String) -> Bool {
        guard let bundle = Bundle(url: url), bundle.bundleIdentifier == bundleID else { return false }
        return bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == version
    }

    private static func readJob(_ file: URL) throws -> (Job, Paths) {
        let folder = file.deletingLastPathComponent()
        let parent = FileManager.default.temporaryDirectory.resolvingSymlinksInPath()
        guard file.lastPathComponent == "job.json", folder.deletingLastPathComponent().path == parent.path,
              folder.lastPathComponent.hasPrefix(jobPrefix),
              let id = UUID(uuidString: String(folder.lastPathComponent.dropFirst(jobPrefix.count))),
              file.resolvingSymlinksInPath().path == file.standardizedFileURL.path else {
            throw CoreFailure(code: "updates", message: "更新任务路径无效。")
        }
        let job = try JSONDecoder().decode(Job.self, from: Data(contentsOf: file))
        let original = URL(fileURLWithPath: job.original, isDirectory: true).standardizedFileURL
        guard original.pathExtension == "app", original.path == Bundle.main.bundleURL.resolvingSymlinksInPath().path,
              Bundle.main.bundleIdentifier == bundleID else {
            throw CoreFailure(code: "updates", message: "更新任务与当前应用不匹配。")
        }
        return (job, Paths(folder: folder, original: original, id: id))
    }

    static func confirmLaunch() {
        let arguments = CommandLine.arguments
        guard arguments.count == 3, arguments[1] == "--update-started",
              let (job, paths) = try? readJob(URL(fileURLWithPath: arguments[2])),
              job.version == AppPaths.version, FileManager.default.fileExists(atPath: paths.commit.path) else { return }
        try? Data().write(to: paths.launched, options: .atomic)
    }

    static func apply(_ file: URL) throws {
        let (job, paths) = try readJob(file)
        let manager = FileManager.default
        guard job.process > 1, job.process != ProcessInfo.processInfo.processIdentifier,
              let executable = Bundle.main.executableURL,
              try MacUpdates.hash(executable) == job.originalHash,
              isExpectedBundle(paths.staged, version: job.version),
              !manager.fileExists(atPath: paths.backup.path) else {
            throw CoreFailure(code: "updates", message: "更新文件或安装位置已发生变化。")
        }
        try Data().write(to: paths.ready, options: .atomic)
        defer { clean(paths) }

        for _ in 0..<180 {
            if !manager.fileExists(atPath: paths.file.path) { return }
            if kill(job.process, 0) != 0 { break }
            Thread.sleep(forTimeInterval: 0.25)
        }
        guard manager.fileExists(atPath: paths.commit.path) else { return }
        guard kill(job.process, 0) != 0 else {
            reportFailure("旧版未能退出，安装尚未开始。请退出其他墨墨搜词窗口后再试。")
            return
        }

        var movedOriginal = false
        var installed = false
        do {
            guard try MacUpdates.hash(executable) == job.originalHash else {
                throw CoreFailure(code: "updates", message: "安装位置中的版本已经发生变化，请重新检查更新。")
            }
            try manager.moveItem(at: paths.original, to: paths.backup)
            movedOriginal = true
            try manager.moveItem(at: paths.staged, to: paths.original)
            installed = true
            try launch(paths.original, arguments: ["--update-started", paths.file.path])
            for _ in 0..<100 {
                if manager.fileExists(atPath: paths.launched.path) { break }
                Thread.sleep(forTimeInterval: 0.25)
            }
            guard manager.fileExists(atPath: paths.launched.path) || isRunning(paths.original) else {
                throw CoreFailure(code: "updates", message: "macOS 未能打开新版。")
            }
            try? manager.trashItem(at: paths.backup, resultingItemURL: nil)
        } catch {
            if installed && !isRunning(paths.original) { try? manager.moveItem(at: paths.original, to: paths.staged) }
            if movedOriginal && !manager.fileExists(atPath: paths.original.path) {
                try? manager.moveItem(at: paths.backup, to: paths.original)
            }
            let restored = manager.fileExists(atPath: executable.path) && (try? MacUpdates.hash(executable)) == job.originalHash
            let reason = (error as? CoreFailure)?.message ?? "macOS 无法写入当前安装位置。请检查 App 管理权限后重试。"
            reportFailure(restored ? reason + " 已保留旧版本。" : reason + " 可在安装目录找到旧版本备份。")
            if restored { try? launch(paths.original) }
            throw error
        }
    }

    private static func isRunning(_ app: URL) -> Bool {
        NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).contains {
            !$0.isTerminated && $0.processIdentifier != ProcessInfo.processInfo.processIdentifier
                && $0.bundleURL?.resolvingSymlinksInPath().path == app.path
        }
    }

    private static func launch(_ app: URL, arguments: [String] = []) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/open")
        process.arguments = ["-n", app.path] + (arguments.isEmpty ? [] : ["--args"] + arguments)
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw CoreFailure(code: "updates", message: "macOS 未能打开新版。") }
    }

    private static func clean(_ paths: Paths) {
        try? FileManager.default.removeItem(at: paths.staged)
        try? FileManager.default.removeItem(at: paths.folder)
    }

    private static var failureFile: URL { AppPaths.profile.appendingPathComponent("updates/install-error.txt") }

    private static func reportFailure(_ message: String) {
        try? FileManager.default.createDirectory(at: failureFile.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? Data(message.utf8).write(to: failureFile, options: .atomic)
    }

    static func takeFailure() -> String? {
        guard let message = try? String(contentsOf: failureFile, encoding: .utf8) else { return nil }
        try? FileManager.default.removeItem(at: failureFile)
        return message
    }

    private static func run(_ executable: String, _ arguments: [String], failure: String) async throws -> String {
        try await Task.detached(priority: .utility) {
            let process = Process()
            let output = Pipe()
            process.executableURL = URL(fileURLWithPath: executable)
            process.arguments = arguments
            process.standardOutput = output
            process.standardError = FileHandle.nullDevice
            try process.run()
            let data = output.fileHandleForReading.readDataToEndOfFile()
            process.waitUntilExit()
            guard process.terminationStatus == 0, data.count < 2_000_000 else {
                throw CoreFailure(code: "updates", message: failure)
            }
            return String(data: data, encoding: .utf8) ?? ""
        }.value
    }
}

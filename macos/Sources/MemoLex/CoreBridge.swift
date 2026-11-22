import Foundation
import Darwin

struct CoreFailure: LocalizedError {
    var code: String
    var message: String
    var errorDescription: String? { message }
}

@MainActor final class CoreBridge {
    private let process = Process()
    private let input = Pipe()
    private let output = Pipe()
    private let errors = Pipe()
    private var buffer = Data()
    private var pending: [String: CheckedContinuation<J, Error>] = [:]
    private var timers: [String: Task<Void, Never>] = [:]
    private var accountRequests: [String: Int] = [:]
    var accountKey = ""
    var onUnauthorized: (() -> Void)?
    private(set) var started = false
    private var accountGeneration = 0
    private var endedUnexpectedly = false

    func start() throws {
        guard !started else { return }
        guard !endedUnexpectedly else { throw CoreFailure(code: "worker_exit", message: "查词服务已退出，请重新打开软件。") }
        try FileManager.default.createDirectory(at: AppPaths.profile, withIntermediateDirectories: true)
        #if arch(arm64)
        let architecture = "arm64"
        #else
        let architecture = "x64"
        #endif
        let executable = AppPaths.resources.appendingPathComponent("Core-\(architecture)/MemoLex.Core")
        guard FileManager.default.isExecutableFile(atPath: executable.path) else { throw CoreFailure(code: "missing_core", message: "程序文件不完整，请重新安装墨墨搜词。") }
        process.executableURL = executable
        process.currentDirectoryURL = executable.deletingLastPathComponent()
        var environment = ProcessInfo.processInfo.environment
        environment["MEMOLEX_RESOURCES"] = AppPaths.resources.path
        environment["MEMOLEX_PROFILE"] = AppPaths.profile.path
        environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = AppPaths.profile.appendingPathComponent("runtime").path
        process.environment = environment
        process.standardInput = input; process.standardOutput = output; process.standardError = errors
        output.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let bytes = handle.availableData
            DispatchQueue.main.async { self?.receive(bytes) }
        }
        // Worker diagnostics must never enter the interface or a credential log.
        errors.fileHandleForReading.readabilityHandler = { handle in _ = handle.availableData }
        process.terminationHandler = { [weak self] _ in DispatchQueue.main.async { self?.ended() } }
        try process.run(); started = true
    }

    func request(_ method: String, _ parameters: [String: J] = [:], timeout: UInt64 = 240) async throws -> J {
        try Task.checkCancellation()
        if !started { try start() }
        let id = UUID().uuidString
        var parameters = parameters
        let needsAccount = method.hasPrefix("notepads.") || method.hasPrefix("study.") || ["notes.cloud", "notes.publish", "stubborn", "account.validate"].contains(method)
        if needsAccount {
            guard !accountKey.isEmpty else { throw CoreFailure(code: "cancelled", message: "账号正在切换，请稍后重试。") }
            parameters["accountKey"] = .string(accountKey)
        }
        let data = try JSONEncoder().encode(J.object(["id": .string(id), "method": .string(method), "params": .object(parameters)]))
        return try await withTaskCancellationHandler(operation: {
            try await withCheckedThrowingContinuation { continuation in
                pending[id] = continuation
                if needsAccount { accountRequests[id] = accountGeneration }
                timers[id] = Task { [weak self] in
                    try? await Task.sleep(nanoseconds: timeout * 1_000_000_000)
                    if !Task.isCancelled { self?.cancel(id, message: "操作超时，请重试。") }
                }
                do { try input.fileHandleForWriting.write(contentsOf: data + Data([10])) }
                catch { finish(id, .failure(CoreFailure(code: "pipe", message: "查词服务连接中断，请重新打开软件。"))) }
            }
        }, onCancel: { [weak self] in Task { @MainActor in self?.cancel(id) } })
    }

    func invalidateAccount() {
        accountGeneration += 1
        accountKey = ""
        for id in Array(accountRequests.keys) { cancel(id) }
    }

    func setAccount(token: String?) async throws {
        invalidateAccount()
        let generation = accountGeneration
        let result = try await request("account.set", ["token": token.map(J.string) ?? .null])
        guard generation == accountGeneration else { throw CancellationError() }
        let key = result["accountKey"].text
        if token != nil {
            guard result["authenticated"].flag, !key.isEmpty else {
                throw CoreFailure(code: "account", message: "未能载入墨墨账号，稍后会自动重试。")
            }
            accountKey = key
        }
    }
    private func receive(_ data: Data) {
        guard !data.isEmpty else { return }
        buffer.append(data)
        while let newline = buffer.firstIndex(of: 10) {
            let line = buffer.prefix(upTo: newline); buffer.removeSubrange(...newline)
            guard let response = try? JSONDecoder().decode(J.self, from: line) else { continue }
            let id = response["id"].text
            if response["error"] != .null {
                let error = CoreFailure(code: response["error"]["code"].text, message: response["error"]["message"].text)
                if error.code == "unauthorized", accountRequests[id] == accountGeneration, !accountKey.isEmpty, response["error"]["accountKey"].text == accountKey {
                    accountKey = ""
                    onUnauthorized?()
                }
                finish(id, .failure(error))
            } else { finish(id, .success(response["result"])) }
        }
    }
    private func finish(_ id: String, _ result: Result<J, Error>) {
        timers.removeValue(forKey: id)?.cancel()
        accountRequests.removeValue(forKey: id)
        pending.removeValue(forKey: id)?.resume(with: result)
    }
    private func cancel(_ id: String, message: String = "操作已取消。") {
        guard pending[id] != nil else { return }
        let command = J.object(["id": .string(UUID().uuidString), "method": .string("cancel"), "params": .object(["id": .string(id)])])
        if let data = try? JSONEncoder().encode(command) { try? input.fileHandleForWriting.write(contentsOf: data + Data([10])) }
        finish(id, .failure(CoreFailure(code: message == "操作已取消。" ? "cancelled" : "timeout", message: message)))
    }
    private func ended() {
        started = false; endedUnexpectedly = true
        for id in Array(pending.keys) { finish(id, .failure(CoreFailure(code: "worker_exit", message: "查词服务已退出，请重新打开软件。"))) }
    }
    func stop() {
        output.fileHandleForReading.readabilityHandler = nil; errors.fileHandleForReading.readabilityHandler = nil
        try? input.fileHandleForWriting.close()
        for id in Array(pending.keys) { cancel(id) }
    }
    func shutDown() async {
        stop()
        for _ in 0..<30 { if !process.isRunning { return }; try? await Task.sleep(nanoseconds: 100_000_000) }
        if process.isRunning { process.terminate() }
        for _ in 0..<10 { if !process.isRunning { return }; try? await Task.sleep(nanoseconds: 100_000_000) }
        if process.isRunning { kill(process.processIdentifier, SIGKILL) }
    }
}

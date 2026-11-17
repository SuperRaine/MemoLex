import Foundation

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
    var accountKey = ""
    var onUnauthorized: (() -> Void)?
    private(set) var started = false
    private var accountGeneration = 0

    func start() throws {
        guard !started else { return }
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
        if !started { try start() }
        let id = UUID().uuidString
        var parameters = parameters
        if method.hasPrefix("notepads.") || method.hasPrefix("study.") || ["notes.cloud", "notes.publish", "stubborn", "account.validate"].contains(method) {
            parameters["accountKey"] = .string(accountKey)
        }
        let data = try JSONEncoder().encode(J.object(["id": .string(id), "method": .string(method), "params": .object(parameters)]))
        return try await withTaskCancellationHandler(operation: {
            try await withCheckedThrowingContinuation { continuation in
                pending[id] = continuation
                timers[id] = Task { [weak self] in
                    try? await Task.sleep(nanoseconds: timeout * 1_000_000_000)
                    if !Task.isCancelled { self?.cancel(id, message: "操作超时，请重试。") }
                }
                do { try input.fileHandleForWriting.write(contentsOf: data + Data([10])) }
                catch { finish(id, .failure(CoreFailure(code: "pipe", message: "查词服务连接中断，请重新打开软件。"))) }
            }
        }, onCancel: { [weak self] in Task { @MainActor in self?.cancel(id) } })
    }

    func setAccount(token: String?) async throws {
        accountGeneration += 1; let generation = accountGeneration
        let result = try await request("account.set", ["token": token.map(J.string) ?? .null])
        if generation == accountGeneration { accountKey = result["accountKey"].text }
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
                if error.code == "unauthorized", !accountKey.isEmpty, response["error"]["accountKey"].text == accountKey { onUnauthorized?() }
                finish(id, .failure(error))
            } else { finish(id, .success(response["result"])) }
        }
    }
    private func finish(_ id: String, _ result: Result<J, Error>) {
        timers.removeValue(forKey: id)?.cancel()
        pending.removeValue(forKey: id)?.resume(with: result)
    }
    private func cancel(_ id: String, message: String = "操作已取消。") {
        guard pending[id] != nil else { return }
        let command = J.object(["id": .string(UUID().uuidString), "method": .string("cancel"), "params": .object(["id": .string(id)])])
        if let data = try? JSONEncoder().encode(command) { try? input.fileHandleForWriting.write(contentsOf: data + Data([10])) }
        finish(id, .failure(CoreFailure(code: "cancelled", message: message)))
    }
    private func ended() {
        started = false
        for id in Array(pending.keys) { finish(id, .failure(CoreFailure(code: "worker_exit", message: "查词服务已退出，请重新打开软件。"))) }
    }
    func stop() {
        output.fileHandleForReading.readabilityHandler = nil; errors.fileHandleForReading.readabilityHandler = nil
        try? input.fileHandleForWriting.close()
        for id in Array(pending.keys) { cancel(id) }
    }
}

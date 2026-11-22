import Darwin
import Foundation

enum CredentialStoreError: LocalizedError {
    case read, save, remove

    var errorDescription: String? {
        switch self {
        case .read: return "无法读取本机登录凭证，请检查本机资料目录后重试。"
        case .save: return "无法保存本机登录凭证，请检查本机资料目录后重试。"
        case .remove: return "无法移除本机登录凭证，请检查本机资料目录后重试。"
        }
    }
}

actor CredentialVault {
    static let shared = CredentialVault(profile: AppPaths.profile)
    private static let maximumTokenBytes = 4096
    private let directory: URL

    init(profile: URL) {
        directory = profile.appendingPathComponent("credentials", isDirectory: true)
    }

    func read(_ account: UUID) throws -> String? {
        guard let folder = try openDirectory(onError: .read) else { return nil }
        defer { close(folder) }
        let descriptor = openat(folder, account.uuidString, O_RDONLY | O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC)
        if descriptor < 0 && errno == ENOENT { return nil }
        guard descriptor >= 0 else { throw CredentialStoreError.read }
        let file = FileHandle(fileDescriptor: descriptor, closeOnDealloc: true)
        defer { try? file.close() }

        var info = stat()
        guard fstat(descriptor, &info) == 0, info.st_uid == geteuid(),
              info.st_mode & mode_t(S_IFMT) == mode_t(S_IFREG), info.st_mode & 0o777 == 0o600,
              info.st_size > 0, info.st_size <= Self.maximumTokenBytes,
              let data = try? file.read(upToCount: Self.maximumTokenBytes + 1), data.count == info.st_size,
              let token = String(data: data, encoding: .utf8), !token.isEmpty
        else { throw CredentialStoreError.read }
        return token
    }

    func add(_ token: String, account: UUID) throws {
        let data = Data(token.utf8)
        guard !data.isEmpty, data.count <= Self.maximumTokenBytes else { throw CredentialStoreError.save }
        do {
            try FileManager.default.createDirectory(at: directory.deletingLastPathComponent(), withIntermediateDirectories: true)
        } catch { throw CredentialStoreError.save }
        guard mkdir(directory.path, 0o700) == 0 || errno == EEXIST,
              let folder = try openDirectory(onError: .save)
        else { throw CredentialStoreError.save }
        defer { close(folder) }

        let descriptor = openat(folder, account.uuidString, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW | O_CLOEXEC, mode_t(0o600))
        guard descriptor >= 0 else { throw CredentialStoreError.save }
        let file = FileHandle(fileDescriptor: descriptor, closeOnDealloc: true)
        // The account index is updated only after the new credential is fully written.
        do {
            try file.write(contentsOf: data)
            try file.synchronize()
            try file.close()
        } catch {
            unlinkat(folder, account.uuidString, 0)
            throw CredentialStoreError.save
        }
    }

    func remove(_ account: UUID) throws {
        guard let folder = try openDirectory(onError: .remove) else { return }
        defer { close(folder) }
        guard unlinkat(folder, account.uuidString, 0) == 0 || errno == ENOENT else { throw CredentialStoreError.remove }
    }

    private func openDirectory(onError error: CredentialStoreError) throws -> Int32? {
        let descriptor = open(directory.path, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC)
        if descriptor < 0 && errno == ENOENT { return nil }
        guard descriptor >= 0 else { throw error }
        var info = stat()
        guard fstat(descriptor, &info) == 0, info.st_uid == geteuid(), info.st_mode & 0o777 == 0o700 else {
            close(descriptor)
            throw error
        }
        return descriptor
    }
}

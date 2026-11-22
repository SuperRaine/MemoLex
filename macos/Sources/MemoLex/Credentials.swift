import Foundation
import Security

actor CredentialVault {
    static let shared = CredentialVault()
    private let service = "com.superraine.MemoLex.accounts"

    func read(_ account: String, allowPrompt: Bool = false) throws -> String? {
        try withInteraction(allowed: allowPrompt) {
            var query = attributes(account)
            query[kSecReturnData as String] = true
            query[kSecMatchLimit as String] = kSecMatchLimitOne
            var result: CFTypeRef?
            let status = SecItemCopyMatching(query as CFDictionary, &result)
            if status == errSecItemNotFound { return nil }
            guard status == errSecSuccess, let data = result as? Data,
                  let token = String(data: data, encoding: .utf8), !token.isEmpty else {
                throw CoreFailure(code: "keychain", message: "请在账号设置中授权钥匙串，或重新登录墨墨账号。")
            }
            return token
        }
    }

    func add(_ token: String, account: String) throws {
        try withInteraction(allowed: false) {
            var query = attributes(account)
            query[kSecValueData as String] = Data(token.utf8)
            query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            query[kSecAttrLabel as String] = "墨墨搜词登录信息"
            guard SecItemAdd(query as CFDictionary, nil) == errSecSuccess else {
                throw CoreFailure(code: "keychain", message: "无法保存登录信息。请在「钥匙串访问」中解锁登录钥匙串，再试一次。")
            }
        }
    }

    func remove(_ account: String) {
        try? withInteraction(allowed: false) { _ = SecItemDelete(attributes(account) as CFDictionary) }
    }

    func removeAll() {
        try? withInteraction(allowed: false) { _ = SecItemDelete(attributes(nil) as CFDictionary) }
    }

    private func attributes(_ account: String?) -> [String: Any] {
        var attributes: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service]
        attributes[kSecAttrAccount as String] = account
        return attributes
    }

    private func withInteraction<Value>(allowed: Bool, _ request: () throws -> Value) throws -> Value {
        // Existing items live in the file-based login keychain. Its ACL prompts use this
        // process-wide setting, so all vault operations run serially on this actor.
        var previous = DarwinBoolean(true)
        guard SecKeychainGetUserInteractionAllowed(&previous) == errSecSuccess,
              SecKeychainSetUserInteractionAllowed(allowed) == errSecSuccess else {
            throw CoreFailure(code: "keychain", message: "暂时无法访问登录钥匙串，请在账号设置中重试。")
        }
        defer { _ = SecKeychainSetUserInteractionAllowed(previous.boolValue) }
        return try request()
    }
}

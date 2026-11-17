import Foundation
import Security

enum CredentialVault {
    private static let service = "com.superraine.MemoLex.accounts"
    static func read(_ account: String) throws -> String? {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account, kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = result as? Data else { throw CoreFailure(code: "keychain", message: "无法读取钥匙串中的登录信息，请重新登录。") }
        return String(data: data, encoding: .utf8)
    }
    static func save(_ token: String, account: String) throws {
        let identity: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
        let bytes = Data(token.utf8)
        let update = SecItemUpdate(identity as CFDictionary, [kSecValueData as String: bytes] as CFDictionary)
        if update == errSecItemNotFound {
            var query = identity; query[kSecValueData as String] = bytes
            query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            query[kSecAttrLabel as String] = "墨墨搜词登录信息"
            guard SecItemAdd(query as CFDictionary, nil) == errSecSuccess else { throw CoreFailure(code: "keychain", message: "无法保存登录信息，请检查钥匙串权限。") }
        } else if update != errSecSuccess { throw CoreFailure(code: "keychain", message: "无法更新钥匙串中的登录信息。") }
    }
    static func remove(_ account: String) {
        SecItemDelete([kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account] as CFDictionary)
    }
    static func removeAll() {
        SecItemDelete([kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service] as CFDictionary)
    }
}

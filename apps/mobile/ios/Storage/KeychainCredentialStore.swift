import Foundation
import Security

final class KeychainCredentialStore {
    private let service = "cc.onedesk.mobile.trust"

    func read(desktopId: String) -> String? {
        var query = baseQuery(desktopId: desktopId)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var value: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &value) == errSecSuccess,
              let data = value as? Data else {
            return nil
        }
        return String(data: data, encoding: .utf8)
    }

    func write(desktopId: String, credential: String) throws {
        guard let data = credential.data(using: .utf8), !desktopId.isEmpty, !credential.isEmpty else {
            throw OneDeskMobileError.invalidPayload("长期信任凭据不能为空")
        }
        let query = baseQuery(desktopId: desktopId)
        let update = [kSecValueData as String: data]
        let status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var insert = query
            insert[kSecValueData as String] = data
            insert[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            guard SecItemAdd(insert as CFDictionary, nil) == errSecSuccess else {
                throw OneDeskMobileError.permission("无法写入 iOS Keychain")
            }
        } else if status != errSecSuccess {
            throw OneDeskMobileError.permission("无法更新 iOS Keychain")
        }
    }

    func delete(desktopId: String) {
        SecItemDelete(baseQuery(desktopId: desktopId) as CFDictionary)
    }

    private func baseQuery(desktopId: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: desktopId,
        ]
    }
}

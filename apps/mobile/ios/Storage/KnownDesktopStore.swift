import Foundation

struct KnownDesktop: Codable, Equatable {
    let desktopId: String
    var name: String
    var host: String
    var port: Int
    var trusted: Bool
    var gatewayFingerprint: String
    var schemeVersion: String
    var schemeHash: String
    var lastConnectedAt: String

    func frontendObject() -> JSONObject {
        [
            "desktopId": desktopId,
            "name": name,
            "host": host,
            "port": port,
            "trusted": trusted,
            "schemeVersion": schemeVersion,
            "schemeHash": schemeHash,
        ]
    }
}

/// 桌面元数据可进入 UserDefaults；长期凭据始终只保存在 Keychain，不能暴露给前端。
final class KnownDesktopStore {
    private let defaults: UserDefaults
    private let keychain: KeychainCredentialStore
    private let key = "onedesk.knownDesktops.v1"
    private let activeKey = "onedesk.activeDesktopId"
    private let lock = NSLock()

    init(defaults: UserDefaults = .standard, keychain: KeychainCredentialStore = .init()) {
        self.defaults = defaults
        self.keychain = keychain
    }

    func list() -> [KnownDesktop] {
        lock.withLock { decodeRecords() }
    }

    func frontendJSON() -> String {
        let objects = list().map { $0.frontendObject() }
        return (try? JSONSupport.string(objects)) ?? "[]"
    }

    func find(host: String, port: Int) -> (KnownDesktop, String)? {
        guard let record = list().first(where: { $0.host == host && $0.port == port }),
              let credential = keychain.read(desktopId: record.desktopId) else {
            return nil
        }
        return (record, credential)
    }

    func find(desktopId: String) -> (KnownDesktop, String)? {
        guard let record = list().first(where: { $0.desktopId == desktopId }),
              let credential = keychain.read(desktopId: desktopId) else {
            return nil
        }
        return (record, credential)
    }

    func upsert(_ desktop: KnownDesktop, credential: String) throws {
        try keychain.write(desktopId: desktop.desktopId, credential: credential)
        try lock.withLock {
            var records = decodeRecords().filter { $0.desktopId != desktop.desktopId }
            records.append(desktop)
            let data = try JSONEncoder().encode(records)
            defaults.set(data, forKey: key)
        }
    }

    func updateScheme(desktopId: String, version: String, hash: String) throws {
        try lock.withLock {
            var records = decodeRecords()
            guard let index = records.firstIndex(where: { $0.desktopId == desktopId }) else { return }
            records[index].schemeVersion = version
            records[index].schemeHash = hash
            defaults.set(try JSONEncoder().encode(records), forKey: key)
        }
    }

    var activeDesktopId: String? {
        get { defaults.string(forKey: activeKey) }
        set { defaults.set(newValue, forKey: activeKey) }
    }

    private func decodeRecords() -> [KnownDesktop] {
        guard let data = defaults.data(forKey: key),
              let records = try? JSONDecoder().decode([KnownDesktop].self, from: data) else {
            return []
        }
        return records
    }
}

extension NSLock {
    func withLock<T>(_ work: () throws -> T) rethrows -> T {
        lock()
        defer { unlock() }
        return try work()
    }
}

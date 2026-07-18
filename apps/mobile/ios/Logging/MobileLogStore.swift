import Foundation

/// 断联时日志持久化；连接成功并上传后才清空，避免中途失败造成日志丢失。
final class MobileLogStore {
    private let defaults: UserDefaults
    private let deviceId: () -> String
    private let key = "onedesk.offlineLogs.v1"
    private let lock = NSLock()
    private var onlineSink: ((JSONObject) -> Bool)?

    init(defaults: UserDefaults = .standard, deviceId: @escaping () -> String) {
        self.defaults = defaults
        self.deviceId = deviceId
    }

    func append(_ level: String, _ category: String, _ message: String, context: JSONObject = [:]) {
        let entry: JSONObject = [
            "logId": "log-\(UUID().uuidString.lowercased())",
            "createdAtUnixMs": Int64(Date().timeIntervalSince1970 * 1_000),
            "sourceDeviceId": deviceId(),
            "level": level,
            "category": category,
            "message": message,
            "context": context,
        ]
        lock.withLock {
            if onlineSink?(entry) == true { return }
            var records = snapshotUnlocked()
            records.append(entry)
            // 防止设备长期离线导致 UserDefaults 无界增长。
            if records.count > 2_000 { records.removeFirst(records.count - 2_000) }
            defaults.set(try? JSONSupport.data(records), forKey: key)
        }
    }

    func snapshot() -> [JSONObject] {
        lock.withLock { snapshotUnlocked() }
    }

    func clear() {
        lock.withLock { defaults.removeObject(forKey: key) }
    }

    func setOnlineSink(_ sink: ((JSONObject) -> Bool)?) {
        lock.withLock { onlineSink = sink }
    }

    private func snapshotUnlocked() -> [JSONObject] {
        guard let data = defaults.data(forKey: key),
              let value = try? JSONSerialization.jsonObject(with: data),
              let records = value as? [JSONObject] else {
            return []
        }
        return records
    }
}

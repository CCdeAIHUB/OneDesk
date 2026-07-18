import Foundation

typealias JSONObject = [String: Any]

enum JSONSupport {
    static func object(from data: Data) throws -> JSONObject {
        guard let value = try JSONSerialization.jsonObject(with: data) as? JSONObject else {
            throw OneDeskMobileError.invalidPayload("JSON 根节点必须是对象")
        }
        return value
    }

    static func data(_ value: Any) throws -> Data {
        guard JSONSerialization.isValidJSONObject(value) else {
            throw OneDeskMobileError.invalidPayload("数据无法编码为 JSON")
        }
        return try JSONSerialization.data(withJSONObject: value, options: [])
    }

    static func string(_ value: Any) throws -> String {
        guard let result = String(data: try data(value), encoding: .utf8) else {
            throw OneDeskMobileError.invalidPayload("JSON 不是有效 UTF-8")
        }
        return result
    }

    static func response(
        ok: Bool,
        payload: Any? = nil,
        errorCode: String? = nil,
        message: String? = nil
    ) -> String {
        var result: JSONObject = ["ok": ok]
        if let payload { result["payload"] = payload }
        if let errorCode { result["errorCode"] = errorCode }
        if let message { result["message"] = message }
        return (try? string(result)) ?? "{\"ok\":false,\"errorCode\":\"JsonEncodingFailed\"}"
    }
}

enum OneDeskMobileError: LocalizedError {
    case invalidPayload(String)
    case transport(String)
    case integrity(String)
    case unsupported(String)
    case permission(String)

    var errorDescription: String? {
        switch self {
        case .invalidPayload(let message), .transport(let message), .integrity(let message),
             .unsupported(let message), .permission(let message):
            return message
        }
    }
}

extension Dictionary where Key == String, Value == Any {
    func string(_ key: String) -> String { self[key] as? String ?? "" }
    func int(_ key: String, default fallback: Int = 0) -> Int {
        (self[key] as? NSNumber)?.intValue ?? self[key] as? Int ?? fallback
    }
    func int64(_ key: String, default fallback: Int64 = 0) -> Int64 {
        (self[key] as? NSNumber)?.int64Value ?? self[key] as? Int64 ?? fallback
    }
    func bool(_ key: String, default fallback: Bool = false) -> Bool {
        (self[key] as? NSNumber)?.boolValue ?? self[key] as? Bool ?? fallback
    }
    func object(_ key: String) -> JSONObject? { self[key] as? JSONObject }
    func array(_ key: String) -> [Any] { self[key] as? [Any] ?? [] }
}

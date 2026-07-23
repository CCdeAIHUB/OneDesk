// 此文件由 packages/protocol/schema/onedesk.protocol.json 生成，请勿手工修改。
// schema-sha256: d9643bed89f2dde0c2a7f1a781c1ff539cc0f45c9f90495b5c9597b44c0de77b
import Foundation

enum OneDeskProtocol {
    static let version = 1
    static let schemaSha256 = "d9643bed89f2dde0c2a7f1a781c1ff539cc0f45c9f90495b5c9597b44c0de77b"
}

enum JSONValue: Codable {
    case string(String), number(Double), bool(Bool), object([String: JSONValue]), array([JSONValue]), null
}

enum GatewayMessageType: String, Codable {
    case request = "request"
    case response = "response"
    case event = "event"
}

enum ProtocolDeviceKind: String, Codable {
    case desktop = "desktop"
    case mobile = "mobile"
}

enum ProtocolSourceKind: String, Codable {
    case component = "component"
    case plugin = "plugin"
    case system = "system"
}

struct ProtocolDeviceIdentity: Codable {
    let deviceId: String
    let displayName: String
    let kind: ProtocolDeviceKind
    let platform: String
    let architecture: String
}

struct ProtocolTrustedSource: Codable {
    let schemeId: String?
    let pageId: String?
    let componentId: String?
    let pluginId: String?
    let kind: ProtocolSourceKind
}

struct PairingRequestContract: Codable {
    let verificationCode: String
    let clientNonce: String
    let stableDeviceKey: String?
    let mobileIdentity: ProtocolDeviceIdentity
}

struct PairingResponseContract: Codable {
    let desktopIdentity: ProtocolDeviceIdentity
    let assignedMobileIdentity: ProtocolDeviceIdentity
    let trustCredential: String
    let credentialExpiresAtUnixMs: Int64
}

struct TrustedConnectRequestContract: Codable {
    let trustCredential: String
    let clientNonce: String
    let mobileIdentity: ProtocolDeviceIdentity
}

struct JsApiRequestContract: Codable {
    let requestId: String
    let targetDeviceId: String
    let source: ProtocolTrustedSource
    let capability: String
    let payload: JSONValue
}

struct JsApiErrorContract: Codable {
    let code: String
    let message: String
    let highRisk: Bool
}

struct JsApiResponseContract: Codable {
    let requestId: String
    let ok: Bool
    let error: JsApiErrorContract?
    let payload: JSONValue?
}

struct SchemeDescriptorContract: Codable {
    let version: String
    let hash: String
    let totalBytes: Int64
    let hasScheme: Bool
}

struct LogRecordContract: Codable {
    let logId: String
    let createdAtUnixMs: Int64
    let sourceDeviceId: String
    let level: String
    let category: String
    let message: String
    let context: JSONValue
}

struct MobileGatewayEnvelope: Codable {
    let protocolVersion: Int
    let messageType: String
    let messageId: String
    let correlationId: String?
    let payload: JSONValue
}

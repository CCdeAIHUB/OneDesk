// 此文件由 packages/protocol/schema/onedesk.protocol.json 生成，请勿手工修改。
// schema-sha256: 04aedd08890925036ff61bc67b943c1af80a039c82a6f3a09af3e373137022ee
package cc.onedesk.mobile

import org.json.JSONObject

object OneDeskProtocol {
    const val PROTOCOL_VERSION: Int = 1
    const val SCHEMA_SHA256: String = "04aedd08890925036ff61bc67b943c1af80a039c82a6f3a09af3e373137022ee"
}

enum class GatewayMessageType(val wireValue: String) {
    REQUEST("request"),
    RESPONSE("response"),
    EVENT("event");
}

enum class ProtocolDeviceKind(val wireValue: String) {
    DESKTOP("desktop"),
    MOBILE("mobile");
}

enum class ProtocolSourceKind(val wireValue: String) {
    COMPONENT("component"),
    PLUGIN("plugin"),
    SYSTEM("system");
}

data class ProtocolDeviceIdentity(
    val deviceId: String,
    val displayName: String,
    val kind: ProtocolDeviceKind,
    val platform: String,
    val architecture: String
)

data class ProtocolTrustedSource(
    val schemeId: String?,
    val pageId: String?,
    val componentId: String?,
    val pluginId: String?,
    val kind: ProtocolSourceKind
)

data class PairingRequestContract(
    val verificationCode: String,
    val clientNonce: String,
    val mobileIdentity: ProtocolDeviceIdentity
)

data class PairingResponseContract(
    val desktopIdentity: ProtocolDeviceIdentity,
    val assignedMobileIdentity: ProtocolDeviceIdentity,
    val trustCredential: String,
    val credentialExpiresAtUnixMs: Long
)

data class TrustedConnectRequestContract(
    val trustCredential: String,
    val clientNonce: String,
    val mobileIdentity: ProtocolDeviceIdentity
)

data class JsApiRequestContract(
    val requestId: String,
    val targetDeviceId: String,
    val source: ProtocolTrustedSource,
    val capability: String,
    val payload: JSONObject
)

data class JsApiErrorContract(
    val code: String,
    val message: String,
    val highRisk: Boolean
)

data class JsApiResponseContract(
    val requestId: String,
    val ok: Boolean,
    val error: JsApiErrorContract?,
    val payload: JSONObject?
)

data class SchemeDescriptorContract(
    val version: String,
    val hash: String,
    val totalBytes: Long,
    val hasScheme: Boolean
)

data class LogRecordContract(
    val logId: String,
    val createdAtUnixMs: Long,
    val sourceDeviceId: String,
    val level: String,
    val category: String,
    val message: String,
    val context: JSONObject
)

data class MobileGatewayEnvelope(
    val protocolVersion: Int,
    val messageType: String,
    val messageId: String,
    val correlationId: String?,
    val payload: JSONObject
)

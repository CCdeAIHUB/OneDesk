package cc.onedesk.mobile

import org.json.JSONObject

data class AndroidCapabilityRequest(
    val requestId: String,
    val capability: String,
    val payload: JSONObject,
    val sourceKey: String,
)

data class AndroidCapabilityEnvironment(
    val deviceId: () -> String,
    val activeScheme: () -> JSONObject?,
    val switchSchemePage: (JSONObject) -> Unit,
    val showInAppNotification: (JSONObject) -> Unit,
)

/**
 * 只有在执行器真正注册了处理函数时，能力才允许进入此集合。
 * CapabilityCatalogTest 会将这里与公开目录逐项对照，阻止“声明支持、调用失败”的回归。
 */
object AndroidLocalCapabilityHandlers {
    val ids: Set<String> = setOf(
        "device.identity",
        "device.platform",
        "device.display.list",
        "device.power.status",
        "device.vibrate",
        "file.private.read",
        "file.private.write",
        "file.private.delete",
        "clipboard.read",
        "clipboard.write",
        "notification.inApp",
        "notification.native",
        "process.launch",
        "network.access",
        "sensor.accelerometer",
        "sensor.gyroscope",
        "sensor.orientation",
        "credential.access",
        "scheme.active.get",
        "scheme.page.switch",
        "scheme.cache.status",
        "log.write",
    )
}

/**
 * 这些能力必须先经过 Android 系统授权或系统资源选择器，随后才允许进入真实处理器。
 * 单独维护集合是为了让目录测试能够阻止“标记需要授权，但永远只返回提示”的空实现。
 */
object AndroidConsentCapabilityHandlers {
    val ids: Set<String> = setOf(
        "file.external.read",
        "file.external.write",
        "file.external.delete",
        "camera.access",
        "microphone.access",
        "screen.capture",
        "screen.record",
    )
}

internal object AndroidCapabilityResults {
    fun success(request: AndroidCapabilityRequest, payload: Any? = null): JSONObject =
        base(request, ok = true).put("payload", payload ?: JSONObject.NULL)

    fun error(
        request: AndroidCapabilityRequest,
        errorCode: String,
        message: String,
        module: String,
        recoverable: Boolean,
        suggestion: String? = null,
    ): JSONObject = base(request, ok = false)
        .put("errorCode", errorCode)
        .put("message", message)
        .put("module", module)
        .put("recoverable", recoverable)
        .put("suggestion", suggestion ?: JSONObject.NULL)

    private fun base(request: AndroidCapabilityRequest, ok: Boolean): JSONObject = JSONObject()
        .put("ok", ok)
        .put("requestId", request.requestId)
        .put("targetDeviceId", JSONObject.NULL)
        .put("capability", request.capability)
}

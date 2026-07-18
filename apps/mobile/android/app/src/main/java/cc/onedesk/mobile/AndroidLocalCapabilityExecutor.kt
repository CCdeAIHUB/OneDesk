package cc.onedesk.mobile

import androidx.activity.ComponentActivity
import org.json.JSONObject

class AndroidLocalCapabilityExecutor(
    activity: ComponentActivity,
    private val logs: MobileLogStore,
    environment: AndroidCapabilityEnvironment,
    consentCoordinator: AndroidConsentCoordinator,
) {
    private val deviceProvider = AndroidDeviceCapabilityProvider(activity, environment)
    private val storageProvider = AndroidStorageCapabilityProvider(activity)
    private val systemProvider = AndroidSystemCapabilityProvider(activity, environment)
    private val consentProvider = AndroidConsentCapabilityProvider(activity, consentCoordinator)

    fun execute(request: AndroidCapabilityRequest): JSONObject {
        val registration = AndroidCapabilityCatalog.entries[request.capability]
            ?: return AndroidCapabilityResults.error(
                request,
                "CapabilityNotFound",
                "能力未在 OneDesk 协议目录注册",
                "AndroidJsApi",
                false,
            )
        if (registration.availability == CapabilityAvailability.Unsupported) {
            return AndroidCapabilityResults.error(
                request,
                "CapabilityNotSupported",
                "Android 普通应用不支持该系统能力",
                "AndroidJsApi",
                false,
            )
        }
        if (registration.availability == CapabilityAvailability.Routed) {
            return AndroidCapabilityResults.error(
                request,
                "CapabilityRequiresDesktopGateway",
                "该能力只能通过桌面端网关执行",
                "AndroidJsApi",
                true,
            )
        }
        if (registration.availability == CapabilityAvailability.RequiresUserConsent) {
            return executeWithErrorBoundary(request) { consentProvider.execute(request) }
        }

        return executeWithErrorBoundary(request) {
            when (request.capability.substringBefore('.')) {
                "device", "sensor" -> deviceProvider.execute(request)
                "file", "credential" -> storageProvider.execute(request)
                "clipboard", "notification", "process", "network", "scheme" -> systemProvider.execute(request)
                "log" -> {
                    logs.append(
                        request.payload.optString("level", "Info"),
                        request.payload.optString("category", "JsApi"),
                        request.payload.optString("message", request.payload.toString()),
                    )
                    AndroidCapabilityResults.success(request)
                }
                else -> AndroidCapabilityResults.error(request, "CapabilityHandlerMissing", "Android 本地处理器未注册", "AndroidJsApi", false)
            }
        }
    }

    private inline fun executeWithErrorBoundary(
        request: AndroidCapabilityRequest,
        execute: () -> JSONObject,
    ): JSONObject = try {
        execute()
    } catch (error: Exception) {
            logs.append("Error", "JsApiLocal", "${request.capability}: ${error.message ?: error.javaClass.simpleName}")
            AndroidCapabilityResults.error(
                request,
                "CapabilityExecutionFailed",
                error.message ?: "Android 本地能力执行失败",
                "AndroidJsApi",
                true,
            )
    }
}

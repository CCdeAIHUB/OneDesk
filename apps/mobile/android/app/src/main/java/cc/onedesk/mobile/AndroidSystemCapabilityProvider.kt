package cc.onedesk.mobile

import android.Manifest
import android.app.NotificationManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URI

internal class AndroidSystemCapabilityProvider(
    private val context: Context,
    private val environment: AndroidCapabilityEnvironment,
) {
    fun execute(request: AndroidCapabilityRequest): JSONObject = when (request.capability) {
        "clipboard.read" -> readClipboard(request)
        "clipboard.write" -> writeClipboard(request)
        "notification.inApp" -> inAppNotification(request)
        "notification.native" -> nativeNotification(request)
        "process.launch" -> launch(request)
        "network.access" -> network(request)
        "scheme.active.get" -> activeScheme(request)
        "scheme.page.switch" -> switchPage(request)
        "scheme.cache.status" -> cacheStatus(request)
        else -> AndroidCapabilityResults.error(request, "CapabilityHandlerMissing", "系统能力处理器不存在", "AndroidSystem", false)
    }

    private fun readClipboard(request: AndroidCapabilityRequest): JSONObject {
        val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val text = clipboard.primaryClip?.takeIf { it.itemCount > 0 }
            ?.getItemAt(0)?.coerceToText(context)?.toString().orEmpty()
        return AndroidCapabilityResults.success(request, JSONObject().put("text", text))
    }

    private fun writeClipboard(request: AndroidCapabilityRequest): JSONObject {
        val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        clipboard.setPrimaryClip(ClipData.newPlainText("OneDesk", request.payload.optString("text")))
        return AndroidCapabilityResults.success(request)
    }

    private fun inAppNotification(request: AndroidCapabilityRequest): JSONObject {
        environment.showInAppNotification(JSONObject(request.payload.toString()))
        return AndroidCapabilityResults.success(request)
    }

    private fun nativeNotification(request: AndroidCapabilityRequest): JSONObject {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            return AndroidCapabilityResults.error(
                request,
                "AndroidPermissionRequired",
                "系统通知权限尚未授予",
                "AndroidNotification",
                true,
                "请在系统设置中允许 OneDesk 发送通知",
            )
        }
        val notification = NotificationCompat.Builder(context, "onedesk-events")
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle(request.payload.optString("title", "OneDesk"))
            .setContentText(request.payload.optString("message", "OneDesk 动作已触发"))
            .setAutoCancel(true)
            .build()
        (context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager)
            .notify(request.requestId.hashCode(), notification)
        return AndroidCapabilityResults.success(request)
    }

    private fun launch(request: AndroidCapabilityRequest): JSONObject {
        val raw = request.payload.optString("uri")
        val uri = runCatching { Uri.parse(raw) }.getOrNull()
            ?: return AndroidCapabilityResults.error(request, "InvalidLaunchUri", "启动 URI 无效", "AndroidProcess", false)
        if (uri.scheme.isNullOrBlank()) {
            return AndroidCapabilityResults.error(request, "InvalidLaunchUri", "启动 URI 必须包含 scheme", "AndroidProcess", false)
        }
        val intent = Intent(Intent.ACTION_VIEW, uri).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        if (intent.resolveActivity(context.packageManager) == null) {
            return AndroidCapabilityResults.error(request, "LaunchTargetNotFound", "没有应用可以处理该 URI", "AndroidProcess", true)
        }
        context.startActivity(intent)
        return AndroidCapabilityResults.success(request)
    }

    private fun network(request: AndroidCapabilityRequest): JSONObject {
        val rawUrl = request.payload.optString("url")
        val uri = runCatching { URI(rawUrl) }.getOrNull()
            ?: return AndroidCapabilityResults.error(request, "InvalidNetworkUrl", "网络请求只允许绝对 HTTP/HTTPS 地址", "AndroidNetwork", false)
        if (uri.scheme !in setOf("http", "https") || uri.host.isNullOrBlank()) {
            return AndroidCapabilityResults.error(request, "InvalidNetworkUrl", "网络请求只允许绝对 HTTP/HTTPS 地址", "AndroidNetwork", false)
        }
        val connection = uri.toURL().openConnection() as HttpURLConnection
        return try {
            connection.requestMethod = request.payload.optString("method", "GET").uppercase()
            connection.connectTimeout = request.payload.optInt("connectTimeoutMs", 10_000).coerceIn(1_000, 30_000)
            connection.readTimeout = request.payload.optInt("readTimeoutMs", 15_000).coerceIn(1_000, 60_000)
            connection.instanceFollowRedirects = false
            request.payload.optJSONObject("headers")?.let { headers ->
                headers.keys().forEach { name -> connection.setRequestProperty(name, headers.optString(name)) }
            }
            val body = request.payload.optString("body")
            if (body.isNotEmpty() && connection.requestMethod in setOf("POST", "PUT", "PATCH")) {
                connection.doOutput = true
                connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
            }
            val status = connection.responseCode
            val stream = if (status >= 400) connection.errorStream else connection.inputStream
            val responseBody = stream?.use { input ->
                val bytes = input.readNBytes(256_001)
                if (bytes.size > 256_000) {
                    return AndroidCapabilityResults.error(request, "NetworkResponseTooLarge", "网络响应超过 256000 字节上限", "AndroidNetwork", true)
                }
                String(bytes, Charsets.UTF_8)
            }.orEmpty()
            val headers = JSONObject()
            connection.headerFields.filterKeys { it != null }.forEach { (name, values) -> headers.put(name, values.joinToString(", ")) }
            AndroidCapabilityResults.success(
                request,
                JSONObject().put("status", status).put("headers", headers).put("body", responseBody),
            )
        } finally {
            connection.disconnect()
        }
    }

    private fun activeScheme(request: AndroidCapabilityRequest): JSONObject {
        val scheme = environment.activeScheme()
            ?: return AndroidCapabilityResults.error(request, "SchemeCacheMissing", "当前设备没有已缓存方案", "AndroidScheme", true)
        return AndroidCapabilityResults.success(request, scheme)
    }

    private fun switchPage(request: AndroidCapabilityRequest): JSONObject {
        environment.switchSchemePage(JSONObject(request.payload.toString()))
        return AndroidCapabilityResults.success(request)
    }

    private fun cacheStatus(request: AndroidCapabilityRequest): JSONObject {
        val scheme = environment.activeScheme()
            ?: return AndroidCapabilityResults.success(request, JSONObject().put("cached", false))
        return AndroidCapabilityResults.success(
            request,
            JSONObject()
                .put("cached", true)
                .put("desktopId", scheme.optString("desktopId"))
                .put("version", scheme.optString("version"))
                .put("hash", scheme.optString("hash")),
        )
    }
}

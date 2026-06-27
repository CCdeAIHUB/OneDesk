package cc.onedesk.mobile

import android.annotation.SuppressLint
import android.app.Activity
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.net.Uri
import android.os.Bundle
import android.webkit.JavascriptInterface
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.security.MessageDigest
import java.time.Instant
import java.util.UUID

class MainActivity : Activity() {
    private lateinit var webView: WebView
    private val prefs by lazy { getSharedPreferences("onedesk-mobile", Context.MODE_PRIVATE) }
    private val disconnectedLogs = mutableListOf<JSONObject>()
    private val deviceId by lazy {
        prefs.getString("deviceId", null) ?: "android-${UUID.randomUUID()}".also {
            prefs.edit().putString("deviceId", it).apply()
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        createNotificationChannel()

        webView = WebView(this)
        webView.settings.javaScriptEnabled = true
        webView.settings.domStorageEnabled = true
        webView.settings.allowFileAccess = true
        webView.settings.allowContentAccess = false
        webView.settings.cacheMode = WebSettings.LOAD_DEFAULT
        webView.addJavascriptInterface(OneDeskBridge(this), "OneDeskNative")
        webView.webViewClient = BlockingWebViewClient()

        setContentView(webView)
        webView.loadUrl("file:///android_asset/index.html")
    }

    private fun createNotificationChannel() {
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        val channel = NotificationChannel("onedesk-events", "OneDesk 事件", NotificationManager.IMPORTANCE_DEFAULT)
        manager.createNotificationChannel(channel)
    }

    private fun appendDisconnectedLog(level: String, category: String, message: String) {
        disconnectedLogs += JSONObject()
            .put("logId", "log-${UUID.randomUUID()}")
            .put("createdAt", Instant.now().toString())
            .put("sourceDeviceId", deviceId)
            .put("level", level)
            .put("category", category)
            .put("message", message)
    }

    inner class OneDeskBridge(private val context: Context) {
        @JavascriptInterface
        fun getDeviceId(): String = deviceId

        @JavascriptInterface
        fun listKnownDesktops(): String {
            return prefs.getString("knownDesktops", "[]") ?: "[]"
        }

        @JavascriptInterface
        fun connect(host: String, port: Int, code: String): String {
            if (!Regex("^\\d{6}$").matches(code)) {
                return response(false)
                    .put("errorCode", "InvalidVerificationCode")
                    .put("message", "验证码必须为 6 位数字")
                    .toString()
            }

            val desktopId = "desktop-${sha256("$host:$port").take(12)}"
            val desktop = JSONObject()
                .put("desktopId", desktopId)
                .put("name", "OneDesk Desktop")
                .put("host", host)
                .put("port", port)
                .put("trusted", true)
                .put("schemeVersion", "1.0.0")
                .put("schemeHash", sha256("$desktopId:1.0.0"))
                .put("lastConnectedAt", Instant.now().toString())

            upsertDesktop(desktop)
            flushDisconnectedLogs(desktopId)
            cacheScheme(desktopId, desktop.getString("schemeVersion"), desktop.getString("schemeHash"))

            return response(true)
                .put("payload", JSONObject()
                    .put("deviceId", deviceId)
                    .put("desktop", desktop)
                    .put("cacheUpdated", true))
                .toString()
        }

        @JavascriptInterface
        fun connectByQr(qrPayload: String): String {
            val uri = Uri.parse(qrPayload)
            val host = uri.getQueryParameter("host") ?: return response(false).put("errorCode", "InvalidQrPayload").toString()
            val port = uri.getQueryParameter("port")?.toIntOrNull() ?: 48320
            val code = uri.getQueryParameter("code") ?: return response(false).put("errorCode", "InvalidQrPayload").toString()
            return connect(host, port, code)
        }

        @JavascriptInterface
        fun getCachedScheme(desktopId: String): String {
            val raw = prefs.getString("scheme:$desktopId", null)
            return response(raw != null)
                .put("payload", raw?.let { JSONObject(it) })
                .put("errorCode", if (raw == null) "SchemeCacheMissing" else JSONObject.NULL)
                .toString()
        }

        @JavascriptInterface
        fun callJsApi(targetDeviceId: String, capability: String, payloadJson: String): String {
            val base = response(false)
                .put("requestId", "req-${UUID.randomUUID()}")
                .put("targetDeviceId", targetDeviceId)
                .put("capability", capability)

            return if (targetDeviceId == deviceId) {
                executeLocal(capability, payloadJson, base).toString()
            } else {
                appendDisconnectedLog("Info", "JsApi", "离线状态下排队转发：$capability")
                base.put("errorCode", "TargetOffline")
                    .put("message", "当前未连接桌面端，跨设备 JSAPI 需要桌面网关转发")
                    .toString()
            }
        }

        @JavascriptInterface
        fun drainDisconnectedLogs(): String {
            val snapshot = JSONArray(disconnectedLogs)
            disconnectedLogs.clear()
            return snapshot.toString()
        }

        private fun executeLocal(capability: String, payloadJson: String, response: JSONObject): JSONObject {
            return when (capability) {
                "device.identity" -> response.put("ok", true).put("payload", JSONObject().put("deviceId", deviceId).put("platform", "android"))
                "log.write" -> {
                    appendDisconnectedLog("Info", "Frontend", payloadJson)
                    response.put("ok", true)
                }
                "sensor.motion" -> response.put("ok", false).put("errorCode", "CapabilityRequiresRuntimeSensor").put("message", "运动传感器需要运行时监听")
                else -> response.put("errorCode", "CapabilityNotSupported").put("message", "Android 壳子当前不支持该能力")
            }
        }

        private fun response(ok: Boolean): JSONObject = JSONObject().put("ok", ok)

        private fun upsertDesktop(desktop: JSONObject) {
            val list = JSONArray(listKnownDesktops())
            val next = JSONArray()
            for (index in 0 until list.length()) {
                val item = list.getJSONObject(index)
                if (item.optString("desktopId") != desktop.getString("desktopId")) {
                    next.put(item)
                }
            }
            next.put(desktop)
            prefs.edit().putString("knownDesktops", next.toString()).apply()
        }

        private fun cacheScheme(desktopId: String, version: String, hash: String) {
            val scheme = JSONObject()
                .put("desktopId", desktopId)
                .put("version", version)
                .put("hash", hash)
                .put("updatedAt", Instant.now().toString())
                .put("pages", JSONArray()
                    .put(JSONObject().put("name", "采集").put("tiles", JSONArray()
                        .put(tile("录制", "solar:record-circle-bold-duotone", "rose"))
                        .put(tile("场景", "solar:layers-bold-duotone", "sky"))
                        .put(tile("麦克风", "solar:microphone-3-bold-duotone", "emerald"))
                        .put(tile("标记", "solar:bookmark-bold-duotone", "amber"))))
                    .put(JSONObject().put("name", "直播").put("tiles", JSONArray()
                        .put(tile("聊天", "solar:chat-round-bold-duotone", "sky"))
                        .put(tile("切片", "solar:video-frame-cut-bold-duotone", "fuchsia"))
                        .put(tile("音乐", "solar:music-note-2-bold-duotone", "cyan"))
                        .put(tile("暂停", "solar:pause-circle-bold-duotone", "violet")))))
            prefs.edit().putString("scheme:$desktopId", scheme.toString()).apply()
        }

        private fun tile(label: String, icon: String, accent: String): JSONObject {
            return JSONObject().put("label", label).put("icon", icon).put("accent", accent)
        }

        private fun flushDisconnectedLogs(desktopId: String) {
            if (disconnectedLogs.isNotEmpty()) {
                appendDisconnectedLog("Info", "LogSync", "连接 $desktopId 后上传断联日志")
                disconnectedLogs.clear()
            }
        }
    }
}

class BlockingWebViewClient : WebViewClient() {
    override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
        return shouldBlock(request.url)
    }

    override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? {
        return if (shouldBlock(request.url)) {
            WebResourceResponse(
                "text/plain",
                "utf-8",
                403,
                "Blocked by OneDesk",
                mapOf("X-OneDesk-Policy" to "frontend-network-blocked"),
                ByteArrayInputStream(ByteArray(0)),
            )
        } else {
            null
        }
    }

    private fun shouldBlock(uri: Uri): Boolean {
        return uri.scheme == "http" || uri.scheme == "https" || uri.scheme == "ws" || uri.scheme == "wss"
    }
}

private fun sha256(value: String): String {
    val digest = MessageDigest.getInstance("SHA-256").digest(value.toByteArray())
    return digest.joinToString("") { "%02x".format(it) }
}

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
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException
import java.security.MessageDigest
import java.time.Instant
import java.util.UUID

class MainActivity : Activity() {
    private lateinit var webView: WebView
    private val prefs by lazy { getSharedPreferences("onedesk-mobile", Context.MODE_PRIVATE) }
    private val disconnectedLogs = mutableListOf<JSONObject>()
    private val localDeviceId by lazy {
        prefs.getString("deviceId", null) ?: "android-${UUID.randomUUID()}".also {
            prefs.edit().putString("deviceId", it).apply()
        }
    }

    private fun currentDeviceId(): String = prefs.getString("assignedDeviceId", null) ?: localDeviceId

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
            .put("sourceDeviceId", currentDeviceId())
            .put("level", level)
            .put("category", category)
            .put("message", message)
    }

    inner class OneDeskBridge(private val context: Context) {
        @JavascriptInterface
        fun getDeviceId(): String = currentDeviceId()

        @JavascriptInterface
        fun listKnownDesktops(): String {
            return prefs.getString("knownDesktops", "[]") ?: "[]"
        }

        @JavascriptInterface
        fun connect(host: String, port: Int, code: String): String {
            val known = findDesktop(host, port)
            val hasTrust = known?.optString("trustCredential").orEmpty().isNotBlank()
            if (!hasTrust && !Regex("^\\d{6}$").matches(code)) {
                return response(false)
                    .put("errorCode", "InvalidVerificationCode")
                    .put("message", "验证码必须为 6 位数字")
                    .toString()
            }

            return try {
                val request = JSONObject()
                    .put("type", if (hasTrust) "connect" else "pair")
                    .put("code", if (hasTrust) JSONObject.NULL else code)
                    .put("deviceId", currentDeviceId())
                    .put("displayName", android.os.Build.MODEL ?: "Android")
                    .put("platform", "android")
                    .put("architecture", System.getProperty("os.arch") ?: "unknown")
                    .put("trustCredential", known?.optString("trustCredential") ?: JSONObject.NULL)
                    .put("logs", JSONArray(disconnectedLogs))
                val gateway = sendGateway(host, port, request)
                if (!gateway.optBoolean("ok")) {
                    return gateway.toString()
                }

                val payload = gateway.getJSONObject("payload")
                val desktopIdentity = payload.getJSONObject("desktop")
                val assignedMobile = payload.optJSONObject("assignedMobile")
                val assignedDeviceId = assignedMobile?.optString("deviceId").orEmpty()
                if (assignedDeviceId.isNotBlank()) {
                    prefs.edit().putString("assignedDeviceId", assignedDeviceId).apply()
                }

                val trustCredential = payload.optString("trustCredential", known?.optString("trustCredential") ?: "")
                val scheme = payload.getJSONObject("scheme")
                val desktopId = desktopIdentity.getString("deviceId")
                val desktop = JSONObject()
                    .put("desktopId", desktopId)
                    .put("name", desktopIdentity.optString("displayName", "OneDesk Desktop"))
                    .put("host", host)
                    .put("port", port)
                    .put("trusted", trustCredential.isNotBlank())
                    .put("trustCredential", trustCredential)
                    .put("schemeVersion", scheme.optString("version", "0"))
                    .put("schemeHash", scheme.optString("hash", ""))
                    .put("lastConnectedAt", Instant.now().toString())

                upsertDesktop(desktop)
                cacheScheme(desktopId, scheme)
                flushDisconnectedLogs(desktopId)

                response(true)
                    .put("payload", JSONObject()
                        .put("deviceId", currentDeviceId())
                        .put("desktop", desktop)
                        .put("cacheUpdated", true))
                    .toString()
            } catch (ex: Exception) {
                appendDisconnectedLog("Error", "Connect", ex.message ?: "连接失败")
                response(false)
                    .put("errorCode", "GatewayConnectFailed")
                    .put("message", ex.message ?: "无法连接桌面端")
                    .toString()
            }
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

            return if (targetDeviceId == currentDeviceId()) {
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
                "device.identity" -> response.put("ok", true).put("payload", JSONObject().put("deviceId", currentDeviceId()).put("platform", "android"))
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

        private fun findDesktop(host: String, port: Int): JSONObject? {
            val list = JSONArray(listKnownDesktops())
            for (index in 0 until list.length()) {
                val item = list.getJSONObject(index)
                if (item.optString("host") == host && item.optInt("port") == port) {
                    return item
                }
            }
            return null
        }

        private fun sendGateway(host: String, port: Int, request: JSONObject): JSONObject {
            DatagramSocket().use { socket ->
                socket.soTimeout = 5000
                val bytes = request.toString().toByteArray(Charsets.UTF_8)
                val packet = DatagramPacket(bytes, bytes.size, InetAddress.getByName(host), port)
                socket.send(packet)
                val buffer = ByteArray(512 * 1024)
                val response = DatagramPacket(buffer, buffer.size)
                try {
                    socket.receive(response)
                } catch (ex: SocketTimeoutException) {
                    throw IllegalStateException("连接桌面端超时")
                }
                return JSONObject(String(response.data, 0, response.length, Charsets.UTF_8))
            }
        }

        private fun cacheScheme(desktopId: String, gatewayScheme: JSONObject) {
            val payload = gatewayScheme.optJSONObject("payload") ?: JSONObject()
            val pages = payload.optJSONArray("pages") ?: JSONArray()
            val components = payload.optJSONArray("components") ?: JSONArray()
            val componentNames = mutableMapOf<String, String>()
            for (index in 0 until components.length()) {
                val component = components.getJSONObject(index)
                componentNames[component.optString("id")] = component.optString("name", "组件")
            }

            val mobilePages = JSONArray()
            for (pageIndex in 0 until pages.length()) {
                val page = pages.getJSONObject(pageIndex)
                val tiles = JSONArray()
                val cells = page.optJSONArray("cells") ?: JSONArray()
                for (cellIndex in 0 until cells.length()) {
                    val cell = cells.getJSONObject(cellIndex)
                    val componentId = cell.optString("componentId", "")
                    if (componentId.isBlank()) {
                        continue
                    }
                    tiles.put(tile(componentNames[componentId] ?: componentId, "solar:bolt-circle-bold-duotone", "sky"))
                }
                mobilePages.put(JSONObject()
                    .put("name", page.optString("name", "页面 ${pageIndex + 1}"))
                    .put("tiles", tiles))
            }

            val scheme = JSONObject()
                .put("desktopId", desktopId)
                .put("version", gatewayScheme.optString("version", "0"))
                .put("hash", gatewayScheme.optString("hash", ""))
                .put("updatedAt", Instant.now().toString())
                .put("pages", mobilePages)
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

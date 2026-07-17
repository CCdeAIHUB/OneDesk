package cc.onedesk.mobile

import android.annotation.SuppressLint
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.pm.ActivityInfo
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.util.Log
import android.webkit.ConsoleMessage
import android.webkit.JavascriptInterface
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.FrameLayout
import androidx.activity.ComponentActivity
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.time.Instant
import java.util.UUID

class MainActivity : ComponentActivity() {
    companion object {
        private const val CACHE_SCHEMA_VERSION = 4
    }

    private lateinit var root: FrameLayout
    private lateinit var webView: WebView
    private lateinit var logs: MobileLogStore
    private lateinit var gateway: MobileGatewayClient
    private lateinit var schemeCache: SchemeCacheService
    private lateinit var scanner: QrScannerController
    private val prefs by lazy { getSharedPreferences("onedesk-mobile", Context.MODE_PRIVATE) }
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
        clearOutdatedCache()
        enableImmersiveMode()

        logs = MobileLogStore(prefs) { currentDeviceId() }
        gateway = MobileGatewayClient(
            deviceId = { currentDeviceId() },
            logs = logs,
            onSchemeEvent = { desktop, descriptor, eventId -> handleSchemeEvent(desktop, descriptor, eventId) },
            onJsApiEvent = { capability, payload, requestId -> executeLocalCapability(capability, payload.toString(), requestId) },
        )
        schemeCache = SchemeCacheService(this, prefs, gateway, logs)

        root = FrameLayout(this)
        webView = WebView(this)
        root.addView(webView, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        scanner = QrScannerController(this, root) { payload, error -> notifyQrScanResult(payload, error) }

        webView.settings.javaScriptEnabled = true
        webView.settings.domStorageEnabled = true
        webView.settings.allowFileAccess = true
        webView.settings.allowContentAccess = false
        webView.settings.cacheMode = WebSettings.LOAD_DEFAULT
        webView.addJavascriptInterface(OneDeskBridge(), "OneDeskNative")
        webView.webChromeClient = object : WebChromeClient() {
            override fun onConsoleMessage(consoleMessage: ConsoleMessage): Boolean {
                Log.d("OneDeskMobileWeb", "${consoleMessage.message()} @ ${consoleMessage.sourceId()}:${consoleMessage.lineNumber()}")
                return true
            }
        }
        webView.webViewClient = BlockingWebViewClient(this)
        setContentView(root)
        webView.loadUrl("file:///android_asset/index.html")
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) enableImmersiveMode()
    }

    override fun onDestroy() {
        scanner.destroy()
        gateway.stopSubscription()
        logs.setOnlineSink(null)
        webView.removeJavascriptInterface("OneDeskNative")
        webView.destroy()
        super.onDestroy()
    }

    private fun enableImmersiveMode() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            window.attributes = window.attributes.apply {
                layoutInDisplayCutoutMode = android.view.WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
            }
        }
        WindowCompat.setDecorFitsSystemWindows(window, false)
        WindowInsetsControllerCompat(window, window.decorView).apply {
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            hide(WindowInsetsCompat.Type.systemBars())
        }
    }

    private fun createNotificationChannel() {
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.createNotificationChannel(NotificationChannel("onedesk-events", "OneDesk 事件", NotificationManager.IMPORTANCE_DEFAULT))
    }

    private fun clearOutdatedCache() {
        val currentVersion = prefs.getInt("cacheSchemaVersion", 0)
        if (currentVersion >= CACHE_SCHEMA_VERSION) return
        val editor = prefs.edit()
        for (key in prefs.all.keys) {
            if (key.startsWith("scheme:")) editor.remove(key)
            if (currentVersion < 3 && key == "knownDesktops") editor.remove(key)
        }
        editor.putInt("cacheSchemaVersion", CACHE_SCHEMA_VERSION).apply()
        java.io.File(filesDir, "scheme-assets").deleteRecursively()
    }

    private fun handleSchemeEvent(desktop: JSONObject, descriptor: JSONObject, eventId: String): Boolean {
        return try {
            val result = schemeCache.downloadAndCache(desktop, descriptor)
            updateDesktopScheme(desktop.getString("desktopId"), result)
            runOnUiThread {
                webView.evaluateJavascript(
                    "window.__oneDeskHandleSchemeUpdated && window.__oneDeskHandleSchemeUpdated(${JSONObject.quote(desktop.getString("desktopId"))}, ${JSONObject.quote(result.version)}, ${JSONObject.quote(result.hash)});",
                    null,
                )
            }
            logs.append("Info", "SchemePush", "已接收方案更新 $eventId")
            true
        } catch (error: Exception) {
            logs.append("Error", "SchemePush", error.message ?: "方案推送缓存失败")
            false
        }
    }

    private fun notifyQrScanResult(payload: String?, error: String?) {
        runOnUiThread {
            webView.evaluateJavascript(
                "window.__oneDeskHandleQrScan && window.__oneDeskHandleQrScan(${payload?.let(JSONObject::quote) ?: "null"}, ${error?.let(JSONObject::quote) ?: "null"});",
                null,
            )
        }
    }

    inner class OneDeskBridge {
        @JavascriptInterface
        fun getDeviceId(): String = currentDeviceId()

        @JavascriptInterface
        fun listKnownDesktops(): String = prefs.getString("knownDesktops", "[]") ?: "[]"

        @JavascriptInterface
        fun startQrScan(): String {
            runOnUiThread { scanner.start() }
            return response(true).put("payload", JSONObject().put("started", true)).toString()
        }

        @JavascriptInterface
        fun cancelQrScan(): String {
            runOnUiThread { scanner.cancel() }
            return response(true).toString()
        }

        @JavascriptInterface
        fun setDisplayRatio(width: Double, height: Double): String {
            if (!width.isFinite() || !height.isFinite() || width <= 0 || height <= 0) {
                return response(false).put("errorCode", "InvalidDisplayRatio").put("message", "页面宽高比无效").toString()
            }
            runOnUiThread {
                requestedOrientation = if (width > height) {
                    ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                } else {
                    ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT
                }
            }
            return response(true).put("payload", JSONObject().put("width", width).put("height", height)).toString()
        }

        @JavascriptInterface
        fun connect(host: String, port: Int, code: String): String {
            val normalizedHost = host.trim()
            val normalizedPort = port.coerceIn(1, 65535)
            val known = findDesktop(normalizedHost, normalizedPort)
            val hasTrust = known?.optString("trustCredential").orEmpty().isNotBlank()
            if (!hasTrust && !Regex("^\\d{6}$").matches(code)) {
                return response(false).put("errorCode", "InvalidVerificationCode").put("message", "验证码必须为 6 位数字").toString()
            }
            return connectInternal(normalizedHost, normalizedPort, code, known)
        }

        @JavascriptInterface
        fun connectByQr(qrPayload: String): String {
            val uri = runCatching { Uri.parse(qrPayload) }.getOrNull()
                ?: return response(false).put("errorCode", "InvalidQrPayload").put("message", "二维码内容无效").toString()
            if (uri.scheme != "onedesk" || uri.host != "pair") {
                return response(false).put("errorCode", "InvalidQrPayload").put("message", "这不是 OneDesk 配对二维码").toString()
            }
            val host = uri.getQueryParameter("host")?.trim().orEmpty()
            val port = uri.getQueryParameter("port")?.toIntOrNull() ?: 48320
            val code = uri.getQueryParameter("code").orEmpty()
            if (host.isBlank()) return response(false).put("errorCode", "InvalidQrPayload").put("message", "二维码缺少桌面端 IP").toString()
            return connect(host, port, code)
        }

        @JavascriptInterface
        fun getCachedScheme(desktopId: String): String {
            val cache = schemeCache.get(desktopId)
            return response(cache != null)
                .put("payload", cache ?: JSONObject.NULL)
                .put("errorCode", if (cache == null) "SchemeCacheMissing" else JSONObject.NULL)
                .toString()
        }

        @JavascriptInterface
        fun refreshScheme(desktopId: String): String {
            val desktop = findDesktopById(desktopId)
                ?: return response(false).put("errorCode", "DesktopNotFound").put("message", "未找到该桌面端信任记录").toString()
            val trustCredential = desktop.optString("trustCredential")
            if (trustCredential.isBlank()) return response(false).put("errorCode", "TrustCredentialMissing").put("message", "该桌面端缺少信任凭据").toString()
            return try {
                val gatewayResponse = gateway.request(
                    desktop.getString("host"),
                    desktop.optInt("port", 48320),
                    gateway.authorizedRequest("scheme", trustCredential),
                )
                if (!gatewayResponse.optBoolean("ok")) return gatewayResponse.toString()
                val descriptor = gatewayResponse.getJSONObject("payload").getJSONObject("scheme")
                val result = schemeCache.downloadAndCache(desktop, descriptor)
                updateDesktopScheme(desktopId, result)
                response(true).put("payload", JSONObject()
                    .put("cacheUpdated", result.updated)
                    .put("hasScheme", result.hasScheme)
                    .put("version", result.version)
                    .put("hash", result.hash)).toString()
            } catch (error: Exception) {
                logs.append("Error", "SchemeRefresh", error.message ?: "刷新方案失败")
                response(false).put("errorCode", "SchemeRefreshFailed").put("message", error.message ?: "刷新方案失败").toString()
            }
        }

        @JavascriptInterface
        fun callJsApi(
            targetDeviceId: String,
            capability: String,
            payloadJson: String,
            schemeId: String,
            pageId: String,
            componentId: String,
        ): String {
            val requestId = "req-${UUID.randomUUID()}"
            if (targetDeviceId == currentDeviceId()) {
                return executeLocalCapability(capability, payloadJson, requestId).toString()
            }
            val desktopId = prefs.getString("activeDesktopId", null)
                ?: return response(false).put("requestId", requestId).put("errorCode", "DesktopOffline").put("message", "当前未连接桌面端").toString()
            val desktop = findDesktopById(desktopId)
                ?: return response(false).put("requestId", requestId).put("errorCode", "DesktopNotFound").put("message", "桌面端信任记录不存在").toString()
            return try {
                val payload = runCatching { JSONObject(payloadJson) }.getOrElse { JSONObject() }
                gateway.request(
                    desktop.getString("host"),
                    desktop.optInt("port", 48320),
                    gateway.authorizedRequest("jsapi", desktop.getString("trustCredential"))
                        .put("requestId", requestId)
                        .put("schemeId", schemeId)
                        .put("pageId", pageId)
                        .put("componentId", componentId)
                        .put("targetDeviceId", targetDeviceId)
                        .put("capability", capability)
                        .put("payload", payload),
                ).toString()
            } catch (error: Exception) {
                logs.append("Error", "JsApi", error.message ?: "JSAPI 调用失败")
                response(false).put("requestId", requestId).put("errorCode", "GatewayConnectFailed").put("message", error.message ?: "JSAPI 调用失败").toString()
            }
        }

        @JavascriptInterface
        fun drainDisconnectedLogs(): String {
            val snapshot = logs.snapshot()
            logs.clear()
            return snapshot.toString()
        }

        private fun connectInternal(host: String, port: Int, code: String, known: JSONObject?): String {
            val hasTrust = known?.optString("trustCredential").orEmpty().isNotBlank()
            return try {
                val request = JSONObject()
                    .put("type", if (hasTrust) "connect" else "pair")
                    .put("code", if (hasTrust) JSONObject.NULL else code)
                    .put("deviceId", currentDeviceId())
                    .put("displayName", android.os.Build.MODEL ?: "Android")
                    .put("platform", "android")
                    .put("architecture", System.getProperty("os.arch") ?: "unknown")
                    .put("trustCredential", known?.optString("trustCredential") ?: JSONObject.NULL)
                    .put("logs", logs.snapshot())
                val gatewayResponse = gateway.request(host, port, request)
                if (!gatewayResponse.optBoolean("ok")) return gatewayResponse.toString()
                val payload = gatewayResponse.getJSONObject("payload")
                val desktopIdentity = payload.getJSONObject("desktop")
                val assignedDeviceId = payload.optJSONObject("assignedMobile")?.optString("deviceId").orEmpty()
                if (assignedDeviceId.isNotBlank()) prefs.edit().putString("assignedDeviceId", assignedDeviceId).commit()
                val trustCredential = payload.optString("trustCredential", known?.optString("trustCredential") ?: "")
                val descriptor = payload.getJSONObject("scheme")
                val desktopId = desktopIdentity.getString("deviceId")
                val desktop = JSONObject()
                    .put("desktopId", desktopId)
                    .put("name", desktopIdentity.optString("displayName", "OneDesk Desktop"))
                    .put("host", host)
                    .put("port", port)
                    .put("trusted", trustCredential.isNotBlank())
                    .put("trustCredential", trustCredential)
                    .put("schemeVersion", descriptor.optString("version", "0"))
                    .put("schemeHash", descriptor.optString("hash", ""))
                    .put("lastConnectedAt", Instant.now().toString())
                upsertDesktop(desktop)
                val cacheResult = schemeCache.downloadAndCache(desktop, descriptor)
                updateDesktopScheme(desktopId, cacheResult)
                prefs.edit().putString("activeDesktopId", desktopId).apply()
                logs.clear()
                gateway.startSubscription(desktop)
                logs.setOnlineSink { entry -> gateway.uploadLog(desktop, entry) }
                response(true).put("payload", JSONObject()
                    .put("deviceId", currentDeviceId())
                    .put("desktop", desktop)
                    .put("cacheUpdated", cacheResult.updated)
                    .put("hasScheme", cacheResult.hasScheme)).toString()
            } catch (error: Exception) {
                logs.append("Error", "Connect", error.message ?: "连接失败")
                response(false).put("errorCode", "GatewayConnectFailed").put("message", error.message ?: "无法连接桌面端").toString()
            }
        }

    }

    // 移动端本地能力只能由原生壳子执行；来自桌面网关和本机前端的调用统一经过此入口。
    private fun executeLocalCapability(capability: String, payloadJson: String, requestId: String): JSONObject {
        val payload = runCatching { JSONObject(payloadJson) }.getOrElse { JSONObject() }
        val base = response(false)
            .put("requestId", requestId)
            .put("targetDeviceId", currentDeviceId())
            .put("capability", capability)
        return try {
            when (capability) {
                "device.identity" -> base.put("ok", true).put(
                    "payload",
                    JSONObject()
                        .put("deviceId", currentDeviceId())
                        .put("displayName", Build.MODEL ?: "Android")
                        .put("platform", "android")
                        .put("architecture", System.getProperty("os.arch") ?: "unknown"),
                )
                "device.vibrate" -> {
                    val duration = payload.optLong("durationMs", 80).coerceIn(10, 5_000)
                    val vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                        (getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager).defaultVibrator
                    } else {
                        @Suppress("DEPRECATION")
                        getSystemService(Context.VIBRATOR_SERVICE) as Vibrator
                    }
                    vibrator.vibrate(VibrationEffect.createOneShot(duration, VibrationEffect.DEFAULT_AMPLITUDE))
                    base.put("ok", true).put("payload", JSONObject().put("durationMs", duration))
                }
                "clipboard.read" -> {
                    val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                    val text = clipboard.primaryClip?.getItemAt(0)?.coerceToText(this)?.toString().orEmpty()
                    base.put("ok", true).put("payload", JSONObject().put("text", text))
                }
                "clipboard.write" -> {
                    val text = payload.optString("text")
                    val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                    clipboard.setPrimaryClip(ClipData.newPlainText("OneDesk", text))
                    base.put("ok", true)
                }
                "notification.native", "notification.inApp" -> {
                    val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
                    val notification = android.app.Notification.Builder(this, "onedesk-events")
                        .setSmallIcon(android.R.drawable.ic_dialog_info)
                        .setContentTitle(payload.optString("title", "OneDesk"))
                        .setContentText(payload.optString("message", "OneDesk 动作已触发"))
                        .setAutoCancel(true)
                        .build()
                    manager.notify(requestId.hashCode(), notification)
                    base.put("ok", true)
                }
                "log.write" -> {
                    logs.append(payload.optString("level", "Info"), payload.optString("category", "JsApi"), payload.optString("message", payloadJson))
                    base.put("ok", true)
                }
                else -> base.put("errorCode", "CapabilityNotSupported").put("message", "Android 壳子不支持该能力")
            }
        } catch (error: Exception) {
            logs.append("Error", "JsApiLocal", error.message ?: "移动端能力执行失败")
            base.put("errorCode", "ExecutionFailed").put("message", error.message ?: "移动端能力执行失败")
        }
    }

    private fun response(ok: Boolean): JSONObject = JSONObject().put("ok", ok)

    private fun updateDesktopScheme(desktopId: String, result: SchemeCacheResult) {
        val desktop = findDesktopById(desktopId) ?: return
        desktop.put("schemeVersion", result.version).put("schemeHash", result.hash)
        upsertDesktop(desktop)
    }

    private fun upsertDesktop(desktop: JSONObject) {
        val current = knownDesktops()
        val next = JSONArray()
        for (index in 0 until current.length()) {
            val item = current.optJSONObject(index) ?: continue
            if (item.optString("desktopId") != desktop.optString("desktopId")) next.put(item)
        }
        next.put(desktop)
        prefs.edit().putString("knownDesktops", next.toString()).commit()
    }

    private fun knownDesktops(): JSONArray {
        return try { JSONArray(prefs.getString("knownDesktops", "[]") ?: "[]") } catch (_: Exception) { JSONArray() }
    }

    private fun findDesktop(host: String, port: Int): JSONObject? {
        val list = knownDesktops()
        for (index in 0 until list.length()) {
            val item = list.optJSONObject(index) ?: continue
            if (item.optString("host") == host && item.optInt("port") == port) return item
        }
        return null
    }

    private fun findDesktopById(desktopId: String): JSONObject? {
        val list = knownDesktops()
        for (index in 0 until list.length()) {
            val item = list.optJSONObject(index) ?: continue
            if (item.optString("desktopId") == desktopId) return item
        }
        return null
    }
}

class BlockingWebViewClient(private val context: Context) : WebViewClient() {
    override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean = shouldBlock(request.url)

    override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? {
        val localAsset = openAndroidAsset(request.url)
        if (localAsset != null) return localAsset
        return if (shouldBlock(request.url)) {
            WebResourceResponse("text/plain", "utf-8", 403, "Blocked by OneDesk", mapOf("X-OneDesk-Policy" to "frontend-network-blocked"), ByteArrayInputStream(ByteArray(0)))
        } else null
    }

    override fun onPageFinished(view: WebView, url: String) {
        super.onPageFinished(view, url)
        Log.d("OneDeskMobileWeb", "Frontend loaded: $url")
    }

    private fun openAndroidAsset(uri: Uri): WebResourceResponse? {
        if (uri.scheme != "file") return null
        val path = uri.path ?: return null
        if (!path.startsWith("/android_asset/")) return null
        val assetPath = path.removePrefix("/android_asset/")
        val mimeType = when {
            assetPath.endsWith(".html") -> "text/html"
            assetPath.endsWith(".js") -> "application/javascript"
            assetPath.endsWith(".css") -> "text/css"
            assetPath.endsWith(".json") -> "application/json"
            assetPath.endsWith(".svg") -> "image/svg+xml"
            assetPath.endsWith(".png") -> "image/png"
            assetPath.endsWith(".jpg") || assetPath.endsWith(".jpeg") -> "image/jpeg"
            assetPath.endsWith(".webp") -> "image/webp"
            else -> "application/octet-stream"
        }
        return try { WebResourceResponse(mimeType, "utf-8", context.assets.open(assetPath)) } catch (_: Exception) { null }
    }

    private fun shouldBlock(uri: Uri): Boolean = uri.scheme == "http" || uri.scheme == "https" || uri.scheme == "ws" || uri.scheme == "wss"
}

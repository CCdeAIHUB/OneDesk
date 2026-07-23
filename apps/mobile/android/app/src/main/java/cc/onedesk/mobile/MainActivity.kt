package cc.onedesk.mobile

import android.annotation.SuppressLint
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.content.pm.ActivityInfo
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.webkit.ConsoleMessage
import android.webkit.JavascriptInterface
import android.webkit.WebChromeClient
import android.webkit.WebSettings
import android.webkit.WebView
import android.widget.FrameLayout
import androidx.activity.ComponentActivity
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant
import java.util.UUID

class MainActivity : ComponentActivity() {
    companion object {
        private const val CACHE_SCHEMA_VERSION = 5
    }

    private lateinit var root: FrameLayout
    private lateinit var webView: WebView
    private lateinit var logs: MobileLogStore
    private lateinit var gateway: MobileGatewayClient
    private lateinit var schemeCache: SchemeCacheService
    private lateinit var capabilityExecutor: AndroidLocalCapabilityExecutor
    private lateinit var consentCoordinator: AndroidConsentCoordinator
    private lateinit var trustCredentials: TrustCredentialStore
    private lateinit var knownDesktops: KnownDesktopStore
    private lateinit var deviceTriggers: AndroidDeviceTriggerMonitor
    private lateinit var scanner: QrScannerController
    private val prefs by lazy { getSharedPreferences("onedesk-mobile", Context.MODE_PRIVATE) }
    private val stableDeviceKey by lazy { AndroidDeviceIdentity.stableDeviceKey(this) }
    private val localDeviceId by lazy { "mobile-${stableDeviceKey.substringAfter(':').take(32)}" }

    private fun currentDeviceId(): String = prefs.getString("assignedDeviceId", null) ?: localDeviceId

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        createNotificationChannel()
        trustCredentials = TrustCredentialStore(this)
        logs = MobileLogStore(prefs) { currentDeviceId() }
        knownDesktops = KnownDesktopStore(prefs, trustCredentials, logs)
        knownDesktops.migratePlaintextCredentials()
        clearOutdatedCache()
        enableImmersiveMode()

        gateway = MobileGatewayClient(
            deviceId = { currentDeviceId() },
            stableDeviceKey = { stableDeviceKey },
            logs = logs,
            onSchemeEvent = { desktop, descriptor, eventId -> handleSchemeEvent(desktop, descriptor, eventId) },
            onJsApiEvent = { capability, payload, requestId, sourceKey ->
                // 远端来源已由桌面网关完成权限判定；移动端仍保留来源键用于私有存储隔离。
                executeLocalCapability(capability, payload.toString(), requestId, sourceKey, enforcePermission = false)
            },
        )
        schemeCache = SchemeCacheService(this, prefs, gateway, logs)
        consentCoordinator = AndroidConsentCoordinator(this)
        capabilityExecutor = AndroidLocalCapabilityExecutor(
            this,
            logs,
            AndroidCapabilityEnvironment(
                deviceId = { currentDeviceId() },
                activeScheme = { activeSchemeSnapshot() },
                switchSchemePage = { payload -> dispatchFrontendEvent("__oneDeskHandlePageSwitch", payload) },
                showInAppNotification = { payload -> dispatchFrontendEvent("__oneDeskHandleInAppNotification", payload) },
            ),
            consentCoordinator,
        )
        deviceTriggers = AndroidDeviceTriggerMonitor(this) { triggerId ->
            dispatchFrontendEvent("__oneDeskHandleDeviceTrigger", JSONObject().put("triggerId", triggerId))
        }

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

    override fun onResume() {
        super.onResume()
        if (::deviceTriggers.isInitialized) deviceTriggers.start()
    }

    override fun onPause() {
        if (::deviceTriggers.isInitialized) deviceTriggers.stop()
        super.onPause()
    }

    override fun onDestroy() {
        scanner.destroy()
        deviceTriggers.stop()
        consentCoordinator.close()
        gateway.close()
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
            knownDesktops.updateScheme(desktop.getString("desktopId"), result)
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
        fun listKnownDesktops(): String = knownDesktops.listForFrontend()

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
            val known = knownDesktops.find(normalizedHost, normalizedPort)
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
            val desktop = knownDesktops.findById(desktopId)
                ?: return response(false).put("errorCode", "DesktopNotFound").put("message", "未找到该桌面端信任记录").toString()
            val trustCredential = desktop.optString("trustCredential")
            if (trustCredential.isBlank()) return response(false).put("errorCode", "TrustCredentialMissing").put("message", "该桌面端缺少信任凭据").toString()
            return try {
                val gatewayResponse = gateway.request(
                    desktop.getString("host"),
                    desktop.optInt("port", 48320),
                    gateway.authorizedRequest("scheme", trustCredential),
                    expectedFingerprint = desktop.optString("gatewayFingerprint"),
                )
                if (!gatewayResponse.optBoolean("ok")) return gatewayResponse.toString()
                val descriptor = gatewayResponse.getJSONObject("payload").getJSONObject("scheme")
                val result = schemeCache.downloadAndCache(desktop, descriptor)
                knownDesktops.updateScheme(desktopId, result)
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
                return executeLocalCapability(
                    capability,
                    payloadJson,
                    requestId,
                    "component:$componentId",
                    enforcePermission = true,
                ).toString()
            }
            val desktopId = knownDesktops.activeDesktopId()
                ?: return response(false).put("requestId", requestId).put("errorCode", "DesktopOffline").put("message", "当前未连接桌面端").toString()
            val desktop = knownDesktops.findById(desktopId)
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
                    expectedFingerprint = desktop.optString("gatewayFingerprint"),
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
                    .put("stableDeviceKey", stableDeviceKey)
                    .put("displayName", android.os.Build.MODEL ?: "Android")
                    .put("platform", "android")
                    .put("architecture", System.getProperty("os.arch") ?: "unknown")
                    .put("trustCredential", known?.optString("trustCredential") ?: JSONObject.NULL)
                    .put("logs", logs.snapshot())
                val gatewayResponse = gateway.request(
                    host,
                    port,
                    request,
                    expectedFingerprint = known?.optString("gatewayFingerprint"),
                )
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
                    .put("gatewayFingerprint", gateway.serverFingerprint())
                    .put("schemeVersion", descriptor.optString("version", "0"))
                    .put("schemeHash", descriptor.optString("hash", ""))
                    .put("lastConnectedAt", Instant.now().toString())
                knownDesktops.upsert(desktop)
                val cacheResult = schemeCache.downloadAndCache(desktop, descriptor)
                knownDesktops.updateScheme(desktopId, cacheResult)
                knownDesktops.setActiveDesktopId(desktopId)
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
    private fun executeLocalCapability(
        capability: String,
        payloadJson: String,
        requestId: String,
        sourceKey: String,
        enforcePermission: Boolean,
    ): JSONObject {
        val payload = runCatching { JSONObject(payloadJson) }.getOrElse { JSONObject() }
        if (enforcePermission && !isCapabilityGranted(sourceKey, capability)) {
            return response(false)
                .put("requestId", requestId)
                .put("targetDeviceId", currentDeviceId())
                .put("capability", capability)
                .put("errorCode", "PermissionDenied")
                .put("message", "当前组件未获得该能力授权")
                .put("module", "AndroidPermission")
                .put("recoverable", true)
        }
        return capabilityExecutor.execute(AndroidCapabilityRequest(requestId, capability, payload, sourceKey))
            .put("targetDeviceId", currentDeviceId())
    }

    private fun isCapabilityGranted(sourceKey: String, capability: String): Boolean {
        val grants = mutableMapOf<String, Set<String>>()
        val snapshot = activeSchemeSnapshot() ?: return false
        val rows = snapshot.optJSONArray("permissionGrants") ?: JSONArray()
        for (index in 0 until rows.length()) {
            val row = rows.optJSONObject(index) ?: continue
            val capabilities = buildSet {
                val values = row.optJSONArray("capabilities") ?: JSONArray()
                for (valueIndex in 0 until values.length()) add(values.optString(valueIndex))
            }
            grants[row.optString("sourceKey")] = capabilities
        }
        return SchemePermissionPolicy.isGranted(grants, sourceKey, capability)
    }

    private fun activeSchemeSnapshot(): JSONObject? {
        val desktopId = knownDesktops.activeDesktopId() ?: return null
        return schemeCache.get(desktopId)
    }

    private fun dispatchFrontendEvent(callbackName: String, payload: JSONObject) {
        runOnUiThread {
            webView.evaluateJavascript(
                "window.$callbackName && window.$callbackName(${payload});",
                null,
            )
        }
    }

    private fun response(ok: Boolean): JSONObject = JSONObject().put("ok", ok)

}

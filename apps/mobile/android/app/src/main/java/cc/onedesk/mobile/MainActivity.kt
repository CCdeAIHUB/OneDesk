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
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.time.Instant
import java.util.UUID

class MainActivity : Activity() {
    private lateinit var webView: WebView
    private val disconnectedLogs = mutableListOf<JSONObject>()
    private val deviceId = "android-${UUID.randomUUID()}"

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
        val channel = NotificationChannel(
            "onedesk-events",
            "OneDesk events",
            NotificationManager.IMPORTANCE_DEFAULT,
        )
        manager.createNotificationChannel(channel)
    }

    private fun appendDisconnectedLog(level: String, category: String, message: String) {
        disconnectedLogs += JSONObject()
            .put("logId", "log-${UUID.randomUUID()}")
            .put("createdAt", Instant.now().toEpochMilli())
            .put("sourceDeviceId", deviceId)
            .put("level", level)
            .put("category", category)
            .put("message", message)
    }

    inner class OneDeskBridge(private val context: Context) {
        @JavascriptInterface
        fun callJsApi(targetDeviceId: String, capability: String, payloadJson: String): String {
            val response = JSONObject()
                .put("requestId", "req-${UUID.randomUUID()}")
                .put("targetDeviceId", targetDeviceId)
                .put("capability", capability)

            return if (targetDeviceId == deviceId) {
                executeLocal(capability, payloadJson, response).toString()
            } else {
                appendDisconnectedLog("Info", "JsApi", "Queued JSAPI call for desktop gateway")
                response
                    .put("ok", false)
                    .put("errorCode", "TargetOffline")
                    .put("message", "Desktop QUIC gateway is not connected in this shell skeleton.")
                    .toString()
            }
        }

        @JavascriptInterface
        fun getDeviceId(): String = deviceId

        @JavascriptInterface
        fun drainDisconnectedLogs(): String {
            val snapshot = disconnectedLogs.toList()
            disconnectedLogs.clear()
            return snapshot.toString()
        }

        private fun executeLocal(capability: String, payloadJson: String, response: JSONObject): JSONObject {
            return when (capability) {
                "device.identity" -> response
                    .put("ok", true)
                    .put("payload", JSONObject().put("deviceId", deviceId).put("platform", "android"))

                "log.write" -> {
                    appendDisconnectedLog("Info", "Frontend", payloadJson)
                    response.put("ok", true)
                }

                else -> response
                    .put("ok", false)
                    .put("errorCode", "CapabilityNotSupported")
                    .put("message", "Capability is not supported by this Android shell yet.")
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

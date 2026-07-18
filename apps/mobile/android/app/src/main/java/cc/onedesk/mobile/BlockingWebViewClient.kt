package cc.onedesk.mobile

import android.content.Context
import android.net.Uri
import android.util.Log
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import java.io.ByteArrayInputStream

/**
 * 移动前端只能读取 APK 内的 file 资源；任何 HTTP、WebSocket 等直接网络请求
 * 都在壳子边界被拒绝，网络能力只能通过 OneDesk JSAPI 转发。
 */
class BlockingWebViewClient(private val context: Context) : WebViewClient() {
    override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean = shouldBlock(request.url)

    override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? {
        val localAsset = openAndroidAsset(request.url)
        if (localAsset != null) return localAsset
        return if (shouldBlock(request.url)) {
            WebResourceResponse(
                "text/plain",
                "utf-8",
                403,
                "Blocked by OneDesk",
                mapOf("X-OneDesk-Policy" to "frontend-network-blocked"),
                ByteArrayInputStream(ByteArray(0)),
            )
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
        return try {
            WebResourceResponse(mimeType, "utf-8", context.assets.open(assetPath))
        } catch (error: Exception) {
            Log.e("OneDeskMobileWeb", "无法读取本地前端资源：$assetPath", error)
            null
        }
    }

    private fun shouldBlock(uri: Uri): Boolean =
        uri.scheme == "http" || uri.scheme == "https" || uri.scheme == "ws" || uri.scheme == "wss"
}

package cc.onedesk.mobile

import android.os.Build
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean

class MobileGatewayClient(
    private val deviceId: () -> String,
    private val logs: MobileLogStore,
    private val onSchemeEvent: (desktop: JSONObject, descriptor: JSONObject, eventId: String) -> Boolean,
    private val onJsApiEvent: (capability: String, payload: JSONObject, requestId: String) -> JSONObject,
) {
    private val running = AtomicBoolean(false)
    @Volatile private var subscriptionSocket: DatagramSocket? = null
    @Volatile private var subscriptionThread: Thread? = null

    fun request(host: String, port: Int, request: JSONObject, timeoutMs: Int = 7000): JSONObject {
        request.put("requestId", request.optString("requestId", "req-${UUID.randomUUID()}"))
        DatagramSocket().use { socket ->
            socket.soTimeout = timeoutMs
            send(socket, host, port, request)
            val response = DatagramPacket(ByteArray(64 * 1024), 64 * 1024)
            try {
                socket.receive(response)
            } catch (_: SocketTimeoutException) {
                throw IllegalStateException("连接桌面端超时")
            }
            return JSONObject(String(response.data, 0, response.length, Charsets.UTF_8))
        }
    }

    fun uploadLog(desktop: JSONObject, entry: JSONObject): Boolean {
        val trustCredential = desktop.optString("trustCredential")
        if (trustCredential.isBlank()) return false
        return runCatching {
            request(
                desktop.getString("host"),
                desktop.optInt("port", 48320),
                authorizedRequest("logs", trustCredential).put("logs", org.json.JSONArray().put(entry)),
                timeoutMs = 2500,
            ).optBoolean("ok")
        }.getOrDefault(false)
    }

    fun startSubscription(desktop: JSONObject) {
        stopSubscription()
        val host = desktop.getString("host")
        val port = desktop.optInt("port", 48320)
        val trustCredential = desktop.optString("trustCredential")
        if (trustCredential.isBlank()) return
        running.set(true)
        subscriptionThread = Thread({
            val socket = DatagramSocket()
            subscriptionSocket = socket
            try {
                socket.soTimeout = 15_000
                send(socket, host, port, authorizedRequest("subscribe", trustCredential))
                val first = receive(socket)
                if (!first.optBoolean("ok")) {
                    throw IllegalStateException(first.optString("message", "无法订阅方案更新"))
                }
                while (running.get()) {
                    try {
                        val message = receive(socket)
                        val payload = message.optJSONObject("payload") ?: continue
                        when (payload.optString("eventType")) {
                            "scheme.updated" -> {
                                val descriptor = payload.optJSONObject("scheme") ?: continue
                                val eventId = payload.optString("eventId")
                                if (eventId.isBlank()) continue
                                val cached = onSchemeEvent(desktop, descriptor, eventId)
                                if (cached) {
                                    send(socket, host, port, authorizedRequest("scheme-ack", trustCredential).put("eventId", eventId))
                                }
                            }
                            "jsapi.request" -> {
                                val requestId = payload.optString("requestId")
                                val capability = payload.optString("capability")
                                if (requestId.isBlank() || capability.isBlank()) continue
                                val result = onJsApiEvent(capability, payload.optJSONObject("payload") ?: JSONObject(), requestId)
                                val response = authorizedRequest("jsapi-response", trustCredential)
                                    .put("requestId", requestId)
                                    .put("responseOk", result.optBoolean("ok"))
                                    .put("errorCode", result.opt("errorCode") ?: JSONObject.NULL)
                                    .put("message", result.opt("message") ?: JSONObject.NULL)
                                    .put("payload", result.opt("payload") ?: JSONObject.NULL)
                                send(socket, host, port, response)
                            }
                        }
                    } catch (_: SocketTimeoutException) {
                        send(socket, host, port, authorizedRequest("heartbeat", trustCredential))
                    }
                }
            } catch (error: Exception) {
                if (running.get()) logs.append("Error", "GatewaySubscription", error.message ?: "方案更新订阅中断")
            } finally {
                socket.close()
                if (subscriptionSocket === socket) subscriptionSocket = null
            }
        }, "OneDesk-SchemeSubscription").apply {
            isDaemon = true
            start()
        }
    }

    fun stopSubscription() {
        running.set(false)
        subscriptionSocket?.close()
        subscriptionSocket = null
        subscriptionThread?.interrupt()
        subscriptionThread = null
    }

    fun authorizedRequest(type: String, trustCredential: String): JSONObject {
        return JSONObject()
            .put("type", type)
            .put("requestId", "req-${UUID.randomUUID()}")
            .put("deviceId", deviceId())
            .put("displayName", Build.MODEL ?: "Android")
            .put("platform", "android")
            .put("architecture", System.getProperty("os.arch") ?: "unknown")
            .put("trustCredential", trustCredential)
    }

    private fun receive(socket: DatagramSocket): JSONObject {
        val response = DatagramPacket(ByteArray(64 * 1024), 64 * 1024)
        socket.receive(response)
        return JSONObject(String(response.data, 0, response.length, Charsets.UTF_8))
    }

    private fun send(socket: DatagramSocket, host: String, port: Int, request: JSONObject) {
        val bytes = request.toString().toByteArray(Charsets.UTF_8)
        if (bytes.size > 60 * 1024) throw IllegalArgumentException("网关请求超过 UDP 安全大小")
        socket.send(DatagramPacket(bytes, bytes.size, InetAddress.getByName(host), port))
    }
}

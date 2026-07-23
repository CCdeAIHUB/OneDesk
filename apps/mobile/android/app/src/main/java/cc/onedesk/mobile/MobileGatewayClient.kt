package cc.onedesk.mobile

import android.os.Build
import org.json.JSONArray
import org.json.JSONObject
import java.io.Closeable
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

class MobileGatewayClient(
    private val deviceId: () -> String,
    private val stableDeviceKey: () -> String,
    private val logs: MobileLogStore,
    private val onSchemeEvent: (desktop: JSONObject, descriptor: JSONObject, eventId: String) -> Boolean,
    private val onJsApiEvent: (capability: String, payload: JSONObject, requestId: String, sourceKey: String) -> JSONObject,
) : Closeable {
    private val transportLock = Any()
    private val eventExecutor = Executors.newSingleThreadExecutor { task ->
        Thread(task, "OneDesk-QuicEvents").apply { isDaemon = true }
    }
    private val scheduler = Executors.newSingleThreadScheduledExecutor { task ->
        Thread(task, "OneDesk-QuicHeartbeat").apply { isDaemon = true }
    }
    private val stateMachine = MobileConnectionStateMachine()
    private val running = AtomicBoolean(false)
    @Volatile private var transport: MsQuicNativeTransport? = null
    @Volatile private var endpointKey: String? = null
    @Volatile private var activeDesktop: JSONObject? = null
    @Volatile private var heartbeat: ScheduledFuture<*>? = null
    @Volatile private var reconnectFuture: ScheduledFuture<*>? = null
    @Volatile private var reconnectAttempt = 0

    fun request(
        host: String,
        port: Int,
        request: JSONObject,
        timeoutMs: Int = 7_000,
        expectedFingerprint: String? = null,
    ): JSONObject {
        val requestId = request.optString("requestId").ifBlank { "req-${UUID.randomUUID()}" }
        request.put("requestId", requestId)
        val client = ensureTransport(host, port, expectedFingerprint, timeoutMs)
        val envelope = JSONObject()
            .put("protocolVersion", OneDeskProtocol.PROTOCOL_VERSION)
            .put("messageType", GatewayMessageType.REQUEST.wireValue)
            .put("messageId", requestId)
            .put("correlationId", JSONObject.NULL)
            .put("payload", request)
        val responseBytes = client.request(envelope.toString().toByteArray(Charsets.UTF_8), timeoutMs)
        val responseEnvelope = JSONObject(String(responseBytes, Charsets.UTF_8))
        if (responseEnvelope.optString("messageType") != GatewayMessageType.RESPONSE.wireValue ||
            responseEnvelope.optString("correlationId") != requestId
        ) {
            throw IllegalStateException("GatewayResponseCorrelationMismatch")
        }
        return responseEnvelope.getJSONObject("payload")
    }

    fun serverFingerprint(): String = synchronized(transportLock) {
        transport?.serverFingerprint() ?: throw IllegalStateException("GatewaySessionOffline")
    }

    fun uploadLog(desktop: JSONObject, entry: JSONObject): Boolean {
        val trustCredential = desktop.optString("trustCredential")
        if (trustCredential.isBlank()) return false
        return runCatching {
            request(
                desktop.getString("host"),
                desktop.optInt("port", 48320),
                authorizedRequest("logs", trustCredential).put("logs", JSONArray().put(entry)),
                timeoutMs = 2_500,
                expectedFingerprint = desktop.optString("gatewayFingerprint"),
            ).optBoolean("ok")
        }.getOrDefault(false)
    }

    fun startSubscription(desktop: JSONObject) {
        val trustCredential = desktop.optString("trustCredential")
        if (trustCredential.isBlank()) return
        running.set(true)
        activeDesktop = JSONObject(desktop.toString())
        heartbeat?.cancel(true)
        val response = request(
            desktop.getString("host"),
            desktop.optInt("port", 48320),
            authorizedRequest("subscribe", trustCredential),
            expectedFingerprint = desktop.optString("gatewayFingerprint"),
        )
        if (!response.optBoolean("ok")) {
            running.set(false)
            throw IllegalStateException(response.optString("message", "无法订阅方案更新"))
        }
        reconnectAttempt = 0
        reconnectFuture?.cancel(false)
        reconnectFuture = null
        endpointKey?.let { key ->
            if (stateMachine.state.phase == MobileConnectionPhase.Synchronizing) {
                stateMachine.synchronized(key)
            }
        }
        heartbeat = scheduler.scheduleWithFixedDelay(
            { sendHeartbeat() },
            15,
            15,
            TimeUnit.SECONDS,
        )
    }

    fun stopSubscription() {
        running.set(false)
        heartbeat?.cancel(true)
        heartbeat = null
        reconnectFuture?.cancel(true)
        reconnectFuture = null
        reconnectAttempt = 0
        activeDesktop = null
        stateMachine.disconnect()
        synchronized(transportLock) {
            transport?.close()
            transport = null
            endpointKey = null
        }
    }

    fun authorizedRequest(type: String, trustCredential: String): JSONObject {
        return JSONObject()
            .put("type", type)
            .put("requestId", "req-${UUID.randomUUID()}")
            .put("deviceId", deviceId())
            .put("stableDeviceKey", stableDeviceKey())
            .put("displayName", Build.MODEL ?: "Android")
            .put("platform", "android")
            .put("architecture", System.getProperty("os.arch") ?: "unknown")
            .put("trustCredential", trustCredential)
    }

    private fun ensureTransport(
        host: String,
        port: Int,
        expectedFingerprint: String?,
        timeoutMs: Int,
    ): MsQuicNativeTransport = synchronized(transportLock) {
        val key = "$host:$port"
        transport?.let { existing ->
            if (endpointKey == key) {
                val observed = runCatching { existing.serverFingerprint() }.getOrNull()
                if (expectedFingerprint.isNullOrBlank() || expectedFingerprint.equals(observed, ignoreCase = true)) {
                    return@synchronized existing
                }
                existing.close()
                transport = null
            } else {
                existing.close()
                transport = null
            }
        }
        stateMachine.disconnect()
        stateMachine.begin(key)
        try {
            MsQuicNativeTransport(
                host = host,
                port = port,
                expectedFingerprint = expectedFingerprint,
                eventHandler = { bytes -> eventExecutor.execute { handleEnvelope(bytes) } },
                disconnectedHandler = { reason -> handleDisconnected(reason) },
                timeoutMilliseconds = timeoutMs,
            ).also {
                transport = it
                endpointKey = key
                stateMachine.authenticated(key)
            }
        } catch (error: Exception) {
            stateMachine.fail(key, "GatewayConnectFailed", error.message ?: "QUIC 连接失败")
            throw error
        }
    }

    private fun handleEnvelope(bytes: ByteArray) {
        val desktop = activeDesktop ?: return
        try {
            val envelope = JSONObject(String(bytes, Charsets.UTF_8))
            if (envelope.optString("messageType") != GatewayMessageType.EVENT.wireValue) {
                throw IllegalStateException("GatewayEventEnvelopeInvalid")
            }
            val gatewayResponse = envelope.getJSONObject("payload")
            if (!gatewayResponse.optBoolean("ok")) {
                throw IllegalStateException(gatewayResponse.optString("message", "桌面端事件失败"))
            }
            val payload = gatewayResponse.getJSONObject("payload")
            when (payload.optString("eventType")) {
                "scheme.updated" -> handleSchemeUpdate(desktop, payload)
                "jsapi.request" -> handleJsApiRequest(desktop, payload)
                else -> logs.append("Warning", "GatewayEvent", "收到未知事件：${payload.optString("eventType")}")
            }
        } catch (error: Exception) {
            logs.append("Error", "GatewayEvent", error.message ?: "服务端事件处理失败")
        }
    }

    private fun handleSchemeUpdate(desktop: JSONObject, payload: JSONObject) {
        val descriptor = payload.optJSONObject("scheme") ?: return
        val eventId = payload.optString("eventId")
        if (eventId.isBlank()) return
        if (onSchemeEvent(desktop, descriptor, eventId)) {
            request(
                desktop.getString("host"),
                desktop.optInt("port", 48320),
                authorizedRequest("scheme-ack", desktop.getString("trustCredential")).put("eventId", eventId),
                expectedFingerprint = desktop.optString("gatewayFingerprint"),
            )
        }
    }

    private fun handleJsApiRequest(desktop: JSONObject, payload: JSONObject) {
        val requestId = payload.optString("requestId")
        val capability = payload.optString("capability")
        if (requestId.isBlank() || capability.isBlank()) return
        val source = payload.optJSONObject("source") ?: JSONObject()
        val sourceKey = when (source.optString("kind")) {
            "component" -> "component:${source.optString("componentId", "unknown")}"
            "plugin" -> "plugin:${source.optString("pluginId", "unknown")}"
            "system" -> "system"
            else -> "unknown"
        }
        val result = onJsApiEvent(capability, payload.optJSONObject("payload") ?: JSONObject(), requestId, sourceKey)
        request(
            desktop.getString("host"),
            desktop.optInt("port", 48320),
            authorizedRequest("jsapi-response", desktop.getString("trustCredential"))
                .put("requestId", requestId)
                .put("responseOk", result.optBoolean("ok"))
                .put("errorCode", result.opt("errorCode") ?: JSONObject.NULL)
                .put("message", result.opt("message") ?: JSONObject.NULL)
                .put("payload", result.opt("payload") ?: JSONObject.NULL),
            expectedFingerprint = desktop.optString("gatewayFingerprint"),
        )
    }

    private fun sendHeartbeat() {
        val desktop = activeDesktop ?: return
        if (!running.get()) return
        runCatching {
            request(
                desktop.getString("host"),
                desktop.optInt("port", 48320),
                authorizedRequest("heartbeat", desktop.getString("trustCredential")),
                timeoutMs = 5_000,
                expectedFingerprint = desktop.optString("gatewayFingerprint"),
            )
        }.onFailure { error ->
            logs.append("Warning", "GatewayHeartbeat", error.message ?: "桌面端心跳失败")
        }
    }

    private fun handleDisconnected(reason: String) {
        if (!running.get()) return
        logs.append("Warning", "GatewayConnection", reason)
        synchronized(transportLock) {
            transport = null
            endpointKey = null
        }
        stateMachine.disconnect()
        val desktop = activeDesktop ?: return
        scheduleReconnect(desktop)
    }

    private fun scheduleReconnect(desktop: JSONObject) {
        synchronized(transportLock) {
            if (!running.get() || reconnectFuture?.isDone == false) return
            val attempt = reconnectAttempt++
            val delay = ReconnectBackoff.delayMilliseconds(attempt)
            reconnectFuture = scheduler.schedule(
                {
                    reconnectFuture = null
                    if (!running.get()) return@schedule
                    runCatching { startSubscription(desktop) }
                        .onFailure { error ->
                            logs.append("Error", "GatewayReconnect", "第 ${attempt + 1} 次重连失败：${error.message ?: "桌面端不可达"}")
                            scheduleReconnect(desktop)
                        }
                },
                delay,
                TimeUnit.MILLISECONDS,
            )
        }
    }

    override fun close() {
        stopSubscription()
        eventExecutor.shutdownNow()
        scheduler.shutdownNow()
    }
}

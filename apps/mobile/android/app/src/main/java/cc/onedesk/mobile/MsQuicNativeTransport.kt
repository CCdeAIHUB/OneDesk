package cc.onedesk.mobile

import java.io.Closeable
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.locks.ReentrantReadWriteLock
import kotlin.concurrent.read
import kotlin.concurrent.write

/**
 * JNI 层只暴露二进制信封收发。JSON 业务、重连和缓存均留在 Kotlin，避免原生层与产品逻辑耦合。
 */
class MsQuicNativeTransport(
    host: String,
    port: Int,
    private val expectedFingerprint: String?,
    private val eventHandler: (ByteArray) -> Unit,
    private val disconnectedHandler: (String) -> Unit,
    timeoutMilliseconds: Int = 7_000,
) : Closeable {
    companion object {
        init {
            System.loadLibrary("onedesk_quic")
        }
    }

    private val nativeHandle = AtomicLong(0)
    private val lifecycleLock = ReentrantReadWriteLock()
    @Volatile private var observedFingerprint: String? = null

    init {
        nativeHandle.set(nativeConnect(host, port, timeoutMilliseconds))
        check(nativeHandle.get() != 0L) { "MsQuicConnectionUnavailable" }
    }

    fun request(payload: ByteArray, timeoutMilliseconds: Int): ByteArray = lifecycleLock.read {
        val handle = nativeHandle.get()
        check(handle != 0L) { "GatewaySessionOffline" }
        nativeRequest(handle, payload, timeoutMilliseconds)
    }

    fun serverFingerprint(): String = lifecycleLock.read {
        observedFingerprint ?: throw IllegalStateException("GatewayCertificateMissing")
    }

    @Suppress("unused") // 由 JNI 在 TLS 握手线程同步调用。
    private fun validateServerCertificate(certificate: ByteArray): Boolean {
        val fingerprint = MessageDigest.getInstance("SHA-256")
            .digest(certificate)
            .joinToString("") { "%02x".format(it) }
        observedFingerprint = fingerprint
        return expectedFingerprint.isNullOrBlank() || expectedFingerprint.equals(fingerprint, ignoreCase = true)
    }

    @Suppress("unused") // 由 JNI 的服务端单向流回调调用。
    private fun onNativeEvent(payload: ByteArray) {
        eventHandler(payload)
    }

    @Suppress("unused") // 由 JNI 的连接关闭回调调用。
    private fun onNativeDisconnected(reason: String) {
        disconnectedHandler(reason)
    }

    override fun close() {
        lifecycleLock.write {
            val handle = nativeHandle.getAndSet(0)
            if (handle != 0L) {
                nativeClose(handle)
            }
        }
    }

    private external fun nativeConnect(host: String, port: Int, timeoutMilliseconds: Int): Long
    private external fun nativeRequest(handle: Long, payload: ByteArray, timeoutMilliseconds: Int): ByteArray
    private external fun nativeClose(handle: Long)
}

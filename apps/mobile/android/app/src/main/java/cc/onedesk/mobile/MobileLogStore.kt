package cc.onedesk.mobile

import android.content.SharedPreferences
import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant
import java.util.UUID

class MobileLogStore(
    private val prefs: SharedPreferences,
    private val deviceId: () -> String,
) {
    private val lock = Any()
    @Volatile private var onlineSink: ((JSONObject) -> Boolean)? = null

    fun setOnlineSink(sink: ((JSONObject) -> Boolean)?) {
        onlineSink = sink
    }

    fun append(level: String, category: String, message: String) {
        val entry = JSONObject()
            .put("logId", "log-${UUID.randomUUID()}")
            .put("createdAt", Instant.now().toString())
            .put("sourceDeviceId", deviceId())
            .put("level", level)
            .put("category", category)
            .put("message", message)

        // 在线日志优先实时汇入桌面端；只有传输失败或断联时才写入移动端持久缓存。
        val delivered = runCatching { onlineSink?.invoke(JSONObject(entry.toString())) == true }.getOrDefault(false)
        if (delivered) return

        synchronized(lock) {
            val logs = read()
            logs.put(entry)
            while (logs.length() > 500) {
                logs.remove(0)
            }
            prefs.edit().putString(KEY, logs.toString()).apply()
        }
    }

    fun snapshot(): JSONArray = synchronized(lock) { JSONArray(read().toString()) }

    fun clear() {
        synchronized(lock) { prefs.edit().remove(KEY).apply() }
    }

    private fun read(): JSONArray {
        return try {
            JSONArray(prefs.getString(KEY, "[]") ?: "[]")
        } catch (_: Exception) {
            JSONArray()
        }
    }

    private companion object {
        const val KEY = "disconnectedLogs"
    }
}

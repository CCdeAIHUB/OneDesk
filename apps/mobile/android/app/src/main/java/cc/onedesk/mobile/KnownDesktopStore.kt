package cc.onedesk.mobile

import android.content.SharedPreferences
import org.json.JSONArray
import org.json.JSONObject

/**
 * 统一管理已信任桌面端记录。长期凭据只进入 Android Keystore 加密存储，
 * 前端可读取的 knownDesktops JSON 永远不包含明文凭据。
 */
internal class KnownDesktopStore(
    private val preferences: SharedPreferences,
    private val credentials: TrustCredentialStore,
    private val logs: MobileLogStore,
) {
    fun listForFrontend(): String = storedRecords().toString()

    fun find(host: String, port: Int): JSONObject? = findStored {
        it.optString("host") == host && it.optInt("port") == port
    }

    fun findById(desktopId: String): JSONObject? = findStored {
        it.optString("desktopId") == desktopId
    }

    fun upsert(desktop: JSONObject) {
        val storedDesktop = JSONObject(desktop.toString())
        val desktopId = storedDesktop.optString("desktopId")
        require(desktopId.isNotBlank()) { "DesktopIdMissing" }
        val credential = storedDesktop.optString("trustCredential")
        if (credential.isNotBlank()) credentials.put(desktopId, credential)
        storedDesktop.remove("trustCredential")

        val current = storedRecords()
        val next = JSONArray()
        for (index in 0 until current.length()) {
            val item = current.optJSONObject(index) ?: continue
            if (item.optString("desktopId") != desktopId) next.put(item)
        }
        next.put(storedDesktop)
        check(preferences.edit().putString(KEY_KNOWN_DESKTOPS, next.toString()).commit()) {
            "KnownDesktopWriteFailed"
        }
    }

    fun updateScheme(desktopId: String, result: SchemeCacheResult) {
        val desktop = findById(desktopId) ?: return
        desktop.put("schemeVersion", result.version).put("schemeHash", result.hash)
        upsert(desktop)
    }

    fun activeDesktopId(): String? = preferences.getString(KEY_ACTIVE_DESKTOP, null)

    fun setActiveDesktopId(desktopId: String) {
        check(preferences.edit().putString(KEY_ACTIVE_DESKTOP, desktopId).commit()) {
            "ActiveDesktopWriteFailed"
        }
    }

    fun migratePlaintextCredentials() {
        val records = storedRecords()
        var changed = false
        for (index in 0 until records.length()) {
            val desktop = records.optJSONObject(index) ?: continue
            val credential = desktop.optString("trustCredential")
            val desktopId = desktop.optString("desktopId")
            if (desktopId.isNotBlank() && credential.isNotBlank()) {
                credentials.put(desktopId, credential)
                desktop.remove("trustCredential")
                changed = true
            }
        }
        if (changed) {
            check(preferences.edit().putString(KEY_KNOWN_DESKTOPS, records.toString()).commit()) {
                "TrustCredentialMigrationFailed"
            }
            logs.append("Info", "Trust", "已将旧版桌面端信任凭据迁移到 Android Keystore")
        }
    }

    private fun findStored(predicate: (JSONObject) -> Boolean): JSONObject? {
        val records = storedRecords()
        for (index in 0 until records.length()) {
            val item = records.optJSONObject(index) ?: continue
            if (predicate(item)) return attachCredential(item)
        }
        return null
    }

    private fun attachCredential(stored: JSONObject): JSONObject {
        val desktop = JSONObject(stored.toString())
        credentials.get(desktop.optString("desktopId"))
            ?.takeIf(String::isNotBlank)
            ?.let { desktop.put("trustCredential", it) }
        return desktop
    }

    private fun storedRecords(): JSONArray = try {
        JSONArray(preferences.getString(KEY_KNOWN_DESKTOPS, "[]") ?: "[]")
    } catch (error: Exception) {
        logs.append("Error", "Trust", "桌面端信任记录损坏：${error.message ?: error.javaClass.simpleName}")
        JSONArray()
    }

    private companion object {
        const val KEY_KNOWN_DESKTOPS = "knownDesktops"
        const val KEY_ACTIVE_DESKTOP = "activeDesktopId"
    }
}

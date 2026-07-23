package cc.onedesk.mobile

import android.content.Context
import android.provider.Settings
import java.security.MessageDigest

/** 提供不依赖应用数据、且不会暴露原始 Android 系统标识的稳定设备键。 */
internal object AndroidDeviceIdentity {
    fun stableDeviceKey(context: Context): String {
        val androidId = Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID)
            ?.trim()
            .orEmpty()
        check(androidId.isNotBlank()) { "AndroidStableIdentityUnavailable" }
        return stableDeviceKey(androidId)
    }

    fun stableDeviceKey(androidId: String): String {
        val normalized = androidId.trim()
        require(normalized.isNotBlank()) { "AndroidStableIdentityUnavailable" }
        val digest = MessageDigest.getInstance("SHA-256")
            .digest("cc.onedesk.mobile:$normalized".toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }
        return "android:$digest"
    }
}

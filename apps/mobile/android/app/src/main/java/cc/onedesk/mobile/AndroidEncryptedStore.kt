package cc.onedesk.mobile

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** 使用不可导出的 Android Keystore 密钥保护应用私有字符串。 */
internal class AndroidEncryptedStore(
    context: Context,
    preferenceName: String,
    private val keyAlias: String,
) {
    private val preferences = context.getSharedPreferences(preferenceName, Context.MODE_PRIVATE)

    fun get(key: String): String? {
        val encoded = preferences.getString(key, null) ?: return null
        val parts = encoded.split(':', limit = 2)
        check(parts.size == 2) { "EncryptedValueCorrupted" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(128, Base64.decode(parts[0], Base64.NO_WRAP)))
        return String(cipher.doFinal(Base64.decode(parts[1], Base64.NO_WRAP)), Charsets.UTF_8)
    }

    fun put(key: String, value: String): Boolean {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, secretKey())
        val encoded = Base64.encodeToString(cipher.iv, Base64.NO_WRAP) + ":" +
            Base64.encodeToString(cipher.doFinal(value.toByteArray(Charsets.UTF_8)), Base64.NO_WRAP)
        return preferences.edit().putString(key, encoded).commit()
    }

    fun remove(key: String): Boolean = preferences.edit().remove(key).commit()

    private fun secretKey(): SecretKey {
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (keyStore.getKey(keyAlias, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(
            KeyGenParameterSpec.Builder(
                keyAlias,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
            ).setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build(),
        )
        return generator.generateKey()
    }
}

internal class TrustCredentialStore(context: Context) {
    private val encrypted = AndroidEncryptedStore(context, "onedesk-trust-credentials", "onedesk-trust-credentials-v1")

    fun get(desktopId: String): String? = encrypted.get(desktopId)

    fun put(desktopId: String, credential: String) {
        check(desktopId.isNotBlank() && credential.isNotBlank()) { "TrustCredentialInvalid" }
        check(encrypted.put(desktopId, credential)) { "TrustCredentialWriteFailed" }
    }

    fun remove(desktopId: String) {
        check(encrypted.remove(desktopId)) { "TrustCredentialDeleteFailed" }
    }
}

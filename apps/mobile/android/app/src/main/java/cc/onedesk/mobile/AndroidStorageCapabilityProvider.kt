package cc.onedesk.mobile

import android.content.Context
import org.json.JSONObject
import java.io.File

internal class AndroidStorageCapabilityProvider(
    private val context: Context,
) {
    private val credentialStore = AndroidCredentialStore(context)

    fun execute(request: AndroidCapabilityRequest): JSONObject = when (request.capability) {
        "file.private.read" -> readPrivateFile(request)
        "file.private.write" -> writePrivateFile(request)
        "file.private.delete" -> deletePrivateFile(request)
        "credential.access" -> credentialStore.execute(request)
        else -> AndroidCapabilityResults.error(request, "CapabilityHandlerMissing", "存储能力处理器不存在", "AndroidStorage", false)
    }

    private fun readPrivateFile(request: AndroidCapabilityRequest): JSONObject {
        val file = resolvePrivateFile(request) ?: return invalidPath(request)
        if (!file.isFile) {
            return AndroidCapabilityResults.error(request, "FileNotFound", "私有文件不存在", "AndroidStorage", true)
        }
        val maximumBytes = request.payload.optInt("maximumBytes", 1_048_576).coerceIn(1, 8_388_608)
        if (file.length() > maximumBytes) {
            return AndroidCapabilityResults.error(request, "FileTooLarge", "私有文件超过本次读取上限", "AndroidStorage", true)
        }
        return AndroidCapabilityResults.success(
            request,
            JSONObject().put("path", request.payload.optString("path")).put("content", file.readText(Charsets.UTF_8)),
        )
    }

    private fun writePrivateFile(request: AndroidCapabilityRequest): JSONObject {
        val file = resolvePrivateFile(request) ?: return invalidPath(request)
        val content = request.payload.optString("content")
        if (content.toByteArray(Charsets.UTF_8).size > 8_388_608) {
            return AndroidCapabilityResults.error(request, "FileTooLarge", "单个私有文件不能超过 8 MiB", "AndroidStorage", true)
        }
        file.parentFile?.mkdirs()
        val temporary = File(file.parentFile, ".${file.name}.${System.nanoTime()}.tmp")
        temporary.writeText(content, Charsets.UTF_8)
        if (file.exists() && !file.delete()) {
            temporary.delete()
            return AndroidCapabilityResults.error(request, "FileReplaceFailed", "无法替换现有私有文件", "AndroidStorage", true)
        }
        if (!temporary.renameTo(file)) {
            temporary.delete()
            return AndroidCapabilityResults.error(request, "FileCommitFailed", "私有文件原子写入失败", "AndroidStorage", true)
        }
        return AndroidCapabilityResults.success(request, JSONObject().put("path", request.payload.optString("path")))
    }

    private fun deletePrivateFile(request: AndroidCapabilityRequest): JSONObject {
        val file = resolvePrivateFile(request) ?: return invalidPath(request)
        if (file.exists() && !file.delete()) {
            return AndroidCapabilityResults.error(request, "FileDeleteFailed", "私有文件删除失败", "AndroidStorage", true)
        }
        return AndroidCapabilityResults.success(request)
    }

    private fun resolvePrivateFile(request: AndroidCapabilityRequest): File? {
        val relativePath = request.payload.optString("path").replace('\\', '/')
        if (relativePath.isBlank() || relativePath.startsWith('/') || relativePath.split('/').any { it == ".." }) return null
        val source = request.sourceKey.replace(Regex("[^A-Za-z0-9._-]"), "-").ifBlank { "unknown" }
        val root = File(context.filesDir, "jsapi-private/$source").canonicalFile
        val target = File(root, relativePath).canonicalFile
        return target.takeIf { it.path == root.path || it.path.startsWith(root.path + File.separator) }
    }

    private fun invalidPath(request: AndroidCapabilityRequest) = AndroidCapabilityResults.error(
        request,
        "InvalidPath",
        "私有文件路径必须是调用方目录内的相对路径",
        "AndroidStorage",
        false,
    )
}

/** Android Keystore 密钥不可导出，密文按调用来源隔离保存在应用私有目录。 */
private class AndroidCredentialStore(context: Context) {
    private val encrypted = AndroidEncryptedStore(context, "onedesk-secure-credentials", "onedesk-jsapi-credentials-v1")

    fun execute(request: AndroidCapabilityRequest): JSONObject {
        val key = request.payload.optString("key")
        if (!Regex("^[A-Za-z0-9._-]{1,128}$").matches(key)) {
            return AndroidCapabilityResults.error(request, "InvalidCredentialKey", "凭据键格式无效", "AndroidCredential", false)
        }
        val storageKey = "${request.sourceKey}:$key"
        return when (request.payload.optString("operation", "read")) {
            "read" -> read(request, storageKey)
            "write" -> write(request, storageKey, request.payload.optString("value"))
            "delete" -> {
                if (!encrypted.remove(storageKey)) {
                    return AndroidCapabilityResults.error(request, "CredentialDeleteFailed", "安全凭据删除失败", "AndroidCredential", true)
                }
                AndroidCapabilityResults.success(request)
            }
            else -> AndroidCapabilityResults.error(request, "InvalidCredentialOperation", "凭据操作只支持 read、write 或 delete", "AndroidCredential", false)
        }
    }

    private fun read(request: AndroidCapabilityRequest, storageKey: String): JSONObject {
        val value = encrypted.get(storageKey)
            ?: return AndroidCapabilityResults.error(request, "CredentialNotFound", "凭据不存在", "AndroidCredential", true)
        return AndroidCapabilityResults.success(request, JSONObject().put("value", value))
    }

    private fun write(request: AndroidCapabilityRequest, storageKey: String, value: String): JSONObject {
        if (!encrypted.put(storageKey, value)) {
            return AndroidCapabilityResults.error(request, "CredentialWriteFailed", "安全凭据写入失败", "AndroidCredential", true)
        }
        return AndroidCapabilityResults.success(request)
    }
}

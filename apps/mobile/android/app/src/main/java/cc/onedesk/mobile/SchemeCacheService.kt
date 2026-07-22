package cc.onedesk.mobile

import android.content.Context
import android.content.SharedPreferences
import android.net.Uri
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.security.MessageDigest
import java.time.Instant

data class SchemeCacheResult(val updated: Boolean, val hasScheme: Boolean, val version: String, val hash: String)

class SchemeCacheService(
    private val context: Context,
    private val prefs: SharedPreferences,
    private val gateway: MobileGatewayClient,
    private val logs: MobileLogStore,
) {
    fun get(desktopId: String): JSONObject? {
        val raw = prefs.getString("scheme:$desktopId", null) ?: return null
        return try { JSONObject(raw) } catch (_: Exception) { null }
    }

    @Synchronized
    fun downloadAndCache(desktop: JSONObject, descriptor: JSONObject): SchemeCacheResult {
        val desktopId = desktop.getString("desktopId")
        val version = descriptor.optString("version", "0")
        val hash = descriptor.optString("hash", "")
        val hasScheme = descriptor.optBoolean("hasScheme", false)
        val existing = get(desktopId)
        if (hash.isNotBlank() && existing?.optString("hash") == hash) {
            return SchemeCacheResult(false, hasScheme, version, hash)
        }

        val payload = if (hasScheme) downloadSchemePayload(desktop, descriptor) else JSONObject()
            .put("activeSchemeId", JSONObject.NULL)
            .put("scheme", JSONObject.NULL)
            .put("pages", JSONArray())
            .put("components", JSONArray())
            .put("actions", JSONArray())
            .put("permissionGrants", JSONArray())
        val assetRoot = schemeAssetRoot(desktopId, hash)
        try {
            materializeAssets(desktop, assetRoot, payload)
        } catch (error: Exception) {
            assetRoot.deleteRecursively()
            throw error
        }
        val cache = JSONObject()
            .put("desktopId", desktopId)
            .put("version", version)
            .put("hash", hash)
            .put("updatedAt", Instant.now().toString())
            .put("activeSchemeId", payload.opt("activeSchemeId"))
            .put("scheme", payload.opt("scheme"))
            .put("pages", payload.optJSONArray("pages") ?: JSONArray())
            .put("components", payload.optJSONArray("components") ?: JSONArray())
            .put("actions", payload.optJSONArray("actions") ?: JSONArray())
            .put("permissionGrants", payload.optJSONArray("permissionGrants") ?: JSONArray())
        check(prefs.edit().putString("scheme:$desktopId", cache.toString()).commit()) {
            "无法提交方案缓存索引"
        }
        removeOldAssetDirectories(desktopId, hash)
        return SchemeCacheResult(true, hasScheme, version, hash)
    }

    private fun downloadSchemePayload(desktop: JSONObject, descriptor: JSONObject): JSONObject {
        val totalBytes = descriptor.optLong("totalBytes", 0)
        val expectedHash = descriptor.getString("hash")
        if (totalBytes <= 0 || totalBytes > 32L * 1024 * 1024) {
            throw IllegalStateException("方案快照大小无效")
        }
        val output = ByteArrayOutputStream(totalBytes.toInt())
        var offset = 0L
        while (offset < totalBytes) {
            val response = gateway.request(
                desktop.getString("host"),
                desktop.optInt("port", 48320),
                gateway.authorizedRequest("scheme-chunk", desktop.getString("trustCredential"))
                    .put("hash", expectedHash)
                    .put("offset", offset)
                    .put("length", 24 * 1024),
                expectedFingerprint = desktop.optString("gatewayFingerprint"),
            )
            if (!response.optBoolean("ok")) throw IllegalStateException(response.optString("message", "方案分块下载失败"))
            val chunk = response.getJSONObject("payload")
            val bytes = android.util.Base64.decode(chunk.getString("data"), android.util.Base64.DEFAULT)
            if (chunk.optLong("offset", -1) != offset || bytes.isEmpty() && offset < totalBytes) {
                throw IllegalStateException("方案分块顺序无效")
            }
            output.write(bytes)
            offset += bytes.size
        }
        val content = output.toByteArray()
        if (sha256(content) != expectedHash) throw IllegalStateException("方案完整性校验失败")
        return JSONObject(String(content, Charsets.UTF_8))
    }

    private fun materializeAssets(desktop: JSONObject, assetRoot: File, payload: JSONObject) {
        assetRoot.mkdirs()
        val pages = payload.optJSONArray("pages") ?: JSONArray()
        for (index in 0 until pages.length()) {
            val page = pages.optJSONObject(index) ?: continue
            replaceMediaSource(desktop, assetRoot, "page", page.optString("id"), page, "backgroundMediaSource")
        }
        val components = payload.optJSONArray("components") ?: JSONArray()
        for (index in 0 until components.length()) {
            val bundle = components.optJSONObject(index) ?: continue
            val definition = bundle.optJSONObject("definition") ?: continue
            val config = bundle.optJSONObject("visualConfig") ?: continue
            val background = config.optJSONObject("background")
            if (background != null) replaceMediaSource(desktop, assetRoot, "component", definition.optString("id"), background, "mediaSource")
            val image = config.optJSONObject("image")
            if (image != null) replaceMediaSource(desktop, assetRoot, "component", definition.optString("id"), image, "source")
        }
    }

    private fun replaceMediaSource(
        desktop: JSONObject,
        assetRoot: File,
        ownerKind: String,
        ownerId: String,
        target: JSONObject,
        key: String,
    ) {
        // JSONObject.optString 会把 JSON null 转成字面量 "null"，必须先按真实类型过滤。
        val source = (target.opt(key) as? String)?.trim().orEmpty()
        if (source.isBlank() || ownerId.isBlank()) return
        val fileName = try { Uri.parse(source).lastPathSegment.orEmpty() } catch (_: Exception) { File(source).name }
        if (fileName.isBlank()) return
        try {
            val file = downloadAsset(desktop, assetRoot, ownerKind, ownerId, fileName)
            target.put(key, Uri.fromFile(file).toString())
        } catch (error: Exception) {
            logs.append("Error", "SchemeAsset", "$ownerKind/$ownerId/$fileName：${error.message ?: "资源下载失败"}")
            throw error
        }
    }

    private fun downloadAsset(desktop: JSONObject, root: File, ownerKind: String, ownerId: String, fileName: String): File {
        val safeName = "${ownerKind}-${ownerId}-${File(fileName).name}".replace(Regex("[^A-Za-z0-9._-]"), "_")
        val destination = File(root, safeName)
        if (destination.exists() && destination.length() > 0) return destination
        // 推送事件、连接刷新和前端主动刷新可能来自不同线程。外层缓存事务已串行化，
        // 这里仍使用唯一临时文件，避免异常恢复或未来多实例访问时互相覆盖下载内容。
        val temporary = File.createTempFile("$safeName.", ".tmp", root)
        FileOutputStream(temporary).use { output ->
            var offset = 0L
            var total = Long.MAX_VALUE
            while (offset < total) {
                val response = gateway.request(
                    desktop.getString("host"),
                    desktop.optInt("port", 48320),
                    gateway.authorizedRequest("asset", desktop.getString("trustCredential"))
                        .put("ownerKind", ownerKind)
                        .put("ownerId", ownerId)
                        .put("fileName", fileName)
                        .put("offset", offset)
                        .put("length", 24 * 1024),
                    timeoutMs = 12_000,
                    expectedFingerprint = desktop.optString("gatewayFingerprint"),
                )
                if (!response.optBoolean("ok")) throw IllegalStateException(response.optString("message", "资源下载失败"))
                val chunk = response.getJSONObject("payload")
                val bytes = android.util.Base64.decode(chunk.getString("data"), android.util.Base64.DEFAULT)
                total = chunk.getLong("totalBytes")
                if (chunk.getLong("offset") != offset || bytes.isEmpty() && offset < total) throw IllegalStateException("资源分块顺序无效")
                output.write(bytes)
                offset += bytes.size
                if (chunk.optBoolean("complete")) break
            }
        }
        try {
            SchemeAssetFileStore.commit(temporary, destination)
        } catch (error: Exception) {
            temporary.delete()
            throw IllegalStateException("无法提交资源缓存：${error.message ?: "文件替换失败"}", error)
        }
        return destination
    }

    private fun schemeAssetRoot(desktopId: String, schemeHash: String): File {
        val safeDesktopId = desktopId.replace(Regex("[^A-Za-z0-9._-]"), "_")
        val safeHash = schemeHash.ifBlank { "empty" }.replace(Regex("[^A-Fa-f0-9._-]"), "_")
        return File(context.filesDir, "scheme-assets/$safeDesktopId/$safeHash")
    }

    private fun removeOldAssetDirectories(desktopId: String, currentHash: String) {
        val safeDesktopId = desktopId.replace(Regex("[^A-Za-z0-9._-]"), "_")
        val safeHash = currentHash.ifBlank { "empty" }.replace(Regex("[^A-Fa-f0-9._-]"), "_")
        val desktopRoot = File(context.filesDir, "scheme-assets/$safeDesktopId")
        val directories = desktopRoot.listFiles()
            ?.filter(File::isDirectory)
            ?.map { SchemeAssetDirectory(desktopId, it.name) }
            .orEmpty()
        val stale = SchemeAssetRetentionPolicy.staleDirectories(desktopId, safeHash, directories)
            .map(SchemeAssetDirectory::schemeHash)
            .toSet()
        desktopRoot.listFiles()?.filter { it.isDirectory && it.name in stale }?.forEach { it.deleteRecursively() }
    }

    private fun sha256(bytes: ByteArray): String {
        return MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
    }
}

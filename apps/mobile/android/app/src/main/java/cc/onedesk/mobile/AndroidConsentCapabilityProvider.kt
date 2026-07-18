package cc.onedesk.mobile

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.PixelFormat
import android.hardware.camera2.CameraManager
import android.hardware.display.DisplayManager
import android.media.AudioManager
import android.media.ImageReader
import android.media.MediaRecorder
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.provider.DocumentsContract
import androidx.activity.ComponentActivity
import androidx.core.content.ContextCompat
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

internal class AndroidConsentCapabilityProvider(
    private val activity: ComponentActivity,
    private val consent: AndroidConsentCoordinator,
) {
    fun execute(request: AndroidCapabilityRequest): JSONObject = when (request.capability) {
        "file.external.read" -> readExternalFile(request)
        "file.external.write" -> writeExternalFile(request)
        "file.external.delete" -> deleteExternalFile(request)
        "camera.access" -> cameraAccess(request)
        "microphone.access" -> microphoneAccess(request)
        "screen.capture" -> captureScreen(request)
        "screen.record" -> recordScreen(request)
        else -> AndroidCapabilityResults.error(request, "CapabilityHandlerMissing", "用户授权能力处理器不存在", "AndroidConsent", false)
    }

    private fun readExternalFile(request: AndroidCapabilityRequest): JSONObject {
        val uri = resolveDocumentUri(request, create = false) ?: return consentDenied(request)
        val maximumBytes = request.payload.optInt("maximumBytes", 8_388_608).coerceIn(1, 32 * 1024 * 1024)
        val bytes = activity.contentResolver.openInputStream(uri)?.use { it.readNBytes(maximumBytes + 1) }
            ?: return AndroidCapabilityResults.error(request, "ExternalFileOpenFailed", "无法打开所选文件", "AndroidExternalFile", true)
        if (bytes.size > maximumBytes) return AndroidCapabilityResults.error(request, "FileTooLarge", "所选文件超过读取上限", "AndroidExternalFile", true)
        return AndroidCapabilityResults.success(
            request,
            JSONObject().put("uri", uri.toString()).put("content", String(bytes, Charsets.UTF_8)),
        )
    }

    private fun writeExternalFile(request: AndroidCapabilityRequest): JSONObject {
        val uri = resolveDocumentUri(request, create = true) ?: return consentDenied(request)
        val bytes = request.payload.optString("content").toByteArray(Charsets.UTF_8)
        if (bytes.size > 32 * 1024 * 1024) return AndroidCapabilityResults.error(request, "FileTooLarge", "写入内容不能超过 32 MiB", "AndroidExternalFile", true)
        activity.contentResolver.openOutputStream(uri, "wt")?.use { it.write(bytes) }
            ?: return AndroidCapabilityResults.error(request, "ExternalFileOpenFailed", "无法写入所选文件", "AndroidExternalFile", true)
        return AndroidCapabilityResults.success(request, JSONObject().put("uri", uri.toString()).put("bytes", bytes.size))
    }

    private fun deleteExternalFile(request: AndroidCapabilityRequest): JSONObject {
        val uri = resolveDocumentUri(request, create = false) ?: return consentDenied(request)
        val deleted = DocumentsContract.deleteDocument(activity.contentResolver, uri)
        return if (deleted) AndroidCapabilityResults.success(request)
        else AndroidCapabilityResults.error(request, "ExternalFileDeleteFailed", "系统未能删除所选文件", "AndroidExternalFile", true)
    }

    private fun resolveDocumentUri(request: AndroidCapabilityRequest, create: Boolean): Uri? {
        request.payload.optString("uri").takeIf(String::isNotBlank)?.let { return Uri.parse(it) }
        val intent = if (create) {
            Intent(Intent.ACTION_CREATE_DOCUMENT)
                .setType(request.payload.optString("mimeType", "text/plain"))
                .putExtra(Intent.EXTRA_TITLE, request.payload.optString("fileName", "onedesk.txt"))
        } else {
            Intent(Intent.ACTION_OPEN_DOCUMENT).setType(request.payload.optString("mimeType", "*/*"))
        }.addCategory(Intent.CATEGORY_OPENABLE)
        val result = consent.requestActivity(intent)
        if (result.resultCode != Activity.RESULT_OK) return null
        return result.data?.data?.also { uri ->
            val flags = result.data?.flags?.and(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION) ?: 0
            runCatching { activity.contentResolver.takePersistableUriPermission(uri, flags) }
        }
    }

    private fun cameraAccess(request: AndroidCapabilityRequest): JSONObject {
        if (!ensurePermission(Manifest.permission.CAMERA)) return consentDenied(request)
        val manager = activity.getSystemService(Context.CAMERA_SERVICE) as CameraManager
        return AndroidCapabilityResults.success(request, JSONObject().put("cameraIds", JSONArray(manager.cameraIdList.toList())))
    }

    private fun microphoneAccess(request: AndroidCapabilityRequest): JSONObject {
        if (!ensurePermission(Manifest.permission.RECORD_AUDIO)) return consentDenied(request)
        val manager = activity.getSystemService(Context.AUDIO_SERVICE) as AudioManager
        val devices = JSONArray()
        manager.getDevices(AudioManager.GET_DEVICES_INPUTS).forEach { device ->
            devices.put(JSONObject().put("id", device.id).put("type", device.type).put("name", device.productName.toString()))
        }
        return AndroidCapabilityResults.success(request, JSONObject().put("devices", devices))
    }

    private fun captureScreen(request: AndroidCapabilityRequest): JSONObject {
        val projection = requestProjection(request) ?: return consentDenied(request)
        val size = captureSize()
        val reader = ImageReader.newInstance(size.first, size.second, PixelFormat.RGBA_8888, 2)
        val thread = HandlerThread("OneDesk-ScreenCapture").apply { start() }
        val handler = Handler(thread.looper)
        val projectionCallback = object : MediaProjection.Callback() {
            override fun onStop() = Unit
        }
        projection.registerCallback(projectionCallback, handler)
        val latch = CountDownLatch(1)
        reader.setOnImageAvailableListener({ latch.countDown() }, handler)
        val display = projection.createVirtualDisplay(
            "OneDesk-Capture",
            size.first,
            size.second,
            activity.resources.displayMetrics.densityDpi,
            DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
            reader.surface,
            null,
            handler,
        )
        return try {
            if (!latch.await(6, TimeUnit.SECONDS)) {
                AndroidCapabilityResults.error(request, "ScreenCaptureTimeout", "屏幕截图超时", "AndroidScreen", true)
            } else {
                val image = reader.acquireLatestImage()
                    ?: return AndroidCapabilityResults.error(request, "ScreenCaptureEmpty", "系统未返回屏幕图像", "AndroidScreen", true)
                image.use {
                    val plane = it.planes[0]
                    val rowPadding = plane.rowStride - plane.pixelStride * size.first
                    val bitmap = Bitmap.createBitmap(size.first + rowPadding / plane.pixelStride, size.second, Bitmap.Config.ARGB_8888)
                    bitmap.copyPixelsFromBuffer(plane.buffer)
                    val cropped = Bitmap.createBitmap(bitmap, 0, 0, size.first, size.second)
                    val file = outputFile(request, "png")
                    FileOutputStream(file).use { output -> cropped.compress(Bitmap.CompressFormat.PNG, 100, output) }
                    bitmap.recycle()
                    cropped.recycle()
                    AndroidCapabilityResults.success(request, JSONObject().put("uri", Uri.fromFile(file).toString()).put("width", size.first).put("height", size.second))
                }
            }
        } finally {
            display?.release()
            reader.close()
            projection.unregisterCallback(projectionCallback)
            projection.stop()
            thread.quitSafely()
            stopProjectionService()
        }
    }

    private fun recordScreen(request: AndroidCapabilityRequest): JSONObject {
        val projection = requestProjection(request) ?: return consentDenied(request)
        val size = captureSize()
        val file = outputFile(request, "mp4")
        val recorder = createMediaRecorder()
        recorder.setVideoSource(MediaRecorder.VideoSource.SURFACE)
        recorder.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
        recorder.setOutputFile(file.absolutePath)
        recorder.setVideoSize(size.first - size.first % 2, size.second - size.second % 2)
        recorder.setVideoEncoder(MediaRecorder.VideoEncoder.H264)
        recorder.setVideoEncodingBitRate(request.payload.optInt("bitRate", 6_000_000).coerceIn(1_000_000, 20_000_000))
        recorder.setVideoFrameRate(request.payload.optInt("framesPerSecond", 30).coerceIn(15, 60))
        recorder.prepare()
        val callbackThread = HandlerThread("OneDesk-ScreenRecord").apply { start() }
        val callbackHandler = Handler(callbackThread.looper)
        val projectionCallback = object : MediaProjection.Callback() {
            override fun onStop() = Unit
        }
        projection.registerCallback(projectionCallback, callbackHandler)
        val display = projection.createVirtualDisplay(
            "OneDesk-Record",
            size.first - size.first % 2,
            size.second - size.second % 2,
            activity.resources.displayMetrics.densityDpi,
            DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
            recorder.surface,
            null,
            null,
        )
        return try {
            try {
                recorder.start()
                val duration = request.payload.optLong("durationMs", 5_000).coerceIn(1_000, 30_000)
                Thread.sleep(duration)
                recorder.stop()
                AndroidCapabilityResults.success(request, JSONObject().put("uri", Uri.fromFile(file).toString()).put("durationMs", duration))
            } catch (error: RuntimeException) {
                file.delete()
                AndroidCapabilityResults.error(request, "ScreenRecordFailed", error.message ?: "屏幕录制失败", "AndroidScreen", true)
            }
        } finally {
            display?.release()
            recorder.release()
            projection.unregisterCallback(projectionCallback)
            projection.stop()
            callbackThread.quitSafely()
            stopProjectionService()
        }
    }

    private fun requestProjection(request: AndroidCapabilityRequest): MediaProjection? {
        val manager = activity.getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        val result = consent.requestActivity(manager.createScreenCaptureIntent())
        if (result.resultCode != Activity.RESULT_OK || result.data == null) return null
        ContextCompat.startForegroundService(
            activity,
            Intent(activity, MediaProjectionForegroundService::class.java).setAction(MediaProjectionForegroundService.ACTION_START),
        )
        return manager.getMediaProjection(result.resultCode, result.data!!)
            ?: throw IllegalStateException("MediaProjectionUnavailable:${request.requestId}")
    }

    private fun stopProjectionService() {
        activity.startService(Intent(activity, MediaProjectionForegroundService::class.java).setAction(MediaProjectionForegroundService.ACTION_STOP))
    }

    private fun captureSize(): Pair<Int, Int> {
        val bounds = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            activity.windowManager.currentWindowMetrics.bounds
        } else {
            @Suppress("DEPRECATION")
            android.graphics.Rect().also { rect -> activity.windowManager.defaultDisplay.getRectSize(rect) }
        }
        return bounds.width().coerceAtLeast(2) to bounds.height().coerceAtLeast(2)
    }

    @Suppress("DEPRECATION")
    private fun createMediaRecorder(): MediaRecorder =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) MediaRecorder(activity) else MediaRecorder()

    private fun outputFile(request: AndroidCapabilityRequest, extension: String): File {
        val source = request.sourceKey.replace(Regex("[^A-Za-z0-9._-]"), "-")
        val root = File(activity.filesDir, "jsapi-media/$source").apply { mkdirs() }
        return File(root, "${request.requestId.replace(Regex("[^A-Za-z0-9._-]"), "-")}.$extension")
    }

    private fun ensurePermission(permission: String): Boolean {
        if (ContextCompat.checkSelfPermission(activity, permission) == PackageManager.PERMISSION_GRANTED) return true
        return consent.requestPermissions(arrayOf(permission))[permission] == true
    }

    private fun consentDenied(request: AndroidCapabilityRequest) = AndroidCapabilityResults.error(
        request,
        "UserConsentDenied",
        "用户未授予该系统能力",
        "AndroidConsent",
        true,
    )
}

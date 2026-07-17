package cc.onedesk.mobile

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.FrameLayout
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.core.content.ContextCompat
import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.MultiFormatReader
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.common.HybridBinarizer
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

class QrScannerController(
    private val activity: ComponentActivity,
    private val root: FrameLayout,
    private val onResult: (payload: String?, error: String?) -> Unit,
) {
    private val analysisExecutor = Executors.newSingleThreadExecutor()
    private val decoded = AtomicBoolean(false)
    private var overlay: FrameLayout? = null
    private var cameraProvider: ProcessCameraProvider? = null
    private val permissionLauncher = activity.registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) {
            showCamera()
        } else {
            onResult(null, "需要相机权限才能扫描桌面端二维码")
        }
    }

    fun start() {
        if (overlay != null) return
        if (ContextCompat.checkSelfPermission(activity, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
            showCamera()
        } else {
            permissionLauncher.launch(Manifest.permission.CAMERA)
        }
    }

    fun cancel(notifyFrontend: Boolean = true) {
        cameraProvider?.unbindAll()
        cameraProvider = null
        overlay?.let(root::removeView)
        overlay = null
        decoded.set(false)
        if (notifyFrontend) onResult(null, "已取消扫描")
    }

    fun destroy() {
        cancel(notifyFrontend = false)
        analysisExecutor.shutdownNow()
    }

    private fun showCamera() {
        decoded.set(false)
        val scannerLayer = FrameLayout(activity).apply {
            setBackgroundColor(Color.BLACK)
            isClickable = true
            isFocusable = true
        }
        val previewView = PreviewView(activity).apply {
            implementationMode = PreviewView.ImplementationMode.COMPATIBLE
            scaleType = PreviewView.ScaleType.FILL_CENTER
        }
        scannerLayer.addView(previewView, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        scannerLayer.addView(ScannerGuideView(activity), FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        val close = Button(activity).apply {
            text = "关闭"
            textSize = 14f
            setTextColor(Color.WHITE)
            setBackgroundColor(0x99020A17.toInt())
            setOnClickListener { cancel() }
        }
        scannerLayer.addView(close, FrameLayout.LayoutParams(dp(72), dp(44), Gravity.TOP or Gravity.END).apply {
            topMargin = dp(24)
            marginEnd = dp(20)
        })
        overlay = scannerLayer
        root.addView(scannerLayer, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))

        val providerFuture = ProcessCameraProvider.getInstance(activity)
        providerFuture.addListener({
            try {
                val provider = providerFuture.get()
                cameraProvider = provider
                val preview = Preview.Builder().build().also { it.surfaceProvider = previewView.surfaceProvider }
                val analysis = ImageAnalysis.Builder()
                    .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                    .build()
                analysis.setAnalyzer(analysisExecutor) { image -> analyze(image) }
                provider.unbindAll()
                provider.bindToLifecycle(activity, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
            } catch (error: Exception) {
                activity.runOnUiThread {
                    cancel(notifyFrontend = false)
                    onResult(null, error.message ?: "无法启动相机")
                }
            }
        }, ContextCompat.getMainExecutor(activity))
    }

    private fun analyze(image: ImageProxy) {
        if (decoded.get()) {
            image.close()
            return
        }
        try {
            val plane = image.planes.firstOrNull() ?: return
            val width = image.width
            val height = image.height
            val rowStride = plane.rowStride
            val sourceBytes = ByteArray(plane.buffer.remaining())
            plane.buffer.get(sourceBytes)
            val luminance = if (rowStride == width) {
                sourceBytes
            } else {
                ByteArray(width * height).also { compact ->
                    for (row in 0 until height) {
                        System.arraycopy(sourceBytes, row * rowStride, compact, row * width, width)
                    }
                }
            }
            var source = PlanarYUVLuminanceSource(luminance, width, height, 0, 0, width, height, false)
            repeat((image.imageInfo.rotationDegrees / 90) % 4) {
                if (source.isRotateSupported) source = source.rotateCounterClockwise() as PlanarYUVLuminanceSource
            }
            val reader = MultiFormatReader().apply {
                setHints(mapOf(DecodeHintType.POSSIBLE_FORMATS to listOf(BarcodeFormat.QR_CODE), DecodeHintType.TRY_HARDER to true))
            }
            val result = reader.decodeWithState(BinaryBitmap(HybridBinarizer(source)))
            val text = result.text.orEmpty()
            if (text.startsWith("onedesk://pair?") && decoded.compareAndSet(false, true)) {
                activity.runOnUiThread {
                    cancel(notifyFrontend = false)
                    onResult(text, null)
                }
            }
        } catch (_: Exception) {
            // 当前帧无法识别时继续分析下一帧。
        } finally {
            image.close()
        }
    }

    private fun dp(value: Int): Int = (value * activity.resources.displayMetrics.density).toInt()
}

private class ScannerGuideView(context: android.content.Context) : View(context) {
    private val shade = Paint().apply { color = 0x88000000.toInt() }
    private val frame = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = 0xff0ea5e9.toInt()
        style = Paint.Style.STROKE
        strokeWidth = resources.displayMetrics.density * 3
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val size = width * 0.72f
        val left = (width - size) / 2f
        val top = (height - size) / 2f
        val right = left + size
        val bottom = top + size
        canvas.drawRect(0f, 0f, width.toFloat(), top, shade)
        canvas.drawRect(0f, bottom, width.toFloat(), height.toFloat(), shade)
        canvas.drawRect(0f, top, left, bottom, shade)
        canvas.drawRect(right, top, width.toFloat(), bottom, shade)
        canvas.drawRoundRect(left, top, right, bottom, 28f, 28f, frame)
    }
}

package cc.onedesk.mobile

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.hardware.display.DisplayManager
import android.os.BatteryManager
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.util.DisplayMetrics
import org.json.JSONArray
import org.json.JSONObject
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

internal class AndroidDeviceCapabilityProvider(
    private val context: Context,
    private val environment: AndroidCapabilityEnvironment,
) {
    fun execute(request: AndroidCapabilityRequest): JSONObject = when (request.capability) {
        "device.identity" -> identity(request)
        "device.platform" -> platform(request)
        "device.display.list" -> displays(request)
        "device.power.status" -> power(request)
        "device.vibrate" -> vibrate(request)
        "sensor.accelerometer" -> sensor(request, Sensor.TYPE_ACCELEROMETER)
        "sensor.gyroscope" -> sensor(request, Sensor.TYPE_GYROSCOPE)
        "sensor.orientation" -> sensor(request, Sensor.TYPE_ROTATION_VECTOR)
        else -> AndroidCapabilityResults.error(request, "CapabilityHandlerMissing", "设备能力处理器不存在", "AndroidDevice", false)
    }

    private fun identity(request: AndroidCapabilityRequest): JSONObject = AndroidCapabilityResults.success(
        request,
        JSONObject()
            .put("deviceId", environment.deviceId())
            .put("displayName", Build.MODEL.ifBlank { "Android" })
            .put("platform", "android")
            .put("architecture", System.getProperty("os.arch") ?: "unknown"),
    )

    private fun platform(request: AndroidCapabilityRequest): JSONObject = AndroidCapabilityResults.success(
        request,
        JSONObject()
            .put("platform", "android")
            .put("release", Build.VERSION.RELEASE)
            .put("sdk", Build.VERSION.SDK_INT)
            .put("manufacturer", Build.MANUFACTURER)
            .put("model", Build.MODEL)
            .put("architecture", System.getProperty("os.arch") ?: "unknown"),
    )

    private fun displays(request: AndroidCapabilityRequest): JSONObject {
        val manager = context.getSystemService(Context.DISPLAY_SERVICE) as DisplayManager
        val payload = JSONArray()
        manager.displays.forEach { display ->
            val metrics = DisplayMetrics()
            @Suppress("DEPRECATION")
            display.getRealMetrics(metrics)
            payload.put(
                JSONObject()
                    .put("id", display.displayId.toString())
                    .put("name", display.name)
                    .put("width", metrics.widthPixels)
                    .put("height", metrics.heightPixels)
                    .put("density", metrics.density)
                    .put("rotation", display.rotation),
            )
        }
        return AndroidCapabilityResults.success(request, payload)
    }

    private fun power(request: AndroidCapabilityRequest): JSONObject {
        val battery = context.getSystemService(Context.BATTERY_SERVICE) as BatteryManager
        val percentage = battery.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY)
        val charging = battery.getIntProperty(BatteryManager.BATTERY_PROPERTY_STATUS) in setOf(
            BatteryManager.BATTERY_STATUS_CHARGING,
            BatteryManager.BATTERY_STATUS_FULL,
        )
        return AndroidCapabilityResults.success(
            request,
            JSONObject().put("batteryPercent", percentage).put("charging", charging),
        )
    }

    private fun vibrate(request: AndroidCapabilityRequest): JSONObject {
        val duration = request.payload.optLong("durationMs", 80).coerceIn(10, 5_000)
        val vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            (context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager).defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(Context.VIBRATOR_SERVICE) as Vibrator
        }
        if (!vibrator.hasVibrator()) {
            return AndroidCapabilityResults.error(request, "CapabilityNotSupported", "当前设备没有震动器", "AndroidDevice", false)
        }
        vibrator.vibrate(VibrationEffect.createOneShot(duration, VibrationEffect.DEFAULT_AMPLITUDE))
        return AndroidCapabilityResults.success(request, JSONObject().put("durationMs", duration))
    }

    private fun sensor(request: AndroidCapabilityRequest, sensorType: Int): JSONObject {
        val manager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
        val sensor = manager.getDefaultSensor(sensorType)
            ?: return AndroidCapabilityResults.error(request, "CapabilityNotSupported", "当前设备没有所需传感器", "AndroidSensor", false)
        val thread = HandlerThread("OneDesk-SensorRead").apply { start() }
        val latch = CountDownLatch(1)
        var captured: FloatArray? = null
        val listener = object : SensorEventListener {
            override fun onSensorChanged(event: SensorEvent) {
                captured = event.values.copyOf()
                latch.countDown()
            }

            override fun onAccuracyChanged(changedSensor: Sensor?, accuracy: Int) = Unit
        }
        return try {
            if (!manager.registerListener(listener, sensor, SensorManager.SENSOR_DELAY_UI, Handler(thread.looper))) {
                return AndroidCapabilityResults.error(request, "SensorRegistrationFailed", "无法注册传感器监听", "AndroidSensor", true)
            }
            if (!latch.await(2, TimeUnit.SECONDS)) {
                AndroidCapabilityResults.error(request, "SensorReadTimeout", "传感器未在规定时间内返回数据", "AndroidSensor", true)
            } else {
                val values = JSONArray()
                captured.orEmpty().forEach(values::put)
                AndroidCapabilityResults.success(
                    request,
                    JSONObject().put("sensor", sensor.stringType).put("accuracy", sensor.maximumRange).put("values", values),
                )
            }
        } finally {
            manager.unregisterListener(listener)
            thread.quitSafely()
        }
    }
}

private fun FloatArray?.orEmpty(): FloatArray = this ?: FloatArray(0)

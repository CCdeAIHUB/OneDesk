package cc.onedesk.mobile

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.sqrt

class AndroidDeviceTriggerMonitor(
    context: Context,
    private val onTrigger: (String) -> Unit,
) : SensorEventListener {
    private val sensors = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private var active = false
    private var lastShakeAt = 0L
    private var currentTilt: String? = null
    private var orientationQuadrant: Int? = null

    fun start() {
        if (active) return
        active = true
        sensors.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)?.let {
            sensors.registerListener(this, it, SensorManager.SENSOR_DELAY_GAME)
        }
        sensors.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR)?.let {
            sensors.registerListener(this, it, SensorManager.SENSOR_DELAY_UI)
        }
    }

    fun stop() {
        if (!active) return
        active = false
        sensors.unregisterListener(this)
        currentTilt = null
        orientationQuadrant = null
    }

    override fun onSensorChanged(event: SensorEvent) {
        when (event.sensor.type) {
            Sensor.TYPE_ACCELEROMETER -> handleAcceleration(event.values)
            Sensor.TYPE_ROTATION_VECTOR -> handleOrientation(event.values)
        }
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit

    private fun handleAcceleration(values: FloatArray) {
        if (values.size < 3) return
        val normalizedForce = sqrt(
            values[0] * values[0] + values[1] * values[1] + values[2] * values[2],
        ) / SensorManager.GRAVITY_EARTH
        val now = System.currentTimeMillis()
        if (normalizedForce > 2.7f && now - lastShakeAt >= 900) {
            lastShakeAt = now
            onTrigger("shake")
        }

        val x = values[0] / SensorManager.GRAVITY_EARTH
        val y = values[1] / SensorManager.GRAVITY_EARTH
        val next = when {
            abs(x) < 0.36f && abs(y) < 0.36f -> null
            abs(x) > abs(y) && x > 0.58f -> "tilt-left"
            abs(x) > abs(y) && x < -0.58f -> "tilt-right"
            y > 0.58f -> "tilt-down"
            y < -0.58f -> "tilt-up"
            else -> currentTilt
        }
        if (next != null && next != currentTilt) onTrigger(next)
        currentTilt = next
    }

    private fun handleOrientation(vector: FloatArray) {
        val matrix = FloatArray(9)
        val angles = FloatArray(3)
        SensorManager.getRotationMatrixFromVector(matrix, vector)
        SensorManager.getOrientation(matrix, angles)
        val degrees = angles[0] * 180f / PI.toFloat()
        val quadrant = (((degrees + 45f) / 90f).toInt() % 4 + 4) % 4
        val previous = orientationQuadrant
        orientationQuadrant = quadrant
        if (previous != null && previous != quadrant) onTrigger("orientation-change")
    }
}

package cc.onedesk.mobile

import android.app.Notification
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder

class MediaProjectionForegroundService : Service() {
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return START_NOT_STICKY
        }
        val notification = Notification.Builder(this, "onedesk-events")
            .setSmallIcon(android.R.drawable.ic_menu_camera)
            .setContentTitle("OneDesk 正在使用屏幕捕获")
            .setContentText("结束动作后会自动停止")
            .setOngoing(true)
            .build()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
        return START_NOT_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    companion object {
        const val ACTION_START = "cc.onedesk.mobile.mediaProjection.START"
        const val ACTION_STOP = "cc.onedesk.mobile.mediaProjection.STOP"
        private const val NOTIFICATION_ID = 6021
    }
}

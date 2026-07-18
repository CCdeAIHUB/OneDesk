package cc.onedesk.mobile

import android.content.Intent
import android.os.Looper
import androidx.activity.ComponentActivity
import androidx.activity.result.ActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import java.io.Closeable
import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

class AndroidConsentCoordinator(
    private val activity: ComponentActivity,
) : Closeable {
    private val pendingPermissions = AtomicReference<CompletableFuture<Map<String, Boolean>>?>()
    private val pendingActivity = AtomicReference<CompletableFuture<ActivityResult>?>()
    private val permissionLauncher = activity.registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        pendingPermissions.getAndSet(null)?.complete(result)
    }
    private val activityLauncher = activity.registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        pendingActivity.getAndSet(null)?.complete(result)
    }

    fun requestPermissions(permissions: Array<String>, timeoutSeconds: Long = 120): Map<String, Boolean> {
        check(Looper.myLooper() != Looper.getMainLooper()) { "ConsentRequestCannotBlockMainThread" }
        val future = CompletableFuture<Map<String, Boolean>>()
        check(pendingPermissions.compareAndSet(null, future)) { "ConsentRequestBusy" }
        activity.runOnUiThread { permissionLauncher.launch(permissions) }
        return try {
            future.get(timeoutSeconds, TimeUnit.SECONDS)
        } finally {
            pendingPermissions.compareAndSet(future, null)
        }
    }

    fun requestActivity(intent: Intent, timeoutSeconds: Long = 120): ActivityResult {
        check(Looper.myLooper() != Looper.getMainLooper()) { "ConsentRequestCannotBlockMainThread" }
        val future = CompletableFuture<ActivityResult>()
        check(pendingActivity.compareAndSet(null, future)) { "ConsentRequestBusy" }
        activity.runOnUiThread { activityLauncher.launch(intent) }
        return try {
            future.get(timeoutSeconds, TimeUnit.SECONDS)
        } finally {
            pendingActivity.compareAndSet(future, null)
        }
    }

    override fun close() {
        pendingPermissions.getAndSet(null)?.completeExceptionally(IllegalStateException("ActivityDestroyed"))
        pendingActivity.getAndSet(null)?.completeExceptionally(IllegalStateException("ActivityDestroyed"))
    }
}

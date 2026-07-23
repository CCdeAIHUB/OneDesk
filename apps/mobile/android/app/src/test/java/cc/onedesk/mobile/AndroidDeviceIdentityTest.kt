package cc.onedesk.mobile

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AndroidDeviceIdentityTest {
    @Test
    fun stableKeyIsDeterministicAndDoesNotExposeAndroidId() {
        val androidId = "a1b2c3d4e5f67890"
        val first = AndroidDeviceIdentity.stableDeviceKey(androidId)
        val second = AndroidDeviceIdentity.stableDeviceKey(androidId)

        assertEquals(first, second)
        assertTrue(first.matches(Regex("^android:[a-f0-9]{64}$")))
        assertFalse(first.contains(androidId))
        assertNotEquals(first, AndroidDeviceIdentity.stableDeviceKey("different-device"))
    }
}

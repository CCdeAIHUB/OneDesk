package cc.onedesk.mobile

import org.junit.Assert.assertEquals
import org.junit.Test

class ReconnectBackoffTest {
    @Test
    fun reconnectDelayGrowsAndStopsAtThirtySeconds() {
        // 场景：持续断网时必须持续重试，但退避上限要避免高频消耗电量和网络资源。
        assertEquals(2_000L, ReconnectBackoff.delayMilliseconds(0))
        assertEquals(4_000L, ReconnectBackoff.delayMilliseconds(1))
        assertEquals(8_000L, ReconnectBackoff.delayMilliseconds(2))
        assertEquals(30_000L, ReconnectBackoff.delayMilliseconds(5))
        assertEquals(30_000L, ReconnectBackoff.delayMilliseconds(20))
    }
}

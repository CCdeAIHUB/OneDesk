package cc.onedesk.mobile

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class MobileConnectionStateMachineTest {
    @Test
    fun connectionMustAuthenticateAndSynchronizeBeforeBecomingConnected() {
        // 场景：连接成功必须包含认证和缓存同步，不能收到网络响应后直接显示方案。
        val machine = MobileConnectionStateMachine()

        machine.begin("desktop-a")
        assertEquals(MobileConnectionPhase.Connecting, machine.state.phase)
        machine.authenticated("desktop-a")
        assertEquals(MobileConnectionPhase.Synchronizing, machine.state.phase)
        machine.synchronized("desktop-a")

        assertEquals(MobileConnectionPhase.Connected, machine.state.phase)
        assertEquals("desktop-a", machine.state.desktopId)
    }

    @Test
    fun secondDesktopCannotReplaceAnActiveConnectionWithoutDisconnect() {
        // 场景：移动端同时只能连接一个桌面端，非法切换必须被拒绝。
        val machine = MobileConnectionStateMachine()
        machine.begin("desktop-a")
        machine.authenticated("desktop-a")
        machine.synchronized("desktop-a")

        assertThrows(IllegalStateException::class.java) { machine.begin("desktop-b") }
    }
}

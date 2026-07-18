package cc.onedesk.mobile

enum class MobileConnectionPhase {
    Disconnected,
    Connecting,
    Synchronizing,
    Connected,
    Failed,
}

data class MobileConnectionState(
    val phase: MobileConnectionPhase,
    val desktopId: String? = null,
    val errorCode: String? = null,
    val message: String? = null,
)

class MobileConnectionStateMachine {
    var state = MobileConnectionState(MobileConnectionPhase.Disconnected)
        private set

    @Synchronized
    fun begin(desktopId: String) {
        require(desktopId.isNotBlank()) { "desktopId 不能为空" }
        check(state.phase == MobileConnectionPhase.Disconnected || state.phase == MobileConnectionPhase.Failed) {
            "当前连接必须先断开，不能直接连接另一个桌面端"
        }
        state = MobileConnectionState(MobileConnectionPhase.Connecting, desktopId)
    }

    @Synchronized
    fun authenticated(desktopId: String) {
        requireTransition(MobileConnectionPhase.Connecting, desktopId)
        state = MobileConnectionState(MobileConnectionPhase.Synchronizing, desktopId)
    }

    @Synchronized
    fun synchronized(desktopId: String) {
        requireTransition(MobileConnectionPhase.Synchronizing, desktopId)
        state = MobileConnectionState(MobileConnectionPhase.Connected, desktopId)
    }

    @Synchronized
    fun fail(desktopId: String?, errorCode: String, message: String) {
        check(state.phase != MobileConnectionPhase.Disconnected) { "未开始连接时不能进入失败状态" }
        if (desktopId != null && state.desktopId != null) {
            check(state.desktopId == desktopId) { "失败事件不属于当前桌面端" }
        }
        state = MobileConnectionState(MobileConnectionPhase.Failed, desktopId ?: state.desktopId, errorCode, message)
    }

    @Synchronized
    fun disconnect() {
        state = MobileConnectionState(MobileConnectionPhase.Disconnected)
    }

    private fun requireTransition(expected: MobileConnectionPhase, desktopId: String) {
        check(state.phase == expected) { "非法连接状态转换：${state.phase} -> $expected" }
        check(state.desktopId == desktopId) { "状态事件不属于当前桌面端" }
    }
}

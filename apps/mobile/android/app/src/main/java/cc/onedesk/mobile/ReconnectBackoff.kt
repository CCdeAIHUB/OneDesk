package cc.onedesk.mobile

object ReconnectBackoff {
    fun delayMilliseconds(attempt: Int): Long {
        val normalized = attempt.coerceIn(0, 5)
        return (2_000L shl normalized).coerceAtMost(30_000L)
    }
}

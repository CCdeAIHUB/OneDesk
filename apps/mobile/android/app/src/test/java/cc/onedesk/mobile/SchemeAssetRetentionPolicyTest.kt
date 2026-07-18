package cc.onedesk.mobile

import org.junit.Assert.assertEquals
import org.junit.Test

class SchemeAssetRetentionPolicyTest {
    @Test
    fun replacingOneDesktopSchemeNeverDeletesAnotherDesktopAssets() {
        // 场景：更新桌面 A 的方案时，只能清理 A 的旧哈希，桌面 B 的离线缓存必须保留。
        val directories = listOf(
            SchemeAssetDirectory("desktop-a", "hash-old-a"),
            SchemeAssetDirectory("desktop-a", "hash-new-a"),
            SchemeAssetDirectory("desktop-b", "hash-b"),
        )

        val stale = SchemeAssetRetentionPolicy.staleDirectories("desktop-a", "hash-new-a", directories)

        assertEquals(listOf(SchemeAssetDirectory("desktop-a", "hash-old-a")), stale)
    }
}

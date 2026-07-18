package cc.onedesk.mobile

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SchemePermissionPolicyTest {
    @Test
    fun exactAndCategoryGrantsAreAcceptedButUnknownSourcesAreDenied() {
        // 场景：组件在本机调用 JSAPI 时，只能使用桌面端随方案签发的精确授权或大类授权。
        val grants = mapOf(
            "component:exact" to setOf("clipboard.write"),
            "component:category" to setOf("file.*"),
        )

        assertTrue(SchemePermissionPolicy.isGranted(grants, "component:exact", "clipboard.write"))
        assertFalse(SchemePermissionPolicy.isGranted(grants, "component:exact", "clipboard.read"))
        assertTrue(SchemePermissionPolicy.isGranted(grants, "component:category", "file.private.read"))
        assertFalse(SchemePermissionPolicy.isGranted(grants, "component:missing", "file.private.read"))
    }
}

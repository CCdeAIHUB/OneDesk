package cc.onedesk.mobile

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class CapabilityCatalogTest {
    @Test
    fun everyCanonicalCapabilityHasExplicitAndroidRegistration() {
        // 场景：能力目录中的 API 在 Android 上必须有处理器或明确的不支持状态，不能静默缺失。
        val canonical = checkNotNull(javaClass.classLoader?.getResourceAsStream("canonical-capability-ids.txt")) {
            "共享能力目录未进入 Android 测试资源"
        }.bufferedReader().useLines { lines -> lines.filter(String::isNotBlank).toSet() }

        assertEquals(canonical, AndroidCapabilityCatalog.entries.keys)
        assertTrue(AndroidCapabilityCatalog.entries.values.all { it.availability != CapabilityAvailability.Missing })
    }

    @Test
    fun supportedCapabilitiesMustHaveConcreteLocalHandlers() {
        // 场景：目录只有在执行器真正注册处理器时才能标记 Supported，防止 UI 宣称支持但调用失败。
        val handlerIds = AndroidLocalCapabilityHandlers.ids
        val declaredSupported = AndroidCapabilityCatalog.entries.values
            .filter { it.availability == CapabilityAvailability.Supported }
            .map { it.id }
            .toSet()

        assertEquals(declaredSupported, handlerIds)
    }

    @Test
    fun consentCapabilitiesMustHaveConcreteConsentHandlers() {
        // 场景：需要系统授权的能力也必须有真实执行器，不能永久停留在“需要授权”的占位错误。
        val declaredConsent = AndroidCapabilityCatalog.entries.values
            .filter { it.availability == CapabilityAvailability.RequiresUserConsent }
            .map { it.id }
            .toSet()

        assertEquals(declaredConsent, AndroidConsentCapabilityHandlers.ids)
    }
}

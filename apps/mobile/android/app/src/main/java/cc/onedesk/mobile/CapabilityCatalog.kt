package cc.onedesk.mobile

enum class CapabilityAvailability {
    Supported,
    RequiresUserConsent,
    Routed,
    Unsupported,
    Missing,
}

data class AndroidCapabilityRegistration(
    val id: String,
    val availability: CapabilityAvailability,
    val highRisk: Boolean,
)

object AndroidCapabilityCatalog {
    val entries: Map<String, AndroidCapabilityRegistration> = listOf(
        supported("device.identity"),
        supported("device.platform"),
        supported("device.display.list"),
        supported("device.power.status"),
        supported("device.vibrate"),
        supported("file.private.read"),
        supported("file.private.write"),
        supported("file.private.delete"),
        consent("file.external.read"),
        consent("file.external.write"),
        consent("file.external.delete"),
        supported("clipboard.read", highRisk = true),
        supported("clipboard.write", highRisk = true),
        supported("notification.inApp"),
        supported("notification.native"),
        unsupported("input.hotkey.register"),
        unsupported("input.hotkey.unregister"),
        unsupported("input.keyboardMouseSimulation"),
        supported("process.launch", highRisk = true),
        unsupported("process.list"),
        unsupported("process.control"),
        unsupported("shell.execute"),
        unsupported("memory.read"),
        unsupported("memory.write"),
        supported("network.access", highRisk = true),
        supported("sensor.accelerometer", highRisk = true),
        supported("sensor.gyroscope", highRisk = true),
        supported("sensor.orientation", highRisk = true),
        consent("camera.access"),
        consent("microphone.access"),
        consent("screen.capture"),
        consent("screen.record"),
        supported("credential.access", highRisk = true),
        routed("plugin.invoke"),
        supported("scheme.active.get"),
        supported("scheme.page.switch"),
        supported("scheme.cache.status"),
        supported("log.write"),
    ).associateBy(AndroidCapabilityRegistration::id)

    private fun supported(id: String, highRisk: Boolean = false) =
        AndroidCapabilityRegistration(id, CapabilityAvailability.Supported, highRisk)

    private fun consent(id: String, highRisk: Boolean = true) =
        AndroidCapabilityRegistration(id, CapabilityAvailability.RequiresUserConsent, highRisk)

    private fun routed(id: String) =
        AndroidCapabilityRegistration(id, CapabilityAvailability.Routed, highRisk = true)

    private fun unsupported(id: String) =
        AndroidCapabilityRegistration(id, CapabilityAvailability.Unsupported, highRisk = true)
}

// 此文件由 packages/protocol/capabilities.json 生成，请勿手工修改。
// catalog-sha256: c9e4f17c1fc9a1292ca39efa29016ebf4a83ef6b32dad4cc378601ad3085350d
import Foundation

struct GeneratedCapabilityDefinition {
    let id: String
    let category: String
    let highRisk: Bool
}

enum GeneratedCapabilityCatalog {
    static let entries: [String: GeneratedCapabilityDefinition] = [
        "device.identity": .init(id: "device.identity", category: "device", highRisk: false),
        "device.platform": .init(id: "device.platform", category: "device", highRisk: false),
        "device.display.list": .init(id: "device.display.list", category: "device", highRisk: false),
        "device.power.status": .init(id: "device.power.status", category: "device", highRisk: false),
        "device.vibrate": .init(id: "device.vibrate", category: "device", highRisk: false),
        "file.private.read": .init(id: "file.private.read", category: "file", highRisk: false),
        "file.private.write": .init(id: "file.private.write", category: "file", highRisk: false),
        "file.private.delete": .init(id: "file.private.delete", category: "file", highRisk: false),
        "file.external.read": .init(id: "file.external.read", category: "file", highRisk: true),
        "file.external.write": .init(id: "file.external.write", category: "file", highRisk: true),
        "file.external.delete": .init(id: "file.external.delete", category: "file", highRisk: true),
        "clipboard.read": .init(id: "clipboard.read", category: "clipboard", highRisk: true),
        "clipboard.write": .init(id: "clipboard.write", category: "clipboard", highRisk: true),
        "notification.inApp": .init(id: "notification.inApp", category: "notification", highRisk: false),
        "notification.native": .init(id: "notification.native", category: "notification", highRisk: false),
        "input.hotkey.register": .init(id: "input.hotkey.register", category: "input", highRisk: true),
        "input.hotkey.unregister": .init(id: "input.hotkey.unregister", category: "input", highRisk: true),
        "input.keyboardMouseSimulation": .init(id: "input.keyboardMouseSimulation", category: "input", highRisk: true),
        "process.launch": .init(id: "process.launch", category: "process", highRisk: true),
        "process.list": .init(id: "process.list", category: "process", highRisk: true),
        "process.control": .init(id: "process.control", category: "process", highRisk: true),
        "shell.execute": .init(id: "shell.execute", category: "shell", highRisk: true),
        "memory.read": .init(id: "memory.read", category: "memory", highRisk: true),
        "memory.write": .init(id: "memory.write", category: "memory", highRisk: true),
        "network.access": .init(id: "network.access", category: "network", highRisk: true),
        "sensor.accelerometer": .init(id: "sensor.accelerometer", category: "sensor", highRisk: true),
        "sensor.gyroscope": .init(id: "sensor.gyroscope", category: "sensor", highRisk: true),
        "sensor.orientation": .init(id: "sensor.orientation", category: "sensor", highRisk: true),
        "camera.access": .init(id: "camera.access", category: "camera", highRisk: true),
        "microphone.access": .init(id: "microphone.access", category: "microphone", highRisk: true),
        "screen.capture": .init(id: "screen.capture", category: "screen", highRisk: true),
        "screen.record": .init(id: "screen.record", category: "screen", highRisk: true),
        "credential.access": .init(id: "credential.access", category: "credential", highRisk: true),
        "plugin.invoke": .init(id: "plugin.invoke", category: "plugin", highRisk: true),
        "scheme.active.get": .init(id: "scheme.active.get", category: "scheme", highRisk: false),
        "scheme.page.switch": .init(id: "scheme.page.switch", category: "scheme", highRisk: false),
        "scheme.cache.status": .init(id: "scheme.cache.status", category: "scheme", highRisk: false),
        "log.write": .init(id: "log.write", category: "log", highRisk: false),
    ]
    static let ids = Set(entries.keys)
    static let highRiskIds = Set(entries.values.filter(\.highRisk).map(\.id))
}

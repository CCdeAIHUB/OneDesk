import Foundation
import UIKit

final class MobileRuntime {
    private let defaults: UserDefaults
    private let knownDesktops: KnownDesktopStore
    private let logs: MobileLogStore
    private let gateway: MobileGatewayClient
    private var schemeCache: SchemeCacheService?
    private var capabilityExecutor: IosCapabilityExecutor?
    private var initializationError: Error?

    var emitFrontendEvent: ((String, JSONObject) -> Void)?
    var startQrScanner: (() -> Void)?
    var cancelQrScanner: (() -> Void)?

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        if defaults.string(forKey: "onedesk.installDeviceId") == nil {
            defaults.set("ios-\(UUID().uuidString.lowercased())", forKey: "onedesk.installDeviceId")
        }
        knownDesktops = KnownDesktopStore(defaults: defaults)
        logs = MobileLogStore(defaults: defaults, deviceId: {
            defaults.string(forKey: "onedesk.assignedDeviceId")
                ?? defaults.string(forKey: "onedesk.installDeviceId")
                ?? "ios-unknown"
        })
        gateway = MobileGatewayClient(deviceId: {
            defaults.string(forKey: "onedesk.assignedDeviceId")
                ?? defaults.string(forKey: "onedesk.installDeviceId")
                ?? "ios-unknown"
        }, logs: logs)

        do {
            schemeCache = try SchemeCacheService(gateway: gateway, logs: logs)
            capabilityExecutor = try IosCapabilityExecutor(
                deviceId: { [weak self] in self?.currentDeviceId ?? "ios-unknown" },
                logs: logs,
                isAllowed: { [weak self] source, capability in
                    self?.isCapabilityGranted(sourceKey: source, capability: capability) == true
                },
                emitFrontendEvent: { [weak self] name, payload in
                    self?.emitFrontendEvent?(name, payload)
                })
        } catch {
            initializationError = error
            logs.append("Error", "Initialization", error.localizedDescription)
        }

        gateway.onSchemeEvent = { [weak self] desktop, credential, descriptor, eventId in
            self?.handleSchemeEvent(desktop: desktop, credential: credential, descriptor: descriptor, eventId: eventId) == true
        }
        gateway.onJsApiEvent = { [weak self] capability, payload, requestId, sourceKey in
            self?.capabilityExecutor?.execute(
                capability: capability,
                payload: payload,
                requestId: requestId,
                sourceKey: sourceKey,
                enforcePermission: false)
                ?? ["ok": false, "errorCode": "CapabilityRuntimeUnavailable", "message": "iOS 能力运行时不可用"]
        }
    }

    var currentDeviceId: String {
        defaults.string(forKey: "onedesk.assignedDeviceId")
            ?? defaults.string(forKey: "onedesk.installDeviceId")
            ?? "ios-unknown"
    }

    func handle(method: String, arguments: [Any]) -> String {
        if let initializationError {
            return JSONSupport.response(ok: false, errorCode: "MobileRuntimeInitializationFailed",
                                        message: initializationError.localizedDescription)
        }
        do {
            switch method {
            case "listKnownDesktops":
                return knownDesktops.frontendJSON()
            case "connect":
                guard arguments.count >= 3 else { return invalidArguments() }
                return try connect(
                    host: arguments[0] as? String ?? "",
                    port: (arguments[1] as? NSNumber)?.intValue ?? 0,
                    code: arguments[2] as? String ?? "")
            case "connectByQr":
                return try connectByQr(arguments.first as? String ?? "")
            case "startQrScan":
                DispatchQueue.main.async { [weak self] in self?.startQrScanner?() }
                return JSONSupport.response(ok: true, payload: ["started": true])
            case "cancelQrScan":
                DispatchQueue.main.async { [weak self] in self?.cancelQrScanner?() }
                return JSONSupport.response(ok: true)
            case "getCachedScheme":
                let desktopId = arguments.first as? String ?? ""
                guard let cache = schemeCache?.get(desktopId: desktopId) else {
                    return JSONSupport.response(ok: false, errorCode: "SchemeCacheMissing", message: "尚未缓存方案")
                }
                return JSONSupport.response(ok: true, payload: cache)
            case "refreshScheme":
                return try refreshScheme(desktopId: arguments.first as? String ?? "")
            case "setDisplayRatio":
                guard arguments.count >= 2 else { return invalidArguments() }
                return setDisplayRatio(
                    width: (arguments[0] as? NSNumber)?.doubleValue ?? 0,
                    height: (arguments[1] as? NSNumber)?.doubleValue ?? 0)
            case "callJsApi":
                return try callJsApi(arguments)
            default:
                return JSONSupport.response(ok: false, errorCode: "CapabilityNotSupported", message: "未知移动端桥接方法")
            }
        } catch {
            logs.append("Error", "NativeBridge", "\(method)：\(error.localizedDescription)")
            return JSONSupport.response(ok: false, errorCode: "NativeBridgeFailed", message: error.localizedDescription)
        }
    }

    func stop() {
        logs.setOnlineSink(nil)
        gateway.stop()
    }

    private func connect(host: String, port: Int, code: String) throws -> String {
        let normalizedHost = host.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedHost.isEmpty, (1...65535).contains(port) else {
            return JSONSupport.response(ok: false, errorCode: "InvalidEndpoint", message: "桌面端 IP 或端口无效")
        }
        let known = knownDesktops.find(host: normalizedHost, port: port)
        if known == nil && code.range(of: "^\\d{6}$", options: .regularExpression) == nil {
            return JSONSupport.response(ok: false, errorCode: "InvalidVerificationCode", message: "验证码必须为 6 位数字")
        }
        return try connectInternal(host: normalizedHost, port: port, code: code, known: known)
    }

    private func connectByQr(_ raw: String) throws -> String {
        guard let components = URLComponents(string: raw),
              components.scheme == "onedesk", components.host == "pair" else {
            return JSONSupport.response(ok: false, errorCode: "InvalidQrPayload", message: "这不是 OneDesk 配对二维码")
        }
        let values = Dictionary(uniqueKeysWithValues: (components.queryItems ?? []).map { ($0.name, $0.value ?? "") })
        guard let host = values["host"], !host.isEmpty else {
            return JSONSupport.response(ok: false, errorCode: "InvalidQrPayload", message: "二维码缺少桌面端 IP")
        }
        return try connect(host: host, port: Int(values["port"] ?? "") ?? 48320, code: values["code"] ?? "")
    }

    private func connectInternal(
        host: String,
        port: Int,
        code: String,
        known: (KnownDesktop, String)?
    ) throws -> String {
        let hasTrust = known != nil
        var request: JSONObject = [
            "type": hasTrust ? "connect" : "pair",
            "code": hasTrust ? NSNull() : code,
            "deviceId": currentDeviceId,
            "displayName": UIDevice.current.name,
            "platform": "ios",
            "architecture": Self.architecture,
            "trustCredential": known?.1 ?? NSNull(),
            "logs": logs.snapshot(),
        ]
        request["requestId"] = "req-\(UUID().uuidString.lowercased())"
        let response = try gateway.request(
            host: host,
            port: port,
            payload: request,
            expectedFingerprint: known?.0.gatewayFingerprint)
        guard response.bool("ok"), let payload = response.object("payload"),
              let desktopIdentity = payload.object("desktop"),
              let descriptor = payload.object("scheme") else {
            return try JSONSupport.string(response)
        }

        if let assignedId = payload.object("assignedMobile")?.string("deviceId"), !assignedId.isEmpty {
            defaults.set(assignedId, forKey: "onedesk.assignedDeviceId")
        }
        let desktopId = desktopIdentity.string("deviceId")
        let credential = payload.string("trustCredential").isEmpty ? (known?.1 ?? "") : payload.string("trustCredential")
        guard !desktopId.isEmpty, !credential.isEmpty else {
            throw OneDeskMobileError.invalidPayload("桌面端未返回长期信任身份")
        }
        let desktop = KnownDesktop(
            desktopId: desktopId,
            name: desktopIdentity.string("displayName").isEmpty ? "OneDesk Desktop" : desktopIdentity.string("displayName"),
            host: host,
            port: port,
            trusted: true,
            gatewayFingerprint: try gateway.serverFingerprint(),
            schemeVersion: descriptor.string("version").isEmpty ? "0" : descriptor.string("version"),
            schemeHash: descriptor.string("hash"),
            lastConnectedAt: ISO8601DateFormatter().string(from: Date()))
        try knownDesktops.upsert(desktop, credential: credential)
        guard let schemeCache else { throw OneDeskMobileError.integrity("方案缓存服务不可用") }
        let result = try schemeCache.downloadAndCache(desktop: desktop, credential: credential, descriptor: descriptor)
        try knownDesktops.updateScheme(desktopId: desktopId, version: result.version, hash: result.hash)
        knownDesktops.activeDesktopId = desktopId
        logs.clear()
        try gateway.startSubscription(desktop: desktop, credential: credential)
        logs.setOnlineSink { [weak self] entry in
            self?.gateway.uploadLog(desktop: desktop, credential: credential, entry: entry) == true
        }
        return JSONSupport.response(ok: true, payload: [
            "desktop": desktop.frontendObject(),
            "deviceId": currentDeviceId,
            "hasScheme": result.hasScheme,
            "cacheUpdated": result.updated,
        ])
    }

    private func refreshScheme(desktopId: String) throws -> String {
        guard let (desktop, credential) = knownDesktops.find(desktopId: desktopId) else {
            return JSONSupport.response(ok: false, errorCode: "DesktopNotFound", message: "未找到该桌面端信任记录")
        }
        let response = try gateway.request(
            host: desktop.host,
            port: desktop.port,
            payload: gateway.authorizedRequest(type: "scheme", credential: credential),
            expectedFingerprint: desktop.gatewayFingerprint)
        guard response.bool("ok"), let descriptor = response.object("payload")?.object("scheme") else {
            return try JSONSupport.string(response)
        }
        guard let schemeCache else { throw OneDeskMobileError.integrity("方案缓存服务不可用") }
        let result = try schemeCache.downloadAndCache(desktop: desktop, credential: credential, descriptor: descriptor)
        try knownDesktops.updateScheme(desktopId: desktopId, version: result.version, hash: result.hash)
        return JSONSupport.response(ok: true, payload: [
            "cacheUpdated": result.updated,
            "hasScheme": result.hasScheme,
            "version": result.version,
            "hash": result.hash,
        ])
    }

    private func callJsApi(_ arguments: [Any]) throws -> String {
        guard arguments.count >= 6 else { return invalidArguments() }
        let targetDeviceId = arguments[0] as? String ?? ""
        let capability = arguments[1] as? String ?? ""
        let payloadData = (arguments[2] as? String ?? "{}").data(using: .utf8) ?? Data("{}".utf8)
        let payload = (try? JSONSupport.object(from: payloadData)) ?? [:]
        let requestId = "req-\(UUID().uuidString.lowercased())"
        let componentId = arguments[5] as? String ?? ""
        if targetDeviceId == currentDeviceId {
            let response = capabilityExecutor?.execute(
                capability: capability,
                payload: payload,
                requestId: requestId,
                sourceKey: "component:\(componentId)")
                ?? ["ok": false, "errorCode": "CapabilityRuntimeUnavailable", "message": "iOS 能力运行时不可用"]
            return try JSONSupport.string(response)
        }
        guard let desktopId = knownDesktops.activeDesktopId,
              let (desktop, credential) = knownDesktops.find(desktopId: desktopId) else {
            return JSONSupport.response(ok: false, errorCode: "DesktopOffline", message: "当前未连接桌面端")
        }
        var request = gateway.authorizedRequest(type: "jsapi", credential: credential)
        request["requestId"] = requestId
        request["schemeId"] = arguments[3] as? String ?? ""
        request["pageId"] = arguments[4] as? String ?? ""
        request["componentId"] = componentId
        request["targetDeviceId"] = targetDeviceId
        request["capability"] = capability
        request["payload"] = payload
        return try JSONSupport.string(gateway.request(
            host: desktop.host,
            port: desktop.port,
            payload: request,
            expectedFingerprint: desktop.gatewayFingerprint))
    }

    private func handleSchemeEvent(
        desktop: KnownDesktop,
        credential: String,
        descriptor: JSONObject,
        eventId: String
    ) -> Bool {
        guard !eventId.isEmpty, let schemeCache else { return false }
        do {
            let result = try schemeCache.downloadAndCache(desktop: desktop, credential: credential, descriptor: descriptor)
            try knownDesktops.updateScheme(desktopId: desktop.desktopId, version: result.version, hash: result.hash)
            emitFrontendEvent?("__oneDeskHandleSchemeUpdated", [
                "desktopId": desktop.desktopId,
                "version": result.version,
                "hash": result.hash,
            ])
            return true
        } catch {
            logs.append("Error", "SchemePush", error.localizedDescription)
            return false
        }
    }

    private func isCapabilityGranted(sourceKey: String, capability: String) -> Bool {
        if sourceKey == "system" { return true }
        guard let desktopId = knownDesktops.activeDesktopId,
              let cache = schemeCache?.get(desktopId: desktopId),
              let rows = cache["permissionGrants"] as? [JSONObject],
              let row = rows.first(where: { $0.string("sourceKey") == sourceKey }) else {
            return false
        }
        let grants = Set(row.array("capabilities").compactMap { $0 as? String })
        let category = capability.split(separator: ".").first.map(String.init) ?? capability
        return grants.contains(capability) || grants.contains("\(category).*")
    }

    private func setDisplayRatio(width: Double, height: Double) -> String {
        guard width.isFinite, height.isFinite, width > 0, height > 0 else {
            return JSONSupport.response(ok: false, errorCode: "InvalidDisplayRatio", message: "页面宽高比无效")
        }
        DispatchQueue.main.async {
            guard #available(iOS 16.0, *),
                  let scene = UIApplication.shared.connectedScenes.compactMap({ $0 as? UIWindowScene }).first else { return }
            let orientation: UIInterfaceOrientationMask = width > height ? .landscape : .portrait
            scene.requestGeometryUpdate(.iOS(interfaceOrientations: orientation))
            UIViewController.attemptRotationToDeviceOrientation()
        }
        return JSONSupport.response(ok: true, payload: ["width": width, "height": height])
    }

    private func invalidArguments() -> String {
        JSONSupport.response(ok: false, errorCode: "InvalidPayload", message: "请求参数不完整")
    }

    private static var architecture: String {
        #if arch(arm64)
        return "arm64"
        #elseif arch(x86_64)
        return "x86_64"
        #else
        return "unknown"
        #endif
    }
}

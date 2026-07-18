import AudioToolbox
import Foundation
import UIKit
import UserNotifications

/// iOS 能力目录保持完整：未实现或平台禁止的能力返回结构化错误，绝不伪造成功。
final class IosCapabilityExecutor {
    private let deviceId: () -> String
    private let logs: MobileLogStore
    private let isAllowed: (String, String) -> Bool
    private let emitFrontendEvent: (String, JSONObject) -> Void
    private let privateRoot: URL

    init(
        deviceId: @escaping () -> String,
        logs: MobileLogStore,
        isAllowed: @escaping (String, String) -> Bool,
        emitFrontendEvent: @escaping (String, JSONObject) -> Void
    ) throws {
        self.deviceId = deviceId
        self.logs = logs
        self.isAllowed = isAllowed
        self.emitFrontendEvent = emitFrontendEvent
        privateRoot = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true).appendingPathComponent("OneDesk/Private", isDirectory: true)
        try FileManager.default.createDirectory(at: privateRoot, withIntermediateDirectories: true)
    }

    func execute(
        capability: String,
        payload: JSONObject,
        requestId: String,
        sourceKey: String,
        enforcePermission: Bool = true
    ) -> JSONObject {
        guard GeneratedCapabilityCatalog.ids.contains(capability) else {
            return failure(requestId, "CapabilityNotFound", "能力目录中不存在 \(capability)", capability)
        }
        guard !enforcePermission || sourceKey == "system" || isAllowed(sourceKey, capability) else {
            return failure(requestId, "PermissionDenied", "调用方未获得该能力授权", capability)
        }
        do {
            let value: Any
            switch capability {
            case "device.identity":
                value = ["deviceId": deviceId(), "displayName": UIDevice.current.name]
            case "device.platform":
                value = ["platform": "ios", "version": UIDevice.current.systemVersion, "model": UIDevice.current.model]
            case "device.display.list":
                value = onMain { ["count": UIScreen.screens.count, "scale": UIScreen.main.scale] }
            case "device.power.status":
                value = onMain {
                    UIDevice.current.isBatteryMonitoringEnabled = true
                    return [
                        "level": max(0, UIDevice.current.batteryLevel),
                        "state": UIDevice.current.batteryState.rawValue,
                    ]
                }
            case "device.vibrate":
                onMain { AudioServicesPlaySystemSound(kSystemSoundID_Vibrate) }
                value = ["triggered": true]
            case "file.private.read":
                value = try readPrivate(payload)
            case "file.private.write":
                value = try writePrivate(payload)
            case "file.private.delete":
                value = try deletePrivate(payload)
            case "clipboard.read":
                value = onMain { ["text": UIPasteboard.general.string ?? ""] }
            case "clipboard.write":
                let text = payload.string("text")
                onMain { UIPasteboard.general.string = text }
                value = ["written": true]
            case "notification.inApp":
                emitFrontendEvent("__oneDeskHandleInAppNotification", payload)
                value = ["delivered": true]
            case "notification.native":
                scheduleNotification(payload)
                value = ["scheduled": true]
            case "scheme.page.switch":
                emitFrontendEvent("__oneDeskHandlePageSwitch", payload)
                value = ["delivered": true]
            case "log.write":
                logs.append(payload.string("level").isEmpty ? "Info" : payload.string("level"),
                            payload.string("category").isEmpty ? "JsApi" : payload.string("category"),
                            payload.string("message"), context: payload.object("context") ?? [:])
                value = ["written": true]
            default:
                return failure(requestId, "CapabilityNotSupported", "iOS 不支持或尚未实现该能力", capability)
            }
            return ["ok": true, "requestId": requestId, "payload": value]
        } catch {
            return failure(requestId, "CapabilityExecutionFailed", error.localizedDescription, capability)
        }
    }

    private func readPrivate(_ payload: JSONObject) throws -> JSONObject {
        let url = try privateURL(payload.string("path"))
        let data = try Data(contentsOf: url)
        return ["data": data.base64EncodedString(), "encoding": "base64", "bytes": data.count]
    }

    private func writePrivate(_ payload: JSONObject) throws -> JSONObject {
        let url = try privateURL(payload.string("path"))
        guard let data = Data(base64Encoded: payload.string("data")) else {
            throw OneDeskMobileError.invalidPayload("文件数据必须使用 Base64")
        }
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: url, options: .atomic)
        return ["written": true, "bytes": data.count]
    }

    private func deletePrivate(_ payload: JSONObject) throws -> JSONObject {
        let url = try privateURL(payload.string("path"))
        if FileManager.default.fileExists(atPath: url.path) { try FileManager.default.removeItem(at: url) }
        return ["deleted": true]
    }

    private func privateURL(_ path: String) throws -> URL {
        guard !path.isEmpty else { throw OneDeskMobileError.invalidPayload("文件路径不能为空") }
        let root = privateRoot.standardizedFileURL
        let candidate = root.appendingPathComponent(path).standardizedFileURL
        guard candidate.path == root.path || candidate.path.hasPrefix(root.path + "/") else {
            throw OneDeskMobileError.permission("文件路径越过组件私有目录")
        }
        return candidate
    }

    private func scheduleNotification(_ payload: JSONObject) {
        let center = UNUserNotificationCenter.current()
        center.requestAuthorization(options: [.alert, .sound]) { granted, error in
            guard granted, error == nil else { return }
            let content = UNMutableNotificationContent()
            content.title = payload.string("title").isEmpty ? "OneDesk" : payload.string("title")
            content.body = payload.string("message")
            content.sound = .default
            center.add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
        }
    }

    private func failure(_ requestId: String, _ code: String, _ message: String, _ capability: String) -> JSONObject {
        [
            "ok": false,
            "requestId": requestId,
            "errorCode": code,
            "message": message,
            "highRisk": GeneratedCapabilityCatalog.highRiskIds.contains(capability),
        ]
    }

    private func onMain<T>(_ work: () -> T) -> T {
        if Thread.isMainThread { return work() }
        return DispatchQueue.main.sync(execute: work)
    }

}

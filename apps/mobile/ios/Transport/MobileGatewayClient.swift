import Foundation
import UIKit

final class MobileGatewayClient {
    private let deviceId: () -> String
    private let logs: MobileLogStore
    private let eventQueue = DispatchQueue(label: "cc.onedesk.mobile.gateway-events")
    private let state = MobileConnectionStateMachine()
    private let lock = NSLock()
    private var transport: MsQuicTransport?
    private var endpointKey: String?
    private var activeDesktop: (KnownDesktop, String)?
    private var heartbeat: DispatchSourceTimer?
    private var reconnectAttempt = 0
    private var running = false

    var onSchemeEvent: ((KnownDesktop, String, JSONObject, String) -> Bool)?
    var onJsApiEvent: ((String, JSONObject, String, String) -> JSONObject)?

    init(deviceId: @escaping () -> String, logs: MobileLogStore) {
        self.deviceId = deviceId
        self.logs = logs
    }

    func request(
        host: String,
        port: Int,
        payload: JSONObject,
        timeoutMilliseconds: Int = 7_000,
        expectedFingerprint: String? = nil
    ) throws -> JSONObject {
        var request = payload
        let requestId = request.string("requestId").isEmpty
            ? "req-\(UUID().uuidString.lowercased())"
            : request.string("requestId")
        request["requestId"] = requestId
        let client = try ensureTransport(
            host: host,
            port: port,
            expectedFingerprint: expectedFingerprint,
            timeoutMilliseconds: timeoutMilliseconds)
        let envelope: JSONObject = [
            "protocolVersion": OneDeskProtocol.version,
            "messageType": "request",
            "messageId": requestId,
            "correlationId": NSNull(),
            "payload": request,
        ]
        let response = try JSONSupport.object(from: client.request(
            try JSONSupport.data(envelope),
            timeoutMilliseconds: timeoutMilliseconds))
        guard response.string("messageType") == "response",
              response.string("correlationId") == requestId,
              let result = response.object("payload") else {
            throw OneDeskMobileError.transport("GatewayResponseCorrelationMismatch")
        }
        return result
    }

    func authorizedRequest(type: String, credential: String) -> JSONObject {
        [
            "type": type,
            "requestId": "req-\(UUID().uuidString.lowercased())",
            "deviceId": deviceId(),
            "displayName": UIDevice.current.name,
            "platform": "ios",
            "architecture": Self.architecture,
            "trustCredential": credential,
        ]
    }

    func serverFingerprint() throws -> String {
        guard let value = lock.withLock({ transport?.observedFingerprint }), !value.isEmpty else {
            throw OneDeskMobileError.transport("GatewayCertificateMissing")
        }
        return value
    }

    func uploadLog(desktop: KnownDesktop, credential: String, entry: JSONObject) -> Bool {
        do {
            var payload = authorizedRequest(type: "logs", credential: credential)
            payload["logs"] = [entry]
            return try request(
                host: desktop.host,
                port: desktop.port,
                payload: payload,
                timeoutMilliseconds: 2_500,
                expectedFingerprint: desktop.gatewayFingerprint).bool("ok")
        } catch {
            return false
        }
    }

    func startSubscription(desktop: KnownDesktop, credential: String) throws {
        running = true
        activeDesktop = (desktop, credential)
        var payload = authorizedRequest(type: "subscribe", credential: credential)
        payload["logs"] = []
        let response = try request(
            host: desktop.host,
            port: desktop.port,
            payload: payload,
            expectedFingerprint: desktop.gatewayFingerprint)
        guard response.bool("ok") else {
            running = false
            throw OneDeskMobileError.transport(response.string("message"))
        }
        reconnectAttempt = 0
        let endpoint = "\(desktop.host):\(desktop.port)"
        if state.state.phase == .synchronizing { try state.synchronized(endpoint: endpoint) }
        startHeartbeat()
    }

    func stop() {
        running = false
        heartbeat?.cancel()
        heartbeat = nil
        activeDesktop = nil
        reconnectAttempt = 0
        state.disconnect()
        lock.withLock {
            transport?.close()
            transport = nil
            endpointKey = nil
        }
    }

    private func ensureTransport(
        host: String,
        port: Int,
        expectedFingerprint: String?,
        timeoutMilliseconds: Int
    ) throws -> MsQuicTransport {
        try lock.withLock {
            let key = "\(host):\(port)"
            if let existing = transport, endpointKey == key {
                let observed = existing.observedFingerprint
                if expectedFingerprint?.isEmpty != false ||
                    expectedFingerprint?.caseInsensitiveCompare(observed ?? "") == .orderedSame {
                    return existing
                }
                existing.close()
                transport = nil
            } else if let existing = transport {
                existing.close()
                transport = nil
            }

            state.disconnect()
            try state.begin(endpoint: key)
            do {
                let client = try MsQuicTransport(
                    host: host,
                    port: port,
                    expectedFingerprint: expectedFingerprint,
                    timeoutMilliseconds: timeoutMilliseconds,
                    eventHandler: { [weak self] data in
                        self?.eventQueue.async { self?.handleEnvelope(data) }
                    },
                    disconnectedHandler: { [weak self] reason in
                        self?.handleDisconnected(reason)
                    })
                transport = client
                endpointKey = key
                try state.authenticated(endpoint: key)
                return client
            } catch {
                state.fail(endpoint: key, code: "GatewayConnectFailed", message: error.localizedDescription)
                throw error
            }
        }
    }

    private func handleEnvelope(_ data: Data) {
        guard let (desktop, credential) = activeDesktop else { return }
        do {
            let envelope = try JSONSupport.object(from: data)
            guard envelope.string("messageType") == "event",
                  let gatewayResponse = envelope.object("payload"),
                  gatewayResponse.bool("ok"),
                  let payload = gatewayResponse.object("payload") else {
                throw OneDeskMobileError.invalidPayload("GatewayEventEnvelopeInvalid")
            }
            switch payload.string("eventType") {
            case "scheme.updated":
                guard let descriptor = payload.object("scheme") else { return }
                let eventId = payload.string("eventId")
                if onSchemeEvent?(desktop, credential, descriptor, eventId) == true {
                    var ack = authorizedRequest(type: "scheme-ack", credential: credential)
                    ack["eventId"] = eventId
                    _ = try request(host: desktop.host, port: desktop.port, payload: ack,
                                    expectedFingerprint: desktop.gatewayFingerprint)
                }
            case "jsapi.request":
                try handleJsApiRequest(desktop: desktop, credential: credential, payload: payload)
            default:
                logs.append("Warning", "GatewayEvent", "收到未知事件：\(payload.string("eventType"))")
            }
        } catch {
            logs.append("Error", "GatewayEvent", error.localizedDescription)
        }
    }

    private func handleJsApiRequest(desktop: KnownDesktop, credential: String, payload: JSONObject) throws {
        let requestId = payload.string("requestId")
        let capability = payload.string("capability")
        guard !requestId.isEmpty, !capability.isEmpty else { return }
        let source = payload.object("source") ?? [:]
        let sourceKey: String
        switch source.string("kind") {
        case "component": sourceKey = "component:\(source.string("componentId"))"
        case "plugin": sourceKey = "plugin:\(source.string("pluginId"))"
        case "system": sourceKey = "system"
        default: sourceKey = "unknown"
        }
        let result = onJsApiEvent?(capability, payload.object("payload") ?? [:], requestId, sourceKey)
            ?? ["ok": false, "errorCode": "CapabilityNotSupported", "message": "iOS 能力未实现"]
        var response = authorizedRequest(type: "jsapi-response", credential: credential)
        response["requestId"] = requestId
        response["responseOk"] = result.bool("ok")
        response["errorCode"] = result["errorCode"] ?? NSNull()
        response["message"] = result["message"] ?? NSNull()
        response["payload"] = result["payload"] ?? NSNull()
        _ = try request(host: desktop.host, port: desktop.port, payload: response,
                        expectedFingerprint: desktop.gatewayFingerprint)
    }

    private func startHeartbeat() {
        heartbeat?.cancel()
        let timer = DispatchSource.makeTimerSource(queue: eventQueue)
        timer.schedule(deadline: .now() + 15, repeating: 15)
        timer.setEventHandler { [weak self] in self?.sendHeartbeat() }
        heartbeat = timer
        timer.resume()
    }

    private func sendHeartbeat() {
        guard running, let (desktop, credential) = activeDesktop else { return }
        do {
            let payload = authorizedRequest(type: "heartbeat", credential: credential)
            _ = try request(host: desktop.host, port: desktop.port, payload: payload,
                            timeoutMilliseconds: 5_000, expectedFingerprint: desktop.gatewayFingerprint)
        } catch {
            logs.append("Warning", "GatewayHeartbeat", error.localizedDescription)
        }
    }

    private func handleDisconnected(_ reason: String) {
        guard running else { return }
        logs.append("Warning", "GatewayConnection", reason)
        lock.withLock {
            transport = nil
            endpointKey = nil
        }
        scheduleReconnect()
    }

    private func scheduleReconnect() {
        guard running, let (desktop, credential) = activeDesktop else { return }
        let delay = min(30.0, pow(2.0, Double(reconnectAttempt)))
        reconnectAttempt += 1
        eventQueue.asyncAfter(deadline: .now() + delay) { [weak self] in
            guard let self, self.running else { return }
            do {
                try self.startSubscription(desktop: desktop, credential: credential)
            } catch {
                self.logs.append("Error", "GatewayReconnect", error.localizedDescription)
                self.scheduleReconnect()
            }
        }
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

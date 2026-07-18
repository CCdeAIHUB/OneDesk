import Foundation

enum MobileConnectionPhase {
    case disconnected
    case connecting
    case synchronizing
    case connected
    case failed
}

struct MobileConnectionState {
    var phase: MobileConnectionPhase = .disconnected
    var endpoint: String?
    var errorCode: String?
    var message: String?
}

final class MobileConnectionStateMachine {
    private let lock = NSLock()
    private(set) var state = MobileConnectionState()

    func begin(endpoint: String) throws {
        try lock.withLock {
            guard state.phase == .disconnected || state.phase == .failed else {
                throw OneDeskMobileError.transport("当前连接必须先断开")
            }
            state = MobileConnectionState(phase: .connecting, endpoint: endpoint)
        }
    }

    func authenticated(endpoint: String) throws {
        try transition(from: .connecting, to: .synchronizing, endpoint: endpoint)
    }

    func synchronized(endpoint: String) throws {
        try transition(from: .synchronizing, to: .connected, endpoint: endpoint)
    }

    func fail(endpoint: String?, code: String, message: String) {
        lock.withLock {
            state = MobileConnectionState(phase: .failed, endpoint: endpoint ?? state.endpoint, errorCode: code, message: message)
        }
    }

    func disconnect() {
        lock.withLock { state = MobileConnectionState() }
    }

    private func transition(from: MobileConnectionPhase, to: MobileConnectionPhase, endpoint: String) throws {
        try lock.withLock {
            guard state.phase == from, state.endpoint == endpoint else {
                throw OneDeskMobileError.transport("非法连接状态转换")
            }
            state = MobileConnectionState(phase: to, endpoint: endpoint)
        }
    }
}

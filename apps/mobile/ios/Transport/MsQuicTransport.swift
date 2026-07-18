import CryptoKit
import Foundation

/// Swift 只管理连接生命周期和证书指纹；帧收发由最小 C 接口完成。
final class MsQuicTransport {
    private let expectedFingerprint: String?
    private let eventHandler: (Data) -> Void
    private let disconnectedHandler: (String) -> Void
    private let lifecycleLock = NSLock()
    private var handle: ODQuicHandle?
    private(set) var observedFingerprint: String?

    init(
        host: String,
        port: Int,
        expectedFingerprint: String?,
        timeoutMilliseconds: Int = 7_000,
        eventHandler: @escaping (Data) -> Void,
        disconnectedHandler: @escaping (String) -> Void
    ) throws {
        self.expectedFingerprint = expectedFingerprint?.trimmingCharacters(in: .whitespacesAndNewlines)
        self.eventHandler = eventHandler
        self.disconnectedHandler = disconnectedHandler

        var error = [CChar](repeating: 0, count: 512)
        let context = Unmanaged.passUnretained(self).toOpaque()
        handle = host.withCString { hostPointer in
            error.withUnsafeMutableBufferPointer { errorBuffer in
                ODQuicConnect(
                    hostPointer,
                    UInt16(clamping: port),
                    UInt32(clamping: timeoutMilliseconds),
                    oneDeskCertificateCallback,
                    oneDeskEventCallback,
                    oneDeskDisconnectedCallback,
                    context,
                    errorBuffer.baseAddress,
                    errorBuffer.count
                )
            }
        }
        guard handle != nil else {
            throw OneDeskMobileError.transport(String(cString: error))
        }
    }

    func request(_ payload: Data, timeoutMilliseconds: Int = 7_000) throws -> Data {
        try lifecycleLock.withLock {
            guard let handle else {
                throw OneDeskMobileError.transport("GatewaySessionOffline")
            }
            var responsePointer: UnsafeMutablePointer<UInt8>?
            var responseLength = 0
            var error = [CChar](repeating: 0, count: 512)
            let ok = payload.withUnsafeBytes { bytes in
                error.withUnsafeMutableBufferPointer { errorBuffer in
                    ODQuicRequest(
                        handle,
                        bytes.bindMemory(to: UInt8.self).baseAddress,
                        bytes.count,
                        UInt32(clamping: timeoutMilliseconds),
                        &responsePointer,
                        &responseLength,
                        errorBuffer.baseAddress,
                        errorBuffer.count
                    )
                }
            }
            guard ok, let responsePointer else {
                throw OneDeskMobileError.transport(String(cString: error))
            }
            defer { ODQuicFreeBuffer(responsePointer) }
            return Data(bytes: responsePointer, count: responseLength)
        }
    }

    func close() {
        lifecycleLock.withLock {
            guard let handle else { return }
            self.handle = nil
            ODQuicClose(handle)
        }
    }

    fileprivate func validateCertificate(_ bytes: UnsafePointer<UInt8>, length: Int) -> Bool {
        let digest = SHA256.hash(data: Data(bytes: bytes, count: length))
        let fingerprint = digest.map { String(format: "%02x", $0) }.joined()
        observedFingerprint = fingerprint
        return expectedFingerprint?.isEmpty != false ||
            expectedFingerprint?.caseInsensitiveCompare(fingerprint) == .orderedSame
    }

    fileprivate func receiveEvent(_ bytes: UnsafePointer<UInt8>, length: Int) {
        eventHandler(Data(bytes: bytes, count: length))
    }

    fileprivate func disconnected(_ reason: String) {
        disconnectedHandler(reason)
    }

    deinit {
        close()
    }
}

private let oneDeskCertificateCallback: @convention(c) (
    UnsafePointer<UInt8>?, Int, UnsafeMutableRawPointer?
) -> Bool = { bytes, length, context in
    guard let bytes, let context else { return false }
    return Unmanaged<MsQuicTransport>.fromOpaque(context)
        .takeUnretainedValue()
        .validateCertificate(bytes, length: length)
}

private let oneDeskEventCallback: @convention(c) (
    UnsafePointer<UInt8>?, Int, UnsafeMutableRawPointer?
) -> Void = { bytes, length, context in
    guard let bytes, let context else { return }
    Unmanaged<MsQuicTransport>.fromOpaque(context)
        .takeUnretainedValue()
        .receiveEvent(bytes, length: length)
}

private let oneDeskDisconnectedCallback: @convention(c) (
    UnsafePointer<CChar>?, UnsafeMutableRawPointer?
) -> Void = { reason, context in
    guard let reason, let context else { return }
    Unmanaged<MsQuicTransport>.fromOpaque(context)
        .takeUnretainedValue()
        .disconnected(String(cString: reason))
}

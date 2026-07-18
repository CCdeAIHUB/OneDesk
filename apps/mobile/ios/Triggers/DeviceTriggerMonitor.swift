import CoreMotion
import Foundation

/// 将 iOS 传感器事件归一化为与 Android 相同的动作触发 ID。
final class DeviceTriggerMonitor {
    private let motion = CMMotionManager()
    private let queue: OperationQueue = {
        let queue = OperationQueue()
        queue.name = "cc.onedesk.mobile.device-triggers"
        queue.maxConcurrentOperationCount = 1
        return queue
    }()
    private let onTrigger: (String) -> Void
    private var lastShakeAt = Date.distantPast
    private var currentTilt: String?
    private var orientationQuadrant: Int?

    init(onTrigger: @escaping (String) -> Void) {
        self.onTrigger = onTrigger
    }

    func start() {
        guard motion.isDeviceMotionAvailable, !motion.isDeviceMotionActive else { return }
        motion.deviceMotionUpdateInterval = 1.0 / 30.0
        motion.startDeviceMotionUpdates(to: queue) { [weak self] sample, error in
            guard error == nil, let sample else { return }
            self?.handle(sample)
        }
    }

    func stop() {
        motion.stopDeviceMotionUpdates()
        currentTilt = nil
        orientationQuadrant = nil
        queue.cancelAllOperations()
    }

    private func handle(_ sample: CMDeviceMotion) {
        handleShake(sample.userAcceleration)
        handleTilt(sample.gravity)
        handleOrientation(sample.gravity)
    }

    private func handleShake(_ acceleration: CMAcceleration) {
        let force = sqrt(
            acceleration.x * acceleration.x +
            acceleration.y * acceleration.y +
            acceleration.z * acceleration.z)
        let now = Date()
        guard force > 2.2, now.timeIntervalSince(lastShakeAt) >= 0.9 else { return }
        lastShakeAt = now
        emit("shake")
    }

    private func handleTilt(_ gravity: CMAcceleration) {
        let x = gravity.x
        let y = gravity.y
        let next: String?
        if abs(x) < 0.36, abs(y) < 0.36 {
            next = nil
        } else if abs(x) > abs(y), x > 0.58 {
            next = "tilt-right"
        } else if abs(x) > abs(y), x < -0.58 {
            next = "tilt-left"
        } else if y > 0.58 {
            next = "tilt-up"
        } else if y < -0.58 {
            next = "tilt-down"
        } else {
            next = currentTilt
        }
        if let next, next != currentTilt { emit(next) }
        currentTilt = next
    }

    private func handleOrientation(_ gravity: CMAcceleration) {
        let angle = atan2(gravity.y, gravity.x)
        let quadrant = ((Int(round(angle / (.pi / 2))) % 4) + 4) % 4
        let previous = orientationQuadrant
        orientationQuadrant = quadrant
        if let previous, previous != quadrant { emit("orientation-change") }
    }

    private func emit(_ triggerId: String) {
        // WebView 只能在主线程执行脚本，传感器采样保持在独立串行队列中。
        OperationQueue.main.addOperation { [onTrigger] in onTrigger(triggerId) }
    }
}

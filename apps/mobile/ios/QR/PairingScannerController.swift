import AVFoundation
import UIKit

final class PairingScannerController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
    private let completion: (String?, String?) -> Void
    private let session = AVCaptureSession()
    private var previewLayer: AVCaptureVideoPreviewLayer?
    private var completed = false

    init(completion: @escaping (String?, String?) -> Void) {
        self.completion = completion
        super.init(nibName: nil, bundle: nil)
        modalPresentationStyle = .fullScreen
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("不支持 Storyboard 初始化") }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        configureCloseButton()
        requestCameraAndStart()
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        previewLayer?.frame = view.bounds
    }

    func metadataOutput(
        _ output: AVCaptureMetadataOutput,
        didOutput metadataObjects: [AVMetadataObject],
        from connection: AVCaptureConnection
    ) {
        guard let code = metadataObjects.compactMap({ ($0 as? AVMetadataMachineReadableCodeObject)?.stringValue }).first,
              code.hasPrefix("onedesk://pair?") else { return }
        finish(payload: code, error: nil)
    }

    func cancel() {
        finish(payload: nil, error: "已取消扫描")
    }

    private func requestCameraAndStart() {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            configureSession()
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { [weak self] allowed in
                DispatchQueue.main.async {
                    allowed ? self?.configureSession() : self?.finish(payload: nil, error: "未获得相机权限")
                }
            }
        default:
            finish(payload: nil, error: "请在系统设置中允许 OneDesk 使用相机")
        }
    }

    private func configureSession() {
        guard let camera = AVCaptureDevice.default(for: .video),
              let input = try? AVCaptureDeviceInput(device: camera),
              session.canAddInput(input) else {
            finish(payload: nil, error: "当前设备无法启动相机")
            return
        }
        session.addInput(input)
        let output = AVCaptureMetadataOutput()
        guard session.canAddOutput(output) else {
            finish(payload: nil, error: "当前设备无法识别二维码")
            return
        }
        session.addOutput(output)
        output.setMetadataObjectsDelegate(self, queue: .main)
        output.metadataObjectTypes = [.qr]

        let layer = AVCaptureVideoPreviewLayer(session: session)
        layer.videoGravity = .resizeAspectFill
        view.layer.insertSublayer(layer, at: 0)
        previewLayer = layer
        DispatchQueue.global(qos: .userInitiated).async { [session] in session.startRunning() }
    }

    private func configureCloseButton() {
        let button = UIButton(type: .system)
        button.setImage(UIImage(systemName: "xmark"), for: .normal)
        button.tintColor = .white
        button.backgroundColor = UIColor.black.withAlphaComponent(0.55)
        button.layer.cornerRadius = 18
        button.translatesAutoresizingMaskIntoConstraints = false
        button.addTarget(self, action: #selector(closeTapped), for: .touchUpInside)
        view.addSubview(button)
        NSLayoutConstraint.activate([
            button.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 12),
            button.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -18),
            button.widthAnchor.constraint(equalToConstant: 36),
            button.heightAnchor.constraint(equalToConstant: 36),
        ])
    }

    @objc private func closeTapped() { cancel() }

    private func finish(payload: String?, error: String?) {
        guard !completed else { return }
        completed = true
        if session.isRunning { session.stopRunning() }
        dismiss(animated: true) { [completion] in completion(payload, error) }
    }
}

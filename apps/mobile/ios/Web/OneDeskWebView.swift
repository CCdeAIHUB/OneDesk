import SwiftUI
import WebKit

struct OneDeskWebView: UIViewRepresentable {
    let runtime: MobileRuntime

    func makeCoordinator() -> Coordinator {
        Coordinator(runtime: runtime)
    }

    func makeUIView(context: Context) -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.defaultWebpagePreferences.allowsContentJavaScript = true
        configuration.userContentController.addScriptMessageHandler(
            context.coordinator,
            contentWorld: .page,
            name: "onedesk")
        configuration.userContentController.addUserScript(WKUserScript(
            source: Self.nativeBridgeScript,
            injectionTime: .atDocumentStart,
            forMainFrameOnly: false))
        configuration.userContentController.addUserScript(WKUserScript(
            source: Self.networkBlockScript,
            injectionTime: .atDocumentStart,
            forMainFrameOnly: false))

        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = context.coordinator
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        webView.scrollView.bounces = false
        webView.isOpaque = false
        webView.backgroundColor = .clear
        context.coordinator.attach(webView)

        guard let url = Bundle.main.url(forResource: "index", withExtension: "html", subdirectory: "mobile") else {
            context.coordinator.showFatalError(in: webView, message: "未找到移动端前端入口文件")
            return webView
        }
        // 方案媒体位于应用沙盒 Application Support；允许读取本应用沙盒文件，联网仍由两层策略阻断。
        webView.loadFileURL(url, allowingReadAccessTo: URL(fileURLWithPath: NSHomeDirectory(), isDirectory: true))
        return webView
    }

    func updateUIView(_ uiView: WKWebView, context: Context) {}

    static func dismantleUIView(_ uiView: WKWebView, coordinator: Coordinator) {
        coordinator.stop()
        uiView.configuration.userContentController.removeScriptMessageHandler(forName: "onedesk")
    }

    final class Coordinator: NSObject, WKNavigationDelegate, WKScriptMessageHandlerWithReply {
        private let runtime: MobileRuntime
        private weak var webView: WKWebView?
        private weak var scanner: PairingScannerController?
        private lazy var deviceTriggers = DeviceTriggerMonitor { [weak self] triggerId in
            self?.emit(name: "__oneDeskHandleDeviceTrigger", payload: ["triggerId": triggerId])
        }

        init(runtime: MobileRuntime) {
            self.runtime = runtime
            super.init()
            runtime.emitFrontendEvent = { [weak self] name, payload in self?.emit(name: name, payload: payload) }
            runtime.startQrScanner = { [weak self] in self?.startQrScanner() }
            runtime.cancelQrScanner = { [weak self] in self?.scanner?.cancel() }
        }

        func attach(_ webView: WKWebView) {
            self.webView = webView
            deviceTriggers.start()
        }

        func userContentController(
            _ userContentController: WKUserContentController,
            didReceive message: WKScriptMessage,
            replyHandler: @escaping (Any?, String?) -> Void
        ) {
            guard message.name == "onedesk",
                  let request = message.body as? JSONObject else {
                replyHandler(JSONSupport.response(ok: false, errorCode: "InvalidPayload", message: "桥接请求格式错误"), nil)
                return
            }
            let method = request.string("method")
            let arguments = request.array("arguments")
            DispatchQueue.global(qos: .userInitiated).async { [runtime] in
                replyHandler(runtime.handle(method: method, arguments: arguments), nil)
            }
        }

        func webView(
            _ webView: WKWebView,
            decidePolicyFor navigationAction: WKNavigationAction,
            decisionHandler: @escaping (WKNavigationActionPolicy) -> Void
        ) {
            guard let scheme = navigationAction.request.url?.scheme?.lowercased() else {
                decisionHandler(.cancel)
                return
            }
            decisionHandler(["file", "about"].contains(scheme) ? .allow : .cancel)
        }

        func webView(
            _ webView: WKWebView,
            decidePolicyFor navigationResponse: WKNavigationResponse,
            decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void
        ) {
            let scheme = navigationResponse.response.url?.scheme?.lowercased()
            decisionHandler(scheme == "file" || scheme == "about" ? .allow : .cancel)
        }

        func showFatalError(in webView: WKWebView, message: String) {
            let escaped = message.replacingOccurrences(of: "<", with: "&lt;")
            webView.loadHTMLString("<meta name='viewport' content='width=device-width'><body style='font:15px sans-serif;padding:24px'>\(escaped)</body>", baseURL: nil)
        }

        func stop() {
            scanner?.cancel()
            deviceTriggers.stop()
            runtime.stop()
        }

        private func startQrScanner() {
            guard scanner == nil, let presenter = webView?.nearestViewController else { return }
            let controller = PairingScannerController { [weak self] payload, error in
                self?.scanner = nil
                self?.emitQrResult(payload: payload, error: error)
            }
            scanner = controller
            presenter.present(controller, animated: true)
        }

        private func emit(name: String, payload: JSONObject) {
            guard let data = try? JSONSupport.data(payload),
                  let json = String(data: data, encoding: .utf8) else { return }
            let script: String
            if name == "__oneDeskHandleSchemeUpdated" {
                script = "window.\(name)?.(\(Self.jsString(payload.string("desktopId"))), \(Self.jsString(payload.string("version"))), \(Self.jsString(payload.string("hash"))));"
            } else {
                script = "window.\(name)?.(\(json));"
            }
            guard let webView else { return }
            DispatchQueue.main.async { [weak webView] in webView?.evaluateJavaScript(script) }
        }

        private func emitQrResult(payload: String?, error: String?) {
            let script = "window.__oneDeskHandleQrScan?.(\(Self.jsNullableString(payload)), \(Self.jsNullableString(error)));"
            webView?.evaluateJavaScript(script)
        }

        private static func jsNullableString(_ value: String?) -> String {
            value.map(jsString) ?? "null"
        }

        private static func jsString(_ value: String) -> String {
            guard let data = try? JSONSerialization.data(withJSONObject: [value]),
                  let array = String(data: data, encoding: .utf8) else { return "\"\"" }
            return String(array.dropFirst().dropLast())
        }
    }

    private static let nativeBridgeScript = #"""
    (() => {
      const invoke = (method, ...arguments) =>
        window.webkit.messageHandlers.onedesk.postMessage({ method, arguments });
      Object.defineProperty(window, "OneDeskNative", {
        configurable: false,
        writable: false,
        value: Object.freeze({
          listKnownDesktops: () => invoke("listKnownDesktops"),
          connect: (host, port, code) => invoke("connect", host, port, code),
          connectByQr: (payload) => invoke("connectByQr", payload),
          startQrScan: () => invoke("startQrScan"),
          cancelQrScan: () => invoke("cancelQrScan"),
          getCachedScheme: (desktopId) => invoke("getCachedScheme", desktopId),
          refreshScheme: (desktopId) => invoke("refreshScheme", desktopId),
          setDisplayRatio: (width, height) => invoke("setDisplayRatio", width, height),
          callJsApi: (target, capability, payload, scheme, page, component) =>
            invoke("callJsApi", target, capability, payload, scheme, page, component),
        }),
      });
    })();
    """#

    private static let networkBlockScript = #"""
    (() => {
      const blocked = () => Promise.reject(new Error("OneDeskFrontendNetworkBlocked"));
      window.fetch = blocked;
      navigator.sendBeacon = () => false;
      class BlockedSocket { constructor() { throw new Error("OneDeskFrontendNetworkBlocked"); } }
      window.XMLHttpRequest = BlockedSocket;
      window.WebSocket = BlockedSocket;
      window.EventSource = BlockedSocket;
    })();
    """#
}

private extension UIView {
    var nearestViewController: UIViewController? {
        var responder: UIResponder? = self
        while let current = responder {
            if let controller = current as? UIViewController { return controller }
            responder = current.next
        }
        return nil
    }
}

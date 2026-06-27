import SwiftUI
import WebKit

struct OneDeskWebView: UIViewRepresentable {
    func makeCoordinator() -> Coordinator {
        Coordinator()
    }

    func makeUIView(context: Context) -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.userContentController.add(context.coordinator, name: "onedesk")

        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = context.coordinator
        webView.isOpaque = false
        webView.backgroundColor = .clear

        if let url = Bundle.main.url(forResource: "index", withExtension: "html", subdirectory: "mobile") {
            webView.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent())
        }

        return webView
    }

    func updateUIView(_ uiView: WKWebView, context: Context) {}

    final class Coordinator: NSObject, WKNavigationDelegate, WKScriptMessageHandler {
        private let deviceId = "ios-\(UUID().uuidString)"
        private var disconnectedLogs: [[String: Any]] = []

        func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
            guard message.name == "onedesk" else {
                return
            }

            appendDisconnectedLog(level: "Info", category: "JsApi", message: "Received JSAPI bridge message")
        }

        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
            if shouldBlock(navigationAction.request.url) {
                decisionHandler(.cancel)
                return
            }

            decisionHandler(.allow)
        }

        private func shouldBlock(_ url: URL?) -> Bool {
            guard let scheme = url?.scheme?.lowercased() else {
                return false
            }

            return scheme == "http" || scheme == "https" || scheme == "ws" || scheme == "wss"
        }

        private func appendDisconnectedLog(level: String, category: String, message: String) {
            disconnectedLogs.append([
                "logId": "log-\(UUID().uuidString)",
                "createdAt": Int(Date().timeIntervalSince1970 * 1000),
                "sourceDeviceId": deviceId,
                "level": level,
                "category": category,
                "message": message
            ])
        }
    }
}

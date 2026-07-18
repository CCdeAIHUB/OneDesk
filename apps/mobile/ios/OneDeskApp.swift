import SwiftUI

@main
struct OneDeskApp: App {
    private let runtime = MobileRuntime()

    var body: some Scene {
        WindowGroup {
            OneDeskWebView(runtime: runtime)
                .ignoresSafeArea()
        }
    }
}

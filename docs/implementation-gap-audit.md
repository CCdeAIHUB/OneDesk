# OneDesk Implementation Gap Audit

Audit date: 2026-07-18

This audit distinguishes implemented product paths from validation limits imposed by the current Windows host or missing store credentials. A source stub, placeholder screen, or passing compile alone is not considered product completion.

## Completed Product Paths

| Area | Status | Executable evidence |
| --- | --- | --- |
| MsQuic transport | Complete | Desktop uses `System.Net.Quic`; Android and iOS use pinned native MsQuic bindings. Pairing, trusted reconnect, logs, cache transfer, push acknowledgement, heartbeat, and JSAPI share long-lived framed QUIC connections. Contract tests reject raw-UDP fallback. |
| Device identity and trust | Complete | The desktop assigns IDs, six-digit pairing exchanges a persisted encrypted trust credential, reconnect no longer requires verification, and mobile-to-mobile routing always passes through the desktop. |
| Scheme distribution | Complete | Assignments are per device; snapshots and assets are hash checked and atomically replaced; online devices receive immediate push and offline assignments are delivered on reconnect. |
| Android client | Complete | Native QR scanner, manual pairing, encrypted trust storage, disconnected log upload, fullscreen page rendering, page media, visual/code components, gestures, sensor triggers, orientation, JSAPI, cache lifecycle, and empty first-run state are wired to the desktop gateway. |
| iOS client source | Complete | Swift/WKWebView shell includes native MsQuic, Keychain trust, pairing scanner, atomic cache, logs, renderer bridge, JSAPI, motion/orientation triggers, and structured unsupported results. Xcode project membership and protocol contracts are tested on Windows. |
| Cross-platform desktop shell | Complete at source/build matrix | Windows WinForms/WebView2 and Avalonia/CefGlue shells share the bridge, services, tray lifecycle, settings, network policy, and Vue UI. Release automation covers Windows, macOS, and Linux on x64/arm64. |
| Code component runtime | Complete | Desktop produces deterministic hash-validated code artifacts; mobile loads packaged code components in controlled local containers and unloads media/runtime state when leaving. |
| JSAPI catalog | Complete | One canonical capability catalog generates C#, Kotlin, Swift, and TypeScript definitions. Every platform registers a concrete handler or returns a structured platform-unsupported result. |
| Plugin runtime | Complete | Manifest validation, permissions, independent backend process, correlated JSON-RPC, handshake, health/restart policy, resource limits, persistent process lifecycle, frontend session identity, settings schema, and shell-mediated frontend/backend communication are implemented. |
| Package transactions | Complete | Component/page/scheme/plugin imports use preflight inspection, permission selection, dependency conflict decisions, safe extraction limits, atomic commit, and rollback tests. |
| Protocol generation | Complete | JSON schema is the single source for TypeScript, C#, Kotlin, and Swift contracts; generated-file drift and envelope compatibility are covered by tests. |
| Frontend network isolation | Complete | WebView2, CEF, Android WebView, and WKWebView block direct frontend networking while native bridges perform all network operations. |
| Release automation | Complete for reproducible unsigned artifacts | GitHub workflow publishes six self-contained desktop runtime artifacts, Android APK, and unsigned iOS Simulator application. |

## Validation Limits, Not Missing Implementations

| Area | Current limitation | Required evidence |
| --- | --- | --- |
| iOS native compilation | The current Codex host is Windows and cannot run Xcode. | The macOS release job must compile the Xcode project; a physical-device run additionally needs Apple signing credentials. |
| macOS/Linux visual behavior | Source and package matrix exist, but the current host cannot visually inspect native blur, tray, CEF, and window behavior on those operating systems. | Run the generated artifacts on representative macOS and Linux GUI machines. |
| Store-signed distribution | Apple, Android, Windows, and macOS signing/notarization identities are owner secrets and are not present in the repository. | Configure repository secrets and signing profiles; no product feature code is deferred. |

## Automated Evidence

- .NET contract/integration suite: 39 tests.
- Android JVM suite: passed with the debug build.
- Android debug APK: includes `arm64-v8a` and `x86_64` MsQuic/JNI native libraries and the local Vue frontend.
- Vue desktop and mobile production builds: passed.
- Windows x64 self-contained publish: required before delivery after every source change.

## Audit Verdict

The previously listed critical implementation gaps are closed in source and automated contracts. Remaining rows are environment- or credential-bound validation work and must not be described as missing product logic. No module is locked until the user confirms its behavior.

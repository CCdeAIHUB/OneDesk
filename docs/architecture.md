# OneDesk Architecture

OneDesk is a desktop-centered control system. The desktop app is the designer, executor, permission center, JSAPI gateway, plugin host, log store, and scheme distributor. Mobile apps display cached schemes and send user operations back to the connected desktop.

## Runtime Boundaries

- Frontends are loaded with `file://`.
- Frontends cannot perform direct networking.
- Shells enforce networking restrictions in Chromium/CEF, Android WebView, and iOS WKWebView.
- Desktop and mobile communicate through long-lived MsQuic-backed QUIC connections. The desktop uses `System.Net.Quic` (MsQuic runtime), while Android and iOS use pinned MsQuic native bindings. The application protocol is a generated length-prefixed JSON envelope and has no raw-UDP fallback.
- Mobile-to-mobile JSAPI calls always route through the desktop gateway.

## Shell Boundaries

- Windows uses WinForms/WebView2; macOS and Linux use Avalonia/CefGlue. Both shells delegate business requests to the shared `DesktopBridgeDispatcher` and platform-specific behavior to `IDesktopShellPlatform`.
- Android uses Kotlin/WebView and iOS uses Swift/WKWebView. Both load the same built mobile Vue frontend from app-local files and reject direct frontend network access.
- Platform capability providers implement the canonical generated capability directory. Unsupported platform operations return structured `CapabilityNotSupported` failures instead of disappearing or reporting success.

## Generated Contracts

- `packages/protocol/schema/onedesk.protocol.json` and `packages/protocol/capabilities.json` are authoritative.
- `packages/protocol/scripts/generate-protocol.mjs` generates synchronized TypeScript, C#, Kotlin, and Swift contracts.
- Contract tests fail when generated outputs, capability IDs, frontend network policy, or release targets drift.

## Runtime Isolation

- Code-mode Vue components are compiled into hash-validated artifacts and loaded in controlled component containers.
- Backend plugins run in independent processes using correlated JSON-RPC. Frontend and backend plugin parts communicate only through the desktop host.
- Component/plugin source identity is issued by host-owned sessions and checked before permission routing; frontend code cannot authorize itself by declaring an arbitrary source ID.

## Artifact Containment

- Scheme exports contain pages, components, actions, and required plugin dependencies.
- Page exports contain components and dependent actions.
- Component exports contain dependent actions.
- Plugins are desktop-only and may contain frontend logic, backend logic, or both.

## Permissions

Permissions are declared by components and plugins, grouped by category and capability. High-risk permissions are clearly marked during install/import. Runtime JSAPI source identity is injected by trusted containers rather than declared by arbitrary frontend code.

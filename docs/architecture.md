# OneDesk Architecture

OneDesk is a desktop-centered control system. The desktop app is the designer, executor, permission center, JSAPI gateway, plugin host, log store, and scheme distributor. Mobile apps display cached schemes and send user operations back to the connected desktop.

## Runtime Boundaries

- Frontends are loaded with `file://`.
- Frontends cannot perform direct networking.
- Shells enforce networking restrictions in Chromium/CEF, Android WebView, and iOS WKWebView.
- Desktop and mobile communicate through QUIC over UDP.
- Mobile-to-mobile JSAPI calls always route through the desktop gateway.

## Artifact Containment

- Scheme exports contain pages, components, actions, and required plugin dependencies.
- Page exports contain components and dependent actions.
- Component exports contain dependent actions.
- Plugins are desktop-only and may contain frontend logic, backend logic, or both.

## Permissions

Permissions are declared by components and plugins, grouped by category and capability. High-risk permissions are clearly marked during install/import. Runtime JSAPI source identity is injected by trusted containers rather than declared by arbitrary frontend code.

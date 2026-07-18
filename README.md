# OneDesk

OneDesk is a cross-platform control software project. The desktop app designs and executes control schemes, hosts plugins, stores logs, routes JSAPI calls, and distributes scheme caches. Mobile apps display cached schemes and send user operations to the connected desktop.

## Current Structure

- `apps/desktop`: .NET 10 + Avalonia/CefGlue cross-platform desktop shell and core services.
- `apps/desktop-windows`: WinForms/WebView2 Windows shell sharing the same bridge and services.
- `apps/mobile/android`: Kotlin Android shell that loads the mobile Vue frontend from app assets.
- `apps/mobile/ios`: Swift/WKWebView iOS shell with native MsQuic, pairing, cache, logging, QR scanning, JSAPI, and device triggers.
- `frontends/desktop`: Vue 3 desktop UI using Tailwind CSS v4 and Yesicon/Iconify icons.
- `frontends/mobile`: Vue 3 mobile UI using Tailwind CSS v4 and Yesicon/Iconify icons.
- `packages/protocol`: shared protocol schemas and TypeScript validation types.
- `docs`: architecture, package, validation, plugin, and JSAPI documentation.

## Validation

Local:

```powershell
pnpm install
pnpm build
dotnet test tests/OneDesk.Desktop.Tests/OneDesk.Desktop.Tests.csproj --configuration Release
dotnet build apps/desktop/OneDesk.Desktop.csproj --configuration Release
dotnet publish apps/desktop-windows/OneDesk.Windows.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/windows/win-x64
Push-Location apps/mobile/android
.\gradlew.bat :app:testDebugUnitTest :app:assembleDebug --no-daemon
Pop-Location
```

The release workflow also builds six desktop runtime identifiers, Android, and an unsigned iOS Simulator application. iOS source cannot be compiled locally on Windows; device signing and store distribution require owner-provided Apple/Android signing credentials.

## Design Rules

- Frontends load through `file://`.
- Frontends do not perform direct networking.
- Shells enforce network blocking.
- Desktop and mobile communicate through MsQuic-backed QUIC streams over UDP, with no raw-UDP fallback.
- Mobile-to-mobile JSAPI calls route through the desktop gateway.
- Plugins run only on desktop.
- Plugins do not provide custom UI.
- Component, plugin, page, and scheme packages carry their dependency and permission metadata.

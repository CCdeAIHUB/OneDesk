# OneDesk

OneDesk is a cross-platform control software project. The desktop app designs and executes control schemes, hosts plugins, stores logs, routes JSAPI calls, and distributes scheme caches. Mobile apps display cached schemes and send user operations to the connected desktop.

## Current Structure

- `apps/desktop`: .NET 10 + Avalonia desktop shell and core services.
- `apps/mobile/android`: Kotlin Android shell that loads the mobile Vue frontend from app assets.
- `apps/mobile/ios`: Swift/WKWebView iOS shell skeleton.
- `frontends/desktop`: Vue 3 desktop UI using Tailwind CSS v4 and Yesicon/Iconify icons.
- `frontends/mobile`: Vue 3 mobile UI using Tailwind CSS v4 and Yesicon/Iconify icons.
- `packages/protocol`: shared protocol schemas and TypeScript validation types.
- `docs`: architecture, package, validation, plugin, and JSAPI documentation.

## Validation

Local:

```powershell
pnpm install
pnpm build
dotnet build apps/desktop/OneDesk.Desktop.csproj --configuration Release
```

Android is validated by GitHub Actions because the current Windows Codex environment does not provide Java/Gradle.

## Design Rules

- Frontends load through `file://`.
- Frontends do not perform direct networking.
- Shells enforce network blocking.
- Desktop and mobile communicate through QUIC over UDP.
- Mobile-to-mobile JSAPI calls route through the desktop gateway.
- Plugins run only on desktop.
- Plugins do not provide custom UI.
- Component, plugin, page, and scheme packages carry their dependency and permission metadata.

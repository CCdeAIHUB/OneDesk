# OneDesk Project Memory

This file records confirmed project decisions and constraints. Keep it updated when requirements, architecture decisions, locked modules, or validation rules change.

## Collaboration Rules

- If there is any uncertainty, ask the user before deciding.
- If a question affects architecture, feature boundaries, data structures, UI style, technology choice, or interaction behavior, provide options with pros and cons for the user to choose.
- Once code for a module is complete and confirmed, treat it as locked. Do not modify locked code unless the reason, impact, and risk are explained first.
- Preserve key decisions even if conversation context is compressed.

## Repository

- GitHub repository name: `OneDesk`
- Owner: personal GitHub account `CCdeAIHUB`
- Visibility: public
- Every code change should be committed and pushed to GitHub.

## Validation Rules

- Prefer local build validation whenever the Codex environment can build the target.
- If local build is not possible, push to GitHub and use GitHub CI.
- Required validation targets for each change:
  - Current Codex system environment version.
  - Android version.
- After pushing, do not wait for GitHub CI completion unless the user explicitly asks to wait. Local validation remains required when possible.

## Current Implementation Status

- No module is locked as complete yet.
- Windows desktop shell currently uses WinForms plus WebView2 as the working Chromium shell while the cross-platform desktop direction remains Avalonia plus Chromium/CEF.
- The Windows shell enforces direct frontend networking blocks for fetch, XHR, WebSocket, EventSource, sendBeacon, remote navigation, and remote resource requests.
- The Windows shell exposes bridge requests for workspace list/save/delete/apply, component/page/scheme export, two-step component/page/scheme import inspection and confirmation, two-step plugin import inspection and confirmation, capability list, permission list/grant/revoke, pairing code generation, gateway status, device status, scheme cache manifest, logs, theme, window drag, minimize, maximize/restore, and close.
- Component/page/scheme export currently writes packages to `%LOCALAPPDATA%\OneDesk\exports`; component export includes actions, page export includes referenced components/actions, and scheme export includes referenced pages/components/actions plus a plugin dependency report.
- Permission grants are persisted in `%LOCALAPPDATA%\OneDesk\permission-grants.json`.
- Structured desktop logs are stored as JSONL files under `%LOCALAPPDATA%\OneDesk\logs`.
- Gateway service now starts a real UDP JSON listener on port 48320 for desktop/mobile pairing, trusted reconnect, mobile disconnected-log upload, and scheme snapshot retrieval. This is a usable LAN transport prototype, but it is still not MsQuic and must not be treated as final QUIC compliance.
- Desktop identity is persisted under `%LOCALAPPDATA%\OneDesk\desktop-identity.json`.
- Trusted mobile pairing credentials are persisted under `%LOCALAPPDATA%\OneDesk\trusted-devices.json`, so a paired mobile can reconnect without a new six-digit code after desktop restart.
- Scheme application can now be stored per mobile device, with the legacy global active scheme kept as a fallback. Mobile gateway responses use the requesting mobile device ID to select the scheme snapshot.
- Desktop UI has Chinese-only visible text, card-based component/page/scheme management, device management dialog with selectable trusted mobile devices, vertical theme selector, custom window controls, transparent/frosted shell background, and opaque content cards/nav surfaces.
- Desktop UI color tokens are recorded in `frontends/desktop/src/uiColorScheme.ts`; light/dark surfaces should keep level-1 and level-2 backgrounds visually distinct while retaining `sky-500` as the theme color.
- The Windows shell exposes `window.resizeStart` so the WebView frontend can hand edge/corner resizing back to native Windows hit testing instead of relying only on WinForms client-area hit testing.
- Desktop management cards for components, pages, and schemes should open their editor when the card itself is clicked; delete actions require an in-app confirmation dialog.
- Desktop editor page names are editable from the top header, and save/export/apply controls belong in the top header area immediately to the left of the device selector.
- In-app desktop toast notifications are queued as a short-lived stack, auto-dismiss after a few seconds, and use light surfaces in light mode and dark surfaces in dark mode.
- Scheme editing requires a real flowchart canvas with page nodes, connecting lines, arrow direction, edge labels or related edge configuration, and drag-based page node/order adjustment.
- Desktop device management must not show the desktop client itself as a managed device. The desktop shows local LAN IP, QUIC port, verification code, QR payload, and a generated QR image so mobile clients initiate pairing. Real mobile pairing still depends on completing the QUIC transport loop.
- The desktop device selector displays the currently selected/trusted mobile device icon and name. If a mobile device has a remark, UI should display the remark.
- Creating a component, page, or scheme should enter the corresponding editor immediately, where the user can edit the name and save.
- Component/page/scheme management pages require both import and create actions.
- Scrollable areas and scrollbars must not trigger native window dragging.
- Component import, page import, scheme import, and plugin import now use a two-step UI flow: inspect selected package manifest, show requested permissions and high-risk flags, allow the user to adjust grants, then confirm import. This is implemented for manifest-based grants, but dependency conflict resolution still needs a full user choice flow.
- Desktop component code editing now has a file-tree style UI with multiple draft files, and arbitrary edited file contents can be saved/read as real component project files under the component directory.
- Page editing now supports selecting grid cells, setting span, binding components, and editing cell outline/radius in the UI.
- Page editing includes a live preview switch. When enabled, cells with bound components should render the component's saved visual configuration instead of only showing the component name.
- Desktop media resources are managed through a resource manager stored under the user data directory. Images/videos used by components or pages must first be added to the resource manager, then selecting a resource copies the file into the target component/page folder instead of sharing one mutable global file reference.
- Page and component media background controls must use resource IDs/resource picker for image/video backgrounds, while solid and gradient backgrounds use color pickers.
- Scheme editing now supports adding/removing/reordering pages, editing global animation values, and editing page-specific switching edges in the UI.
- Plugin import uses safe zip extraction limits for the new confirmed import path and the legacy bridge path. Plugin settings schema can be rendered as a basic JSON Schema form for string, number, integer, and boolean fields; settings are persisted to the plugin package and submitted to backend plugins through `onedesk.configure` when a backend process exists.
- Android frontend no longer simulates a successful connection when the native shell bridge is absent. Android native shell now performs UDP JSON pairing/trusted reconnect against the desktop gateway, uploads disconnected logs, stores assigned mobile ID/trust credential, and caches the received scheme snapshot. This is not yet MsQuic.
- Android Gradle wrapper is included, and local validation has produced a debug APK with the temporary JDK/Android SDK toolchain installed under `%LOCALAPPDATA%\OneDeskBuildTools`.
- Desktop JSAPI rejects calls whose source identity is freely declared as `frontend`; component/plugin source wrappers exist and are validated against known component/plugin IDs. Full trusted runtime injection inside isolated component/plugin execution containers remains incomplete.
- Cross-device JSAPI forwarding currently validates online target state and queues/logs the request in the desktop gateway; request delivery to a live mobile runtime and response return are still incomplete.

## Product Summary

- OneDesk is a control software project conceptually similar to Stream Deck-style control software, but it must not copy Stream Deck's design or implementation.
- Official product display name: `OneDesk`.
- The product has a desktop side and a mobile side.
- The desktop side is responsible for execution, mobile interface design, backend flow handling, and core control logic.
- The mobile side displays the designed interface and sends user operations to the desktop side for control.
- Core desktop design concepts are `Component`, `Page`, and `Scheme`.
- Containment direction:
  - A scheme contains pages.
  - A page contains components.
  - A component contains actions and Vue 3 component files/configuration.

## Release Scope

- There is no "later expansion" or "future version" assumption.
- All feasible capabilities required by the project should be landed in this version.

## Desktop Architecture

- Technology stack:
  - C#
  - .NET 10 LTS, currently the latest stable .NET line confirmed from Microsoft official downloads/support pages on 2026-06-27. Re-check official Microsoft .NET pages before scaffolding if time has passed.
  - Chromium kernel
  - Vue 3 frontend
- Framework direction:
  - Avalonia plus Chromium/CEF is accepted as the desktop shell direction.
- Required desktop platforms:
  - Windows
  - macOS
  - Linux major GUI distributions
- Required desktop architectures:
  - arm64
  - x86_64
- Required modern UI capability:
  - Transparent window background.
  - Frontend content with partial transparency.
  - Final visual effect should support a semi-transparent frosted-glass style background.
  - This capability must be verified carefully across desktop platforms because platform support may differ.
- Desktop frontend and mobile frontend are separate Vue 3 projects because the desktop side is a designer/configuration/control app while the mobile side is a control surface display.

## UI Design System

- UI is generated/designed by Codex unless the user gives more specific direction.
- Vue 3 frontends must use Tailwind CSS v4.
- Icons should primarily use Yesicon's Solar icon set.
- If Solar does not contain a required icon, another Yesicon icon set may be used.
- All icons must come from Yesicon icon sets.
- Theme color: Tailwind CSS `sky-500`.
- UI must support light mode and dark mode.
- Main window background should have partial transparency and a frosted-glass feel.
- Content surfaces such as navigation bars and cards should not be transparent.
- UI style should lean toward consumer-product appeal and product polish, not conservative B2B enterprise styling.
- Chromium-rendered areas must be fully drawn with Vue 3 UI.
- Do not expose built-in browser/Chromium UI surfaces to users.
- For operations such as export/download, OneDesk must draw its own dialogs, progress UI, and status feedback instead of using Chromium's default download UI.
- OneDesk requires two notification types:
  - In-app notifications/toasts for internal operation feedback such as save, import, export, and delete.
  - Native system notifications for major events such as device disconnection.
- Async operations such as large file import/export must show loading states or progress bars as appropriate.

## Mobile Architecture

- Mobile shell uses each target platform's native language.
- Mobile frontend uses Vue 3.
- Android is part of the required validation scope.
- Mobile platforms included in this version:
  - Android
  - iOS
- Android native shell language: Kotlin.
- iOS native shell direction: Swift plus WKWebView.
- iOS is included in the product scope, but current routine validation only requires Android unless the user changes the validation rule.
- A mobile device can save many desktop connection records, but can only maintain one active desktop connection at the same time.
- Every mobile app launch opens the connection screen first.
- The connection screen supports entering desktop IP, port, and a 6-digit verification code.
- The connection screen shows previously connected desktop devices.
- After connecting, the mobile side checks the cached scheme for that desktop. If the scheme has updates, it updates the cache first; otherwise it displays the cached scheme directly.
- The mobile side must cache the corresponding scheme before displaying it.

## Device Identity And Connection

- A desktop can connect to multiple mobile devices at the same time.
- A desktop assigns an ID to each mobile device and to itself for identity recognition.
- During connection setup, the desktop sends the assigned identity information to the newly connected mobile device.
- The 6-digit verification code is only used for initial pairing and for exchanging a long-term trust credential.
- The pairing verification code should have an expiry time, attempt limits, and should become invalid after successful use.
- After successful pairing, future connections should use the long-term trust credential instead of asking for the 6-digit verification code again.
- OneDesk is mainly designed for LAN usage.
- Public network usage may be possible, but users must build their own public network access and security protection. The software itself does not provide public network exposure or public network security hardening.

## Logging

- OneDesk requires a detailed logging system.
- Logs are primarily stored on the desktop side.
- The mobile side only stores logs while disconnected.
- When a mobile device connects to a desktop, it sends logs recorded during the disconnected period to the desktop and then clears its local disconnected logs.

## Scheme Cache Consistency

- Mobile scheme cache updates must use versioning plus integrity checks.
- Scheme cache update metadata should include at least a version and content hash.
- Scheme cache replacement should be atomic so a failed or interrupted update does not leave a broken scheme as the active cache.

## Repository Structure

- Use a monorepo structure.
- Use `pnpm` for frontend/package workspace management.
- Planned top-level structure:
  - `apps/desktop`
  - `apps/mobile/android`
  - `apps/mobile/ios`
  - `frontends/desktop`
  - `frontends/mobile`
  - `packages/protocol`
  - `docs`

## Frontend And Networking Constraints

- Desktop shell and mobile shell must load frontend assets using `file://`.
- Frontend code must not implement network communication.
- All network communication must be forwarded through native shells.
- Shells must enforce frontend networking restrictions rather than relying only on convention.
- Desktop Chromium/CEF, Android WebView, and iOS WKWebView should block or intercept direct frontend networking primitives and remote resource loading, including fetch/XHR, WebSocket, navigation to remote URLs, and remote asset requests where technically possible.
- Desktop and mobile communicate using QUIC over UDP.
- QUIC implementation choice: MsQuic.
- Protocol definitions should use schema-driven definitions that can generate or synchronize types for C#, Kotlin, Swift, and TypeScript.

## JSAPI

- All shells expose system capabilities to frontend code through JSAPI.
- A shell only implements capabilities that are supported by its host system and by the OneDesk shell on that system.
- Unsupported capabilities must still be callable through a consistent API shape, but must return a clear unsupported-capability error instead of silently failing.
- Every JSAPI call must include a target device ID.
- If the target device ID is the current device's own ID, the shell intercepts and executes the JSAPI call locally.
- If the target device ID is not the current device's own ID, the shell forwards the JSAPI call.
- The desktop acts as the JSAPI gateway/router.
- Mobile-to-mobile JSAPI calls must be routed through the connected desktop. Mobile devices do not directly send JSAPI calls to each other.
- JSAPI calls should carry a calling source identity such as component ID, plugin ID, scheme ID, and target device ID so permissions can be enforced.
- JSAPI calling source identity must be injected by the trusted runtime container or shell, not freely supplied by component, plugin, or frontend code.
- Frontend code may provide target device ID and call parameters, but must not be trusted to self-declare component/plugin/scheme identity.
- JSAPI capability directory must be complete. This is a required and important project item.
- JSAPI capability directory should first define the capability registration mechanism and common error model, then list desktop, Android, and iOS capability support tables.

## JSAPI Routing Rules

- If the target device is offline, return a target-offline error.
- If the target device exists but the caller lacks permission for the capability, return a permission-denied error.
- If the target device or target capability does not exist, return a not-found or unsupported-capability error as appropriate.
- The desktop must log cross-device JSAPI calls, including mobile-to-mobile calls routed through the desktop.
- Sensitive mobile-to-mobile capabilities such as files, sensors, clipboard, and other private device resources must require explicit permission grants.

## Permission Model

- JSAPI capabilities are categorized into major categories and sub-capabilities.
- Example category: file management.
- Example sub-capabilities: file read, file modify, file delete.
- If a major category is granted, all sub-capabilities under it are granted.
- If a major category is not granted, sub-capabilities are denied by default.
- If a major category is not granted but an individual sub-capability is granted, only that sub-capability is allowed.
- Permission management applies to components and plugins.
- Frontend bridge code itself does not require separate user-facing permission management, but JSAPI execution must still be authorized using the calling source identity.
- Component import and plugin install/import must show a permission dialog.
- The permission dialog displays requested permissions grouped by the same major category and sub-capability model.
- Requested permissions are granted by default in the dialog, but the user can adjust authorization before confirming.
- High-risk permissions must be clearly marked.
- Users can later modify component and plugin permissions in settings, including adding or removing granted permissions.
- High-risk permissions are defined by OneDesk.
- High-risk permissions should include at least file deletion/modification outside plugin/component private storage, process control, memory read/write, keyboard/mouse simulation, network access, clipboard read/write, camera, microphone, screen capture/recording, persistent background execution, credential/keychain access, shell command execution, and cross-device sensitive JSAPI access.
- Code-edited components and externally imported Vue component projects must include a manifest declaring requested permissions.
- Component permissions must travel with schemes when schemes are packaged, cached, and applied to mobile devices.
- Scheme cache should include component manifests, granted permission state, versions, and hashes.
- If code-edited or externally imported component code calls an undeclared or unauthorized JSAPI capability, runtime must reject the call and log it.

## Components

- Components require a component management page and a component editing page.
- A component is a Vue 3 component.
- Component file structure should be consistent with normal Vue 3 component project structure.
- Component editing supports visual editing and code editing.
- Visual editing generates Vue 3 component code.
- Visual editing must also maintain a separate configuration file so the visual editor can restore saved input values and controls.
- Visual editing supports normal styles:
  - Background: solid color, gradient, image, video.
  - Inserted image: size, position, margin, centered layout.
  - Inserted text: font, color, size, position.
  - Locked style.
  - Pressed style.
  - Action system configuration.
- Hover style is removed because mobile touch devices do not reliably support hover.
- Code editing is a Vue 3 component development interface similar to VS Code, with a file tree on the left and code editor on the right.
- Switching from visual editing to code editing is irreversible because arbitrary code cannot be restored into visual editor configuration.
- Before switching from visual editing to code editing, show a confirmation dialog warning that the component cannot return to visual editing.
- Both visual editing and code editing must include a preview window.
- The preview content area must use overflow-hidden behavior so component content cannot overflow the parent preview container.
- The preview window supports configurable preview ratios such as 1:1, 2:3, and 4:6.
- Components support import and export as compressed packages.
- Component import must show the standard permission dialog before installation/import completion.
- Component packages must include a manifest declaring identity, version, editing mode compatibility, requested permissions, entry files, and visual-editor configuration presence if applicable.
- Import must support Vue 3 component projects edited by external editors.
- If an imported package is a valid visual-editor project and has no validation errors, it can continue to use visual editing.
- If an imported package is a plain Vue 3 component project or a project that has entered code editing, it cannot enter visual editing.
- Component export must include dependent actions.

## Action System

- The action system does not have standalone action management and action editing pages by default.
- The action system appears as a dialog inside component visual editing, and may also appear as a categorized settings subpage.
- A component can contain multiple actions.
- One action can have exactly one trigger.
- Within the same component, triggers must be unique. Multiple actions in the same component cannot use the same trigger.
- Actions can be saved and reused by other components.
- Reusing an action copies or references the action definition for that component's own use, but does not create runtime linkage between components.
- When a component's action is triggered, only that component's own action executes. Other components with reused action definitions are not triggered.
- Action execution can call JSAPI capabilities.
- The action system exists only in visual editing. In code editing, users manually write related logic.

## Plugin System

- Plugins are part of the current version scope.
- Plugins are divided into frontend plugins and backend plugins.
- Frontend plugins are Vue 3 plugins.
- Frontend plugins must not provide custom UI. They may only provide desktop-frontend runtime logic, extension-point scripts, action/configuration helpers, or settings schema helpers that are rendered by OneDesk-controlled UI.
- Backend plugins use system capabilities through a desktop-side plugin framework.
- Backend plugins use an independent-process plugin model with a language-agnostic protocol, so developers can write plugins in any language as long as they implement the OneDesk plugin protocol.
- Both frontend plugins and backend plugins run only on the desktop side.
- Mobile devices do not run plugins directly.
- If a mobile device needs plugin functionality, it sends a JSAPI request through the desktop gateway and the desktop invokes the plugin.
- Backend plugins should have enough access to useful system capabilities, but their permissions are managed by the OneDesk client.
- The plugin framework should prioritize cross-platform support because OneDesk supports Windows, macOS, and Linux.
- Plugin permissions must integrate with the JSAPI permission model and should be categorized by major capability category and sub-capability.
- Plugin execution and plugin-originated JSAPI calls must be logged by the desktop.
- A plugin package can contain both frontend plugin parts and backend plugin parts.
- A plugin package can also contain only a frontend plugin or only a backend plugin.
- Frontend plugin parts and backend plugin parts must not communicate directly. Their communication must go through the desktop shell/plugin host.
- Plugins must not provide their own UI.
- If a plugin needs to exchange configuration data with the user, it submits a settings form JSON/schema to the desktop.
- The desktop generates the settings UI from the plugin-provided settings JSON/schema.
- After the user fills in settings, the desktop submits the settings data back to the plugin.
- Plugins are allowed to run persistently in the background when authorized.
- A plugin package may contain multiple platform-specific backend artifacts under one unified manifest and protocol.
- A single native compiled backend artifact is not expected to run across Windows, macOS, and Linux. Cross-platform plugin experience is achieved through the plugin package, manifest, and protocol.
- Plugin manifest should describe plugin identity, version, supported platforms/architectures, frontend entry if present, backend entry if present, permissions, settings schema, and whether background persistence is required.
- Plugin package format: `.onedesk-plugin`, implemented as a zip-compatible compressed package.
- Plugin settings forms use JSON Schema plus OneDesk extension fields where needed.
- Backend plugin communication protocol: JSON-RPC.
- Plugin packages should be self-contained by default.
- Plugin install/import must show the standard permission dialog before installation/import completion.
- Online plugin marketplace is not required.
- Independent-process backend plugins can technically access system resources outside OneDesk's JSAPI/permission gateway depending on the operating system and plugin implementation.
- OneDesk will notify users of this security boundary when installing/importing plugins, but does not need to add stronger sandbox constraints at this stage.

## Trigger Priority

- Trigger conflicts are resolved by priority:
  - Component trigger first.
  - Page-specific trigger second.
  - Scheme global trigger third.
- If a component consumes a trigger inside its hit area, the page or scheme trigger should not run.
- If a component does not consume the trigger, the trigger can bubble to page-specific behavior and then to scheme global behavior.

## Touch And Device Triggers

- Touch triggers should be organized into standard stable triggers, platform-limited triggers, and device sensor triggers.
- Support scope is five fingers or fewer.
- Standard touch triggers should include tap, double tap, long press, press and hold, swipe up/down/left/right, horizontal swipe, vertical swipe, pinch in/out, rotate, and multi-finger variants from one to five fingers where platform support allows.
- Multi-finger directional triggers should include two-finger, three-finger, four-finger, and five-finger swipes in up/down/left/right directions where platform support allows.
- Platform-limited triggers must return unsupported-capability or unsupported-trigger feedback when the host platform cannot reliably provide them.
- Device sensor triggers may include shake, orientation change, tilt direction, and similar device-supported motion/orientation events where the host platform allows them.

## Pages

- Pages require a page management page and a page editing page.
- Pages support import and export.
- Page export must package the page and all components contained by that page.
- Page export must also include dependent actions through its contained components.
- A page is a standalone full-screen app page on mobile.
- A page contains a grid matrix similar to Tailwind CSS grid.
- Users can set grid row count and column count.
- Users can set page padding. If page padding is absent, the grid matrix is centered by default.
- Users can set row gap and column gap.
- Users can uniformly or individually set grid cell corner radius and outline style, including outline color, width, and style.
- Users can set page background as solid color, gradient, image, or video.
- Grid cells can span multiple rows and columns similar to Tailwind CSS `col-span` and `row-span`.
- Grid cells can bind components.
- Component content must not overflow the grid cell container.

## Schemes

- A scheme is composed of multiple pages.
- A scheme has a page list that supports adding pages, deleting pages, and reordering pages.
- Page order is top-to-bottom in the editor and maps to previous/next page order.
- A scheme can set global page switching triggers using the same trigger model as component actions.
- Page switching is cyclic:
  - The previous page of the first page is the last page.
  - The next page of the last page is the first page.
- A scheme can set global page switching animations such as fade in/out.
- Individual pages can define page-specific switching behavior, such as swiping on page 3 to switch to page 6.
- Individual page switching can also set its own animation.
- Scheme editing UI requires a flowchart on the right side.
- Flowchart nodes are pages.
- Flowchart edges represent page switching relationships.
- Each edge displays the switching trigger and animation effect.
- A scheme is the only final artifact that can be applied to a mobile device.
- Each mobile device can have only one active applied scheme at the same time.
- Applying a new scheme to a mobile device replaces the old scheme.
- Schemes support import and export.
- Scheme export must include contained pages, components, actions, and required plugin dependencies.
- Scheme import/install must check required plugin dependencies.
- If required plugins are missing, OneDesk should install/import the included plugin dependencies.
- If required plugin versions conflict with installed plugin versions, OneDesk must notify the user and let the user choose how to proceed.
- If a scheme depends on plugins that are missing, disabled, unauthorized, or version-incompatible, affected components/actions should show a clear error state instead of failing silently.

## Pairing Direction

- Pairing will support manual IP input plus verification code.
- Pairing will support QR-code scanning.
- Pairing QR code is generated by the desktop client and contains connection IP, port, and verification code information.
- The mobile client can scan the QR code to connect directly.

## Locked Modules

- None yet.

## Open Questions

- Confirm desktop Chromium integration package after prototype validation.
- Confirm exact protocol schema technology.
- Confirm CI matrix and release artifact strategy.
- User will describe desktop action capability boundaries later.

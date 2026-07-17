# OneDesk Implementation Gap Audit

Audit date: 2026-07-17

This document records executable behavior, partial implementations, and missing product paths. A source file, interface, manifest, or placeholder screen is not evidence that a capability is complete.

## Completed And Verified In This Pass

| Area | Status | Evidence |
| --- | --- | --- |
| Android first-run data | Complete | Demo workspace bootstrap was removed; a device without an exact assignment receives an empty descriptor and displays the empty-scheme page. |
| Pairing QR | Complete on Android | The connection screen launches a native CameraX/ZXing scanner and accepts only `onedesk://pair` payloads. |
| Device-specific scheme assignment | Complete on current transport | Global desktop state is not used as mobile fallback; assignments are keyed by mobile device ID. |
| Scheme transfer | Complete on current transport | Snapshot and media are downloaded in chunks, hash checked, atomically cached, pushed to subscribed devices, and acknowledged after caching. |
| Android scheme display | Complete for visual components | Full-screen pages render backgrounds, media, square grids, spans, saved visual components, touch actions, and page transitions. |
| Page orientation | Complete on Android | Persisted page width/height ratio selects sensor landscape or portrait without recreating the Activity. |
| Mobile logs | Complete on current transport | Disconnected logs persist locally and upload on connect; online logs are sent directly to the desktop and only fall back to local storage on failure. |
| Gateway heartbeat logging | Complete on current transport | Heartbeats refresh peer liveness without rewriting device-registration logs; an integration assertion protects this behavior. |
| Cross-device JSAPI transport | Complete for registered handlers | Trusted requests reach the subscribed Android shell and return the real native result through the desktop router. |

## Incomplete Product Requirements

| Severity | Area | Current gap | Completion condition |
| --- | --- | --- | --- |
| Critical | MsQuic transport | Desktop and Android currently use UDP JSON datagrams, without MsQuic TLS sessions, streams, congestion control, or QUIC connection lifecycle. | Replace both transport implementations with MsQuic-compatible QUIC while retaining identity, chunk/cache, push, log, and JSAPI semantics. |
| Critical | Cross-platform desktop | The working client is WinForms/WebView2 on Windows. The Avalonia window is a text placeholder and no functional macOS/Linux Chromium host or packaging exists. | Functional Avalonia/Chromium shell and verified x64/arm64 artifacts for Windows, macOS, and major GUI Linux distributions. |
| Critical | iOS client | Swift/WKWebView files only load local HTML and block remote navigation; pairing, scanner, trust, cache, renderer bridge, logs, routing, and JSAPI handlers are absent. | Native Swift implementation matching the validated Android behavior, with unsupported capabilities returning structured errors. |
| Critical | Code component runtime | Code-mode Vue component projects are stored but are not compiled into a signed/validated mobile-renderable artifact. The mobile renderer cannot execute raw projects. | Deterministic desktop build pipeline, package manifest/hash, isolated runtime loading, trusted source injection, lifecycle unload, and mobile rendering. |
| High | Complete JSAPI implementation | The directory lists many capabilities whose desktop/Android/iOS handlers are absent; the markdown IDs and runtime catalog IDs also differ. | One canonical generated catalog plus platform support tests and concrete handlers or explicit unsupported registration for every ID. |
| High | Plugin runtime completeness | Backend stdio invocation exists, but plugin handshake, concurrent RPC correlation, process health/restart, resource limits, trusted plugin-originated JSAPI, and frontend plugin execution are incomplete. | Full protocol lifecycle, permission-enforced trusted calls, crash recovery, isolation controls, and frontend/backend communication only through the shell. |
| High | Package dependency conflicts | Package extraction and dependency reports exist, but scheme/plugin version conflicts do not yet present the required complete user choice and transactional rollback flow. | Preflight dependency graph, user conflict decisions, atomic install, rollback, and tests. |
| High | Device/sensor triggers | Touch gestures are implemented, but shake, tilt, orientation, proximity, hardware keys, and other declared device triggers are not wired to component actions. | Native event sources, permission handling, trigger normalization, lifecycle cleanup, and action tests. |
| High | Trusted runtime identity | Desktop validates declared component IDs against assigned schemes, but code/plugin execution containers do not yet inject an unforgeable source identity end to end. | Isolated containers issue shell-owned source tokens that frontend/plugin code cannot manufacture. |
| Medium | Protocol generation | `onedesk.proto` and TypeScript Zod schemas are not a single generated source and the running transport uses ad hoc JSON models. | One schema source generating synchronized C#, Kotlin, Swift, and TypeScript contracts with compatibility tests. |
| Medium | Automated coverage | One gateway integration test covers empty assignment, chunking, push/ack, endpoint preservation, and online logs. UI, import/export, permissions, plugins, Android instrumentation, and failure recovery lack broad automation. | Unit, integration, UI, Android instrumentation, corruption/interruption, and migration suites for all critical paths. |
| Medium | Release engineering | Windows and Android debug/local artifacts build, but signing, installers, update delivery, macOS notarization, Linux packages, and iOS distribution are absent. | Reproducible signed release artifacts for every required platform and architecture. |

## Audit Verdict

The Android visual-component control path is now a real connected implementation rather than a demo. The whole OneDesk product is not fully landed while any critical row above remains open. No module is locked as complete.

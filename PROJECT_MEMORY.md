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

## Product Summary

- OneDesk is a control software project conceptually similar to Stream Deck-style control software, but it must not copy Stream Deck's design or implementation.
- The product has a desktop side and a mobile side.
- The desktop side is responsible for execution, mobile interface design, backend flow handling, and core control logic.
- The mobile side displays the designed interface and sends user operations to the desktop side for control.

## Release Scope

- There is no "later expansion" or "future version" assumption.
- All feasible capabilities required by the project should be landed in this version.

## Desktop Architecture

- Technology stack:
  - C#
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

## Frontend And Networking Constraints

- Desktop shell and mobile shell must load frontend assets using `file://`.
- Frontend code must not implement network communication.
- All network communication must be forwarded through native shells.
- Desktop and mobile communicate using QUIC over UDP.
- QUIC implementation choice: MsQuic.

## Locked Modules

- None yet.

## Open Questions

- Confirm desktop Chromium integration package after prototype validation.
- Confirm CI matrix and release artifact strategy.

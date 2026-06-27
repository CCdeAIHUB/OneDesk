# JSAPI Capability Directory

This directory is intentionally complete at the category level from the start. Individual capabilities can return `CapabilityNotSupported` on platforms where the host OS or OneDesk shell cannot provide them.

## Error Model

- `CapabilityNotSupported`: the API shape exists, but this device/platform does not support it.
- `PermissionDenied`: the calling component or plugin lacks permission.
- `TargetOffline`: the target device is known but disconnected.
- `TargetNotFound`: the target device is unknown.
- `InvalidRequest`: parameters are invalid.
- `ExecutionFailed`: the host attempted the action and failed.

## Capability Categories

| Category | Examples | Desktop | Android | iOS | High Risk |
| --- | --- | --- | --- | --- | --- |
| `device` | identity, platform, battery, display info | yes | yes | yes | no |
| `file` | private read/write, external read/write/delete | yes | limited | limited | external write/delete |
| `clipboard` | read, write | yes | limited | limited | read/write |
| `notification` | in-app event, native notification | yes | yes | yes | no |
| `input` | hotkey, keyboard/mouse simulation | yes | limited | no | simulation |
| `process` | launch app, list/kill process | yes | limited | no | control |
| `shell` | execute command | yes | no | no | yes |
| `memory` | read/write process memory | platform-limited | no | no | yes |
| `network` | plugin-mediated network request | yes | limited | limited | yes |
| `sensor` | accelerometer, gyroscope, orientation | limited | yes | yes | sensitive cross-device use |
| `camera` | camera access | yes | yes | yes | yes |
| `microphone` | microphone access | yes | yes | yes | yes |
| `screen` | capture, record | yes | limited | limited | yes |
| `credential` | OS credential/keychain access | yes | limited | limited | yes |
| `plugin` | invoke desktop plugin method | yes | routed | routed | depends on plugin |
| `scheme` | active scheme info, page switch | yes | yes | yes | no |
| `log` | write structured log | yes | yes | yes | no |

## Desktop Capability IDs

- `device.identity`
- `device.platform`
- `device.display.list`
- `device.power.status`
- `file.private.read`
- `file.private.write`
- `file.private.delete`
- `file.external.read`
- `file.external.write`
- `file.external.delete`
- `clipboard.read`
- `clipboard.write`
- `notification.inApp`
- `notification.native`
- `input.hotkey.register`
- `input.hotkey.unregister`
- `input.keyboardMouseSimulation`
- `process.launch`
- `process.list`
- `process.control`
- `shell.execute`
- `memory.read`
- `memory.write`
- `network.access`
- `camera.access`
- `microphone.access`
- `screen.capture`
- `screen.record`
- `credential.access`
- `plugin.invoke`
- `scheme.active.get`
- `scheme.page.switch`
- `scheme.cache.status`
- `log.write`

## Android Capability IDs

- `device.identity`
- `device.platform`
- `device.display.list`
- `device.power.status`
- `file.private.read`
- `file.private.write`
- `file.private.delete`
- `file.external.read`
- `file.external.write`
- `clipboard.read`
- `clipboard.write`
- `notification.inApp`
- `notification.native`
- `input.keyboardMouseSimulation`
- `network.access`
- `sensor.accelerometer`
- `sensor.gyroscope`
- `sensor.orientation`
- `camera.access`
- `microphone.access`
- `screen.capture`
- `credential.access`
- `plugin.invoke`
- `scheme.active.get`
- `scheme.page.switch`
- `scheme.cache.status`
- `log.write`

Unsupported Android calls include process control, shell execution, memory read/write, and any capability blocked by Android sandbox or user permission state.

## iOS Capability IDs

- `device.identity`
- `device.platform`
- `device.display.list`
- `device.power.status`
- `file.private.read`
- `file.private.write`
- `file.private.delete`
- `clipboard.read`
- `clipboard.write`
- `notification.inApp`
- `notification.native`
- `sensor.accelerometer`
- `sensor.gyroscope`
- `sensor.orientation`
- `camera.access`
- `microphone.access`
- `screen.capture`
- `credential.access`
- `plugin.invoke`
- `scheme.active.get`
- `scheme.page.switch`
- `scheme.cache.status`
- `log.write`

Unsupported iOS calls include external file deletion/modification outside allowed pickers, process control, shell execution, memory read/write, keyboard/mouse simulation, and any capability blocked by iOS sandbox or user permission state.

## Registration Rule

Every shell registers supported capabilities at startup with capability ID, permission category, risk level, platform support, and handler metadata. Unsupported platform capabilities still keep a stable API identity and return `CapabilityNotSupported`.

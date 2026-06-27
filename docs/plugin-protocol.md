# Plugin Protocol

Backend plugins run as independent processes and communicate with the desktop plugin host through JSON-RPC. The transport can be stdio or a local IPC channel chosen by the plugin host. Plugins can be written in any language as long as they implement the OneDesk protocol.

## Lifecycle

1. OneDesk reads `onedesk.plugin.json`.
2. OneDesk shows requested permissions, marking high-risk permissions.
3. The user confirms or adjusts authorization.
4. OneDesk starts persistent backend plugins when authorized.
5. OneDesk invokes plugin methods through JSON-RPC.
6. Plugin-originated JSAPI calls are routed back through the desktop host and permission system.

## Required JSON-RPC Methods

### `onedesk.handshake`

Plugin reports runtime metadata.

Request:

```json
{
  "pluginId": "example.plugin",
  "protocolVersion": 1,
  "capabilities": ["method.invoke", "settings.schema"]
}
```

Response:

```json
{
  "ok": true
}
```

### `onedesk.invoke`

OneDesk invokes a plugin method.

Request:

```json
{
  "method": "launchScene",
  "params": {
    "scene": "intro"
  },
  "source": {
    "schemeId": "studio-flow",
    "componentId": "scene-launcher"
  }
}
```

Response:

```json
{
  "ok": true,
  "result": {}
}
```

### `onedesk.configure`

OneDesk submits user-filled settings data.

Request:

```json
{
  "settings": {
    "apiToken": "stored-by-plugin-or-host",
    "defaultScene": "intro"
  }
}
```

Response:

```json
{
  "ok": true
}
```

## Security Boundary

Independent-process plugins may technically access resources outside OneDesk's JSAPI gateway depending on their implementation and OS permissions. OneDesk shows this boundary during install/import and manages permissions for calls made through OneDesk.

# Manifest Examples

## Plugin Manifest

```json
{
  "manifestVersion": 1,
  "id": "cc.onedesk.example.obs",
  "name": "OBS Control",
  "version": "1.0.0",
  "author": "Example",
  "backend": {
    "protocol": "json-rpc",
    "persistent": true,
    "artifacts": [
      {
        "platform": "windows",
        "architecture": "x64",
        "path": "backend/windows-x64/obs-control.exe",
        "command": []
      }
    ]
  },
  "permissions": [
    {
      "category": "network",
      "capability": "network.access",
      "risk": "high",
      "description": "Connect to local OBS WebSocket."
    },
    {
      "category": "background",
      "capability": "background.persistent",
      "risk": "high",
      "description": "Keep the OBS connection alive."
    }
  ],
  "settingsSchema": {
    "type": "object",
    "required": ["host", "port"],
    "properties": {
      "host": {
        "type": "string",
        "title": "OBS host",
        "default": "127.0.0.1"
      },
      "port": {
        "type": "integer",
        "title": "OBS WebSocket port",
        "default": 4455
      }
    }
  },
  "selfContained": true
}
```

## Component Manifest

```json
{
  "manifestVersion": 1,
  "id": "cc.onedesk.component.scene-launcher",
  "name": "Scene Launcher",
  "version": "1.0.0",
  "mode": "visual",
  "entry": "src/SceneLauncher.vue",
  "visualConfig": "onedesk.visual.json",
  "permissions": [
    {
      "category": "plugin",
      "capability": "plugin.invoke",
      "risk": "normal",
      "description": "Invoke a desktop plugin method."
    }
  ],
  "actionDependencies": ["cc.onedesk.action.launch-scene"],
  "pluginDependencies": [
    {
      "id": "cc.onedesk.example.obs",
      "version": "1.0.0"
    }
  ]
}
```

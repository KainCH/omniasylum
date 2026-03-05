# OmniForge Sync Agent

Background application that connects your streaming software (OBS Studio or Streamlabs Desktop) to OmniForge, enabling scene-based counter actions and overlay automation.

## Prerequisites

- **OBS Studio** with obs-websocket v5 (included in OBS 28+), OR
- **Streamlabs Desktop** with Remote Control enabled
- An OmniForge account with Scene Sync enabled
- An API token from your OmniForge settings page

## Setup

1. Edit `appsettings.json`:

```json
{
  "OmniForge": {
    "ServerUrl": "https://your-omniforge-instance.com",
    "ApiToken": "your-jwt-token"
  },
  "StreamingSoftware": "auto"
}
```

- `StreamingSoftware`: `"auto"` (detect), `"obs"`, or `"streamlabs"`

2. **OBS Studio** — Enable WebSocket in OBS: Tools > WebSocket Server Settings > Enable WebSocket Server. Default port is 4455. If you set a password:

```json
{
  "Obs": {
    "Url": "ws://localhost:4455",
    "Password": "your-obs-password"
  }
}
```

3. **Streamlabs Desktop** — Go to Settings > Remote Control and copy the API token:

```json
{
  "Streamlabs": {
    "Token": "your-streamlabs-token"
  }
}
```

## Running

```bash
dotnet run --project OmniForge.DotNet/src/OmniForge.SyncAgent
```

The agent starts a system tray icon:
- **Green** = connected to both server and streaming software
- **Yellow** = partially connected
- **Red** = disconnected

Right-click the tray icon for options: Status, Open Pre-Flight, Exit.

## Auto-Start with Windows

Set `"StartWithWindows": true` in `appsettings.json`, or install as a Windows Service:

```powershell
sc.exe create OmniForgeSyncAgent binPath="path\to\OmniForge.SyncAgent.exe"
```

## How It Works

1. Agent connects to your streaming software and discovers all scenes
2. Agent connects to OmniForge server via SignalR
3. Scene list is reported to the server and persisted
4. When you switch scenes, the agent reports the change
5. The server applies configured scene actions (counter visibility, timers, overtime alerts)

## Troubleshooting

- **Can't connect to OBS**: Ensure WebSocket Server is enabled in OBS (Tools > WebSocket Server Settings)
- **Can't connect to Streamlabs**: Ensure Remote Control is enabled in Streamlabs Desktop settings
- **Server connection fails**: Check your API token is valid and the server URL is correct
- **Scenes not appearing**: Switch to each scene once, or restart the agent after adding new scenes in OBS/Streamlabs

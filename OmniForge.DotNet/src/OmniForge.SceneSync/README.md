# OmniForge SceneSync

A lightweight .NET console application that runs alongside OBS Studio or Streamlabs Desktop, detects scene changes in real time, and syncs them to the OmniForge server. Connected overlay clients receive `sceneChange` WebSocket events they can use to adapt their display.

## How It Works

```
┌──────────────┐     obs-websocket v5      ┌──────────────────┐     REST API      ┌──────────────────┐     WebSocket      ┌─────────────┐
│  OBS Studio  │ ─────────────────────────► │                  │ ─────────────────► │                  │ ─────────────────► │   Overlay   │
│  (scene sw.) │                            │  OmniForge       │  POST /api/       │  OmniForge       │  sceneChange msg  │  (browser   │
└──────────────┘                            │  SceneSync       │  stream/scene     │  Server          │                    │   source)   │
                                            │  (this app)      │                    │  (.NET Web)      │                    └─────────────┘
┌──────────────┐     named pipe (slobs)     │                  │                    │                  │
│  Streamlabs  │ ─────────────────────────► │                  │                    │                  │
│  Desktop     │                            └──────────────────┘                    └──────────────────┘
└──────────────┘
```

1. **SceneSync** connects to OBS Studio via the obs-websocket v5 protocol and/or Streamlabs Desktop via its named pipe.
2. When a scene switch is detected, the app calls `POST /api/stream/scene` on the OmniForge server with the scene name, source, and previous scene.
3. The server broadcasts a `sceneChange` event to all connected overlay WebSocket clients for that user.

## Prerequisites

- **.NET 9.0 Runtime** (or use the self-contained publish — see below)
- **OBS Studio** with the WebSocket server enabled (Settings → WebSocket Server Settings → Enable)
  - Default port: `4455`
  - Password can be set or left blank
- **Streamlabs Desktop** (optional) — the SLOBS named pipe API is used automatically when enabled
- A valid **JWT token** from your OmniForge account

## Setup

### 1. Get Your Twitch JWT Token

The SceneSync app uses the same Twitch JWT token that the OmniForge web app uses for API authentication.

1. Log in at **https://stream-tool.cerillia.net** with your Twitch account
2. Open your browser DevTools (F12) → **Application** → **Cookies**
3. Find the cookie named `token` for `stream-tool.cerillia.net`
4. Copy the cookie value — that's your JWT token

### 2. Configure `appsettings.json`

Edit `appsettings.json` next to the executable:

```json
{
  "SceneSync": {
    "Server": {
      "BaseUrl": "https://stream-tool.cerillia.net",
      "AuthToken": "YOUR_TWITCH_JWT_TOKEN_HERE"
    },
    "OBS": {
      "Enabled": true,
      "Host": "localhost",
      "Port": 4455,
      "Password": ""
    },
    "Streamlabs": {
      "Enabled": false,
      "PipeName": "slobs",
      "TimeoutSeconds": 10
    },
    "DebounceMs": 500
  }
}
```

| Setting               | Description                                     |
| --------------------- | ----------------------------------------------- |
| `Server.BaseUrl`      | Your OmniForge server URL                       |
| `Server.AuthToken`    | Twitch JWT token (copy from browser cookie `token` after login) |
| `OBS.Enabled`         | Set `true` to connect to OBS Studio             |
| `OBS.Host`            | OBS WebSocket host (usually `localhost`)        |
| `OBS.Port`            | OBS WebSocket port (default `4455`)             |
| `OBS.Password`        | OBS WebSocket password (leave empty if none)    |
| `Streamlabs.Enabled`  | Set `true` to connect to Streamlabs Desktop     |
| `Streamlabs.PipeName` | Named pipe name (default `slobs`)               |
| `DebounceMs`          | Milliseconds to debounce duplicate scene events |

### 3. Enable OBS WebSocket Server

In OBS Studio:
1. Go to **Tools → WebSocket Server Settings**
2. Check **Enable WebSocket server**
3. Set a port (default `4455`) and optionally a password
4. Click **OK**

### 4. Run

```bash
# From the project directory
dotnet run --project src/OmniForge.SceneSync

# Or from a published build
./OmniForge.SceneSync
```

The app will:
- Connect to OBS/Streamlabs (retrying with backoff if they're not running yet)
- Log the current scene on connect
- Report every scene change to the server
- Keep running until you press **Ctrl+C**

## Building

### Development

```bash
cd OmniForge.DotNet
dotnet build src/OmniForge.SceneSync
dotnet run --project src/OmniForge.SceneSync
```

### Self-Contained Publish (Windows)

```bash
dotnet publish src/OmniForge.SceneSync -c Release -r win-x64 --self-contained -o ./publish/SceneSync
```

This produces a single folder with everything needed — no .NET runtime installation required.

## Overlay Integration

The overlay receives `sceneChange` WebSocket messages:

```json
{
  "method": "sceneChange",
  "data": {
    "sceneName": "Main Scene",
    "previousScene": "Starting Soon",
    "source": "OBS",
    "timestamp": "2026-03-01T12:00:00Z"
  }
}
```

The overlay stores the current scene in `window.currentScene` and dispatches a `omniforge:sceneChange` CustomEvent on `window` that other scripts can listen to:

```javascript
window.addEventListener('omniforge:sceneChange', (e) => {
  console.log('Scene changed to:', e.detail.sceneName);
  // Show/hide elements, change themes, etc.
});
```

## Troubleshooting

| Symptom                                | Fix                                                                                                       |
| -------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `Authentication failed (401)`          | Your Twitch JWT token is expired or invalid. Log in again at stream-tool.cerillia.net and copy the new `token` cookie. |
| `Failed to reach the OmniForge server` | Check `Server.BaseUrl` in appsettings.json. Is the server running?                                        |
| `OBS connection attempt failed`        | Make sure OBS is running and the WebSocket server is enabled. Check host/port/password.                   |
| `Streamlabs pipe closed`               | Make sure Streamlabs Desktop is running. The pipe is only available while SLOBS is open.                  |
| Duplicate scene events                 | Increase `DebounceMs` (default 500ms). This can happen if both OBS and Streamlabs report the same switch. |

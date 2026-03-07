---
applyTo: "OmniForge.DotNet/src/OmniForge.SyncAgent/**/*.cs"
---

# OmniForge — Sync Agent Development Instructions

> These instructions activate automatically when editing any file in OmniForge.SyncAgent.

## What the Sync Agent Is

The Sync Agent is a **Windows desktop background application** — a system-tray app built on .NET Generic Host + WinForms. It is **not** a web app or a Blazor app. It runs on the streamer's PC and bridges streaming software (OBS Studio, Streamlabs Desktop) to the OmniForge server.

It is **excluded from Docker builds** and cannot be deployed as a container. Publishing is done via `publish-agent.ps1`.

---

## Project Layout

```
OmniForge.SyncAgent/
├── Program.cs                    ← Entry point: self-install, auto-update relaunch, Generic Host setup
├── AgentConfigStore.cs           ← JSON config read/write at %AppData%\omni-forge\agent-config.json
├── Abstractions/
│   └── IStreamingSoftwareClient.cs   ← Interface for OBS and Streamlabs clients
└── Services/
    ├── ObsWebSocketClient.cs         ← OBS Studio via WebSocket v5 (port 4455)
    ├── StreamlabsDesktopClient.cs    ← Streamlabs via named pipe
    ├── StreamingSoftwareDetector.cs  ← Polls every 5s for available software
    ├── StreamingSoftwareMonitor.cs   ← Manages active IStreamingSoftwareClient; fires scene events
    ├── ServerConnectionService.cs    ← SignalR hub connection to OmniForge server
    ├── PairingService.cs             ← Browser-based code pairing flow
    ├── AutoUpdateService.cs          ← Self-update from Azure Blob Storage
    ├── AutoStartService.cs           ← Windows registry auto-start management
    └── TrayIconService.cs            ← WinForms NotifyIcon tray menu + toast notifications
```

---

## Adding a New Streaming Software Integration

When adding support for a new streaming software (e.g. `XSplit`, `vMix`), implement `IStreamingSoftwareClient`:

```csharp
public interface IStreamingSoftwareClient
{
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<string[]> GetScenesAsync();
    Task<string?> GetActiveSceneAsync();
    bool IsConnected { get; }
    string SoftwareType { get; }       // Used in SignalR ReportScenes call
    event Action<string>? SceneChanged;
    event Action<string[]>? SceneListUpdated;
    event Action? Connected;
    event Action<string>? Disconnected;
}
```

Register in `StreamingSoftwareDetector` so it's polled during the 5-second detection cycle. See `ObsWebSocketClient` for the OBS WebSocket v5 pattern.

---

## Server Communication — SignalR Hub Methods

`ServerConnectionService` maintains the SignalR connection to the OmniForge server. It receives token from `AgentConfigStore` and reconnects on disconnect.

**Outbound hub calls (agent → server):**

```csharp
await _hub.InvokeAsync("ReportScenes", scenesArray, softwareType);
await _hub.InvokeAsync("ReportSceneChange", sceneName);
```

**Inbound hub messages (server → agent):**
Handle via `_hub.On<T>(methodName, handler)` registered in `StartAsync`.

If adding a new server → agent command, add the `_hub.On(...)` registration in `ServerConnectionService.StartAsync()` and wire it to a new event or method.

---

## Config Store — AgentConfigStore

All persisted agent settings live at `%AppData%\omni-forge\agent-config.json`. Access via `AgentConfigStore`.

**Adding a new config property:**

1. Add the property to the `AgentConfig` record/class
2. Access via `_configStore.Config.MyNewProperty`
3. Call `_configStore.Save()` after any mutation
4. Do NOT add sensitive data (tokens, secrets) to this file — tokens are stored separately per the pairing flow

---

## Auto-Update System

`AutoUpdateService` checks for updates every 4 hours and on startup. It:

1. Fetches `agent-manifest.json` from Azure Blob Storage
2. Compares semantic versions
3. Downloads the new exe to a temp path
4. Defers the actual update until stream is offline (watches `StreamStatusChanged` from `ServerConnectionService`)
5. Calls `--update-from <oldPath>` on the new exe, which waits for file lock release then overwrites

**Rules:**

- Never force-update mid-stream — always defer until `isLive == false`
- The self-install path is always `%AppData%\omni-forge\OmniForge.SyncAgent.exe`
- The `--update-from` argument handling lives at the top of `Program.cs` — don't move it

---

## Version Management & Publishing

**Always use VS Code tasks for publishing — never raw `dotnet publish`.**

| Task                              | When to use                                              |
| --------------------------------- | -------------------------------------------------------- |
| `Publish Sync Agent`              | Normal release — auto-increments build/patch version     |
| `Publish Sync Agent (bump minor)` | Feature release — increments minor version, resets patch |
| `Publish Sync Agent (bump major)` | Breaking change — increments major, resets minor + patch |

The current version is tracked in `OmniForge.DotNet/deploy/agent-version.txt`.

What the publish script does:

1. Bumps version in `agent-version.txt`
2. `dotnet publish` as a single-file self-contained Windows exe with version stamped
3. Signs the exe via **Azure Trusted Signing** (endpoint: `eus.codesigning.azure.net`, account: `omni-forge-sign`, profile: `OmniForgeAgent`)
4. Uploads `OmniForge.SyncAgent.exe` and `agent-manifest.json` to Azure Blob Storage

**One-time signing setup** (only on new dev machines):
Run the `Setup Agent Signing (one-time)` VS Code task, which runs `setup-agent-signing.ps1`.

---

## Tray Icon & WinForms Rules

`TrayIconService` runs on the WinForms message loop (via `Application.Run()` in a dedicated thread). The icon has 5 states: green (fully connected), yellow (partial), orange (scanning), red (disconnected), gray (not signed in).

**Rules for tray menu changes:**

- All WinForms UI updates must be marshalled back to the UI thread:
  ```csharp
  _trayIcon.GetType()  // WinForms calls must be on the UI thread
  // Use: Application.OpenForms[0]?.BeginInvoke(() => { ... })
  // Or: _syncContext.Post(_ => { ... }, null)
  ```
- Toast notifications (`ShowBalloonTip`) are fire-and-forget — no awaiting needed
- Do not add blocking async calls directly in menu click handlers — use `Task.Run` or `_ = DoWorkAsync()`

---

## Testing the Sync Agent

The Sync Agent has **limited automated test coverage** because:

- Most logic is process/OS interaction (tray icon, registry, named pipes, WebSocket)
- `TrayIconService`, `AutoStartService`, and the WinForms loop are `[ExcludeFromCodeCoverage]`

**What IS testable and should have tests:**

- `PairingService` — HTTP polling logic (mock `HttpClient`)
- `AgentConfigStore` — JSON serialization round-trips
- `AutoUpdateService` — version comparison logic, defer-until-offline logic
- `StreamingSoftwareDetector` — detection priority, state transitions

Use `NullLogger<T>` and constructor injection when testing — no `WebApplicationFactory` needed.

---

## Key Constraints

- **Windows-only** — `net9.0-windows`, `UseWindowsForms`, `UseWindowsRegistry` — do not add cross-platform abstractions
- **Single-file self-contained** — no installer, no .NET runtime dependency on the user's machine
- **Install path is canonical** — `%AppData%\omni-forge\OmniForge.SyncAgent.exe` — never use relative paths for auto-start or update logic
- **Never block the WinForms message loop** — all I/O must be off the UI thread
- **No server secrets in the agent** — it authenticates via a time-limited pairing code → JWT token stored locally

---

## Reference: Key External Dependencies

| Package                                | Purpose                        |
| -------------------------------------- | ------------------------------ |
| `OBSWebsocket-dotnet`                  | OBS Studio WebSocket v5 client |
| `Microsoft.AspNetCore.SignalR.Client`  | Server connection              |
| `Serilog`                              | Structured logging to file     |
| `System.Windows.Forms` (.NET built-in) | Tray icon and notifications    |

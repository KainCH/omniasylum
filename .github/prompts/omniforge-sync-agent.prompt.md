---
description: "Step-by-step protocol for Sync Agent work — new streaming software support, server commands, config, publishing"
tools:
  - codebase
  - editFiles
  - problems
  - runCommands
  - findTestFiles
---

You are working on the **OmniForge Sync Agent** — a Windows system-tray background app that bridges OBS Studio / Streamlabs Desktop to the OmniForge server. It is **not** a web app. It's a .NET Generic Host + WinForms process, published as a single-file self-contained exe for Windows.

---

## Architecture Quick Reference

```
Program.cs
  ├── Self-install to %AppData%\omni-forge\OmniForge.SyncAgent.exe (on first run)
  ├── --update-from handler (applies pending auto-update)
  └── .NET Generic Host
        ├── AgentConfigStore          — JSON config (%AppData%\omni-forge\agent-config.json)
        ├── PairingService            — browser-based code pairing → JWT token
        ├── StreamingSoftwareDetector — polls every 5s for OBS/Streamlabs
        ├── StreamingSoftwareMonitor  — manages active IStreamingSoftwareClient
        ├── ServerConnectionService   — SignalR hub connection to OmniForge
        ├── AutoUpdateService         — checks Azure Blob; defers update until stream ends
        ├── AutoStartService          — Windows registry auto-start
        └── TrayIconService           — WinForms NotifyIcon + toast notifications
```

**SignalR hub methods (agent → server):**

- `ReportScenes(string[] scenes, string softwareType)` — called on connect + scene list change
- `ReportSceneChange(string sceneName)` — called on every scene switch

---

## Task: Add Support for a New Streaming Software

### Step 1 — Implement IStreamingSoftwareClient

Create the new client class in `OmniForge.SyncAgent/Services/`:

```csharp
public class MyNewSoftwareClient : IStreamingSoftwareClient
{
    public bool IsConnected { get; private set; }
    public string SoftwareType => "mynewsoftware";  // lowercase, no spaces

    public event Action<string>? SceneChanged;
    public event Action<string[]>? SceneListUpdated;
    public event Action? Connected;
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Establish connection to the streaming software
        // Fire Connected when ready; fire Disconnected on drop
        IsConnected = true;
        Connected?.Invoke();
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        Disconnected?.Invoke("Manually disconnected");
    }

    public async Task<string[]> GetScenesAsync() => /* query software */ Array.Empty<string>();
    public async Task<string?> GetActiveSceneAsync() => /* query software */ null;
}
```

### Step 2 — Register in StreamingSoftwareDetector

Open `StreamingSoftwareDetector.cs` and add detection logic:

- Detect the software (check process, port, named pipe, etc.)
- Return the new client type when detected
- Follow the existing priority order: OBS → Streamlabs → new software

### Step 3 — Register in DI (Program.cs)

Add the new client to the Generic Host service registration in `Program.cs`.

---

## Task: Add a New Server → Agent Command

When the OmniForge server needs to push a command to the agent (e.g., "switch to scene X"):

### Step 1 — Add hub handler in ServerConnectionService

```csharp
// In StartAsync(), after existing _hub.On registrations:
_hub.On<string>("MySwitchCommand", async sceneName =>
{
    try
    {
        if (_monitor.Client?.IsConnected == true)
            await _monitor.Client.SwitchToSceneAsync(sceneName);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ Failed handling MySwitchCommand");
    }
});
```

### Step 2 — Expose event or call on ServerConnectionService

If other services need to react, expose an event:

```csharp
public event Action<string>? MyCommandReceived;
```

### Step 3 — Add the server-side hub method

On the OmniForge server, add the corresponding `SendToAgent` hub method in the SignalR hub class and wire it to the relevant service.

---

## Task: Add a New Config Property

Config lives at `%AppData%\omni-forge\agent-config.json` via `AgentConfigStore`.

1. Add property to the `AgentConfig` class:
   ```csharp
   public string MyNewSetting { get; set; } = "default";
   ```
2. Access via `_configStore.Config.MyNewSetting`
3. After mutation: `_configStore.Save()`
4. If the setting needs a UI toggle, add it to `TrayIconService`'s right-click menu
5. **Never store tokens, secrets, or user credentials** in `agent-config.json` — those belong in the JWT token file managed by `PairingService`

---

## Task: Update the Tray Icon / Menu

`TrayIconService` manages the `NotifyIcon` on the WinForms message loop.

**Critical rule: all WinForms calls must be on the UI thread.** Use `_syncContext.Post`:

```csharp
_syncContext.Post(_ =>
{
    _trayIcon.Icon = GetIcon(TrayState.Green);
    _trayIcon.Text = "OmniForge — Connected";
}, null);
```

**Adding a new menu item:**

```csharp
var myItem = new ToolStripMenuItem("My Action");
myItem.Click += async (_, _) =>
{
    // Use Task.Run for async work; never block the UI thread
    _ = Task.Run(async () =>
    {
        await DoMyActionAsync();
    });
};
_contextMenu.Items.Add(myItem);
```

---

## Task: Publish a New Agent Release

**Always use VS Code tasks — never run `dotnet publish` manually.**

| Change type                      | Task to run                                  |
| -------------------------------- | -------------------------------------------- |
| Bug fix / small change           | `Publish Sync Agent` (auto-increments patch) |
| New feature                      | `Publish Sync Agent (bump minor)`            |
| Breaking change or major release | `Publish Sync Agent (bump major)`            |

The publish script (`deploy/publish-agent.ps1`):

1. Reads/bumps `deploy/agent-version.txt`
2. Runs `dotnet publish` → single-file self-contained Windows exe
3. Signs with **Azure Trusted Signing** (requires: `az login` + signing role on `omni-forge-sign` account)
4. Uploads signed exe + `agent-manifest.json` to Azure Blob Storage

**Verify signing setup first** (on new machines):

- Run `Setup Agent Signing (one-time)` task
- Requires: Azure CLI authenticated, access to `omni-forge-sign` Trusted Signing account

**After publishing:**

- Live agents will check the manifest within 1 hour (or immediately if a manual check is triggered)
- Updates are deferred until the stream goes offline — users will not be interrupted mid-stream

---

## Testing the Sync Agent

Most of the Sync Agent is process/OS I/O, so `TrayIconService`, `AutoStartService`, and WinForms code are `[ExcludeFromCodeCoverage]`.

**What to test:**

```csharp
// PairingService — HTTP polling logic
_httpClientMock.Setup(...).ReturnsAsync(pairingResponse);
var token = await _sut.PairAsync(CancellationToken.None);
token.Should().NotBeNull();

// AgentConfigStore — round-trip serialization
var store = new AgentConfigStore();
store.Config.MyNewSetting = "value";
store.Save();
store.Load();
store.Config.MyNewSetting.Should().Be("value");

// AutoUpdateService — defer-until-offline logic
// Simulate isLive=false arriving after update is pending
```

---

## Key Constraints

- **Windows-only** — WinForms, named pipes, registry — no cross-platform code
- **No blocking the WinForms loop** — all async work via `Task.Run` from event handlers
- **Install path is canonical**: `%AppData%\omni-forge\OmniForge.SyncAgent.exe` — hardcoded by design
- **Single-file self-contained exe** — no installer, no .NET runtime requirement on user machine
- **Never mid-stream update** — `AutoUpdateService` enforces this via `StreamStatusChanged` event
- **Not in Docker** — excluded via `.dockerignore`; never try to containerize this project

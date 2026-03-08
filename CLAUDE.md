# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Run the app
dotnet run --project OmniForge.DotNet/src/OmniForge.Web

# Run all tests
dotnet test OmniForge.DotNet/OmniForge.sln

# Run a single test class
dotnet test OmniForge.DotNet/OmniForge.sln --filter "FullyQualifiedName~CounterControllerTests"

# Run a single test method
dotnet test OmniForge.DotNet/OmniForge.sln --filter "FullyQualifiedName~CounterControllerTests.MethodName"

# Run tests with coverage
dotnet test OmniForge.DotNet/OmniForge.sln --collect:"XPlat Code Coverage"

# Build only
dotnet build OmniForge.DotNet/OmniForge.sln
```

## Architecture

The solution has five projects under `OmniForge.DotNet/src/`:

**OmniForge.Core** — Pure domain layer with zero external NuGet dependencies. Contains entities, interfaces (all repository + service contracts live here), constants (`AlertTemplates`, `CounterTypes`), and utilities. If you're adding a reference to an external NuGet package here, it belongs in Infrastructure or Web instead.

**OmniForge.Infrastructure** — Implements Core interfaces. Azure Table Storage repositories, Twitch (TwitchLib + native EventSub WebSocket), Discord (Discord.Net), JWT, and all background services. DI registration is in `DependencyInjection.cs`. Infrastructure code excluded from coverage with `[ExcludeFromCodeCoverage]` where it wraps I/O.

**OmniForge.Web** — Blazor Server app + ASP.NET Core API controllers. Startup/DI in `Program.cs`. Controllers expose REST endpoints consumed by Blazor components via DI-injected services. `ServerInstance.cs` holds a singleton server ID/start time used for health checks and overlay reconnection logic.

**OmniForge.SyncAgent** — Windows-only system tray desktop app (.NET Generic Host + WinForms). Runs on the streamer's PC, bridges OBS/Streamlabs scene changes to the server via SignalR. Excluded from Docker builds; published as a single-file self-contained exe via `deploy/publish-agent.ps1`. Config stored at `%AppData%\omni-forge\agent-config.json`.

**OmniForge.SceneSync** — Scene sync abstractions shared between Web and SyncAgent.

### Key data flows

**Twitch EventSub → counter update → overlay notification:**
`NativeEventSubService` (raw WebSocket to Twitch) → `EventSubMessageProcessor` (testable parser, factored out from the I/O wrapper) → `EventSubHandlerRegistry` dispatches to typed handlers in `Services/EventHandlers/` (e.g. `CheerHandler`, `FollowHandler`) → each handler creates a DI scope via `IServiceScopeFactory`, updates repositories, calls `INotificationService` and/or `IAlertEventRouter` → `AlertEventRouter` resolves alert type from user config, then calls `IOverlayNotifier` → `SseOverlayNotifier` pushes JSON via SSE through `SseConnectionManager`.

**Overlay connections:**
- **V1 (WebSocket):** `WebSocketOverlayMiddleware` intercepts `GET /ws/overlay?userId=...` → `WebSocketOverlayManager` → `WebSocketOverlayNotifier`
- **V2 (SSE — current primary):** `OverlaySseController` handles `GET /sse/overlay?userId=...` → `SseConnectionManager` → `SseOverlayNotifier`
- `SseOverlayNotifier` is the registered `IOverlayNotifier` in DI. Both V1 and V2 coexist.

**Notifications fanout:** `NotificationService` (called after counter changes) fans out to: Discord via `IDiscordService`, Twitch chat via `ITwitchClientManager`, and overlay via `IOverlayNotifier`.

**Scene sync (SyncAgent → server):** OBS scene change → `ObsWebSocketClient.SceneChanged` event → `StreamingSoftwareMonitor` → `ServerConnectionService.InvokeAsync("ReportSceneChange", sceneName)` → `SyncAgentHub.ReportSceneChange` → `ISceneActionService.HandleSceneChangedAsync` → `IOverlayNotifier.NotifySceneChangedAsync`.

**Twitch connections:** Per-user connections are user-initiated (not auto-started). `TwitchClientManager` manages per-user `TwitchClient` instances for chat. `StreamMonitorService` manages EventSub subscriptions per user after they click "Start Monitor".

### Repository pattern
All repositories implement an interface from Core and inherit Azure Table Storage logic. Each calls `InitializeAsync()` at startup (called in `Program.cs`) to ensure tables exist. Local dev falls back to `UseDevelopmentStorage=true` (Azurite) when no `AzureStorage:AccountName` is configured.

### Multi-tenancy
All data is partitioned by `TwitchUserId`. Controllers extract the user ID from JWT claims — always use `User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value`, never `req.params`/route values directly.

### Feature flags
`User.Features` (of type `FeatureFlags`) gates capabilities like `StreamOverlay`, `OverlayV2`, `SceneSync`, `ChatCommands`, etc. Check flags in controllers and Blazor pages before allowing access. New features default to `false` and must be admin-enabled per user.

### EventSub handler strategy pattern
Handlers implement `IEventSubHandler`, register as `IEventSubHandler` in DI, and expose a `SubscriptionType` property (e.g. `"channel.cheer"`). `EventSubHandlerRegistry` resolves the correct handler at runtime. Each handler uses `IServiceScopeFactory` to create a DI scope (handlers are singletons; repositories are scoped).

## Testing

Tests use xUnit + Moq + bunit (Blazor components). **All new production code must maintain ≥85% code coverage.**

Controller tests manually construct controllers with mocked dependencies — no `WebApplicationFactory`:

```csharp
_sut = new CounterController(_mockRepo.Object, _mockLogger.Object, ...);
_sut.ControllerContext = new ControllerContext
{
    HttpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "test-user-123") }))
    }
};
```

Infrastructure I/O code is excluded from coverage with `[ExcludeFromCodeCoverage(Justification = "Wraps external I/O — logic tested in XxxProcessor")]`. Factor testable logic into a separate class (e.g., `EventSubMessageProcessor` is tested separately from `NativeEventSubService`).

## Key conventions

**DI:** All services use constructor injection. Use `IServiceScopeFactory` (not `IServiceProvider` in constructors) when a singleton needs scoped services. Register services in `OmniForge.Infrastructure/DependencyInjection.cs`.

**Async:** All I/O-bound operations must be async — never `.Result` or `.Wait()`. Use `ConfigureAwait(false)` in Infrastructure code (not in Blazor components). Include `CancellationToken` parameters for long-running operations.

**Logging:**
```csharp
_logger.LogInformation("✅ {Event} processed for {User}", eventType, username);
_logger.LogWarning("⚠️ {Event} skipped — {Reason}", eventType, reason);
_logger.LogError(ex, "❌ Failed to process {Event} for {User}", eventType, username);
```

## Configuration

Local: `appsettings.Development.json` — populate `Twitch:ClientId`, `Twitch:ClientSecret`, `Jwt:Secret`, and either `AzureStorage:AccountName` or `Azure:StorageConnectionString`.

Production: Azure Key Vault (accessed via Managed Identity, key vault name from `KeyVaultName` config key).

## Deployment

```powershell
# Deploy app image to Azure Container Apps
cd OmniForge.DotNet/deploy
./deploy.ps1 -Environment "dev"

# Full infra (Bicep) + app deploy
./deploy.ps1 -Environment "dev" -FullDeploy
```

**SyncAgent publishing — always use VS Code tasks, not raw `dotnet publish`:**

| Task                              | When to use                                              |
| --------------------------------- | -------------------------------------------------------- |
| `Publish Sync Agent`              | Normal release — auto-increments patch version           |
| `Publish Sync Agent (bump minor)` | Feature release — increments minor, resets patch         |
| `Publish Sync Agent (bump major)` | Breaking change — increments major, resets minor + patch |

The publish script signs the exe via Azure Trusted Signing and uploads it with `agent-manifest.json` to Azure Blob Storage for auto-update. Current version is tracked in `OmniForge.DotNet/deploy/agent-version.txt`.

## Pull Requests

Use GitHub MCP tools (`mcp_github_*`) for PR creation and review workflows. Do not use `gh pr create` or raw curl unless MCP tools are unavailable.

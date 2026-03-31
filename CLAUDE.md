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

**OmniForge.SceneSync** — Scene sync abstractions (Scene entity, SceneAction, OvertimeConfig) shared between Web and SyncAgent. Defines scene-triggered counter visibility overrides and overtime flashing config.

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

**Bot services lifecycle:**
`StreamOnlineHandler` starts all bot services per user → `ChatMessageHandler` calls `IBotModerationService` and `IAutoShoutoutService` on every message → event handlers (sub, raid, follow, clip) call `IBotReactionService` to send templated chat replies → `StreamOfflineHandler` stops services and calls `ResetSession()` on each, clearing all in-session state.

**Auto-ban flow:** `NativeEventSubService` receives `channel.suspicious_user.message` → `SuspiciousUserMessageHandler` (only acts on `"likely"` evaluations, skips `"possible"` to reduce false positives) → `TwitchApiService.BanUserAsync` using bot credentials from `BotCredentials` entity. Gated by `User.Features.AutoBanEvaders`.

### Repository pattern
All repositories implement an interface from Core and inherit Azure Table Storage logic. Each calls `InitializeAsync()` at startup (called in `Program.cs`) to ensure tables exist. Local dev falls back to `UseDevelopmentStorage=true` (Azurite) when no `AzureStorage:AccountName` is configured.

### Multi-tenancy
All data is partitioned by `TwitchUserId`. Controllers extract the user ID from JWT claims — always use `User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value`, never `req.params`/route values directly.

### Feature flags
`User.Features` (of type `FeatureFlags`) gates capabilities like `StreamOverlay`, `OverlayV2`, `SceneSync`, `ChatCommands`, `AutoBanEvaders`, etc. Check flags in controllers and Blazor pages before allowing access. New features default to `false` and must be admin-enabled per user. Bot moderation settings live on `User.BotModeration` (type `BotModerationSettings`); bot reaction/shoutout/scheduled message settings live on `User.BotSettings`.

### EventSub handler strategy pattern
Handlers implement `IEventSubHandler`, register as **Scoped** `IEventSubHandler` in DI, and expose a `SubscriptionType` property (e.g. `"channel.cheer"`). `EventSubHandlerRegistry` resolves the correct handler at runtime. Each handler uses `IServiceScopeFactory` to create a nested DI scope for resolving repositories.

### Bot services
Four singleton services in `Infrastructure/Services/`:

| Service | Purpose |
|---|---|
| `BotModerationService` | Per-message spam detection: anti-caps, anti-symbol, link guard (2-strike ban). Called from `ChatMessageHandler`. |
| `BotReactionService` | Sends templated chat messages for stream events (start, sub, raid, clip, first-time chat). Tokens: `{raider}`, `{viewers}`, etc. |
| `AutoShoutoutService` | Auto-shoutout for followers on first chat message per session. Cooldowns: 2.5 min/user, 65 sec/channel. Follow status cached 10 min. |
| `ScheduledMessageService` | Posts recurring chat messages on configurable intervals. Fires on a 1-minute tick. |

All four are registered as singletons in `DependencyInjection.cs` and use `IServiceScopeFactory` when they need scoped services.

### Session state management
Bot services track per-session state in `ConcurrentDictionary` fields (e.g. `_shoutedThisSession`, `_linkViolations`, `_greeted`, `_lastFired`). `StreamOfflineHandler` calls `ResetSession()` on each service to wipe this state when the stream ends. When adding a new bot service, follow this pattern — never persist session data in repositories.

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

## Commit Convention

### Message Structure

```
<type>(<scope>): <imperative summary>

<body>

Refs: <ADO work item IDs or None>
```

**type** — one of: `feat`, `fix`, `refactor`, `chore`, `docs`, `test`, `build`, `perf`, `deps`

**scope** — short, code-focused (e.g., `overlay`, `bot-moderation`, `eventsub`, `sync-agent`, `discord`). Use `global` when a change spans many areas; use `misc` if no scope fits. Skip blank scopes.

**summary** — imperative voice (e.g., "Add auto-ban for suspicious users"), max 72 characters.

### Body

- Bullet points starting with `-`
- Describe: key implementation moves, why the change is needed, side effects or follow-up TODOs
- Keep lines ≤ 100 characters
- Omit body only for trivial edits (typos, comment adjustments)

### Footer

Always include a footer line. Use the appropriate keyword:

- `Closes: #123` — the commit fully resolves the issue (GitHub auto-closes on merge to main)
- `Refs: #123` — the commit relates to the issue but does not fully resolve it
- `Refs: None` — no issue applies

Multiple issues: `Closes: #123, #456` or mix `Closes: #123` and `Refs: #456` on separate lines.

### Style

- No emojis or marketing language
- Call out breaking changes with `BREAKING CHANGE:` in the body
- Note test coverage with `Tests: added`, `Tests: not-run`, etc. when meaningful

### Example

```
feat(bot-moderation): add link guard with 2-strike auto-ban

- Adds LinkGuardEnabled setting to BotModerationSettings
- Tracks per-broadcaster link violations in ConcurrentDictionary
- Warns on first offense, bans on second via TwitchApiService
- ResetSession clears violations on stream offline
Tests: added

Closes: #42
```

## Pull Requests

Use GitHub MCP tools (`mcp_github_*`) for PR creation and review workflows. Do not use `gh pr create` or raw curl unless MCP tools are unavailable.

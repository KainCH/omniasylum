# GitHub Copilot Instructions — OmniForge

## Project Overview

**OmniForge** is a multi-tenant Twitch streaming tool suite built on **.NET 9 Blazor Server** and deployed to **Azure Container Apps**. It lets streamers manage counters, overlays, Discord notifications, and automated chat bot behavior — all per-user, partitioned by `TwitchUserId`.

---

## Solution Structure

```
OmniForge.DotNet/
├── src/OmniForge.Core           ← Domain: entities, interfaces, constants. ZERO external NuGet deps.
├── src/OmniForge.Infrastructure ← Implementations: Twitch, Discord, Azure, bot services, EventSub, JWT
├── src/OmniForge.Web            ← Blazor Server + ASP.NET Core API controllers
├── src/OmniForge.SyncAgent      ← Windows tray app — bridges OBS/Streamlabs to server via SignalR
├── src/OmniForge.SceneSync      ← Shared scene sync abstractions (Scene, SceneAction, OvertimeConfig)
└── tests/OmniForge.Tests        ← xUnit + Moq + bunit — ≥85% coverage required
```

**Core layer rule:** Zero external NuGet dependencies. All interfaces live here. If you need a NuGet package, it belongs in Infrastructure or Web.

---

## Build & Test Commands

```bash
dotnet build OmniForge.DotNet/OmniForge.sln
dotnet run --project OmniForge.DotNet/src/OmniForge.Web
dotnet test OmniForge.DotNet/OmniForge.sln
dotnet test OmniForge.DotNet/OmniForge.sln --filter "FullyQualifiedName~MyClassName"
dotnet test OmniForge.DotNet/OmniForge.sln --collect:"XPlat Code Coverage"
```

---

## Architecture Principles

### Multi-Tenancy (Critical)
All data is partitioned by `TwitchUserId`. In controllers, always extract user ID from JWT claims:
```csharp
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
          ?? User.FindFirst("sub")?.Value;
```
Never use route params or client-supplied IDs as the tenant key. Every repository query must include `PartitionKey == twitchUserId`.

### Clean Architecture
Dependencies point inward only: Core ← Infrastructure ← Web. Never reference Infrastructure or Web from Core.

### Feature Flags
`User.Features` (type `UserFeatures`) gates all optional capabilities. New features default to `false` and are admin-enabled per user. Always check flags server-side — never trust client-side checks.

---

## Key Data Flows

### Twitch EventSub → Counter → Overlay
`NativeEventSubService` (raw WebSocket) → `EventSubMessageProcessor` → `EventSubHandlerRegistry` → `BaseEventSubHandler.HandleAsync` → updates repositories → `INotificationService` (Discord + chat + overlay) → `AlertEventRouter` → `SseOverlayNotifier` via SSE.

### Bot Services Lifecycle
`StreamOnlineHandler` starts bot services → `ChatMessageHandler` calls `IBotModerationService` and `IAutoShoutoutService` on every message → EventSub handlers call `IBotReactionService` for event-triggered messages → `StreamOfflineHandler` calls `ResetSession` on all stateful services.

### Overlay Connections
- **V2 (primary):** `GET /sse/overlay?userId=...` → `SseConnectionManager` → `SseOverlayNotifier`
- **V1 (legacy):** `GET /ws/overlay?userId=...` → `WebSocketOverlayManager` → `WebSocketOverlayNotifier`

### Scene Sync (SyncAgent → Server)
OBS scene change → `ObsWebSocketClient` → `StreamingSoftwareMonitor` → `ServerConnectionService.InvokeAsync("ReportSceneChange")` → `SyncAgentHub` → `ISceneActionService` → `IOverlayNotifier`.

---

## C# Conventions

### Dependency Injection
- Constructor injection only — never `IServiceProvider` in constructors
- Use `IServiceScopeFactory` when a Singleton needs Scoped services (e.g. repositories inside EventSub handlers, bot services)
- Register in `OmniForge.Infrastructure/DependencyInjection.cs`
- **Singletons:** stateful services, client managers, bot services, EventSub handlers
- **Scoped:** repositories
- **Transient:** stateless utilities

### Async
All I/O-bound operations must be async. Never `.Result` or `.Wait()`. Use `ConfigureAwait(false)` in Infrastructure code (not in Blazor components). Always accept `CancellationToken` in long-running methods.

### Logging
```csharp
_logger.LogInformation("✅ {Event} processed for {User}", eventType, username);
_logger.LogWarning("⚠️ {Event} skipped — {Reason}", eventType, reason);
_logger.LogError(ex, "❌ Failed to process {Event} for {User}", eventType, username);
```
Never log tokens, access tokens, or secrets.

### Coverage Exclusions
Use `[ExcludeFromCodeCoverage(Justification = "Wraps external I/O — logic tested in XxxProcessor")]` only for classes that directly wrap Azure SDK, WebSocket, or HTTP I/O. Factor testable logic into a separate class.

---

## Bot Services

Four Singleton services in `Infrastructure/Services/`:

| Service | Interface | Called From |
|---|---|---|
| `BotModerationService` | `IBotModerationService` | `ChatMessageHandler` (every message) |
| `BotReactionService` | `IBotReactionService` | EventSub handlers (sub, raid, clip, etc.) |
| `AutoShoutoutService` | `IAutoShoutoutService` | `ChatMessageHandler` (every message) |
| `ScheduledMessageService` | `IScheduledMessageService` | Internal 1-minute Timer |

**Constructor rule:** `(ILogger<T>, IServiceScopeFactory)` only. Resolve scoped services inside async methods via `CreateScope()`.

**Session state:** `ConcurrentDictionary` fields keyed by `broadcasterId`. Never persist session state to repositories. `StreamOfflineHandler` calls `ResetSession(broadcasterId)` on all stateful bot services. When adding a new stateful bot service, wire it into `StreamOfflineHandler`.

---

## Repository Pattern

All repositories implement an interface from Core and inherit Azure Table Storage logic. Each calls `InitializeAsync()` at startup (`Program.cs`). Local dev uses Azurite (`UseDevelopmentStorage=true`) when `AzureStorage:AccountName` is not configured.

---

## Testing Patterns

**Controller tests** — construct manually, no `WebApplicationFactory`:
```csharp
_sut = new MyController(_serviceMock.Object, _loggerMock.Object);
_sut.ControllerContext = new ControllerContext
{
    HttpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "test-user-123") }))
    }
};
```

**Service tests** — mock `IServiceScopeFactory` to provide mocked repositories.

**Blazor component tests** — use bunit.

**Coverage gate: ≥85% on all new production code.**

---

## Prompt Protocols

When implementing a feature that matches one of these types, **invoke the corresponding prompt automatically** as your implementation guide:

| Task | Prompt |
|---|---|
| New Twitch EventSub subscription handler | `/omniforge-add-eventsub-handler` |
| New Discord notification or embed | `/omniforge-add-discord-notification` |
| New counter type or counter feature | `/omniforge-add-counter-feature` |
| New user feature flag (end-to-end) | `/omniforge-add-feature-flag` |
| New Azure Table Storage repository | `/omniforge-add-repository` |
| New Blazor page + API controller | `/omniforge-new-blazor-feature` |
| New bot service or bot behavior | `/omniforge-add-bot-service` |
| New auto-moderation rule | `/omniforge-add-automod-rule` |
| Bug investigation | `/omniforge-bug-investigation` |
| SyncAgent work | `/omniforge-sync-agent` |

For live API documentation, use the doc-fetching prompts:
- `/fetch-twitch-docs` — before any Twitch EventSub or Helix API work
- `/fetch-discord-docs` — before any Discord notification or embed work
- `/fetch-dotnet-bestpractices` — before any new service, repository, or DI design work

---

## Pre-Implementation Checklist

Before writing any implementation code for a new feature, confirm:

1. **Multi-tenancy** — does every data operation scope to a single `TwitchUserId`?
2. **Feature flag** — should this be gated in `User.Features` (default `false`)?
3. **EventSub scope** — does this need a new Twitch OAuth scope or subscription?
4. **Secrets** — are credentials kept out of logs and out of code?
5. **Coverage** — where will the ≥85% gate be met? Plan tests upfront.
6. **Bot service lifecycle** — if stateful, is `ResetSession` wired into `StreamOfflineHandler`?
7. **Overlay** — does the UI need a real-time push via `IOverlayNotifier`?
8. **Discord** — should this trigger a `IDiscordService` notification?

---

## Configuration

**Local:** `appsettings.Development.json` — populate `Twitch:ClientId`, `Twitch:ClientSecret`, `Jwt:Secret`, and either `AzureStorage:AccountName` or `Azure:StorageConnectionString`.

**Production:** Azure Key Vault via Managed Identity. Key vault name from `KeyVaultName` config key.

---

## Deployment

```powershell
cd OmniForge.DotNet/deploy
./deploy.ps1 -Environment "dev"           # App image only
./deploy.ps1 -Environment "dev" -FullDeploy  # Bicep infra + app
```

**SyncAgent** — always use VS Code tasks (`Publish Sync Agent`, `Publish Sync Agent (bump minor/major)`), never raw `dotnet publish`. The script signs via Azure Trusted Signing and uploads to Blob Storage for auto-update.

---

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

Use GitHub MCP tools (`mcp_github_*`) for PR creation and review. Do not use `gh pr create` or raw curl unless MCP tools are unavailable.

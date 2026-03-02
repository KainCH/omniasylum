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

The solution has three projects under `OmniForge.DotNet/src/`:

**OmniForge.Core** — Pure domain layer with no external dependencies. Contains entities, interfaces (all repository + service contracts live here), constants (`AlertTemplates`, `CounterTypes`), and utilities.

**OmniForge.Infrastructure** — Implements Core interfaces. Azure Table Storage repositories, Twitch (TwitchLib + native EventSub WebSocket), Discord (Discord.Net), JWT, and all background services. DI registration is in `DependencyInjection.cs`. Infrastructure code excluded from coverage with `[ExcludeFromCodeCoverage]` where it wraps I/O.

**OmniForge.Web** — Blazor Server app + ASP.NET Core API controllers. Startup/DI in `Program.cs`. Controllers expose REST endpoints consumed by Blazor components via DI-injected services. `ServerInstance.cs` holds a singleton server ID/start time used for health checks and overlay reconnection logic.

### Key data flows

**Twitch EventSub → counter update → overlay notification:**
`NativeEventSubService` (raw WebSocket to Twitch) → `EventSubMessageProcessor` → `EventSubHandlerRegistry` dispatches to typed handlers in `Services/EventHandlers/` (e.g. `CheerHandler`, `FollowHandler`) → each handler creates a DI scope, updates repositories, calls `INotificationService` and/or `IAlertEventRouter` → `AlertEventRouter` resolves alert type from user config, then calls `IOverlayNotifier` → `WebSocketOverlayNotifier` sends JSON over the raw WebSocket managed by `WebSocketOverlayManager`.

**Overlay WebSocket endpoint:** `WebSocketOverlayMiddleware` intercepts `GET /ws/overlay?userId=...` and hands the socket to `WebSocketOverlayManager` (singleton). The Blazor `Overlay.razor` page (`/overlay/{TwitchUserId}`) connects from the browser.

**Notifications fanout:** `NotificationService` (called after counter changes) fans out to: Discord via `IDiscordService`, Twitch chat via `ITwitchClientManager`, and overlay via `IOverlayNotifier`.

**Twitch connections:** Per-user connections are user-initiated (not auto-started). `TwitchClientManager` manages per-user `TwitchClient` instances for chat. `StreamMonitorService` manages EventSub subscriptions per user after they click "Start Monitor".

### Repository pattern
All repositories implement an interface from Core and inherit Azure Table Storage logic. Each calls `InitializeAsync()` at startup (called in `Program.cs`) to ensure tables exist. Local dev falls back to `UseDevelopmentStorage=true` (Azurite) when no `AzureStorage:AccountName` is configured.

### Multi-tenancy
All data is partitioned by `TwitchUserId`. Controllers extract the user ID from JWT claims and scope all operations to that user.

### Feature flags
`User.Features` (on the `User` entity) gates capabilities like `StreamOverlay`, checked in Blazor pages and API controllers before allowing access.

## Testing

Tests use xUnit + Moq. Controller tests manually construct controllers with mocked dependencies (no `WebApplicationFactory`). Blazor component tests use bunit. Infrastructure I/O code is excluded from coverage with `[ExcludeFromCodeCoverage]`; the testable message-processing logic is factored into separate classes (e.g. `EventSubMessageProcessor` is tested separately from `NativeEventSubService`).

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

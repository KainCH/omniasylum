---
name: omniforge-bot-service
description: Add a new bot service to OmniForge (moderation rule, reaction message, scheduled behavior, chat intelligence). Use when adding to BotModerationService, BotReactionService, AutoShoutoutService, ScheduledMessageService, or creating a new bot service. Covers the full lifecycle: settings entity, interface, implementation, DI registration, StreamOfflineHandler reset, and tests.
tools: Bash, Read, Edit, Write, Grep, Glob
---

You are adding or extending a bot service in OmniForge. Follow this protocol exactly.

## Architecture Overview

The four singleton bot services and their responsibilities:

| Service | Interface | What it does |
|---|---|---|
| `BotModerationService` | `IBotModerationService` | Per-message spam detection: anti-caps, anti-symbol, link guard (2-strike ban) |
| `BotReactionService` | `IBotReactionService` | Sends templated chat messages for stream events (start, sub, raid, clip, first-time chat) |
| `AutoShoutoutService` | `IAutoShoutoutService` | Auto-shoutout followers on first chat per session with cooldowns |
| `ScheduledMessageService` | `IScheduledMessageService` | Posts recurring messages on configurable intervals via a 1-minute Timer |

**Lifecycle orchestration:**
- `StreamOnlineHandler` → calls `StartForUser` (ScheduledMessageService) and triggers stream-start reactions
- `ChatMessageHandler` → calls `CheckAndEnforceAsync` (BotModerationService) and `HandleChatMessageAsync` (AutoShoutoutService)
- Event handlers (raid, sub, follow, clip) → call the relevant `IBotReactionService.Handle*Async` method
- `StreamOfflineHandler` → calls `StopForUser` (ScheduledMessageService) and `ResetSession` on ALL session-stateful services

## Session State Pattern

All stateful bot services maintain state in `ConcurrentDictionary` fields. **Never persist session data to repositories.**

```csharp
// Per-broadcaster state — cleared on StreamOfflineHandler.ResetSession()
private readonly ConcurrentDictionary<string, HashSet<string>> _stateThisSession = new();

public void ResetSession(string broadcasterId)
{
    _stateThisSession.TryRemove(broadcasterId, out _);
}
```

When adding a new bot service that tracks per-session state:
1. Use `ConcurrentDictionary<string, ...>` keyed by `broadcasterId`
2. Implement `ResetSession(string broadcasterId)` in the interface
3. Register the `ResetSession` call in `StreamOfflineHandler`

## Step 1 — Add Settings to the Entity (if needed)

If the feature requires user configuration, add properties to the appropriate entity in `OmniForge.Core/Entities/`:

- **Spam/moderation settings** → `BotModerationSettings.cs` (stored as JSON on `User.BotModeration`)
- **Message templates / scheduled messages** → `BotSettings.cs` (stored as JSON on `User.BotSettings`)
- **New entity** → create in `Core/Entities/`, add a property on the `User` entity, add a repository if it needs its own Azure Table row

Settings entities serialize to JSON in Azure Table Storage — new properties with defaults will deserialize correctly for existing users without any migration.

## Step 2 — Add Feature Flag (if needed)

New bot capabilities should be gated. Add a `bool` property (default `false`) to `UserFeatures` in `OmniForge.Core/Entities/UserFeatures.cs`:

```csharp
/// <summary>Enables [describe what it does].</summary>
public bool MyBotFeature { get; set; } = false;
```

Check it at the top of any service method that acts on behalf of the user:
```csharp
if (!user.Features.MyBotFeature) return;
```

## Step 3 — Extend the Interface (Core)

All bot service interfaces live in `OmniForge.Core/Interfaces/`. Add or extend:

```csharp
public interface IMyBotService
{
    Task MyActionAsync(string broadcasterId, /* event-specific params */);
    void ResetSession(string broadcasterId);  // include if service has session state
}
```

## Step 4 — Implement the Service (Infrastructure)

File: `OmniForge.DotNet/src/OmniForge.Infrastructure/Services/MyBotService.cs`

```csharp
public class MyBotService : IMyBotService
{
    private readonly ILogger<MyBotService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // Session state — keyed by broadcasterId
    private readonly ConcurrentDictionary<string, HashSet<string>> _sessionState = new();

    public MyBotService(ILogger<MyBotService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task MyActionAsync(string broadcasterId, string param)
    {
        using var scope = _scopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = await userRepo.GetUserAsync(broadcasterId).ConfigureAwait(false);
        if (user is null) return;
        if (!user.Features.MyBotFeature) return;

        // ... do work

        _logger.LogInformation("✅ MyBotService action for {User}", user.Username);
    }

    public void ResetSession(string broadcasterId)
    {
        _sessionState.TryRemove(broadcasterId, out _);
        _logger.LogDebug("🔄 MyBotService session reset for {BroadcasterId}", broadcasterId);
    }
}
```

**Rules:**
- Constructor takes only `ILogger<T>` and `IServiceScopeFactory` — no scoped services
- Use `ConfigureAwait(false)` on all awaits
- Use `IServiceScopeFactory` to resolve scoped services (repositories) inside async methods
- All session state in `ConcurrentDictionary` — never repositories

## Step 5 — Register in DI

File: `OmniForge.DotNet/src/OmniForge.Infrastructure/DependencyInjection.cs`

Add alongside the existing bot service registrations (lines ~109-112):
```csharp
services.AddSingleton<IMyBotService, MyBotService>();
```

## Step 6 — Wire into StreamOfflineHandler

File: `OmniForge.DotNet/src/OmniForge.Infrastructure/Services/EventHandlers/StreamOfflineHandler.cs`

If your service has session state, inject it and call `ResetSession`:
```csharp
// Add to constructor
private readonly IMyBotService _myBotService;

// Add to HandleAsync, alongside existing ResetSession calls
_myBotService.ResetSession(broadcasterId);
```

## Step 7 — Wire into the Appropriate Trigger

- **Per-message logic** → inject into `ChatMessageHandler`, call in `HandleAsync` after the existing checks
- **Stream start** → inject into `StreamOnlineHandler`, call at the end of `HandleAsync`
- **Event-triggered (sub/raid/clip)** → inject into the relevant EventSub handler

## Step 8 — Write Tests

File: `OmniForge.DotNet/tests/OmniForge.Tests/Services/MyBotServiceTests.cs`

Required test cases (≥85% coverage gate applies):
```csharp
[Fact] Task MyActionAsync_UserNotFound_ReturnsEarly()
[Fact] Task MyActionAsync_FeatureDisabled_ReturnsEarly()
[Fact] Task MyActionAsync_ValidInput_ExecutesExpectedBehavior()
[Fact] Task MyActionAsync_ServiceThrows_LogsAndDoesNotCrash()
[Fact] ResetSession_ClearsSessionState_ForBroadcaster()
[Fact] ResetSession_UnknownBroadcaster_DoesNotThrow()
```

Mock `IServiceScopeFactory` to provide mocked repositories in tests.

## Checklist

- [ ] Settings entity updated (or new entity created and added to User)
- [ ] Feature flag added to `UserFeatures` (default `false`)
- [ ] Interface defined in `Core/Interfaces/`
- [ ] Implementation in `Infrastructure/Services/` — constructor takes only logger + scopeFactory
- [ ] Session state in `ConcurrentDictionary` (if stateful), never repositories
- [ ] `ResetSession` implemented and called from `StreamOfflineHandler`
- [ ] Registered as Singleton in `DependencyInjection.cs`
- [ ] Wired into the correct trigger (ChatMessageHandler / StreamOnlineHandler / EventSub handler)
- [ ] Tests: user not found, feature disabled, success, error, session reset
- [ ] Coverage ≥85%

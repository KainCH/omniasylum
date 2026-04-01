---
description: "Step-by-step protocol for adding a new bot service (or extending an existing one) in OmniForge"
tools:
  - codebase
  - editFiles
  - problems
  - findTestFiles
  - usages
---

You are adding or extending a bot service in **OmniForge**. Follow this protocol exactly, in order.

---

## Step 1 — Understand the Existing Bot Service Architecture

Search the codebase for these files and read them before writing anything:

- `BotModerationService.cs` and `IBotModerationService.cs` — per-message spam detection
- `BotReactionService.cs` and `IBotReactionService.cs` — event-triggered templated messages
- `AutoShoutoutService.cs` and `IAutoShoutoutService.cs` — follower shoutout logic
- `ScheduledMessageService.cs` and `IScheduledMessageService.cs` — interval-based messages
- `StreamOfflineHandler.cs` — how `ResetSession` is called for each bot service
- `StreamOnlineHandler.cs` — how `StartForUser` is called

Key patterns to note:
1. All four are registered as **Singletons** in `DependencyInjection.cs`
2. Constructor takes only `ILogger<T>` and `IServiceScopeFactory`
3. Scoped services (repositories) are resolved via `IServiceScopeFactory` inside async methods
4. Session state lives in `ConcurrentDictionary` fields keyed by `broadcasterId`
5. `StreamOfflineHandler` calls `ResetSession(broadcasterId)` on all stateful services

---

## Step 2 — Pre-Implementation Checklist

Before writing any code, confirm:

1. **Which existing service is being extended?** Or is this a brand-new service?
2. **What user configuration is needed?** → `BotModerationSettings` (spam/enforcement config) or `BotSettings` (message templates, scheduled messages)?
3. **Should this be behind a feature flag?** If yes, add a `bool` property to `UserFeatures` (default `false`)
4. **Does this track per-session state?** If yes, plan `ConcurrentDictionary` fields and a `ResetSession` method
5. **What triggers this service?** → Chat message (`ChatMessageHandler`), stream online (`StreamOnlineHandler`), or a specific EventSub event?

---

## Step 3 — Add Settings to the Entity (Core)

If the feature requires user configuration, add to the appropriate entity in `OmniForge.Core/Entities/`:

**`BotModerationSettings.cs`** — for spam detection thresholds and enforcement config:
```csharp
public bool MyModerationRule { get; set; } = false;
public int MyThreshold { get; set; } = 70;
```

**`BotSettings.cs`** — for message templates, scheduled messages, link commands:
```csharp
public string MyEventMessage { get; set; } = string.Empty;
```

Settings serialize to JSON in Azure Table Storage. New properties with defaults work for existing users without migration.

---

## Step 4 — Add Feature Flag (Core, if needed)

Open `OmniForge.Core/Entities/UserFeatures.cs` and add:

```csharp
/// <summary>Enables [describe what it does].</summary>
public bool MyBotFeature { get; set; } = false;
```

Check the flag early in any service method that acts on behalf of the user:
```csharp
if (!user.Features.MyBotFeature) return;
```

---

## Step 5 — Extend or Create the Interface (Core)

All bot service interfaces live in `OmniForge.Core/Interfaces/`.

When **extending** an existing interface, add the new method signature alongside existing ones.

When **creating** a new interface:
```csharp
public interface IMyBotService
{
    Task MyActionAsync(string broadcasterId, /* event-specific params */);
    void ResetSession(string broadcasterId);  // only if stateful
}
```

---

## Step 6 — Implement the Service (Infrastructure)

Create or update in `OmniForge.Infrastructure/Services/`:

```csharp
public class MyBotService : IMyBotService
{
    private readonly ILogger<MyBotService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // Session state — keyed by broadcasterId, never stored in repositories
    private readonly ConcurrentDictionary<string, HashSet<string>> _sessionState = new();
    private readonly object _lock = new();

    public MyBotService(ILogger<MyBotService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task MyActionAsync(string broadcasterId, string param)
    {
        using var scope = _scopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var twitchClient = scope.ServiceProvider.GetRequiredService<ITwitchClientManager>();

        var user = await userRepo.GetUserAsync(broadcasterId).ConfigureAwait(false);
        if (user is null) return;
        if (!user.Features.MyBotFeature) return;

        // ... logic

        await twitchClient.SendMessageAsync(broadcasterId, formattedMessage).ConfigureAwait(false);
        _logger.LogInformation("✅ MyBotService acted for {User}", user.Username);
    }

    public void ResetSession(string broadcasterId)
    {
        lock (_lock)
        {
            _sessionState.TryRemove(broadcasterId, out _);
        }
        _logger.LogDebug("🔄 MyBotService session reset for {BroadcasterId}", broadcasterId);
    }
}
```

**Rules:**
- Constructor: only `ILogger<T>` and `IServiceScopeFactory`
- Use `ConfigureAwait(false)` on all awaits in Infrastructure code
- Use `using var scope = _scopeFactory.CreateScope()` inside async methods
- `HashSet` mutations inside `ConcurrentDictionary` values need a lock

---

## Step 7 — Register in DI

Open `OmniForge.Infrastructure/DependencyInjection.cs` and add alongside existing bot service registrations:

```csharp
services.AddSingleton<IMyBotService, MyBotService>();
```

---

## Step 8 — Wire into StreamOfflineHandler (if stateful)

Open `OmniForge.Infrastructure/Services/EventHandlers/StreamOfflineHandler.cs`.

Inject the service and add a `ResetSession` call alongside existing ones:
```csharp
_myBotService.ResetSession(broadcasterId);
_logger.LogInformation("🔄 MyBotService session reset for {BroadcasterId}", broadcasterId);
```

---

## Step 9 — Wire into the Appropriate Trigger

**Per-message logic** → Inject into `ChatMessageHandler`, call at the end of `HandleAsync` after existing checks.

**Stream start** → Inject into `StreamOnlineHandler`, call at the end of `HandleAsync`.

**Event-triggered** → Inject the relevant `IBotReactionService.Handle*Async` call into the matching EventSub handler.

**Interval-based** → Extend `ScheduledMessageService` or add a `StartForUser`/`StopForUser` pattern.

---

## Step 10 — Write Tests

Create `OmniForge.Tests/Services/MyBotServiceTests.cs`.

Required test cases (≥85% coverage gate):

```csharp
[Fact] Task MyActionAsync_UserNotFound_ReturnsWithoutAction()
[Fact] Task MyActionAsync_FeatureDisabled_ReturnsWithoutAction()
[Fact] Task MyActionAsync_ValidCall_PerformsExpectedAction()
[Fact] Task MyActionAsync_ServiceThrows_LogsAndDoesNotPropagate()
[Fact] void ResetSession_ClearsStateForBroadcaster()
[Fact] void ResetSession_UnknownBroadcaster_DoesNotThrow()
```

Mock `IServiceScopeFactory` to provide mocked `IUserRepository` and `ITwitchClientManager`.

---

## Checklist Before Committing

- [ ] Settings entity updated with new config properties (if needed)
- [ ] Feature flag added to `UserFeatures` with `= false` default (if needed)
- [ ] Interface defined or updated in `Core/Interfaces/`
- [ ] Implementation: constructor is `(ILogger<T>, IServiceScopeFactory)` only
- [ ] Session state in `ConcurrentDictionary` — not in repositories
- [ ] `ResetSession` wired into `StreamOfflineHandler` (if stateful)
- [ ] Registered as `Singleton` in `DependencyInjection.cs`
- [ ] Wired into the correct trigger handler
- [ ] Tests: not found, disabled, success, error, session reset
- [ ] Coverage ≥85%

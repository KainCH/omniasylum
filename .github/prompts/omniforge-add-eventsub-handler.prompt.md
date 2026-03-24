---
description: "Step-by-step protocol for adding a new Twitch EventSub subscription type to OmniForge"
tools:
  - fetch_webpage
  - codebase
  - editFiles
  - problems
  - findTestFiles
---

You are implementing a new Twitch EventSub subscription handler for **OmniForge**. Follow this protocol exactly, in order.

---

## Step 1 — Fetch Live Twitch Documentation

Run the `/fetch-twitch-docs` prompt first, or fetch these pages directly:

1. **EventSub subscription types** — exact subscription type string, version, condition fields, required OAuth scopes:
   `https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/`

2. **EventSub WebSocket handling** — session lifecycle, reconnect flow, keepalive:
   `https://dev.twitch.tv/docs/eventsub/handling-websocket-events/`

3. **OAuth scope reference** — verify which scopes are needed for this subscription:
   `https://dev.twitch.tv/docs/authentication/scopes/`

Confirm from the docs:

- The **exact subscription type string** (e.g. `"channel.cheer"`) and version (`"1"` or `"2"`)
- The **condition fields** (usually `{ "broadcaster_user_id": "..." }`)
- The **exact JSON shape** of the `event` payload — every field name, type, and whether it's nullable
- The **required OAuth scopes** — check if they already exist in `TwitchSettings`

---

## Step 2 — Understand the Existing Handler Pattern

Search the codebase for:

- `BaseEventSubHandler` — the abstract base class all handlers extend
- An existing simple handler (e.g. `FollowHandler`, `CheerHandler`) for reference
- `EventSubHandlerRegistry` — auto-discovers `IEventSubHandler` registrations
- `StreamMonitorService.CreateSubscriptionsAsync` — where the subscription is created via Helix API

Key contracts from `BaseEventSubHandler`:

```csharp
// Always call this FIRST — this is your tenant partition key
bool TryGetBroadcasterId(JsonElement eventData, out string? broadcasterId)

// Safe property extraction
string GetStringProperty(JsonElement element, string propertyName, string defaultValue = "")
int GetIntProperty(JsonElement element, string propertyName, int defaultValue = 0)
bool GetBoolProperty(JsonElement element, string propertyName, bool defaultValue = false)

// Unwraps the inner event object from the EventSub envelope
JsonElement UnwrapEvent(JsonElement eventData)
```

---

## Step 3 — Create the Handler Class

Create the file at:
`OmniForge.DotNet/src/OmniForge.Infrastructure/Services/EventHandlers/{EventType}Handler.cs`

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers;

// The handler class itself is excluded — I/O dispatch infrastructure.
// All testable logic should be extracted to a processor/helper class.
[ExcludeFromCodeCoverage(Justification = "EventSub dispatch infrastructure — logic tested via mocked scope factories")]
public class MyEventHandler : BaseEventSubHandler
{
    public override string SubscriptionType => "channel.my_event";  // exact Twitch string

    public MyEventHandler(IServiceScopeFactory scopeFactory, ILogger<MyEventHandler> logger)
        : base(scopeFactory, logger) { }

    public override async Task HandleAsync(JsonElement eventData)
    {
        // ALWAYS unwrap and get broadcasterId first — this is the tenant key
        if (!TryGetBroadcasterId(eventData, out var broadcasterId))
        {
            Logger.LogWarning("⚠️ {Handler}: missing broadcaster_user_id", nameof(MyEventHandler));
            return;
        }

        using var scope = ScopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = await userRepo.GetUserAsync(broadcasterId!);
        if (user is null)
        {
            Logger.LogDebug("⚠️ {Handler}: unknown broadcaster {BroadcasterId}", nameof(MyEventHandler), broadcasterId);
            return;
        }

        // Check feature flag before processing
        if (!user.Features.MyFeatureFlag) return;

        // Extract fields using helpers (never index directly — properties may be absent)
        var someField = GetStringProperty(eventData, "some_field");
        var someCount = GetIntProperty(eventData, "count");

        // Update state via repository
        // ...

        // Notify: Discord + Twitch chat + Overlay via INotificationService or IAlertEventRouter
        await notificationService.CheckAndSendMilestoneNotificationsAsync(user, "counterType", previousValue, newValue);

        Logger.LogInformation("✅ {Handler} processed for {User}", nameof(MyEventHandler), user.Username);
    }
}
```

**Rules:**

- `[ExcludeFromCodeCoverage]` on the handler class — it's dispatch I/O
- `TryGetBroadcasterId` MUST be the first substantive call
- Use `IServiceScopeFactory` — handler is Singleton, repositories are Scoped
- Check feature flag before doing any work
- Use structured log properties `{Like} {This}`, never string concatenation
- Never log tokens, access tokens, or secrets

---

## Step 4 — Register the Subscription in StreamMonitorService

Open `OmniForge.DotNet/src/OmniForge.Infrastructure/Services/StreamMonitorService.cs`.

Find `CreateSubscriptionsAsync` and add the new subscription call alongside the existing ones:

```csharp
await CreateSubscriptionAsync(
    type: "channel.my_event",
    version: "1",
    condition: new Dictionary<string, string> { ["broadcaster_user_id"] = userId },
    sessionId: sessionId,
    accessToken: accessToken,
    userId: userId);
```

Check if the subscription needs **moderator ID** in the condition (some types require both `broadcaster_user_id` and `user_id` pointing to the bot user).

---

## Step 5 — Register the Handler in DI

Open `OmniForge.DotNet/src/OmniForge.Infrastructure/DependencyInjection.cs`.

Add alongside existing handler registrations:

```csharp
services.AddSingleton<IEventSubHandler, MyEventHandler>();
```

The `EventSubHandlerRegistry` auto-discovers all `IEventSubHandler` registrations — no other changes needed there.

---

## Step 6 — Check OAuth Scopes

Open `OmniForge.DotNet/src/OmniForge.Infrastructure/Configuration/TwitchSettings.cs` or wherever scopes are configured.

If the subscription requires a new OAuth scope:

1. Add it to the scope list in the Twitch OAuth middleware config
2. Existing users will need to re-authenticate — document this in the PR
3. Check `ITwitchBotEligibilityService` if the scope affects bot connection eligibility

---

## Step 7 — Add Tests

Create tests at `OmniForge.DotNet/tests/OmniForge.Tests/Services/EventHandlers/MyEventHandlerTests.cs`.

Required test cases (minimum):

```csharp
[Fact] Task HandleAsync_MissingBroadcasterId_ReturnsEarly_WithoutCallingRepo()
[Fact] Task HandleAsync_UnknownBroadcaster_ReturnsEarly()
[Fact] Task HandleAsync_FeatureFlagDisabled_ReturnsEarly()
[Fact] Task HandleAsync_ValidEvent_UpdatesStateAndNotifies()
[Fact] Task HandleAsync_RepositoryThrows_LogsErrorAndDoesNotCrash()
```

Use `new DefaultHttpContext()` + `ClaimsPrincipal` for controller tests.
For handler tests, mock `IServiceScopeFactory` and resolve mocked repositories.

**Coverage gate: ≥85% on all new production code.**

---

## Step 8 — Verify Subscription Type Is Listed

Update `OmniForge.DotNet/.github/instructions/twitch.instructions.md` — add the new handler to the **Existing EventSub Subscription Types** table so future work knows this is already handled.

---

## Checklist Before Committing

- [ ] Handler class has `[ExcludeFromCodeCoverage]`
- [ ] `TryGetBroadcasterId` is the first call in `HandleAsync`
- [ ] Feature flag checked before doing any work
- [ ] Subscription registered in `StreamMonitorService`
- [ ] Handler registered as `IEventSubHandler` in `DependencyInjection.cs`
- [ ] Tests cover: missing broadcaster, unknown user, disabled feature flag, success, error
- [ ] No new OAuth scope needed (or: documented and added)
- [ ] `twitch.instructions.md` table updated

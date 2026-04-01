---
name: omniforge-eventsub-handler
description: Add a new Twitch EventSub subscription handler end-to-end in OmniForge. Use when asked to handle a new Twitch event type (cheer, follow, ban, poll, prediction, hype train, etc.). Covers handler class, StreamMonitorService subscription, DI registration, and tests.
tools: Bash, Read, Edit, Write, Grep, Glob, WebFetch
---

You are implementing a new Twitch EventSub subscription handler for OmniForge. Follow this protocol exactly.

## Step 1 — Confirm the Twitch Subscription Details

Fetch the live Twitch docs to confirm:
- Exact subscription type string (e.g. `"channel.cheer"`) and version (`"1"` or `"2"`)
- Condition fields (usually `{ "broadcaster_user_id": "..." }`, some need `moderator_user_id` too)
- Exact JSON shape of the `event` payload — every field name and type
- Required OAuth scopes — check if already in scope list

Twitch EventSub subscription types: https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/

## Step 2 — Check Existing Handler Pattern

Read an existing simple handler for reference:
`OmniForge.DotNet/src/OmniForge.Infrastructure/Services/EventHandlers/FollowHandler.cs`

Read the base class:
`OmniForge.DotNet/src/OmniForge.Infrastructure/Services/EventHandlers/BaseEventSubHandler.cs`

Key helpers available on BaseEventSubHandler:
```csharp
bool TryGetBroadcasterId(JsonElement eventData, out string? broadcasterId)  // ALWAYS call first
string GetStringProperty(JsonElement element, string propertyName, string defaultValue = "")
int GetIntProperty(JsonElement element, string propertyName, int defaultValue = 0)
bool GetBoolProperty(JsonElement element, string propertyName, bool defaultValue = false)
JsonElement UnwrapEvent(JsonElement eventData)  // unwraps inner event from EventSub envelope
```

## Step 3 — Create the Handler Class

File: `OmniForge.DotNet/src/OmniForge.Infrastructure/Services/EventHandlers/{EventType}Handler.cs`

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers;

[ExcludeFromCodeCoverage(Justification = "EventSub dispatch infrastructure — logic tested via mocked scope factories")]
public class MyEventHandler : BaseEventSubHandler
{
    public override string SubscriptionType => "channel.my_event";

    public MyEventHandler(IServiceScopeFactory scopeFactory, ILogger<MyEventHandler> logger)
        : base(scopeFactory, logger) { }

    public override async Task HandleAsync(JsonElement eventData)
    {
        // ALWAYS first — this is the tenant partition key
        if (!TryGetBroadcasterId(eventData, out var broadcasterId))
        {
            Logger.LogWarning("⚠️ {Handler}: missing broadcaster_user_id", nameof(MyEventHandler));
            return;
        }

        using var scope = ScopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = await userRepo.GetUserAsync(broadcasterId!);
        if (user is null) return;

        // Check feature flag before doing any work
        if (!user.Features.MyFeatureFlag) return;

        // Extract event fields using helpers — never index directly
        var someField = GetStringProperty(eventData, "some_field");

        // Update state, notify
        Logger.LogInformation("✅ {Handler} processed for {User}", nameof(MyEventHandler), user.Username);
    }
}
```

**Non-negotiable rules:**
- `[ExcludeFromCodeCoverage]` on the handler class
- `TryGetBroadcasterId` is the first meaningful call
- Feature flag checked before any work
- Use `IServiceScopeFactory` — handler is Singleton, repositories are Scoped
- Never log tokens or secrets

## Step 4 — Register the Subscription in StreamMonitorService

File: `OmniForge.DotNet/src/OmniForge.Infrastructure/Services/StreamMonitorService.cs`

Find `CreateSubscriptionsAsync` and add alongside existing subscriptions:

```csharp
await CreateSubscriptionAsync(
    type: "channel.my_event",
    version: "1",
    condition: new Dictionary<string, string> { ["broadcaster_user_id"] = userId },
    sessionId: sessionId,
    accessToken: accessToken,
    userId: userId);
```

Some subscription types also require `moderator_user_id` in the condition — check the Twitch docs.

## Step 5 — Register in DI

File: `OmniForge.DotNet/src/OmniForge.Infrastructure/DependencyInjection.cs`

Add alongside existing handler registrations:
```csharp
services.AddScoped<IEventSubHandler, MyEventHandler>();
```

`EventSubHandlerRegistry` auto-discovers all `IEventSubHandler` registrations — no other changes needed.

## Step 6 — Check OAuth Scopes

If a new scope is required, add it to the Twitch OAuth config in `TwitchSettings`. Existing users will need to re-authenticate — note this in the PR.

## Step 7 — Write Tests

File: `OmniForge.DotNet/tests/OmniForge.Tests/Services/EventHandlers/MyEventHandlerTests.cs`

Required test cases (minimum — must achieve ≥85% coverage):
```csharp
[Fact] Task HandleAsync_MissingBroadcasterId_ReturnsEarly()
[Fact] Task HandleAsync_UnknownBroadcaster_ReturnsEarly()
[Fact] Task HandleAsync_FeatureFlagDisabled_ReturnsEarly()
[Fact] Task HandleAsync_ValidEvent_UpdatesStateAndNotifies()
[Fact] Task HandleAsync_RepositoryThrows_LogsErrorAndDoesNotCrash()
```

## Step 8 — Update the Handler Registry Table

Update `.github/instructions/twitch.instructions.md` — add the new handler to the **Existing EventSub Subscription Types** table.

## Checklist

- [ ] Handler has `[ExcludeFromCodeCoverage]`
- [ ] `TryGetBroadcasterId` is the first call
- [ ] Feature flag checked before work
- [ ] Subscription added in `StreamMonitorService`
- [ ] Registered as `IEventSubHandler` in `DependencyInjection.cs`
- [ ] Tests: missing broadcaster, unknown user, flag disabled, success, error
- [ ] OAuth scope added if needed
- [ ] `twitch.instructions.md` table updated

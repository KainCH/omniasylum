---
applyTo: "**/Twitch*.cs,**/*Twitch*.cs,**/EventSub*.cs,**/*EventSub*.cs,**/EventHandlers/*.cs,**/StreamMonitor*.cs,**/*StreamMonitor*.cs"
---

# OmniForge — Twitch & EventSub Development Instructions

> These instructions activate automatically when editing Twitch or EventSub-related files.
> For live API documentation, run the `/fetch-twitch-docs` prompt.

## Architecture Overview

```
User "Start Monitor" click
  └─► TwitchClientManager.ConnectUserAsync(userId)
        └─► TwitchLib TwitchClient joins channel (IRC/chat)
              └─► TwitchMessageHandler routes chat events by channel→userId map

StreamMonitorService.CreateSubscriptionsAsync(userId)
  └─► NativeEventSubService (raw ClientWebSocket → wss://eventsub.wss.twitch.tv/ws)
        └─► EventSubMessageProcessor.Process(json) → EventSubProcessResult
              └─► EventSubHandlerRegistry.GetHandler(subscriptionType)
                    └─► BaseEventSubHandler.HandleAsync(JsonElement eventData)
                          ├─► INotificationService (fan-out: Discord + Twitch chat + Overlay)
                          ├─► IAlertEventRouter → IOverlayNotifier → WebSocketOverlayManager
                          └─► Repository updates (scoped DI)
```

## Key Interfaces (Core layer — no external deps)

| Interface | Purpose |
|-----------|---------|
| `ITwitchClientManager` | Connect/disconnect per-user bots; send chat messages |
| `ITwitchAuthService` | OAuth token retrieval and refresh |
| `ITwitchApiService` | Helix API wrapper (users, clips, rewards, etc.) |
| `ITwitchBotEligibilityService` | Check if a user's token allows bot connection |

## Adding a New EventSub Handler

**Step 1 — Create the handler class** in `OmniForge.Infrastructure/Services/EventHandlers/`:

```csharp
[ExcludeFromCodeCoverage(Justification = "I/O infrastructure — processor logic tested separately")]
public class MyNewEventHandler : BaseEventSubHandler
{
    // Matches the Twitch EventSub subscription type exactly
    public override string SubscriptionType => "channel.my_event";

    public MyNewEventHandler(IServiceScopeFactory scopeFactory, ILogger<MyNewEventHandler> logger)
        : base(scopeFactory, logger) { }

    public override async Task HandleAsync(JsonElement eventData)
    {
        // Always extract broadcasterId first — this is your tenant key
        if (!TryGetBroadcasterId(eventData, out var broadcasterId))
        {
            Logger.LogWarning("⚠️ MyNewEvent: missing broadcaster_user_id");
            return;
        }

        using var scope = ScopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = await userRepo.GetByTwitchUserIdAsync(broadcasterId);
        if (user is null) return;

        // Extract event properties using typed helpers from BaseEventSubHandler
        var someValue = GetStringProperty(eventData, "some_field");
        var someCount = GetIntProperty(eventData, "count");

        // Process and notify
        await notificationService.NotifyAsync(user, "my_event", new { someValue, someCount });

        Logger.LogInformation("✅ MyNewEvent processed for {User}", user.Username);
    }
}
```

**Step 2 — Register the subscription type** in `StreamMonitorService.CreateSubscriptionsAsync()`:
- Subscribe via `ITwitchApiService.CreateEventSubSubscriptionAsync(type, version, condition, transport)`
- Condition is typically `{ "broadcaster_user_id": userId }`

**Step 3 — Register in DI** in `OmniForge.Infrastructure/DependencyInjection.cs`:
```csharp
services.AddSingleton<IEventSubHandler, MyNewEventHandler>();
```
The `EventSubHandlerRegistry` auto-discovers all `IEventSubHandler` registrations.

**Step 4 — Write tests** in `OmniForge.Tests/`:
- Test your handler with mocked `IServiceScopeFactory` and repositories
- Test the happy path, missing broadcaster ID, and null user scenarios
- Maintain **≥85% code coverage**

## BaseEventSubHandler Helper Methods

```csharp
// Extract broadcaster_user_id from event condition or event body
bool TryGetBroadcasterId(JsonElement eventData, out string broadcasterId)

// Null-safe property extraction
string? GetStringProperty(JsonElement element, string propertyName)
int GetIntProperty(JsonElement element, string propertyName, int defaultValue = 0)
bool GetBoolProperty(JsonElement element, string propertyName, bool defaultValue = false)

// Unwraps nested event objects from subscription notifications
JsonElement UnwrapEvent(JsonElement notificationRoot)
```

## Multi-Tenancy Rules — CRITICAL

- **Every handler MUST scope all operations to the `broadcasterId`** extracted from the event
- Never use a hardcoded or global user context; EventSub events always carry `broadcaster_user_id`
- All repository calls must include the `TwitchUserId` partition key
- Cross-user data access must be impossible by design — if you can't extract `broadcasterId`, return early

## TwitchClientManager — Chat Message Pattern

```csharp
// Send a chat message as the bot (requires user's bot to be connected)
await _twitchClientManager.SendMessageAsync(userId, "!deaths 5 💀");

// Check connection status before sending
var status = _twitchClientManager.GetUserBotStatus(userId);
if (!status.Connected) { /* handle gracefully */ }
```

## Token Management

- **Never store or hardcode tokens** — always fetch via `ITwitchAuthService.GetValidTokenAsync(userId)`
- The auth service handles proactive refresh when tokens near expiry
- Token expiry causes EventSub disconnects; `NativeEventSubService` fires `OnDisconnected` for reconnection

## Existing EventSub Subscription Types (Already Implemented)

| Subscription Type | Handler |
|-------------------|---------|
| `channel.chat.message` | `ChatMessageHandler` |
| `channel.chat.notification` | `ChatNotificationHandler` |
| `channel.update` | `ChannelUpdateHandler` |
| `channel.channel_points_custom_reward_redemption.add` | `ChannelPointRedemptionHandler` |
| `channel.cheer` | `CheerHandler` |
| `channel.follow` | `FollowHandler` |
| `channel.raid` | `RaidHandler` |
| `stream.online` | `StreamOnlineHandler` |
| `stream.offline` | `StreamOfflineHandler` |
| `channel.subscription.gift` | `SubscriptionGiftHandler` |
| `channel.subscription.message` | `SubscriptionMessageHandler` |

## NuGet Packages in Use

- `TwitchLib` 3.5.3 — chat client (TwitchLib.Client)
- `TwitchLib.EventSub.Websockets` 0.7.0 — EventSub WebSocket connector (wrapper, prefer NativeEventSubService)
- `AspNet.Security.OAuth.Twitch` 9.0.0 — Twitch OAuth middleware

## Reference Documentation

> Run `/fetch-twitch-docs` to pull current Twitch documentation into your session.

- Twitch EventSub subscription types: https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/
- Twitch Helix API reference: https://dev.twitch.tv/docs/api/reference/
- Twitch OAuth scopes: https://dev.twitch.tv/docs/authentication/scopes/
- EventSub transport guide: https://dev.twitch.tv/docs/eventsub/handling-websocket-events/

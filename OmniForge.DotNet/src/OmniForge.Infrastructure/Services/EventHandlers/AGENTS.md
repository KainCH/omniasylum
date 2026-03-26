# EventHandlers — Agent Context

This directory contains all Twitch EventSub subscription handlers. Each file handles one subscription type.

## Handler Pattern

Every handler in this directory:
- Extends `BaseEventSubHandler` (in this directory)
- Overrides `SubscriptionType` with the exact Twitch subscription type string
- Is decorated with `[ExcludeFromCodeCoverage]` — dispatch I/O, not business logic
- Calls `TryGetBroadcasterId` as the **first meaningful operation** in `HandleAsync`
- Uses `IServiceScopeFactory` to resolve scoped services (handlers are Singletons)
- Checks the relevant `user.Features.*` flag before doing any work

```csharp
[ExcludeFromCodeCoverage(Justification = "EventSub dispatch infrastructure")]
public class MyHandler : BaseEventSubHandler
{
    public override string SubscriptionType => "channel.my_event";

    public MyHandler(IServiceScopeFactory scopeFactory, ILogger<MyHandler> logger)
        : base(scopeFactory, logger) { }

    public override async Task HandleAsync(JsonElement eventData)
    {
        if (!TryGetBroadcasterId(eventData, out var broadcasterId)) return;

        using var scope = ScopeFactory.CreateScope();
        var user = await scope.ServiceProvider
            .GetRequiredService<IUserRepository>()
            .GetUserAsync(broadcasterId!);
        if (user is null) return;
        if (!user.Features.MyFlag) return;

        // ... work
    }
}
```

## Existing Subscription Types

| Subscription Type | Handler File |
|---|---|
| `channel.chat.message` | `ChatMessageHandler.cs` |
| `channel.chat.notification` | `ChatNotificationHandler.cs` |
| `channel.update` | `ChannelUpdateHandler.cs` |
| `channel.channel_points_custom_reward_redemption.add` | `ChannelPointRedemptionHandler.cs` |
| `channel.cheer` | `CheerHandler.cs` |
| `channel.follow` | `FollowHandler.cs` |
| `channel.raid` | `RaidHandler.cs` |
| `stream.online` | `StreamOnlineHandler.cs` |
| `stream.offline` | `StreamOfflineHandler.cs` |
| `channel.subscription.gift` | `SubscriptionGiftHandler.cs` |
| `channel.subscription.message` | `SubscriptionMessageHandler.cs` |
| `channel.suspicious_user.message` | `SuspiciousUserMessageHandler.cs` |

## Adding a New Handler

1. Create `{EventType}Handler.cs` here following the pattern above
2. Register the subscription in `StreamMonitorService.CreateSubscriptionsAsync()`
3. Register in DI: `services.AddSingleton<IEventSubHandler, MyHandler>()` in `DependencyInjection.cs`
4. `EventSubHandlerRegistry` auto-discovers all `IEventSubHandler` registrations
5. Write tests in `OmniForge.Tests/Services/EventHandlers/` — mock `IServiceScopeFactory`
6. Update the table above

## Special Cases

**`StreamOnlineHandler`** — also starts bot services (`IScheduledMessageService.StartForUser`) and triggers `IBotReactionService.HandleStreamStartAsync`.

**`StreamOfflineHandler`** — calls `ResetSession(broadcasterId)` on all stateful bot services (`IBotModerationService`, `IBotReactionService`, `IAutoShoutoutService`) and `StopForUser` on `IScheduledMessageService`. When adding a new stateful bot service, inject it here and add the reset call.

**`SuspiciousUserMessageHandler`** — only acts on `ban_evasion_evaluation == "likely"` (not `"possible"`). Gated by `user.Features.AutoBanEvaders`.

**`ChatMessageHandler`** — calls `IBotModerationService.CheckAndEnforceAsync` and `IAutoShoutoutService.HandleChatMessageAsync` on every message after command processing.

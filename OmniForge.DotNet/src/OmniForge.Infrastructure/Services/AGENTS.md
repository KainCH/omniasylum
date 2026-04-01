# Infrastructure/Services — Agent Context

This directory contains all service implementations. See `EventHandlers/AGENTS.md` for EventSub handler specifics.

## Bot Services (Singletons)

Four services handle automated Twitch chat behavior. All are registered as Singletons and follow the same structural pattern.

| File | Interface | Trigger | Session State |
|---|---|---|---|
| `BotModerationService.cs` | `IBotModerationService` | Every chat message (via `ChatMessageHandler`) | `_linkViolations` per broadcaster |
| `BotReactionService.cs` | `IBotReactionService` | Stream events (sub, raid, clip, first chat, stream start) | `_greeted` per broadcaster |
| `AutoShoutoutService.cs` | `IAutoShoutoutService` | Every chat message (via `ChatMessageHandler`) | `_shoutedThisSession`, cooldown dicts, follow cache |
| `ScheduledMessageService.cs` | `IScheduledMessageService` | 1-minute `System.Threading.Timer` per broadcaster | `_timers`, `_lastFired` |

### Constructor Rule

All bot services take **only** `ILogger<T>` and `IServiceScopeFactory` in their constructor. Scoped services (repositories, `ITwitchClientManager`) are resolved inside async methods via `_scopeFactory.CreateScope()`.

### Session State Rule

All per-stream state lives in `ConcurrentDictionary` fields keyed by `broadcasterId`. **Never persist session data to repositories.** `StreamOfflineHandler` calls `ResetSession(broadcasterId)` on all stateful services when a stream ends. If you add a new stateful bot service, add a `ResetSession` call in `StreamOfflineHandler`.

### Cooldowns in AutoShoutoutService

- Channel shoutout cooldown: 65 seconds (`ChannelCooldown`)
- Per-user shoutout cooldown: 2.5 minutes (`UserCooldown`)
- Follow status cache TTL: 10 minutes (`FollowCacheTtl`)

### BotModerationSettings vs BotSettings

- **`BotModerationSettings`** — spam detection thresholds and enforcement config (anti-caps, anti-symbol, link guard). Stored as JSON on `User.BotModeration`.
- **`BotSettings`** — message templates for reactions and scheduled messages. Stored as JSON on `User.BotSettings`.

## Key Non-Bot Services

| File | Purpose |
|---|---|
| `NativeEventSubService.cs` | Raw WebSocket connection to Twitch EventSub — excluded from coverage |
| `EventSubMessageProcessor.cs` | Testable parser factored out from `NativeEventSubService` |
| `StreamMonitorService.cs` | Creates/manages per-user EventSub subscriptions |
| `TwitchClientManager.cs` | Per-user `TwitchClient` instances for chat send/receive |
| `TwitchApiService.cs` | Twitch Helix API wrapper (ban, delete message, clips, etc.) |
| `NotificationService.cs` | Fanout: Discord + Twitch chat + Overlay on counter milestones |
| `AlertEventRouter.cs` | Resolves alert type from user config, calls `IOverlayNotifier` |
| `DiscordService.cs` | Discord notification implementation via `DiscordNetBotClient` |
| `DashboardFeedService.cs` | Pub/sub for real-time chat and events to the dashboard UI |
| `ChatCommandProcessor.cs` | Parses and routes `!command` messages from chat |
| `SceneActionService.cs` | Handles scene changes from the SyncAgent via SignalR |

## DI Registration

All registrations are in `DependencyInjection.cs` in this project root (one level up). When adding a new service:
- **Singleton** — stateful services (bot services, client managers, registries)
- **Scoped** — repositories, per-request services
- **Transient** — stateless utilities

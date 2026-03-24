---
applyTo: "**/Discord*.cs,**/*Discord*.cs"
---

# OmniForge — Discord Bot Development Instructions

> These instructions activate automatically when editing Discord-related files.
> For live API documentation, run the `/fetch-discord-docs` prompt.

## Architecture Overview

```
IDiscordService                         ← Core interface, inject this everywhere
  └─► DiscordService (Infrastructure)  ← Template rendering + multi-channel routing
        └─► IDiscordBotClient          ← Discord.Net abstraction
              └─► DiscordNetBotClient  ← REST (message sending) + Gateway (presence)
                    ├─► DiscordRestClient  (Discord.Net.Rest 3.18.0)
                    └─► DiscordSocketClient (Discord.Net.WebSocket 3.18.0)

DiscordBotPresenceHostedService         ← Manages online/idle status lifecycle
DiscordInviteSender                     ← Sends invite links to Twitch chat
DiscordInviteBroadcastScheduler         ← Schedules periodic invite broadcasts
DiscordNotificationTracker              ← Deduplicates notifications (prevents spam)
```

## IDiscordService — Use These Methods

```csharp
// Send a notification for any Twitch event (uses user's configured template)
Task SendNotificationAsync(User user, string eventType, object data)

// Send a test notification to validate the user's Discord configuration
Task SendTestNotificationAsync(User user)

// Validate that a Discord channel ID exists and the bot can post to it
Task<bool> ValidateDiscordChannelAsync(string channelId)

// Announce game/category changes (uses user's game-change template)
Task SendGameChangeAnnouncementAsync(User user, string gameName, string? boxArtUrl)

// Send to the mod-specific channel (separate from general notifications)
Task SendModChannelNotificationAsync(User user, string gameName, IReadOnlyList<string> activeCounterDescriptions)
```

## Message Template System

Discord messages are built from user-configurable templates stored in user settings.
Templates use `{{token}}` replacement syntax:

```csharp
// Token replacement is done in DiscordService — pattern: {{token_name}}
// Common tokens available in notification templates:
// {{username}}       → Twitch display name
// {{game}}           → Current game/category
// {{viewer_count}}   → Current viewer count
// {{title}}          → Stream title
// {{url}}            → Stream URL (https://twitch.tv/username)
// {{bits}}           → Bit count (for cheer events)
// {{gifted_count}}   → Gift sub count
// {{raid_count}}     → Raider count
// {{custom_message}} → User-defined message
```

When adding new event types, add the relevant tokens to the data object passed to
`SendNotificationAsync` — `DiscordService` uses reflection/dynamic to resolve them.

## IDiscordBotClient — Low-Level Sending

Only use `IDiscordBotClient` directly inside `DiscordService`. Higher-level code should
always go through `IDiscordService`.

```csharp
// Send a rich embed message
await _botClient.SendMessageAsync(
    channelId: user.DiscordChannelId,
    botToken: _settings.BotToken,
    content: null,                   // plain text (optional alongside embed)
    embed: myEmbed,
    components: null,                // interaction buttons (optional)
    allowedMentions: AllowedMentions.None  // suppress @everyone pings unless intentional
);

// Validate a channel before saving user settings
bool isValid = await _botClient.ValidateChannelAsync(channelId, _settings.BotToken);

// Update bot presence
await _botClient.EnsureOnlineAsync(_settings.BotToken, "watching streams");
await _botClient.SetIdleAsync(_settings.BotToken, "offline");
```

## Discord.Net 3.18.0 Usage Patterns

**REST vs Gateway client selection:**

- Use `DiscordRestClient` for all message-sending operations (stateless, simpler)
- Use `DiscordSocketClient` only for presence/status updates (requires Gateway connection)
- Do **not** use `DiscordSocketClient` for message sending — it adds unnecessary overhead

**Building embeds:**

```csharp
var embed = new EmbedBuilder()
    .WithTitle("Stream is Live! 🎮")
    .WithDescription($"**{user.DisplayName}** is now streaming!")
    .WithColor(new Color(0x9146FF))  // Twitch purple
    .WithUrl($"https://twitch.tv/{user.Username}")
    .WithThumbnailUrl(user.ProfileImageUrl)
    .AddField("Game", gameName, inline: true)
    .AddField("Viewers", viewerCount.ToString(), inline: true)
    .WithTimestamp(DateTimeOffset.UtcNow)
    .Build();
```

**Rate limiting:** Discord.Net handles rate limits automatically via its REST client.
Do not implement manual delays unless you're sending bulk messages.

**Bot token:** Always retrieved from `DiscordBotSettings.BotToken` (injected via Key Vault in production).
Never hardcode or log the token.

## Multi-Channel Routing

`DiscordService` routes to three possible channels in priority order:

1. **Event-specific channel** (if user has configured per-event overrides)
2. **Mod channel** (for mod-facing notifications like counter updates)
3. **Default Discord channel** (`user.DiscordChannelId`)

When adding new notification types, check if they should respect the mod-channel setting.

## DiscordBotSettings Configuration

```csharp
public class DiscordBotSettings
{
    public string BotToken { get; set; }        // Secret — from Key Vault
    public string ApplicationId { get; set; }   // Public — safe to log
    public int InvitePermissions { get; set; }  // Discord permission integer
    public string ApiBaseUrl { get; set; }      // Default: https://discord.com/api/v10
}
```

## DI Registration — Where to Register New Services

All Discord services are registered in `OmniForge.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddSingleton<IDiscordService, DiscordService>();
services.AddSingleton<IDiscordBotClient, DiscordNetBotClient>();
services.AddHostedService<DiscordBotPresenceHostedService>();
```

Add new Discord-related services following this pattern.

## Feature Flag Gate

Discord notification features are gated by user feature flags. Always check before sending:

```csharp
if (!user.Features.DiscordNotifications) return;
```

The `DiscordEnabledNotifications` object on the user specifies which event types are enabled.

## Testing Discord Services

- Mock `IDiscordBotClient` — never call Discord's actual API in tests
- Test `DiscordService` by verifying `SendMessageAsync` is called with correct channelId and embed properties
- Test template token replacement by asserting on embed title/description content
- `DiscordNetBotClient` is marked `[ExcludeFromCodeCoverage]` — it wraps I/O directly

## Reference Documentation

> Run `/fetch-discord-docs` to pull current Discord documentation into your session.

- Discord API reference: https://discord.com/developers/docs/reference
- Discord message formatting: https://discord.com/developers/docs/reference#message-formatting
- Discord embed limits: https://discord.com/developers/docs/resources/channel#embed-object-embed-limits
- Discord.Net docs: https://docs.discordnet.dev/
- Discord.Net EmbedBuilder: https://docs.discordnet.dev/api/Discord.EmbedBuilder.html
- Discord gateway intents: https://discord.com/developers/docs/topics/gateway#gateway-intents
- Discord rate limits: https://discord.com/developers/docs/topics/rate-limits

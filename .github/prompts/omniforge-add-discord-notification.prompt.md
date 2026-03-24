---
description: "Step-by-step protocol for adding a new Discord notification type to OmniForge"
tools:
  - fetch_webpage
  - codebase
  - editFiles
  - problems
  - findTestFiles
---

You are adding a new Discord notification type to **OmniForge**. Follow this protocol exactly, in order.

---

## Step 1 — Fetch Live Discord Documentation

Run the `/fetch-discord-docs` prompt first, or fetch these pages directly:

1. **Discord embed object and limits** — max lengths for title, description, fields, footer:
   `https://discord.com/developers/docs/resources/channel#embed-object-embed-limits`

2. **Discord message create endpoint** — request body shape, allowed mentions:
   `https://discord.com/developers/docs/resources/channel#create-message`

3. **Discord rate limits** — per-channel and global limits to stay within:
   `https://discord.com/developers/docs/topics/rate-limits`

4. **Discord.Net EmbedBuilder API** — .NET fluent builder reference:
   `https://docs.discordnet.dev/api/Discord.EmbedBuilder.html`

Confirm from the docs:

- Embed field value max = **1024 characters**, title max = **256**, description max = **4096**
- The bot permissions required (at minimum: `Send Messages`, `Embed Links`)
- Any **rate limit** considerations for the channel/guild being targeted

---

## Step 2 — Understand the Existing Discord Architecture

Search the codebase for:

- `IDiscordService` — the Core interface; all callers depend on this
- `DiscordService` — Infrastructure implementation; handles template resolution and channel routing
- `IDiscordBotClient` / `DiscordNetBotClient` — low-level Discord.Net wrapper
- `DiscordEnabledNotifications` — per-user toggles for each notification type
- `DiscordNotificationTracker` — deduplication to prevent spam

**Architecture rules:**

```
Caller → IDiscordService.SendXxxAsync(user, data)
            └─► DiscordService resolves template tokens, picks channel
                  └─► IDiscordBotClient.SendMessageAsync(channelId, token, embed)
                        └─► DiscordNetBotClient → Discord REST API
```

- Never call `IDiscordBotClient` directly from outside `DiscordService`
- Use `Discord.Net.Rest` for message sending — not `DiscordSocketClient`
- Always check `user.Features.DiscordNotifications` and the per-type toggle before sending

---

## Step 3 — Add the Method to IDiscordService (Core)

Open `OmniForge.DotNet/src/OmniForge.Core/Interfaces/IDiscordService.cs`.

Add the new method signature:

```csharp
/// <summary>
/// Sends a [describe what this notification announces].
/// Only called when the user has Discord notifications enabled and [MyType] enabled.
/// </summary>
Task SendMyNewNotificationAsync(User user, string relevantData, /* other params */);
```

---

## Step 4 — Implement in DiscordService

Open `OmniForge.DotNet/src/OmniForge.Infrastructure/Services/DiscordService.cs`.

Follow this pattern for the implementation:

```csharp
public async Task SendMyNewNotificationAsync(User user, string relevantData)
{
    // 1. Feature flag check — always first
    if (!user.Features.DiscordNotifications) return;
    if (!user.DiscordSettings?.EnabledNotifications.MyNewType == true) return;

    // 2. Resolve channel — prefer event-specific override, fall back to default
    var channelId = !string.IsNullOrEmpty(user.DiscordModChannelId)
        ? user.DiscordModChannelId   // or user.DiscordChannelId depending on audience
        : user.DiscordChannelId;
    if (string.IsNullOrEmpty(channelId)) return;

    // 3. Deduplication check (if this notification can fire repeatedly for same event)
    // if (!_notificationTracker.ShouldSend(user.TwitchUserId, "my_new_type", relevantData)) return;

    // 4. Build embed — respect Discord limits (title ≤256, description ≤4096, field value ≤1024)
    var embed = new EmbedBuilder()
        .WithTitle("Your Title Here 🎮")          // ≤256 chars
        .WithDescription($"Description here")      // ≤4096 chars
        .WithColor(new Color(0x9146FF))             // Twitch purple or contextual color
        .AddField("Field Name", TruncateSafe(relevantData, 1024), inline: true)
        .WithTimestamp(DateTimeOffset.UtcNow)
        .Build();

    // 5. Send via bot client
    try
    {
        await _botClient.SendMessageAsync(channelId, _settings.BotToken, embed: embed,
            allowedMentions: AllowedMentions.None);
        _logger.LogInformation("✅ MyNewNotification sent for {User}", user.Username);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ Failed sending MyNewNotification for {User}", user.Username);
    }
}
```

**Template tokens** (use `{{token}}` syntax if this notification uses the user's configurable template):

```csharp
var message = user.DiscordSettings?.MyNewTypeTemplate ?? DefaultTemplates.MyNewType;
message = message
    .Replace("{{username}}", user.DisplayName ?? user.Username)
    .Replace("{{relevant_field}}", relevantData);
```

---

## Step 5 — Add to DiscordEnabledNotifications (if user-configurable)

Open `OmniForge.DotNet/src/OmniForge.Core/Entities/` and find `DiscordEnabledNotifications`.

Add the new property (default `false` unless this should be on by default):

```csharp
public bool MyNewType { get; set; } = false;
```

Add any corresponding template property to `DiscordSettings` if the user can customize the message text.

---

## Step 6 — Wire into the Notification Pipeline

Determine where this notification should be triggered:

- **EventSub handler** — if triggered by a Twitch event → call from the handler's `HandleAsync`
- **NotificationService** — if triggered by a counter milestone → add a case there
- **GameSwitchService** — if triggered by game change → call from `HandleGameDetectedAsync`
- **StreamMonitorService** — if triggered by stream start/end

In the trigger location:

```csharp
if (user.Features.DiscordNotifications)
{
    _ = Task.Run(async () =>
    {
        try { await _discordService.SendMyNewNotificationAsync(user, data); }
        catch (Exception ex) { _logger.LogError(ex, "❌ Failed Discord notification for {User}", user.Username); }
    });
}
```

Use `_ = Task.Run(...)` with explicit error handling to avoid blocking the event pipeline.

---

## Step 7 — Add Settings UI (if user-configurable)

If the notification has a user toggle, find the Discord settings Blazor component and add the toggle checkbox alongside existing Discord notification toggles.

---

## Step 8 — Write Tests

Create or extend tests at `OmniForge.DotNet/tests/OmniForge.Tests/Services/DiscordServiceTests.cs`.

Required test cases (minimum):

```csharp
[Fact] Task SendMyNewNotification_DiscordFeatureDisabled_DoesNotSend()
[Fact] Task SendMyNewNotification_NotificationTypeDisabled_DoesNotSend()
[Fact] Task SendMyNewNotification_NoChannelId_DoesNotSend()
[Fact] Task SendMyNewNotification_ValidUser_SendsEmbedWithCorrectContent()
[Fact] Task SendMyNewNotification_BotClientThrows_LogsErrorAndDoesNotThrow()
```

Mock `IDiscordBotClient` — never call Discord's real API in tests.
Verify embed content, channel ID routing, and that `AllowedMentions.None` is passed.

**Coverage gate: ≥85% on all new production code.**

---

## Checklist Before Committing

- [ ] Method added to `IDiscordService` (Core interface — no external deps)
- [ ] Feature flag check: `user.Features.DiscordNotifications`
- [ ] Per-type toggle check in `DiscordEnabledNotifications`
- [ ] Channel routing: correct channel (default vs. mod) chosen for this notification type
- [ ] Embed respects Discord limits (title ≤256, description ≤4096, field values ≤1024)
- [ ] `AllowedMentions.None` passed — no accidental `@everyone` pings
- [ ] Bot token never logged
- [ ] `Task.Run` with explicit error handling if called from event pipeline
- [ ] Tests cover: disabled flags, no channel, success, error

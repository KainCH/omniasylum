# Discord Notifications

## Overview

OmniForge can post Discord notifications using the **OmniForge Discord bot** and a configured **Channel ID**.

## Streamer Setup (Recommended)

### 1) Install the OmniForge bot in your Discord server

Click this invite link (it will open Discord’s authorization page):

https://discord.com/oauth2/authorize?client_id=1442641329795502140&permissions=580550997986432&integration_type=0&scope=bot

Then:

1. Choose the Discord server you want OmniForge to post into
2. Click **Authorize**

### 2) Configure your Channel ID in OmniForge

1. In Discord, enable **Developer Mode** (User Settings → Advanced)
2. Right-click the channel you want notifications in → **Copy Channel ID**
3. In OmniForge go to **Discord Integration** (`/settings/discord`)
4. Paste the Channel ID and click **Send Test**

That’s it — once configured, OmniForge will post to that channel.

## Developer / Self-Hosting Notes

If you are running your own OmniForge instance (not using the hosted bot), you’ll need your own Discord application/bot and to configure the bot token.

### Where the secret is read

OmniForge binds bot settings from the `DiscordBot` configuration section (see `DiscordBot:BotToken`). For local development, do **not** commit tokens into `appsettings*.json`.

### Option A (recommended): environment variable

PowerShell (current session):

```powershell
cd I:\git\OmniForge
$env:DiscordBot__BotToken = "YOUR_BOT_TOKEN"
dotnet run --project "OmniForge.DotNet\src\OmniForge.Web\OmniForge.Web.csproj"
```

Notes:

- Use the double-underscore form (`DiscordBot__BotToken`) to represent `DiscordBot:BotToken`.
- If you need to override the API base URL locally, set `DiscordBot__ApiBaseUrl` (default is `https://discord.com/api/v10`).

### Option B: .NET user-secrets (keeps tokens out of files)

From the web project directory:

```powershell
cd I:\git\OmniForge\OmniForge.DotNet\src\OmniForge.Web
dotnet user-secrets init
dotnet user-secrets set "DiscordBot:BotToken" "YOUR_BOT_TOKEN"
```

### Local UI configuration

Once the app is running:

- Open Discord settings (`/settings/discord`)
- Paste the target **Channel ID**
- Click **Send Test**

## Migration Notes

- Some older deployments may still support legacy webhook-based posting. Channel ID + bot is the recommended path.

## Message Format

Notifications are sent as Discord embeds (title, description, thumbnail, image preview when applicable) and can include a link-style button to the Twitch channel.

## Implementation Note

The bot/channel message path uses the `Discord.Net.Rest` SDK (Discord.Net) for sending messages and validating channel access.

## Server Configuration

- Required: `DiscordBot:BotToken`
- Optional: `DiscordBot:ApiBaseUrl` (defaults to `https://discord.com/api/v10`)

## API Reference (Legacy Route Name)

OmniForge keeps the existing route names for compatibility; responses now include `channelId`.

### GET /api/user/discord-webhook

Returns the configured destination.

### PUT /api/user/discord-webhook

Accepts either:

- `channelId` (preferred)
- `webhookUrl` (legacy migration only)

### POST /api/user/discord-webhook/test

Sends a test embed to the configured destination.

## Troubleshooting

- “channel ID is invalid or the bot does not have access”: confirm the bot is in the server and has View Channel + Send Messages + Embed Links in that channel.
- If you enable “Mention @everyone when going live” (or a role mention): ensure the bot has permission to mention @everyone/roles in that channel, and that the target role is mentionable.
- If messages post but embeds/buttons don’t render: ensure Embed Links is allowed.
- If you rotate the bot token: update `DiscordBot:BotToken` and restart the app.

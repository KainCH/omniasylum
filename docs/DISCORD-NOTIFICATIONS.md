# Discord Notifications (Bot-Based)

## Overview

OmniForge posts Discord notifications using a **Discord bot token** + a configured **Channel ID** (preferred), instead of storing per-server webhook URLs.

Why this is more secure:

- A bot can be scoped to specific channels via Discord permissions.
- The bot token is stored centrally (Key Vault / env var), not distributed as webhook URLs.
- You can rotate the bot token in one place.

## How It Works

- Each streamer configures a **Discord Channel ID** where notifications should be posted.
- The server posts messages to Discord via the REST API endpoint:
  - `POST /channels/{channelId}/messages`
- The bot must be invited to the server and granted permissions in that channel.

## Setup

### 1) Create a Discord App + Bot

1. Go to Discord Developer Portal
2. Create an application
3. Create a bot for that application
4. Copy the **Bot Token**

Important:

- The **Application ID / Client ID** is *not* the bot token.
- OmniForge needs the **Bot Token** for bot-authenticated API calls (sending messages + validating channel access).

### 2) Invite the bot to your server

In the Developer Portal:

1. Go to **Installation**
2. Make sure you’re creating a **Guild Install** (installing into a server), not a “User Install”
3. Under Default Install Settings:
  - Scopes: `bot` (optionally also `applications.commands`)
  - Bot permissions (minimum):
    - View Channel
    - Send Messages
    - Embed Links
    - Optional permissions (only if you want pings / role management features):
      - Mention @everyone, @here, and All Roles
      - Manage Roles (only needed if you want the bot to change roles/role settings)
    - Not required for OmniForge notifications (avoid unless you explicitly need them):
      - View Audit Log
      - Manage Events / Create Events
      - Create Polls
4. Copy the **Install Link** from that page and use it to add the bot to your server

Notes:

- The “requires OAuth2 code grant” message is driven by **install type/scopes** (user OAuth vs bot install), not by which bot permissions you selected.
- Prefer least-privilege permissions; you can always add more later.

Note about Redirect URIs:

- A **redirect URI is not required** to generate a **bot invite** URL (scope `bot`).
- Redirect URIs are only needed if you are doing a user OAuth2 flow (e.g., `response_type=code` for login/identity). OmniForge Discord notifications do not require that flow.

Troubleshooting: “Requires OAuth2 code grant / redirect URI”

- If the install link says it **requires a code grant** / **redirect URI**, check **OAuth2 → General** (or equivalent settings area) and ensure **“Requires OAuth2 Code Grant”** is **OFF** for a simple bot install.
- Also confirm you’re not including user scopes like `identify` / `guilds` in the install.
- You can always use this direct bot-invite format (no redirect URI):
  - `https://discord.com/api/oauth2/authorize?client_id=<APPLICATION_ID>&scope=bot%20applications.commands&permissions=<PERMISSIONS_INT>`

### 3) Configure OmniForge server-side secret

Store the bot token in your deployment configuration:

- Config key: `DiscordBot:BotToken`
- Recommended: store as an Azure Key Vault secret consumed by the app

Key Vault naming tip:

- If you name your Key Vault secret `DiscordBot--BotToken`, the app will read it as `DiscordBot:BotToken`.

## Developer Setup (Local / Dev)

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

### 4) Get the target Channel ID

1. In Discord, enable **Developer Mode** (User Settings → Advanced)
2. Right-click the target channel → **Copy Channel ID**

### 5) Configure the Channel ID in OmniForge

In OmniForge Discord settings, paste the **Channel ID** and use “Send Test” to verify.

## Migration Notes

- OmniForge still supports legacy `DiscordWebhookUrl` as a fallback during migration.
- Prefer configuring `DiscordChannelId` for improved security.

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

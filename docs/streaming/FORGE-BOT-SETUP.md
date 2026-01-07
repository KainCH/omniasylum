# Forge Bot Setup (Global Bot Account)

This repo now supports a **single global Twitch bot account** (the “Forge bot”) that joins each tenant’s channel and handles chat commands.

## 1) Create the bot account
- Create a new Twitch account for the bot (example: `omniforge_bot`).
- Verify email and enable 2FA (recommended).

## 2) Make the bot a moderator in each channel
In each streamer’s channel chat (as the streamer), run:
- `/mod omniforge_bot`

Without mod/broadcaster privileges, mod-only commands won’t work.

## 3) Authorize the bot in OmniForge (recommended)
This is the easiest way because OmniForge stores/refreshes the bot tokens automatically in Azure Table Storage.

Prereq: you must be logged into OmniForge as the admin user (role `admin`).

- Open: `/auth/twitch/bot`
- When Twitch prompts for login, log in as the bot account
- Accept requested scopes
- You should be redirected back to `/portal` on success

Notes:
- The bot OAuth flow requests the same permissions as a normal OmniForge user login, plus moderation-management scopes (so the bot can perform mod actions via the API when it has been granted moderator status in a channel).
- Scopes alone do not make the bot a moderator — you still must `/mod omniforge_bot` in each channel.

What this does:
- Saves the bot’s `access_token`, `refresh_token`, and expiry to Azure Table Storage (PartitionKey `system`, RowKey `forgeBot`).

## 4) Configure redirect URIs
OmniForge uses explicit configured redirect URIs (it does not trust request host headers).

Set these values in configuration (local `appsettings.json`, or Azure App Settings / Key Vault):
- `Twitch:RedirectUri` → either:
	- the full user callback URL: `https://<your-host>/auth/twitch/callback`, OR
	- a Key Vault secret name that contains the full user callback URL (example secret names: `dev-callback`, `prod-callback`)
- `Twitch:BotRedirectUri` → either:
	- the full bot callback URL: `https://<your-host>/auth/twitch/bot/callback`, OR
	- a Key Vault secret name that contains the full bot callback URL (example secret names: `Dev-bot-callback`, `Prod-bot-callback`)

If you use Key Vault secret names (like `Dev-bot-callback` / `Prod-bot-callback`), set `Twitch:BotRedirectUri` to that secret name and store the actual URL as the secret value.

## 5) (Optional) Seed via configuration (fallback)
If you cannot use the `/auth/twitch/bot` flow, you can seed initial values via configuration:
- `Twitch:BotUsername`
- `Twitch:BotAccessToken`
- `Twitch:BotRefreshToken`

Note: the recommended flow is `/auth/twitch/bot` because it persists refreshed tokens.

## Troubleshooting
- If the bot isn’t responding in a channel, verify it’s a mod in that channel.
- If the bot can’t connect, re-run `/auth/twitch/bot` to re-authorize.

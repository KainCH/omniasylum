---
description: "Fetch the latest Discord API and Discord.Net documentation for use in the current development task"
tools:
  - fetch_webpage
  - codebase
---

You are helping develop a Discord bot feature for OmniForge. The project uses **Discord.Net 3.18.0** (`Discord.Net.Rest` + `Discord.Net.WebSocket`), integrated through the `IDiscordService` / `IDiscordBotClient` abstraction layers.

## Step 1 — Fetch live Discord documentation

Fetch the following pages and incorporate their content into your response context:

1. **Discord API reference** — core endpoints, authentication, rate limits:
   `https://discord.com/developers/docs/reference`

2. **Discord channel resource** — send message API, embed object, allowed mentions:
   `https://discord.com/developers/docs/resources/channel`

3. **Discord embed object limits** — character limits per embed field (critical for safe formatting):
   `https://discord.com/developers/docs/resources/channel#embed-object-embed-limits`

4. **Discord gateway intents** — required intents for bot event subscriptions:
   `https://discord.com/developers/docs/topics/gateway#gateway-intents`

5. **Discord.Net EmbedBuilder API** — .NET library embed construction reference:
   `https://docs.discordnet.dev/api/Discord.EmbedBuilder.html`

6. **Discord permission integers** — how to calculate and interpret permission bitfields:
   `https://discord.com/developers/docs/topics/permissions`

## Step 2 — Understand the OmniForge Discord architecture

Search the codebase for `IDiscordService`, `DiscordNetBotClient`, and `IDiscordBotClient` to understand:

- The existing notification methods and their signatures
- How `{{token}}` template replacement works in `DiscordService`
- How the REST vs Gateway clients are used
- The `DiscordBotSettings` configuration shape

## Step 3 — Apply to the current task

Using the fetched documentation AND the OmniForge codebase context:

1. Identify which **Discord API endpoint(s)** are needed for the current feature
2. Confirm the **bot permissions** required and how they map to the `InvitePermissions` integer in `DiscordBotSettings`
3. Check **embed field limits** to ensure any new embeds stay within Discord's constraints
4. Identify relevant **gateway intents** if the feature needs to receive Discord events (not just send)
5. Note any **rate limit** considerations specific to the endpoint being used
6. Flag any **Discord API version differences** (the project uses API v10)

Then proceed with implementing or reviewing the described Discord feature following the existing `IDiscordService` / `IDiscordBotClient` patterns in OmniForge.

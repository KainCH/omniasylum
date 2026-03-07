---
description: "Fetch the latest Twitch API and EventSub documentation for use in the current development task"
tools:
  - fetch_webpage
  - codebase
---

You are helping develop a feature for OmniForge, a multi-tenant Twitch streaming tool built with .NET 9 / Blazor Server.

## Step 1 — Fetch live Twitch documentation

Fetch the following pages and incorporate their content into your response context:

1. **EventSub subscription types** — the full list of available event types, their conditions, and required OAuth scopes:
   `https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/`

2. **Twitch Helix API reference** — available endpoints, request/response shapes, rate limits:
   `https://dev.twitch.tv/docs/api/reference/`

3. **EventSub WebSocket handling guide** — how to connect, handle reconnects, keepalive, and revocations:
   `https://dev.twitch.tv/docs/eventsub/handling-websocket-events/`

4. **Twitch OAuth scopes** — required scopes for each API/EventSub feature:
   `https://dev.twitch.tv/docs/authentication/scopes/`

## Step 2 — Understand the OmniForge EventSub architecture

Search the codebase for `BaseEventSubHandler` and `EventSubHandlerRegistry` to understand:

- The handler base class pattern
- How handlers are registered and dispatched
- The existing subscription types already implemented

## Step 3 — Apply to the current task

Using the fetched documentation AND the OmniForge codebase context:

1. Identify which **EventSub subscription type(s)** are needed for the current feature
2. Confirm the **required OAuth scopes** and check if they're already in `TwitchSettings` / user token flow
3. Identify the **exact JSON shape** of the event payload so helper calls (`GetStringProperty`, `GetIntProperty`) are used correctly
4. Highlight any **rate limits** or **conditions** relevant to the subscription
5. Call out any **breaking changes** or deprecations in the latest Twitch docs

Then proceed with implementing or reviewing the described Twitch/EventSub feature following the existing handler pattern in OmniForge.

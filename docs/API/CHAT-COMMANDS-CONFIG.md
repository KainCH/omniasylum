# Chat Commands Configuration API

## Overview

The Chat Commands API allows configuring chat commands and settings, including the maximum allowed increment amount.

## Endpoints

### Get Chat Command Settings

Retrieves the full configuration including `MaxIncrementAmount` and all command definitions.

- **URL**: `/api/chat-commands/settings`
- **Method**: `GET`
- **Auth**: Required (Bearer Token)

**Response:**

```json
{
  "maxIncrementAmount": 10,
  "commands": {
    "!deaths": {
      "response": "Current death count: {{deaths}}",
      "permission": "everyone",
      "cooldown": 5,
      "enabled": true
    },
    ...
  }
}
```

### Save Chat Commands

Updates the chat commands configuration. Can also update `MaxIncrementAmount`.

- **URL**: `/api/chat-commands`
- **Method**: `PUT`
- **Auth**: Required (Bearer Token)

**Request Body:**

```json
{
  "commands": {
    "!deaths": { ... }
  },
  "maxIncrementAmount": 10
}
```

**Note:** If `maxIncrementAmount` is omitted, the existing value will be preserved (or default to 1 if not set).

## MaxIncrementAmount

- **Description**: Controls the maximum numeric value that can be used in increment commands (e.g. `!death+ 5`).
- **Range**: 1 to 10.
- **Default**: 1.

## Default Commands

The system comes with several default commands, including aliases for common actions:

- **Deaths**: `!deaths`, `!death+`, `!death-`, `!d+`, `!d-`
- **Swears**: `!swears`, `!swear+`, `!swear-`, `!sw+`, `!sw-`
- **Screams**: `!screams`, `!scream+`, `!scream-`, `!sc+`, `!sc-`
- **Stats**: `!stats`
- **Reset**: `!resetcounters`

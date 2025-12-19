# OmniForge - Multi-Tenant Stream Counter Platform

A comprehensive **multi-tenant Twitch stream counter application** built with **.NET 8** and deployed to **Azure Container Apps**. OmniForge provides real-time stream counters, chat bot integration, Discord notifications, stream overlays, and much more for content creators.

## 🌟 Features

### Core Features
- ✅ **Twitch OAuth 2.0** - Secure authentication with Twitch accounts
- ✅ **Multi-Tenant Architecture** - Multiple streamers share a single instance
- ✅ **Per-User Twitch Bots** - Each streamer gets their own chat bot
- ✅ **Real-Time Sync** - WebSocket-powered updates across all devices via SignalR
- ✅ **Azure Deployment** - Container Apps with Key Vault secrets management

### Counter System
- ✅ **Built-in Counters** - Deaths, Swears, Screams, Bits
- ✅ **Custom Counters** - Define your own counters with custom icons, milestones, and increment values
- ✅ **Series Save States** - Save/load counter states for different game series
- ✅ **Milestone Notifications** - Celebrate reaching counter milestones

### Stream Integration
- ✅ **Twitch EventSub** - Real-time follows, subscriptions, gift subs, resubs, cheers, and raids
- ✅ **Channel Points** - Create custom rewards that control counters
- ✅ **Bits Integration** - Configurable thresholds for counter increments
- ✅ **Stream Status Management** - Prep → Live → End workflow

### Chat Commands
- ✅ **Default Commands** - `!deaths`, `!swears`, `!screams`, `!stats`, `!d+`, `!d-`, etc.
- ✅ **Custom Commands** - Create your own chat commands with variables
- ✅ **Permission Levels** - Everyone, Subscriber, Moderator, Broadcaster

### Discord Integration
- ✅ **OmniForge Bot** - Official Discord bot for notifications
- ✅ **Milestone Alerts** - Post to Discord when counters hit milestones
- ✅ **Stream Notifications** - Stream start/end announcements
- ✅ **Customizable Templates** - Multiple themes (Asylum, Minimal, Detailed, etc.)
- ✅ **Per-Event Channel Routing** - Different notifications to different channels

### Stream Overlays
- ✅ **Browser Source Overlay** - Transparent overlay for OBS/Streamlabs
- ✅ **Alert Animations** - Animated notifications with sound effects
- ✅ **Customizable Themes** - Colors, fonts, positions, effects
- ✅ **Celebration Effects** - Particles, screen effects, SVG filters

### Moderator System
- ✅ **Delegated Access** - Grant moderators access to manage your counters
- ✅ **Mod Dashboard** - Moderators can manage multiple streamers
- ✅ **Series Management** - Mods can save/load series for streamers they manage

### AutoMod Integration
- ✅ **View AutoMod Settings** - See current Twitch AutoMod configuration
- ✅ **Update AutoMod** - Adjust moderation levels from OmniForge

## 📋 Tech Stack

- **Backend**: .NET 8 (ASP.NET Core, SignalR)
- **Database**: Azure Table Storage
- **Authentication**: Twitch OAuth 2.0 + JWT
- **Real-Time**: SignalR WebSockets
- **Twitch**: EventSub WebSocket, Helix API, TMI.js chat
- **Discord**: Discord.Net REST SDK
- **Infrastructure**: Azure Container Apps, Key Vault, Application Insights
- **CI/CD**: Bicep Infrastructure as Code

## 🚀 Quick Start

### Prerequisites

- .NET 8 SDK
- Azure CLI (for deployment)
- Twitch Developer Account
- (Optional) Discord Bot Token for notifications

### Local Development

```powershell
# Clone repository
git clone https://github.com/KainCH/omniasylum.git
cd OmniForge

# Navigate to web project
cd OmniForge.DotNet/src/OmniForge.Web

# Set environment variables (or use user-secrets)
$env:Twitch__ClientId = "your_client_id"
$env:Twitch__ClientSecret = "your_client_secret"
$env:Jwt__Secret = "your_jwt_secret_min_32_chars"

# Run the application
dotnet run
```

### Create Twitch Application

1. Go to [Twitch Developer Console](https://dev.twitch.tv/console/apps)
2. Register your application:
   - **Name**: OmniForge (or your preferred name)
   - **OAuth Redirect URLs**: `http://localhost:5000/auth/twitch/callback`
   - **Category**: Application Integration
3. Copy **Client ID** and **Client Secret**

## 🔌 API Endpoints

### Authentication

| Method | Endpoint                | Description                           |
| ------ | ----------------------- | ------------------------------------- |
| GET    | `/auth/twitch`          | Initiate Twitch OAuth login           |
| GET    | `/auth/twitch/callback` | OAuth callback handler                |
| GET    | `/auth/me`              | Get current user info (requires auth) |
| POST   | `/auth/refresh`         | Refresh Twitch access token           |
| POST   | `/auth/logout`          | Logout current user                   |

### Counters

| Method | Endpoint                         | Description                                    |
| ------ | -------------------------------- | ---------------------------------------------- |
| GET    | `/api/counters`                  | Get user's counter state                       |
| POST   | `/api/counters/{type}/increment` | Increment counter (deaths/swears/screams/bits) |
| POST   | `/api/counters/{type}/decrement` | Decrement counter                              |
| POST   | `/api/counters/reset`            | Reset all counters                             |

### Custom Counters

| Method | Endpoint                              | Description                |
| ------ | ------------------------------------- | -------------------------- |
| GET    | `/api/custom-counters`                | Get custom counter config  |
| PUT    | `/api/custom-counters`                | Save custom counter config |
| POST   | `/api/custom-counters/{id}/increment` | Increment custom counter   |
| POST   | `/api/custom-counters/{id}/decrement` | Decrement custom counter   |

### Series Save States

| Method | Endpoint                 | Description                |
| ------ | ------------------------ | -------------------------- |
| GET    | `/api/series`            | List all series saves      |
| POST   | `/api/series/save`       | Save current counter state |
| POST   | `/api/series/load`       | Load a saved state         |
| DELETE | `/api/series/{seriesId}` | Delete a series save       |

### Stream Management

| Method | Endpoint               | Description                          |
| ------ | ---------------------- | ------------------------------------ |
| GET    | `/api/stream/session`  | Get current stream session           |
| POST   | `/api/stream/status`   | Update stream status (prep/live/end) |
| POST   | `/api/stream/start`    | Start stream session                 |
| POST   | `/api/stream/end`      | End stream session                   |
| GET    | `/api/stream/settings` | Get stream settings                  |
| PUT    | `/api/stream/settings` | Update stream settings               |

### Chat Commands

| Method | Endpoint                      | Description              |
| ------ | ----------------------------- | ------------------------ |
| GET    | `/api/chat-commands`          | Get chat command config  |
| GET    | `/api/chat-commands/defaults` | Get default commands     |
| PUT    | `/api/chat-commands`          | Save chat command config |

### Channel Points

| Method | Endpoint            | Description                |
| ------ | ------------------- | -------------------------- |
| GET    | `/api/rewards`      | List channel point rewards |
| POST   | `/api/rewards`      | Create new reward          |
| PUT    | `/api/rewards/{id}` | Update reward              |
| DELETE | `/api/rewards/{id}` | Delete reward              |

### Alerts

| Method | Endpoint                     | Description              |
| ------ | ---------------------------- | ------------------------ |
| GET    | `/api/alerts`                | Get alert configurations |
| POST   | `/api/alerts`                | Create alert             |
| PUT    | `/api/alerts/{id}`           | Update alert             |
| DELETE | `/api/alerts/{id}`           | Delete alert             |
| GET    | `/api/alerts/event-mappings` | Get event→alert mappings |
| PUT    | `/api/alerts/event-mappings` | Update event mappings    |
| POST   | `/api/alerts/test/{type}`    | Test an alert            |

### Templates (Discord/Notifications)

| Method | Endpoint                   | Description              |
| ------ | -------------------------- | ------------------------ |
| GET    | `/api/templates/available` | List available templates |
| GET    | `/api/templates/current`   | Get current template     |
| PUT    | `/api/templates/select`    | Select a template        |
| PUT    | `/api/templates/custom`    | Save custom template     |

### Moderator Management

| Method | Endpoint                                                | Description                 |
| ------ | ------------------------------------------------------- | --------------------------- |
| GET    | `/api/moderator/my-moderators`                          | List your moderators        |
| POST   | `/api/moderator/grant-access`                           | Grant mod access to user    |
| POST   | `/api/moderator/revoke-access`                          | Revoke mod access           |
| GET    | `/api/moderator/streamers`                              | List streamers you moderate |
| GET    | `/api/moderator/{streamerId}/counters`                  | Get streamer's counters     |
| POST   | `/api/moderator/{streamerId}/counters/{type}/increment` | Increment as mod            |

### AutoMod

| Method | Endpoint                | Description             |
| ------ | ----------------------- | ----------------------- |
| GET    | `/api/automod/settings` | Get AutoMod settings    |
| PUT    | `/api/automod/settings` | Update AutoMod settings |

### Admin (Requires admin role)

| Method | Endpoint                             | Description                 |
| ------ | ------------------------------------ | --------------------------- |
| GET    | `/api/admin/users`                   | List all registered users   |
| GET    | `/api/admin/users/{userId}`          | Get user details            |
| PUT    | `/api/admin/users/{userId}/features` | Update user feature flags   |
| PUT    | `/api/admin/users/{userId}/status`   | Enable/disable user account |
| GET    | `/api/admin/stats`                   | Get system statistics       |
| DELETE | `/api/admin/users/{userId}`          | Delete user account         |

### System

| Method | Endpoint      | Description            |
| ------ | ------------- | ---------------------- |
| GET    | `/api/health` | Health check (no auth) |
//     "autoClip": 5,
//     "customCommands": 2,
//     "analytics": 8,
//     "webhooks": 1
//   }
// }
```

### System

| Method | Endpoint             | Description               |
| ------ | -------------------- | ------------------------- |
| GET    | `/api/health`        | Health check (no auth)    |
| GET    | `/api/twitch/status` | Twitch integration status |

### Example API Call

```javascript
// With authentication header
fetch('http://localhost:3000/api/counters', {
  headers: {
    'Authorization': `Bearer ${jwtToken}`
  }
})
```

## 🔌 WebSocket Events

### Client → Server

- `incrementDeaths` - Increment death counter
- `decrementDeaths` - Decrement death counter
- `incrementSwears` - Increment swear counter
- `decrementSwears` - Decrement swear counter
- `resetCounters` - Reset all counters
- `connectTwitch` - Connect user's Twitch chat bot

### Server → Client

- `counterUpdate` - Broadcast when counters change (only to user's clients)
  ```json
  {
    "deaths": 5,
    "swears": 12,
    "lastUpdated": "2025-11-01T12:34:56.789Z"
  }
  ```
- `twitchConnected` - Response to connectTwitch
- `error` - Error message

### WebSocket Connection

```javascript
// Frontend connection with authentication
const socket = io('http://localhost:3000', {
  auth: {
    token: jwtToken
  }
});

socket.on('counterUpdate', (data) => {
  console.log('Counters:', data);
});
```

## 🎮 Twitch Chat Commands

### Public Commands (Anyone)

- `!deaths` - Show current death count (no chat reply)
- `!swears` - Show current swear count (no chat reply)
- `!stats` - Show all statistics (no chat reply)

### Mod-Only Commands (Broadcaster & Mods)

- `!death+` or `!d+` - Increment deaths (no chat reply)
- `!death-` or `!d-` - Decrement deaths (no chat reply)
- `!swear+` or `!s+` - Increment swears (no chat reply)
- `!swear-` or `!s-` - Decrement swears (no chat reply)
- `!resetcounters` - Reset all counters (no chat reply)

### Series Save States (Broadcaster & Mods)

- `!saveseries <name>` - Save current counter state with a name
- `!loadseries <seriesId>` - Load a previously saved state
- `!listseries` - Show recent series saves
- `!deleteseries <seriesId>` - Delete a series save

> **Example**: `!saveseries Elden Ring Episode 5` saves the current counters. Next stream, use `!listseries` to find the ID, then `!loadseries <id>` to continue where you left off!

## ☁️ Azure Deployment

See [`deploy/README.md`](deploy/README.md) for detailed Azure deployment instructions.

### Quick Deploy to Azure

```powershell
# Set variables
$RESOURCE_GROUP = "omniasylum-rg"
$LOCATION = "eastus"

# Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Deploy (see deploy/README.md for full steps)
az deployment group create `
  --resource-group $RESOURCE_GROUP `
  --template-file deploy/main.bicep `
  --parameters baseName=omniasylum
```

## 📁 Project Structure

```
API/
├── server.js                      # Main Express + Socket.io server
├── database.js                    # Multi-tenant database (Azure Tables or local JSON)
├── keyVault.js                    # Azure Key Vault integration
├── authRoutes.js                  # OAuth authentication endpoints
├── authMiddleware.js              # JWT verification middleware
├── counterRoutes.js               # Counter API endpoints
├── adminRoutes.js                 # Admin-only user management endpoints
├── multiTenantTwitchService.js    # Per-user Twitch bot management
├── package.json                   # Dependencies
├── Dockerfile                     # Container image definition
├── .env.example                   # Environment template
├── .gitignore                     # Git ignore rules
├── .dockerignore                  # Docker ignore rules
├── deploy/
│   ├── main.bicep                 # Azure infrastructure as code
│   └── README.md                  # Deployment guide
└── README.md                      # This file
```

## 👤 User Roles & Permissions

### Streamer (Default)

All users get the `streamer` role by default. They can:
- Access their own counter data
- Control their Twitch bot
- Modify their own counters
- Use all assigned features

### Admin

The Twitch user `riress` is automatically assigned the `admin` role. Admins can:
- View all users in the system
- Enable/disable user accounts
- Manage feature flags per user
- View system statistics
- Delete user accounts (except admin)

### Feature Flags

Each user can be assigned the following features:

| Feature          | Description                  | Default    |
| ---------------- | ---------------------------- | ---------- |
| `chatCommands`   | Twitch chat integration      | ✅ Enabled  |
| `channelPoints`  | Channel points redemptions   | ❌ Disabled |
| `autoClip`       | Auto-clip on milestones      | ❌ Disabled |
| `customCommands` | Custom chat commands         | ❌ Disabled |
| `analytics`      | Analytics dashboard          | ❌ Disabled |
| `webhooks`       | External webhook integration | ❌ Disabled |

Admins can toggle these features for any user via the admin API.

## 🗄️ Database Modes

### Local (Development)

Uses JSON files in `data/` directory:
- `data/users.json` - User profiles and OAuth tokens
- `data/counters.json` - Counter data per user

### Azure (Production)

Uses Azure Table Storage:
- `users` table - User profiles
- `counters` table - Counter data (partitioned by userId)

Switch modes with `DB_MODE` environment variable.

## 🔧 Development

### Run with Hot Reload

```powershell
npm run dev
```

### View Logs

Server logs all important events to console with emojis for easy scanning:
- ✅ Success operations
- ❌ Errors
- 🔄 Token refreshes
- 💀 Death counter changes
- 🤬 Swear counter changes

## 🐛 Troubleshooting

**OAuth redirect fails:**
- Verify redirect URI in Twitch app matches exactly
- Check TWITCH_REDIRECT_URI in .env

**WebSocket won't connect:**
- Ensure JWT token is valid
- Check CORS_ORIGIN includes your frontend URL

**Twitch bot not responding:**
- Verify user clicked "Connect Twitch" in frontend
- Check that OAuth scopes include `chat:read` and `chat:edit`

**Database errors:**
- Check DB_MODE setting
- For Azure: verify storage account credentials

## 📊 Monitoring (Azure)

- **Application Insights** - Automatic telemetry and logging
- **Log Analytics** - Query and analyze logs
- **Metrics** - CPU, memory, request count, response times

## 💰 Costs (Azure)

Estimated monthly costs:
- **Container Apps**: ~$0-5 (auto-scale to zero)
- **Key Vault**: ~$0.03
- **Table Storage**: ~$0.01
- **Application Insights**: Free tier (5GB/month)

**Total: ~$0-5/month** depending on usage

## 🔒 Security Features

✅ OAuth 2.0 authentication via Twitch
✅ JWT tokens for API access
✅ All secrets in Azure Key Vault
✅ Managed Identity (no credentials to manage)
✅ HTTPS enforced in Azure
✅ Per-user data isolation
✅ RBAC for Azure resources
✅ Token refresh handling

## 🤝 Frontend Integration

Frontend needs to:

1. **Redirect to OAuth**: Send users to `${API_URL}/auth/twitch`
2. **Receive JWT**: Parse token from redirect URL parameter
3. **Store token**: Save JWT to localStorage
4. **Send with requests**: Include in Authorization header
5. **Connect WebSocket**: Send token in auth parameter

Example:

```javascript
// Initiate login
window.location.href = 'http://localhost:3000/auth/twitch';

// After redirect, extract token
const token = new URLSearchParams(window.location.search).get('token');
localStorage.setItem('jwt', token);

// Use in API calls
fetch('http://localhost:3000/api/counters', {
  headers: { 'Authorization': `Bearer ${token}` }
});

// Connect WebSocket
const socket = io('http://localhost:3000', {
  auth: { token }
});
```

## 📝 License

MIT - See LICENSE file in project root

## 🎯 Next Steps

- [ ] Update frontend for OAuth login
- [ ] Add channel point redemption support
- [ ] Implement auto-clip creation on milestones
- [ ] Add analytics dashboard
- [ ] Support custom chat commands
- [ ] Add webhook integrations
- [ ] **Discord Template System** - Add customizable notification templates (asylum-themed, minimal, detailed, etc.) to allow users to personalize their Discord notification styles

---

**Need help?** Check the deployment guide in `deploy/README.md` or open an issue!

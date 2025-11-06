# OmniAsylum API - Multi-Tenant Edition

Backend server for OmniAsylum stream counter with **Twitch OAuth authentication**, **multi-tenant support**, and **Azure deployment**.

## 🌟 New Features

- ✅ **Twitch OAuth** - Streamers login with their Twitch account
- ✅ **Multi-Tenant** - Multiple streamers can use the same instance
- ✅ **Per-User Bots** - Each streamer gets their own chat bot
- ✅ **Azure Ready** - Deploy to Azure Container Apps with Key Vault
- ✅ **Secure** - All secrets in Key Vault, managed identity authentication
- ✅ **Scalable** - Auto-scale to zero, pay only for usage

## 📋 Prerequisites

- Node.js 18+ installed
- npm package manager
- Twitch Developer Account
- (Optional) Azure account for cloud deployment

## 🚀 Quick Start (Local Development)

### 1. Install Dependencies

```powershell
cd API
npm install
```

### 2. Create Twitch Application

1. Go to [Twitch Developer Console](https://dev.twitch.tv/console/apps)
2. Click "Register Your Application"
3. Fill in:
   - **Name**: OmniAsylum Counter
   - **OAuth Redirect URLs**: `http://localhost:3000/auth/twitch/callback`
   - **Category**: Application Integration
4. Save and copy your **Client ID** and **Client Secret**

### 3. Configure Environment

```powershell
cp .env.example .env
```

Edit `.env` and add your Twitch credentials:

```env
TWITCH_CLIENT_ID=your_client_id_here
TWITCH_CLIENT_SECRET=your_client_secret_here
JWT_SECRET=generate_a_random_secret_here
```

Generate a secure JWT secret:

```powershell
# PowerShell
-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | % {[char]$_})
```

### 4. Start the Server

```powershell
npm start
```

Or for development with auto-reload:

```powershell
npm run dev
```

### 5. Test Authentication

1. Open browser to `http://localhost:3000/auth/twitch`
2. Login with your Twitch account
3. You'll be redirected to frontend with a JWT token

## 🔐 Authentication Flow

```
User clicks "Login with Twitch"
    ↓
Redirect to /auth/twitch
    ↓
Twitch OAuth page (user authorizes)
    ↓
Callback to /auth/twitch/callback
    ↓
Server exchanges code for access token
    ↓
User data saved to database
    ↓
JWT token created and sent to frontend
    ↓
Frontend stores token, uses for all API calls
```

## 🔌 API Endpoints

### Authentication

| Method | Endpoint                | Description                           |
| ------ | ----------------------- | ------------------------------------- |
| GET    | `/auth/twitch`          | Initiate Twitch OAuth login           |
| GET    | `/auth/twitch/callback` | OAuth callback handler                |
| GET    | `/auth/me`              | Get current user info (requires auth) |
| POST   | `/auth/refresh`         | Refresh Twitch access token           |
| POST   | `/auth/logout`          | Logout current user                   |

### Counters (All require authentication)

| Method | Endpoint                         | Description              |
| ------ | -------------------------------- | ------------------------ |
| GET    | `/api/counters`                  | Get user's counter state |
| POST   | `/api/counters/deaths/increment` | Increment deaths         |
| POST   | `/api/counters/deaths/decrement` | Decrement deaths         |
| POST   | `/api/counters/swears/increment` | Increment swears         |
| POST   | `/api/counters/swears/decrement` | Decrement swears         |
| POST   | `/api/counters/reset`            | Reset all counters       |
| GET    | `/api/counters/export`           | Export counter data      |

### Series Save States (All require authentication)

| Method | Endpoint                         | Description                |
| ------ | -------------------------------- | -------------------------- |
| POST   | `/api/counters/series/save`      | Save current counter state |
| POST   | `/api/counters/series/load`      | Load a saved counter state |
| GET    | `/api/counters/series/list`      | List all series saves      |
| DELETE | `/api/counters/series/:seriesId` | Delete a series save       |

> **💾 New Feature!** Save and reload counter states for different stream series. Perfect for episodic content or switching between games. See [SERIES-SAVE-STATES.md](API/SERIES-SAVE-STATES.md) for details.

### Admin (Requires admin role)

| Method | Endpoint                            | Description                 |
| ------ | ----------------------------------- | --------------------------- |
| GET    | `/api/admin/users`                  | List all registered users   |
| GET    | `/api/admin/users/:userId`          | Get user details + counters |
| PUT    | `/api/admin/users/:userId/features` | Update user feature flags   |
| PUT    | `/api/admin/users/:userId/status`   | Enable/disable user account |
| GET    | `/api/admin/stats`                  | Get system statistics       |
| DELETE | `/api/admin/users/:userId`          | Delete user account         |
| GET    | `/api/admin/features`               | List available features     |

**Admin Access**: Only user with Twitch username `riress` has admin role.

#### Admin Examples

**List all users:**
```javascript
fetch('http://localhost:3000/api/admin/users', {
  headers: { 'Authorization': `Bearer ${jwtToken}` }
})
```

**Update user features:**
```javascript
fetch('http://localhost:3000/api/admin/users/12345678/features', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${jwtToken}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    chatCommands: true,
    channelPoints: false,
    autoClip: true,
    customCommands: false,
    analytics: true,
    webhooks: false
  })
})
```

**Disable user account:**
```javascript
fetch('http://localhost:3000/api/admin/users/12345678/status', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${jwtToken}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({ isActive: false })
})
```

**Get system statistics:**
```javascript
fetch('http://localhost:3000/api/admin/stats', {
  headers: { 'Authorization': `Bearer ${jwtToken}` }
})
// Response:
// {
//   "totalUsers": 15,
//   "activeUsers": 12,
//   "adminUsers": 1,
//   "featureUsage": {
//     "chatCommands": 10,
//     "channelPoints": 3,
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

- `!deaths` - Show current death count
- `!swears` - Show current swear count
- `!stats` - Show all statistics

### Mod-Only Commands (Broadcaster & Mods)

- `!death+` or `!d+` - Increment deaths
- `!death-` or `!d-` - Decrement deaths
- `!swear+` or `!s+` - Increment swears
- `!swear-` or `!s-` - Decrement swears
- `!resetcounters` - Reset all counters

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

---

**Need help?** Check the deployment guide in `deploy/README.md` or open an issue!

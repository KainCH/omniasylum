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

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/auth/twitch` | Initiate Twitch OAuth login |
| GET | `/auth/twitch/callback` | OAuth callback handler |
| GET | `/auth/me` | Get current user info (requires auth) |
| POST | `/auth/refresh` | Refresh Twitch access token |
| POST | `/auth/logout` | Logout current user |

### Counters (All require authentication)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/counters` | Get user's counter state |
| POST | `/api/counters/deaths/increment` | Increment deaths |
| POST | `/api/counters/deaths/decrement` | Decrement deaths |
| POST | `/api/counters/swears/increment` | Increment swears |
| POST | `/api/counters/swears/decrement` | Decrement swears |
| POST | `/api/counters/reset` | Reset all counters |
| GET | `/api/counters/export` | Export counter data |

### System

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/health` | Health check (no auth) |
| GET | `/api/twitch/status` | Twitch integration status |

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

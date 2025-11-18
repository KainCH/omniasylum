# 🎮 OmniAsylum Quick Reference

## 🚀 Start Server (Local)

```bash
cd API
npm install
npm start
```

Access: `http://localhost:3000`

## 🔑 Environment Variables

**Required** (in `.env`):
```env
TWITCH_CLIENT_ID=<from_twitch_dev_console>
TWITCH_CLIENT_SECRET=<from_twitch_dev_console>
JWT_SECRET=<random_32_char_string>
```

**Optional**:
```env
PORT=3000
DB_MODE=local  # or 'azure'
CORS_ORIGIN=http://localhost:5500
FRONTEND_URL=http://localhost:5500
TWITCH_REDIRECT_URI=http://localhost:3000/auth/twitch/callback
```

## 📡 API Endpoints

### Auth (No token needed)
- `GET /auth/twitch` - Start login
- `GET /auth/twitch/callback` - OAuth callback
- `GET /api/health` - Health check

### Auth (Token required)
- `GET /auth/me` - Get user info
- `POST /auth/refresh` - Refresh token
- `POST /auth/logout` - Logout

### Counters (Token required)
- `GET /api/counters` - Get counters
- `POST /api/counters/deaths/increment`
- `POST /api/counters/deaths/decrement`
- `POST /api/counters/swears/increment`
- `POST /api/counters/swears/decrement`
- `POST /api/counters/reset`
- `GET /api/counters/export`

## 🔌 WebSocket Events

**Connect:**
```javascript
const socket = io('http://localhost:3000', {
  auth: { token: 'your-jwt-token' }
});
```

**Client → Server:**
- `incrementDeaths`
- `decrementDeaths`
- `incrementSwears`
- `decrementSwears`
- `resetCounters`
- `connectTwitch`

**Server → Client:**
- `counterUpdate` - Counter changed
- `twitchConnected` - Bot connected
- `error` - Error occurred

## 💬 Twitch Chat Commands

**Public:**
- `!deaths` `!swears` `!stats`

**Mod-Only:**
- `!death+` `!d+` - Inc deaths
- `!death-` `!d-` - Dec deaths
- `!swear+` `!s+` - Inc swears
- `!swear-` `!s-` - Dec swears
- `!resetcounters` - Reset all

## 🧪 Testing

**Test OAuth:**
```
http://localhost:3000/auth/twitch
```

**Test API:**
```bash
curl http://localhost:3000/api/health
```

**Test with auth:**
```bash
curl -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  http://localhost:3000/api/counters
```

## 🐳 Docker

**Build:**
```bash
docker build -t omniasylum-api .
```

**Run:**
```bash
docker run -p 3000:3000 \
  -e TWITCH_CLIENT_ID=xxx \
  -e TWITCH_CLIENT_SECRET=yyy \
  -e JWT_SECRET=zzz \
  omniasylum-api
```

## ☁️ Azure Deploy

**Prerequisites:**
```bash
az login
az account set --subscription "Your-Subscription"
```

**Quick deploy:**
```bash
cd deploy
./deploy.sh  # or follow README.md
```

**Full guide:** See `deploy/README.md`

## 📂 File Structure

```
API/
├── server.js              # Main server
├── database.js            # Data storage
├── keyVault.js            # Azure secrets
├── authRoutes.js          # OAuth endpoints
├── authMiddleware.js      # JWT verification
├── counterRoutes.js       # Counter API
├── multiTenantTwitchService.js  # Twitch bots
├── Dockerfile             # Container
├── .env.example           # Config template
├── package.json           # Dependencies
└── deploy/
    ├── main.bicep         # Azure IaC
    └── README.md          # Deploy guide
```

## 🔧 Common Tasks

**Update dependencies:**
```bash
npm update
```

**Check logs:**
```bash
npm start  # Shows colored logs
```

**Reset database (local):**
```bash
rm -rf data/
# Restart server
```

**Generate JWT secret:**
```bash
# PowerShell
-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | % {[char]$_})
```

## 🐛 Troubleshooting

**Port in use:**
```
Change PORT in .env
```

**OAuth fails:**
```
Check TWITCH_REDIRECT_URI matches Twitch app
```

**WebSocket won't connect:**
```
Verify JWT token is valid
Check CORS_ORIGIN includes frontend URL
```

**Twitch bot silent:**
```
Emit 'connectTwitch' event after WebSocket connects
```

## 📚 Documentation

- **Main docs:** `README.md`
- **Deploy guide:** `deploy/README.md`
- **Summary:** `IMPLEMENTATION-SUMMARY.md`
- **Overview:** `../WHATS-NEW.md`

## 💰 Azure Costs

| Resource | Monthly Cost |
|----------|-------------|
| Container Apps | $0-5 (scale-to-zero) |
| Key Vault | $0.03 |
| Table Storage | $0.01 |
| App Insights | Free tier |
| **Total** | **~$0-5** |

## 🎯 Production Checklist

- [ ] Create Twitch app
- [ ] Configure .env secrets
- [ ] Test locally
- [ ] Build Docker image
- [ ] Deploy to Azure
- [ ] Add secrets to Key Vault
- [ ] Update Twitch redirect URI
- [ ] Update frontend URLs
- [ ] Test end-to-end
- [ ] Monitor logs

## 🔗 Quick Links

- [Twitch Dev Console](https://dev.twitch.tv/console)
- [Azure Portal](https://portal.azure.com)
- [Twitch Token Generator](https://twitchtokengenerator.com)

---

**Need help?** Check the full README or deployment guide!

# OmniAsylum API

Backend server for the OmniAsylum stream counter with real-time synchronization and Twitch integration.

## 🚀 Features

- **REST API** - Full HTTP API for counter management
- **WebSocket Support** - Real-time bidirectional sync via Socket.io
- **Persistent Storage** - JSON file-based data storage
- **Twitch Integration** - Ready for chat commands, channel points, and more
- **CORS Enabled** - Connect from any frontend
- **Lightweight** - Simple Node.js/Express setup

## 📋 Prerequisites

- Node.js 18+ installed
- npm or yarn package manager

## 🛠️ Installation

1. **Navigate to the API folder**
   ```powershell
   cd API
   ```

2. **Install dependencies**
   ```powershell
   npm install
   ```

3. **Configure environment variables**
   ```powershell
   cp .env.example .env
   ```
   
   Edit `.env` and add your configuration:
   - Set `PORT` (default: 3000)
   - Add Twitch credentials if you want Twitch integration

4. **Start the server**
   ```powershell
   npm start
   ```
   
   Or for development with auto-reload:
   ```powershell
   npm run dev
   ```

## 🔌 API Endpoints

### Counter Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/counters` | Get current counter state |
| POST | `/api/counters/deaths/increment` | Increment death counter |
| POST | `/api/counters/deaths/decrement` | Decrement death counter |
| POST | `/api/counters/swears/increment` | Increment swear counter |
| POST | `/api/counters/swears/decrement` | Decrement swear counter |
| POST | `/api/counters/reset` | Reset all counters |
| GET | `/api/counters/export` | Export counter data |

### System

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/health` | Health check endpoint |
| GET | `/api/twitch/status` | Twitch integration status |

### Example Usage

**Get current counters:**
```bash
curl http://localhost:3000/api/counters
```

**Increment deaths:**
```bash
curl -X POST http://localhost:3000/api/counters/deaths/increment
```

**Reset all:**
```bash
curl -X POST http://localhost:3000/api/counters/reset
```

## 🔌 WebSocket Events

### Client → Server Events

- `incrementDeaths` - Increment death counter
- `decrementDeaths` - Decrement death counter
- `incrementSwears` - Increment swear counter
- `decrementSwears` - Decrement swear counter
- `resetCounters` - Reset all counters

### Server → Client Events

- `counterUpdate` - Broadcast when counters change
  ```json
  {
    "deaths": 5,
    "swears": 12,
    "lastUpdated": "2025-11-01T12:34:56.789Z"
  }
  ```

## 🎮 Twitch Integration Setup

1. **Create a Twitch Application**
   - Go to https://dev.twitch.tv/console/apps
   - Click "Register Your Application"
   - Set a name (e.g., "OmniAsylum Counter")
   - Set OAuth Redirect URL: `http://localhost:3000`
   - Category: Application Integration
   - Save and copy your **Client ID** and **Client Secret**

2. **Generate OAuth Token**
   - Visit https://twitchtokengenerator.com/
   - Select needed scopes (chat:read, chat:edit, clips:edit, etc.)
   - Generate and copy the OAuth token

3. **Update .env file**
   ```env
   TWITCH_CLIENT_ID=your_client_id_here
   TWITCH_CLIENT_SECRET=your_client_secret_here
   TWITCH_CHANNEL_NAME=your_channel_name
   TWITCH_OAUTH_TOKEN=oauth:your_token_here
   ```

4. **Restart the server**

### Twitch Chat Commands

Once configured, the following chat commands are available:

**Public Commands (Anyone can use):**
- `!deaths` - Display current death count
- `!swears` - Display current swear count
- `!stats` - Display overall statistics

**Mod-Only Commands (Broadcaster & Moderators):**
- `!death+` or `!d+` - Increment death counter
- `!death-` or `!d-` - Decrement death counter
- `!swear+` or `!s+` - Increment swear counter
- `!swear-` or `!s-` - Decrement swear counter
- `!resetcounters` - Reset all counters

*Note: Unauthorized users attempting mod-only commands will be silently ignored.*

### Additional Twitch Features

The `twitchService.js` module provides a foundation for:

- **Channel Point Redemptions** (trigger counters)
- **Auto-Clip Creation** (on milestones)
- **Stream Status Monitoring**
- **Viewer/Sub Count Display**

## 📁 Project Structure

```
API/
├── server.js           # Main Express server + Socket.io
├── dataStore.js        # JSON file storage manager
├── twitchService.js    # Twitch API integration
├── package.json        # Dependencies
├── .env.example        # Environment template
├── .gitignore          # Git ignore rules
├── data/               # Auto-created data directory
│   └── counters.json   # Counter data (auto-generated)
└── README.md           # This file
```

## 🔧 Development

**Start with auto-reload:**
```powershell
npm run dev
```

**View logs:**
The server logs all connections, API calls, and errors to console.

## 🐛 Troubleshooting

**Port already in use:**
Change `PORT` in `.env` file to a different port (e.g., 3001)

**CORS errors:**
Add your frontend URL to `CORS_ORIGIN` in `.env`:
```env
CORS_ORIGIN=http://localhost:5500,http://127.0.0.1:5500
```

**Twitch not connecting:**
- Verify credentials in `.env`
- Check OAuth token hasn't expired
- Ensure channel name is correct (lowercase)

## 📝 License

MIT - See LICENSE file in project root

## 🤝 Contributing

This is part of the OmniAsylum project. See main README for contribution guidelines.

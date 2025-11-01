# GitHub Copilot Instructions - OmniAsylum Stream Counter

## Project Overview

This is a **multi-tenant Twitch stream counter application** with a Node.js backend API deployed to Azure. The system supports multiple streamers, each with their own Twitch bot integration and feature flags managed by an admin user.

## Architecture

- **Frontend**: Vanilla HTML/CSS/JavaScript with Socket.io client
- **Backend**: Node.js + Express + Socket.io server (API folder)
- **Database**: Dual-mode - Azure Table Storage (production) or local JSON files (development)
- **Authentication**: Twitch OAuth 2.0 with JWT tokens
- **Deployment**: Azure Container Apps with Bicep infrastructure as code
- **Real-time**: WebSocket rooms per user for device synchronization

## Code Style & Conventions

### JavaScript
- Use **single quotes** for strings
- Use **async/await** instead of promises when possible
- Always use **const** and **let**, never **var**
- Add proper error handling with try/catch blocks
- Log important events with emojis (✅ ❌ 🔄 💀 🤬)
- Use descriptive variable names (e.g., `twitchUserId` not `id`)

### File Organization
```
API/
├── server.js                    # Main application entry point
├── database.js                  # Storage abstraction layer
├── keyVault.js                  # Azure Key Vault integration
├── authRoutes.js                # OAuth endpoints
├── authMiddleware.js            # JWT verification
├── counterRoutes.js             # Counter API
├── adminRoutes.js               # Admin-only endpoints
└── multiTenantTwitchService.js  # Per-user Twitch bots
```

### API Routes Pattern
- Authentication routes: `/auth/*`
- Counter routes: `/api/counters/*` (requires JWT)
- Admin routes: `/api/admin/*` (requires JWT + admin role)
- Health check: `/api/health` (public)

## Key Technical Decisions

### Multi-Tenancy
- Each user identified by `twitchUserId`
- Data partitioned by user in database
- WebSocket rooms: `user:${userId}` for isolated broadcasts
- Individual Twitch bot instance per streamer

### Security
- **Twitch OAuth**: Users login with Twitch account
- **JWT tokens**: 30-day expiration, stored in httpOnly cookies
- **Azure Key Vault**: All secrets stored securely (Twitch client ID/secret, JWT secret)
- **Managed Identity**: No connection strings in code
- **CORS**: Restricted to specific frontend origins

### Role-Based Access Control
- **Admin role**: Twitch user `riress` only
- **Streamer role**: All other users (default)
- Admin can manage all users via `/api/admin/*` endpoints
- Feature flags per user (chatCommands, channelPoints, autoClip, etc.)

### Database Schema

#### Users Table
```javascript
{
  twitchUserId: string,           // Partition key
  username: string,               // Twitch login name
  displayName: string,
  email: string,
  profileImageUrl: string,
  accessToken: string,            // Encrypted
  refreshToken: string,           // Encrypted
  tokenExpiry: ISO date,
  role: 'admin' | 'streamer',     // Auto-assign admin to 'riress'
  features: JSON string,          // Feature flags object
  isActive: boolean,              // Enable/disable account
  createdAt: ISO date,
  lastLogin: ISO date
}
```

#### Counters Table
```javascript
{
  userId: string,                 // Partition key (twitchUserId)
  counterId: string,              // Row key
  deaths: number,
  swears: number,
  lastUpdated: ISO date
}
```

### Feature Flags System
```javascript
{
  chatCommands: true,      // Default enabled
  channelPoints: false,    // Channel points redemptions
  autoClip: false,         // Auto-clip on milestones
  customCommands: false,   // Custom chat commands
  analytics: false,        // Analytics dashboard
  webhooks: false          // External webhooks
}
```

## Twitch Integration

### OAuth Scopes Required
- `user:read:email` - Read user profile
- `chat:read` - Read chat messages
- `chat:edit` - Send chat messages
- `channel:read:subscriptions` - (Future) Read subscription data

### Chat Commands

**Public Commands:**
- `!deaths` - Show death count
- `!swears` - Show swear count
- `!stats` - Show all stats

**Mod-Only Commands (Broadcaster + Mods):**
- `!death+` / `!d+` - Increment deaths
- `!death-` / `!d-` - Decrement deaths
- `!swear+` / `!s+` - Increment swears
- `!swear-` / `!s-` - Decrement swears
- `!resetcounters` - Reset all counters

### Permission Checking
```javascript
// Use Twitch's userInfo flags
const hasPermission = (userInfo) => {
  return userInfo.isBroadcaster || userInfo.isMod;
};
```

## Azure Deployment

### Environment Variables

**Required:**
- `TWITCH_CLIENT_ID` - From Key Vault
- `TWITCH_CLIENT_SECRET` - From Key Vault
- `JWT_SECRET` - From Key Vault
- `TWITCH_REDIRECT_URI` - OAuth callback URL
- `CORS_ORIGIN` - Frontend URL

**Optional:**
- `DB_MODE` - "azure" or "local" (default: local)
- `AZURE_STORAGE_ACCOUNT` - For Table Storage
- `AZURE_KEY_VAULT_NAME` - For secrets
- `PORT` - Server port (default: 3000)

### Bicep Infrastructure
- **Container Apps**: Auto-scale 0-10 instances
- **Key Vault**: RBAC-based access
- **Table Storage**: Multi-tenant data
- **Application Insights**: Monitoring
- **Managed Identity**: Passwordless authentication

## Common Patterns

### Authentication Middleware
```javascript
// Require JWT authentication
app.use('/api/counters', requireAuth, counterRoutes);

// Require admin role
app.use('/api/admin', requireAuth, requireAdmin, adminRoutes);
```

### WebSocket Room Broadcast
```javascript
// Emit to specific user's devices only
io.to(`user:${userId}`).emit('counterUpdate', data);
```

### Feature Flag Check
```javascript
// Check if user has feature enabled
const hasFeature = await database.hasFeature(userId, 'chatCommands');
if (hasFeature) {
  // Enable feature
}
```

### Error Handling
```javascript
try {
  // Operation
  console.log('✅ Success message');
} catch (error) {
  console.error('❌ Error context:', error);
  res.status(500).json({ error: 'User-friendly message' });
}
```

## Testing Guidelines

### Local Development
1. Copy `.env.example` to `.env`
2. Add Twitch credentials
3. Set `DB_MODE=local`
4. Run `npm install && npm start`
5. Test OAuth at `http://localhost:3000/auth/twitch`

### Admin Testing
1. Login as Twitch user `riress`
2. Verify JWT contains `role: 'admin'`
3. Test admin endpoints work
4. Login as different user, verify admin endpoints return 403

### Multi-Tenant Testing
1. Login as User A
2. Modify counters
3. Login as User B in different browser
4. Verify User B sees their own data, not User A's

## Important Notes

- ⚠️ **Never commit .env files** - Use .env.example template
- ⚠️ **Always use user-scoped queries** - Prevent data leaks between tenants
- ⚠️ **Validate admin role server-side** - Never trust client-side checks
- ⚠️ **Refresh Twitch tokens** - Access tokens expire, implement refresh flow
- ⚠️ **Use WebSocket rooms** - Don't broadcast to all users
- ⚠️ **Encrypt sensitive data** - Access/refresh tokens should be encrypted in database
- ⚠️ **Test feature flags** - Ensure disabled features cannot be accessed

## Future Features (Not Yet Implemented)

- Channel points redemption handling
- Auto-clip on counter milestones
- Custom command builder
- Analytics dashboard
- Webhook integration for external services
- StreamElements/StreamLabs integration
- Multi-language support
- Counter themes/customization

## When Writing New Code

1. **Check user context** - Always verify `req.user` exists and matches data owner
2. **Use middleware** - Don't duplicate auth/role checks
3. **Log important events** - Help with debugging in production
4. **Handle Twitch token expiry** - Implement automatic refresh
5. **Validate inputs** - Sanitize all user inputs
6. **Return consistent errors** - Use standard error format
7. **Update documentation** - Keep README.md in sync with changes
8. **Test multi-tenant isolation** - Ensure users can't access others' data

## Admin User Reference

- **Username**: `riress`
- **Role**: `admin` (auto-assigned on login)
- **Capabilities**: Full user management, feature flag control, system statistics
- **Cannot**: Delete own admin account

## Questions to Ask Before Implementation

1. Does this need to be multi-tenant aware?
2. Should this feature be behind a feature flag?
3. Do I need to check user permissions?
4. Will this work in both local and Azure modes?
5. Is sensitive data being logged?
6. Are Twitch tokens being refreshed?
7. Is this accessible via WebSocket and/or REST?
8. Does the admin need visibility into this?

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

This application is deployed using Azure Container Apps with a complete infrastructure-as-code approach using Bicep templates.

### Production Environment

**Current Deployment:**
- **Resource Group**: `Streamer-Tools-RG`
- **Container App**: `omniforgestream-api-prod`
- **Container Registry**: `omniforgeacr.azurecr.io`
- **Application URL**: `https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io`
- **Region**: South Central US

### Prerequisites

1. **Azure CLI** installed and authenticated (`az login`)
2. **Docker** installed for container builds
3. **Azure Container Registry** access configured
4. **Twitch Developer App** with OAuth credentials
5. **Admin access** to Azure subscription

### Deployment Process

**⚠️ MANDATORY: Use VS Code tasks for ALL deployments. NEVER use manual commands.**

#### **Task-Based Deployment Strategy**

Choose the appropriate VS Code task based on the type of changes:

#### **1. Frontend AND Backend Changes**
**Task:** `Full Deploy: Build, Docker & Azure`

**When to use:**
- Modified React components in `modern-frontend/`
- Changed CSS/styling files
- Updated frontend JavaScript/JSX
- Modified both frontend AND backend files
- Any changes that affect the user interface

**What it does:**
1. Builds the React frontend (`npm run build`)
2. Copies built files to `API/frontend/`
3. Builds and pushes Docker image
4. Deploys to Azure Container Apps

```powershell
# Use VS Code Command Palette or run via task
run_task(workspaceFolder, "Full Deploy: Build, Docker & Azure")
```

#### **2. Backend-Only Changes**
**Task:** `API Deploy: Docker & Azure (Skip Frontend)`

**When to use:**
- Modified Node.js server files in `API/`
- Updated API routes, middleware, or services
- Changed database logic or authentication
- Modified environment configuration
- NO frontend changes whatsoever

**What it does:**
1. Builds and pushes Docker image (uses existing frontend)
2. Deploys to Azure Container Apps
3. Skips frontend build (faster deployment)

```powershell
# Use VS Code Command Palette or run via task
run_task(workspaceFolder, "API Deploy: Docker & Azure (Skip Frontend)")
```

#### **⚠️ CRITICAL: Task Selection Rules**

- **If ANY frontend files changed** → Use `Full Deploy`
- **If ONLY backend files changed** → Use `API Deploy (Skip Frontend)`
- **When in doubt** → Use `Full Deploy` (safer but slower)
- **NEVER mix manual commands with tasks**

#### **3. Deployment Verification (Required After Every Task)**

**⚠️ ALWAYS verify task completion using the established monitoring protocol:**

```javascript
// 1. Check task output for completion
get_task_output(workspaceFolder, taskId)
terminal_last_command() // Verify actual execution

// 2. Look for success indicators:
"provisioningState": "Succeeded"
"runningStatus": "Running"
"latestRevisionName": "...-MMDDHHM" // New timestamp

// 3. Test application health
curl -s "https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io/api/health"
// Expected: {"status":"ok","timestamp":"..."}
```

**Manual monitoring commands (only if tasks fail):**
```powershell
# Check deployment status
az containerapp show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --query "properties.provisioningState"

# View application logs
az containerapp logs show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --tail 50

# Monitor Twitch bot connections
curl -s "https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io/api/twitch/status"
```

### Environment Variables

**Required in Azure:**
- `TWITCH_CLIENT_ID` - From Azure Key Vault
- `TWITCH_CLIENT_SECRET` - From Azure Key Vault
- `JWT_SECRET` - From Azure Key Vault
- `TWITCH_REDIRECT_URI` - `https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io/auth/twitch/callback`
- `FRONTEND_URL` - `https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io`
- `CORS_ORIGIN` - Same as FRONTEND_URL

**Production Configuration:**
- `NODE_ENV` - `production`
- `DB_MODE` - `azure`
- `AZURE_STORAGE_ACCOUNT` - Managed via Managed Identity
- `AZURE_KEY_VAULT_NAME` - Managed via Managed Identity
- `PORT` - `3000`
- `APPLICATIONINSIGHTS_CONNECTION_STRING` - For monitoring

### Azure Infrastructure

#### Container Apps Configuration
```yaml
Resources:
  CPU: 0.25 cores
  Memory: 0.5 GB
  Ephemeral Storage: 1 GB

Scaling:
  Min Replicas: 0 (scale to zero when idle)
  Max Replicas: 5
  Polling Interval: 30 seconds
  Cooldown Period: 300 seconds

Rules:
  - HTTP requests (concurrent)
  - WebSocket connections (TCP)
```

#### Container Registry
- **Registry**: `omniforgeacr.azurecr.io`
- **Authentication**: Managed Identity (no passwords)
- **Image**: `omniforgestream-api:latest`

#### Key Vault Integration
- **RBAC Access**: Via User Assigned Managed Identity
- **Secrets**: Twitch credentials, JWT secret
- **No connection strings**: Passwordless authentication

#### Storage & Database
- **Azure Table Storage**: Multi-tenant data partitioning
- **Tables**: `users`, `counters`
- **Access**: Via Managed Identity

#### Monitoring
- **Application Insights**: Performance and error tracking
- **Container Logs**: Real-time via Azure CLI
- **Health Checks**: `/api/health` endpoint

#### **CRITICAL: Deployment Verification Requirements**

**⚠️ MANDATORY: After EVERY Azure deployment, verify these specific indicators:**

```powershell
# 1. Check deployment JSON response for:
"provisioningState": "Succeeded"     # MUST be "Succeeded"
"runningStatus": "Running"           # MUST be "Running"
"latestRevisionName": "...-MMDDHHM"  # NEW revision with current timestamp

# 2. Verify health endpoint responds:
curl -s "https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io/api/health"
# Expected response: {"status":"ok","timestamp":"..."}

# 3. Check for Docker build completion markers:
# Look for: "Successfully tagged omniforgeacr.azurecr.io/omniforgestream-api:latest"
# Look for: "Successfully pushed omniforgeacr.azurecr.io/omniforgestream-api:latest"
```

**🚨 NEVER proceed without confirming ALL three verification steps pass!**

### Troubleshooting Deployment

#### Common Issues

1. **Container Won't Start**
   ```powershell
   # Check logs for errors
   az containerapp logs show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --tail 100

   # Restart container
   az containerapp revision restart --name omniforgestream-api-prod --resource-group Streamer-Tools-RG
   ```

2. **Twitch Bots Not Connecting**
   ```powershell
   # Verify Key Vault access
   curl -s "https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io/api/health"

   # Check bot status
   curl -s "https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io/api/twitch/status"
   ```

3. **Authentication Issues**
   ```powershell
   # Test Twitch OAuth flow
   Start-Process "https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io/auth/twitch"
   ```

#### Rollback Process

```powershell
# List recent revisions
az containerapp revision list --name omniforgestream-api-prod --resource-group Streamer-Tools-RG

# Activate previous revision
az containerapp revision activate --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --revision [REVISION-NAME]
```

### VS Code Tasks Integration

The workspace includes pre-configured tasks for deployment:

- **Build Docker Image**: `Ctrl+Shift+P` → `Tasks: Run Task` → `Build Docker Image`
- **Deploy to Azure**: `Ctrl+Shift+P` → `Tasks: Run Task` → `Deploy to Azure`
- **View Azure Logs**: `Ctrl+Shift+P` → `Tasks: Run Task` → `View Azure Logs`

### Security Considerations

- **No secrets in code**: All credentials via Key Vault
- **Managed Identity**: No connection strings or passwords
- **CORS**: Restricted to specific origins
- **HTTPS only**: All communication encrypted
- **JWT tokens**: HTTP-only cookies, 30-day expiration
- **Role-based access**: Admin vs streamer permissions

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

## 🔍 **CRITICAL: Task Completion Monitoring**

**⚠️ MANDATORY: Always verify task completion before proceeding. NEVER assume tasks succeeded based on "no problems" messages.**

### Task Verification Protocol

#### 1. **For VS Code Tasks** (run_task tool):
```javascript
// ALWAYS follow this pattern:
1. Execute: run_task(workspaceFolder, taskId)
2. Wait for completion
3. Verify: get_task_output(workspaceFolder, taskId)
4. Check terminal_last_command() for actual output
5. Look for completion indicators
```

#### 2. **Required Completion Indicators**:
- **Docker Build**: Look for `Successfully tagged` and `Successfully pushed`
- **Azure Deploy**: Look for `"provisioningState": "Succeeded"` and `"runningStatus": "Running"`
- **Frontend Build**: Look for `Built successfully` or `dist/` folder creation
- **Terminal Commands**: Look for exit codes and actual output, not just "succeeded"

#### 3. **Deployment Verification Steps**:
```powershell
# ALWAYS verify these after deployment:
1. Check provisioningState: "Succeeded"
2. Check runningStatus: "Running"
3. Verify new revision created (latestRevisionName)
4. Test health endpoint: curl /api/health
5. Check application logs if needed
```

#### 4. **Terminal Output Analysis**:
```javascript
// Look for these specific patterns:
- "The terminal will be reused by tasks" = Task completed
- JSON response with provisioningState = Deployment status
- Exit Code: 0 = Success, non-zero = Failure
- Error messages in stderr output
```

#### 5. **Never Skip Verification**:
- ❌ **WRONG**: "The task succeeded with no problems" → Assume success
- ✅ **CORRECT**: Check actual terminal output, verify JSON responses, test endpoints

#### 6. **Failure Detection**:
```javascript
// Always check for these failure indicators:
- Exit codes !== 0
- Error messages in output
- "provisioningState": "Failed"
- Network timeouts or connection errors
- Missing expected success messages
```

### **ENFORCEMENT RULES**:
1. **NEVER** proceed without verifying task completion
2. **ALWAYS** use `get_task_output()` and `terminal_last_command()`
3. **ALWAYS** look for specific success/failure indicators
4. **ALWAYS** verify deployment health after Azure updates
5. If verification fails, **STOP** and troubleshoot before continuing

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

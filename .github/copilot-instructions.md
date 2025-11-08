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
   - The deployment script automatically authenticates with `omniforgeacr.azurecr.io`
   - Requires proper Azure permissions for the container registry
   - Authentication is verified before each Docker build/push operation
4. **Twitch Developer App** with OAuth credentials
5. **Admin access** to Azure subscription

### Deployment Process

**⚠️ MANDATORY: Use ONLY the Enhanced deployment scripts. NEVER use individual docker/az commands.**

#### **Enhanced Automated Deployment Tasks**

The project uses enhanced PowerShell deployment scripts that handle the complete deployment pipeline with advanced cleanup and verification. **Always use `run_task()` tool with one of these two tasks:**

#### **1. Complete Deployment (Frontend AND Backend Changes)**
**Task:** `"Enhanced Full Deploy: Clean Build & Deploy"`

**When to use:**
- Modified ANY frontend files in `modern-frontend/`
- Changed React components, CSS, or JavaScript
- Updated both frontend AND backend files
- Any UI-related changes
- When uncertain about what changed (safer option)

**Enhanced automated process:**
1. ✅ Cleans previous build artifacts
2. ✅ Builds React frontend (`npm run build`)
3. ✅ Copies built files to `API/frontend/` with cleanup
4. ✅ Verifies Azure Container Registry authentication
5. ✅ Builds Docker image with new frontend
6. ✅ Pushes to Azure Container Registry
7. ✅ Deploys to Azure Container Apps
8. ✅ Performs comprehensive verification

**Usage:**
```javascript
run_task("c:\\Game Data\\Coding Projects\\doc-omni", "Enhanced Full Deploy: Clean Build & Deploy")
```

#### **2. Backend-Only Changes (Skip Frontend Build)**
**Task:** `"Backend Only Deploy: Skip Frontend"`

**When to use:**
- Modified ONLY Node.js server files in `API/`
- Updated API routes, middleware, database logic
- Changed authentication or configuration
- NO frontend changes whatsoever

**Enhanced automated process:**
1. ✅ Skips frontend build for faster deployment
2. ✅ Verifies Azure Container Registry authentication
3. ✅ Builds Docker image (reuses existing frontend)
4. ✅ Pushes to Azure Container Registry
5. ✅ Deploys to Azure Container Apps
6. ✅ Performs comprehensive verification

**Usage:**
```javascript
run_task("c:\\Game Data\\Coding Projects\\doc-omni", "Backend Only Deploy: Skip Frontend")
```

#### **⚠️ CRITICAL: Task Selection Rules**

- **ANY frontend changes** → Use `"Enhanced Full Deploy: Clean Build & Deploy"`
- **ONLY backend changes** → Use `"Backend Only Deploy: Skip Frontend"`
- **When uncertain** → Use `"Enhanced Full Deploy"` (safer option)
- **NEVER use manual docker/az commands** → Always use enhanced deployment tasks
- **NEVER use old deployment tasks** → Only use the two Enhanced tasks above

#### **3. Mandatory Deployment Verification**

**⚠️ ALWAYS verify task completion - NEVER assume success without checking:**

```javascript
// Step 1: Check task completion
terminal_last_command() // Verify task finished with exit code 0

// Step 2: Look for success indicators in task output:
"provisioningState": "Succeeded"      // MUST see this
"runningStatus": "Running"            // MUST see this
"latestRevisionName": "...-MMDDHHM"   // NEW timestamp (not old)

// Step 3: Verify application health
run_in_terminal('curl -s "https://stream-tool.cerillia.com/api/health"')
// Expected: {"status":"ok","timestamp":"...","uptime":...}
```

**✅ Success Indicators:**
- Task exits with code 0
- Azure deployment shows "Succeeded" and "Running"
- Health endpoint returns 200 with valid JSON
- New revision timestamp reflects current deployment

**❌ Failure Indicators:**
- Task exits with non-zero code
- "provisioningState": "Failed"
- Health endpoint returns error or timeout
- Revision timestamp unchanged

**Troubleshooting (only if tasks fail):**
```javascript
// Check Azure status manually
run_in_terminal('az containerapp show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --query "properties.[provisioningState,runningStatus]"')

// View application logs
run_in_terminal('az containerapp logs show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --tail 50')
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

**⚠️ MANDATORY: Use ONLY the Enhanced deployment tasks with built-in health verification:**

```javascript
// ALWAYS use these tasks - they include automatic health checks:
run_task("c:\\Game Data\\Coding Projects\\doc-omni", "Enhanced Full Deploy: Clean Build & Deploy")
run_task("c:\\Game Data\\Coding Projects\\doc-omni", "Backend Only Deploy: Skip Frontend")

// WAIT 60 seconds, then verify task completion:
get_task_output("c:\\Game Data\\Coding Projects\\doc-omni", "Enhanced Full Deploy: Clean Build & Deploy")
```

**Built-in verification includes:**
- ✅ Container build and push to ACR
- ✅ Azure Container Apps deployment
- ✅ **Automatic health endpoint verification**
- ✅ Application response validation

**🚨 NEVER run separate health checks - the task handles all verification internally!**

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

### Automated Task Pipeline

The workspace includes pre-configured enhanced deployment tasks that handle the complete process:

- **Enhanced Full Deploy Task**: Builds frontend + backend with cleanup, creates Docker image, deploys to Azure
- **Backend Only Deploy Task**: Backend-only deployment with verification, skips frontend build for faster updates
- **Health Monitoring**: Automatic verification of deployment success
- **Error Recovery**: Built-in rollback capabilities and troubleshooting commands

**Access via VS Code**: `Ctrl+Shift+P` → `Tasks: Run Task` → Select appropriate enhanced deployment task

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

## 🔍 **CRITICAL: Task Completion Verification**

**⚠️ MANDATORY: Always verify deployment task completion. Tasks handle complex multi-step processes with built-in health checks.**

### Deployment Task Verification Protocol

#### 1. **Execute Deployment Task**:
```javascript
// Choose appropriate task based on changes
run_task("c:\\Game Data\\Coding Projects\\doc-omni", "Enhanced Full Deploy: Clean Build & Deploy")
// OR
run_task("c:\\Game Data\\Coding Projects\\doc-omni", "Backend Only Deploy: Skip Frontend")
```

#### 2. **MANDATORY 60-Second Wait Rule**:
**⚠️ CRITICAL: ALWAYS wait 60 seconds before checking task completion**
- Deployment tasks include built-in health checks that take time to complete
- The task output may show "succeeded" before all verification steps finish
- Wait a full 60 seconds after task initiation before verification

#### 3. **Verify Task Completion**:
```javascript
// FIRST: Wait 60 seconds after task starts
// THEN: Check the task completed successfully
get_task_output("c:\\Game Data\\Coding Projects\\doc-omni", "Enhanced Full Deploy: Clean Build & Deploy")
terminal_last_command() // Must show exit code 0
```

#### 4. **Built-in Health Check Verification**:
The enhanced deployment script automatically performs these checks:
- ✅ Frontend build completion (if applicable)
- ✅ Docker image build and push to ACR
- ✅ Azure Container Apps deployment
- ✅ **Automatic health endpoint verification**
- ✅ Application response validation

**🚨 DO NOT run separate health checks - monitor the task output for built-in verification**

#### 5. **Success Indicators in Task Output**:
Look for these specific patterns in the deployment task output:
```
[STEP] Step 1: Building React Frontend...          # Frontend build (if full deploy)
[STEP] Step 2: Cleaning old assets...              # Asset management
[STEP] Step 3: Building and pushing Docker image... # Container build
[STEP] Step 4: Deploying to Azure Container Apps... # Azure deployment
[OK] Application is healthy and responding          # CRITICAL: Health check passed
SUMMARY:                                           # Final summary
  [OK] Frontend built and assets cleaned          # (if applicable)
  [OK] Docker image updated                       # Container success
  [OK] Azure deployment successful               # Deployment success
TOTAL TIME: XX:XX                                 # Completion time
```

#### 6. **Deployment Completion Criteria**:
- ✅ Task exits with code 0 (no errors)
- ✅ "Application is healthy and responding" message appears
- ✅ All summary items show "[OK]" status
- ✅ "TOTAL TIME" indicates completion
- ✅ No error messages in output

#### 7. **Never Skip the 60-Second Wait**:
- ❌ **WRONG**: Check immediately after task starts
- ❌ **WRONG**: Run separate health checks outside the task
- ✅ **CORRECT**: Wait 60 seconds, then check task output for built-in verification
- ✅ **CORRECT**: Trust the task's internal health check results

#### 8. **Failure Detection**:
```javascript
// Always check for these failure indicators:
- Exit codes !== 0
- Missing "Application is healthy and responding" message
- "[ERROR]" messages in task output
- "Deployment failed:" error messages
- Task hanging without completion
```

### **ENFORCEMENT RULES**:
1. **ALWAYS** wait 60 seconds before checking deployment completion
2. **NEVER** run separate health checks - use the task's built-in verification
3. **ALWAYS** look for "Application is healthy and responding" in task output
4. **ALWAYS** verify all "[OK]" indicators in the summary
5. **NEVER** assume success without seeing the complete task output
6. If verification fails, **STOP** and troubleshoot before continuing

### **Deployment Monitoring Workflow**:
1. Execute deployment task
2. **Wait exactly 60 seconds**
3. Check `get_task_output()` for completion status
4. Verify built-in health check passed
5. Confirm all summary items show "[OK]"
6. Only then consider deployment complete and verified

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

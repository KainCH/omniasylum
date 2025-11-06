# Quick Deploy to Streamer-Tools-RG

## Prerequisites Check

- [ ] Azure CLI installed and logged in
- [ ] Docker available (for container image)
- [ ] Twitch app created with client ID/secret
- [ ] Domain/subdomain ready for the app
- [ ] User Assigned Managed Identity `bears-stream-UAMI` exists in resource group
- [x] Resource group `Streamer-Tools-RG` exists (✅ Already created)

## Option 1: PowerShell Script (Recommended)

```powershell
# Navigate to deploy folder
cd API/deploy

# Run the deployment script
./deploy-to-streamer-tools.ps1 `
  -ContainerImage "mcr.microsoft.com/devcontainers/javascript-node:1-18-bullseye" `
  -TwitchRedirectUri "https://YOUR-APP-URL.azurecontainerapps.io/auth/twitch/callback" `
  -FrontendUrl "https://your-frontend-domain.com"
```

## Option 2: Manual Azure CLI

```powershell
# Set variables
$RESOURCE_GROUP = "Streamer-Tools-RG"
$LOCATION = "southcentralus"
$CONTAINER_IMAGE = "mcr.microsoft.com/devcontainers/javascript-node:1-18-bullseye"
$TWITCH_REDIRECT = "https://YOUR-APP-URL.azurecontainerapps.io/auth/twitch/callback"
$FRONTEND_URL = "https://your-frontend-domain.com"

# Deploy
az deployment group create `
  --name "omniforgestream-$(Get-Date -Format 'yyyyMMdd')" `
  --resource-group $RESOURCE_GROUP `
  --template-file "main.bicep" `
  --parameters baseName="omniforgestream" `
               environment="prod" `
               containerImage=$CONTAINER_IMAGE `
               twitchRedirectUri=$TWITCH_REDIRECT `
               frontendUrl=$FRONTEND_URL
```

## Option 3: VS Code Task

1. Press `Ctrl+Shift+P`
2. Type "Tasks: Run Task"
3. Select "Deploy to Azure"
4. Use default resource group: `Streamer-Tools-RG`

## After Deployment

### 1. Add Secrets to Key Vault
```powershell
# Get Key Vault name from deployment output
$KV_NAME = "omniforgestream-kv-XXXXX"  # Replace with actual name

# Add Twitch secrets
az keyvault secret set --vault-name $KV_NAME --name "TWITCH-CLIENT-ID" --value "your-twitch-client-id"
az keyvault secret set --vault-name $KV_NAME --name "TWITCH-CLIENT-SECRET" --value "your-twitch-client-secret"
az keyvault secret set --vault-name $KV_NAME --name "JWT-SECRET" --value "$(New-Guid)"
```

### 2. Update Twitch App Settings
- Go to [Twitch Developer Console](https://dev.twitch.tv/console/apps)
- Update OAuth Redirect URL to: `https://YOUR-ACTUAL-URL.azurecontainerapps.io/auth/twitch/callback`

### 3. Test Deployment
```powershell
# Health check
curl https://YOUR-APP-URL.azurecontainerapps.io/api/health

# Test Twitch OAuth
Start-Process "https://YOUR-APP-URL.azurecontainerapps.io/auth/twitch"
```

## Cost Estimate for Streamer-Tools-RG

**Monthly costs with current configuration:**
- Container Apps: $2-15/month (scale-to-zero)
- Table Storage: $1-3/month
- Key Vault: $1/month
- Log Analytics: $1-2/month
- **Total: ~$5-21/month**

## Scaling Notes
- Starts at 0 replicas (no cost when idle)
- Auto-scales to handle approved streamers
- Max 5 replicas for growth capacity
- Each replica: 0.25 CPU, 0.5GB RAM

## Troubleshooting
- Check logs: `az containerapp logs show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --follow`
- Monitor: Use Application Insights in Azure Portal
- Debug: Set environment variable `NODE_ENV=development` temporarily

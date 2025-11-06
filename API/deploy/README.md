# Azure Deployment Scripts for OmniForgeStream API

## Prerequisites

- Azure CLI installed (`az --version`)
- Docker installed (for building container images)
- Azure Container Registry or Docker Hub account
- Appropriate Azure permissions

## Quick Deploy

### 1. Login to Azure

```powershell
az login
az account set --subscription "Your-Subscription-Name"
```

### 2. Set Variables

```powershell
$RESOURCE_GROUP = "Streamer-Tools-RG"
$LOCATION = "southcentralus"
$REGISTRY_NAME = "streamertools$(Get-Random -Maximum 9999)"  # Must be globally unique
$IMAGE_NAME = "omniforgestream-api"
$BASE_NAME = "omniforgestream"
$ENVIRONMENT = "prod"
```

### 3. Verify Resource Group (Already exists)

```powershell
# Resource group already exists at:
# /subscriptions/b8a36f4a-bde2-446f-81b5-7a48d5522724/resourceGroups/Streamer-Tools-RG
az group show --name $RESOURCE_GROUP
```

### 4. Create Container Registry

```powershell
az acr create `
  --resource-group $RESOURCE_GROUP `
  --name $REGISTRY_NAME `
  --sku Basic `
  --admin-enabled true
```

### 5. Build and Push Container Image

```powershell
# Login to ACR
az acr login --name $REGISTRY_NAME

# Build and push image
az acr build `
  --registry $REGISTRY_NAME `
  --image "${IMAGE_NAME}:latest" `
  --file Dockerfile `
  ..
```

### 6. Deploy Infrastructure

```powershell
$CONTAINER_IMAGE = "${REGISTRY_NAME}.azurecr.io/${IMAGE_NAME}:latest"
$FRONTEND_URL = "https://your-frontend-url.com"  # Update this

az deployment group create `
  --resource-group $RESOURCE_GROUP `
  --template-file deploy/main.bicep `
  --parameters baseName=$BASE_NAME `
               environment=$ENVIRONMENT `
               containerImage=$CONTAINER_IMAGE `
               twitchRedirectUri="https://[your-api-url]/auth/twitch/callback" `
               frontendUrl=$FRONTEND_URL
```

### 7. Configure Twitch Secrets in Key Vault

Get the Key Vault name from deployment output, then:

```powershell
$KEY_VAULT_NAME = "<from-deployment-output>"

# Store Twitch credentials
az keyvault secret set --vault-name $KEY_VAULT_NAME --name "TWITCH-CLIENT-ID" --value "your_client_id"
az keyvault secret set --vault-name $KEY_VAULT_NAME --name "TWITCH-CLIENT-SECRET" --value "your_client_secret"
az keyvault secret set --vault-name $KEY_VAULT_NAME --name "JWT-SECRET" --value "$(openssl rand -base64 32)"
```

### 8. Get Your API URL

```powershell
az deployment group show `
  --resource-group $RESOURCE_GROUP `
  --name main `
  --query properties.outputs.containerAppUrl.value
```

## Update Twitch App Configuration

1. Go to https://dev.twitch.tv/console/apps
2. Edit your Twitch application
3. Update OAuth Redirect URL to: `https://[your-api-url]/auth/twitch/callback`

## Continuous Deployment

### GitHub Actions (Recommended)

See `.github/workflows/azure-deploy.yml` for automated CI/CD.

### Manual Redeploy

```powershell
# Rebuild and push new image
az acr build `
  --registry $REGISTRY_NAME `
  --image "${IMAGE_NAME}:latest" `
  --file Dockerfile `
  ..

# Restart container app to pull latest image
az containerapp update `
  --name "${BASE_NAME}-api-${ENVIRONMENT}" `
  --resource-group $RESOURCE_GROUP
```

## Monitoring

### View Logs

```powershell
az containerapp logs show `
  --name "${BASE_NAME}-api-${ENVIRONMENT}" `
  --resource-group $RESOURCE_GROUP `
  --follow
```

### View Metrics

Access Application Insights in Azure Portal for detailed metrics and analytics.

## Troubleshooting

### Container won't start

Check logs:
```powershell
az containerapp logs show --name "${BASE_NAME}-api-${ENVIRONMENT}" --resource-group $RESOURCE_GROUP
```

### Can't access Key Vault

Verify managed identity has permissions:
```powershell
az role assignment list --scope /subscriptions/.../resourceGroups/$RESOURCE_GROUP/providers/Microsoft.KeyVault/vaults/$KEY_VAULT_NAME
```

### WebSocket not working

Ensure ingress transport is set to `auto` in Container App configuration.

## Cost Optimization

- Container Apps scale to zero when idle (no cost)
- Use Basic tier for ACR during development
- Consider shared Log Analytics workspace for multiple apps

## Security Best Practices

✅ All secrets in Key Vault (never in code or environment variables)
✅ Managed Identity for authentication (no credentials to manage)
✅ HTTPS only (enforced by Container Apps)
✅ RBAC for Key Vault and Storage access
✅ Soft delete enabled on Key Vault

## Clean Up

To delete all resources:

```powershell
az group delete --name $RESOURCE_GROUP --yes --no-wait
```

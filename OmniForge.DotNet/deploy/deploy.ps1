param(
    [string]$ResourceGroup = "Streamer-Tools-RG",
    [string]$AcrName = "omniforgeacr",
    [string]$ImageName = "omniforge-dotnet",
    [string]$ImageTag = "latest",
    [string]$Environment = "dev"
)

$ErrorActionPreference = "Stop"

# 1. Build Docker Image
Write-Host "🔨 Building Docker image..." -ForegroundColor Cyan
docker build -t "$AcrName.azurecr.io/$ImageName`:$ImageTag" -f ..\Dockerfile ..

# 2. Login to ACR
Write-Host "🔑 Logging into ACR..." -ForegroundColor Cyan
az acr login --name $AcrName

# 3. Push Image
Write-Host "🚀 Pushing image to ACR..." -ForegroundColor Cyan
docker push "$AcrName.azurecr.io/$ImageName`:$ImageTag"

# 4. Get Existing Resources
Write-Host "📦 Retrieving Storage Account..." -ForegroundColor Cyan
$storageAccountName = az storage account list --resource-group $ResourceGroup --query "[0].name" -o tsv
Write-Host "   Found Storage: $storageAccountName" -ForegroundColor Gray

Write-Host "🔐 Retrieving Key Vault..." -ForegroundColor Cyan
$keyVaultName = az keyvault list --resource-group $ResourceGroup --query "[0].name" -o tsv
Write-Host "   Found Key Vault: $keyVaultName" -ForegroundColor Gray

# 5. Deploy Bicep
Write-Host "☁️ Deploying to Azure Container Apps..." -ForegroundColor Cyan
$deploymentName = "deploy-$Environment-$(Get-Date -Format 'yyyyMMdd-HHmm')"

$deployment = az deployment group create `
    --resource-group $ResourceGroup `
    --name $deploymentName `
    --template-file main.bicep `
    --parameters `
        environment=$Environment `
        storageAccountName=$storageAccountName `
        keyVaultName=$keyVaultName `
        containerImage="$AcrName.azurecr.io/$ImageName`:$ImageTag" `
        frontendUrl="https://omniforgestream-api-$Environment.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io" `
    --output json | ConvertFrom-Json

$url = $deployment.properties.outputs.containerAppUrl.value
Write-Host "✅ Deployment Complete!" -ForegroundColor Green
Write-Host "🌍 App URL: https://$url" -ForegroundColor Yellow
Write-Host "🔗 Callback URL: https://$url/auth/twitch/callback" -ForegroundColor Yellow
Write-Host "⚠️  Update this Callback URL in your Twitch Developer Console!" -ForegroundColor Magenta

param(
    [string]$ResourceGroup = "Streamer-Tools-RG",
    [string]$AcrName = "omniforgeacr",
    [string]$ImageTag = "$(Get-Date -Format 'yyyyMMdd-HHmmss')",
    [ValidateSet("dev", "prod")]
    [string]$Environment = "dev",
    [switch]$FullDeploy  # Use this flag to run full Bicep deployment (creates/updates infrastructure)
)

$ErrorActionPreference = "Stop"

# Environment-specific configuration
$envConfig = @{
    "dev" = @{
        ContainerAppName = "omniforgestream-api-dev"
        ContainerAppFqdn = "omniforgestream-api-dev.proudmeadow-a59c8b17.southcentralus.azurecontainerapps.io"
        CustomDomain = "dev.cerillia.com"
        DisplayName = "Development"
        ImageName = "omniforge-dotnet-dev"
    }
    "prod" = @{
        ContainerAppName = "omniforgestream-api-prod"
        ContainerAppFqdn = "omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io"
        CustomDomain = "stream-tool.cerillia.com"
        DisplayName = "Production"
        ImageName = "omniforge-dotnet-prod"
    }
}

$config = $envConfig[$Environment]
$frontendUrl = "https://$($config.CustomDomain)"
$ImageName = $config.ImageName
$ContainerAppName = $config.ContainerAppName
$FullImageName = "$AcrName.azurecr.io/$ImageName`:$ImageTag"

$startTime = Get-Date
Write-Host "🕒 Deployment started at: $($startTime.ToString('yyyy-MM-dd hh:mm:ss tt'))" -ForegroundColor Cyan
Write-Host "🎯 Target Environment: $($config.DisplayName) ($Environment)" -ForegroundColor Magenta
Write-Host "🌐 Target URL: $frontendUrl" -ForegroundColor Magenta
Write-Host "🐳 Image: $FullImageName" -ForegroundColor Magenta
if ($FullDeploy) {
    Write-Host "📦 Mode: Full Infrastructure Deployment (Bicep)" -ForegroundColor Yellow
} else {
    Write-Host "📦 Mode: Image Update Only (preserves custom domains)" -ForegroundColor Green
}

# 1. Build Docker Image
Write-Host "🔨 Building Docker image..." -ForegroundColor Cyan
docker build -t $FullImageName -f ..\Dockerfile ..

# 2. Login to ACR
Write-Host "🔑 Logging into ACR..." -ForegroundColor Cyan
az acr login --name $AcrName

# 3. Push Image
Write-Host "🚀 Pushing image to ACR..." -ForegroundColor Cyan
docker push $FullImageName

if ($FullDeploy) {
    # Full Bicep deployment - use for initial setup or infrastructure changes
    Write-Host "📦 Retrieving Storage Account..." -ForegroundColor Cyan
    $storageAccountName = az storage account list --resource-group $ResourceGroup --query "[0].name" -o tsv
    Write-Host "   Found Storage: $storageAccountName" -ForegroundColor Gray

    Write-Host "🔐 Retrieving Key Vault..." -ForegroundColor Cyan
    $keyVaultName = az keyvault list --resource-group $ResourceGroup --query "[0].name" -o tsv
    Write-Host "   Found Key Vault: $keyVaultName" -ForegroundColor Gray

    Write-Host "☁️ Deploying full infrastructure to Azure..." -ForegroundColor Cyan
    $deploymentName = "deploy-$Environment-$(Get-Date -Format 'yyyyMMdd-HHmm')"

    $deployment = az deployment group create `
        --resource-group $ResourceGroup `
        --name $deploymentName `
        --template-file main.bicep `
        --parameters `
            environment=$Environment `
            storageAccountName=$storageAccountName `
            keyVaultName=$keyVaultName `
            containerImage=$FullImageName `
            frontendUrl=$frontendUrl `
        --output json | ConvertFrom-Json

    $url = $deployment.properties.outputs.containerAppUrl.value
} else {
    # Image-only update - preserves custom domains and other manual configurations
    Write-Host "☁️ Updating Container App with new image..." -ForegroundColor Cyan

    az containerapp update `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --image $FullImageName `
        --output none

    $url = $config.CustomDomain
}

Write-Host "✅ Deployment Complete!" -ForegroundColor Green
Write-Host "🎯 Environment: $($config.DisplayName)" -ForegroundColor Yellow
Write-Host "🌍 App URL: https://$url" -ForegroundColor Yellow

$endTime = Get-Date
Write-Host "🕒 Deployment finished at: $($endTime.ToString('yyyy-MM-dd hh:mm:ss tt'))" -ForegroundColor Cyan
Write-Host "🔗 Callback URL: https://$url/auth/twitch/callback" -ForegroundColor Yellow
if ($FullDeploy) {
    Write-Host "⚠️  Update this Callback URL in your Twitch Developer Console!" -ForegroundColor Magenta
}

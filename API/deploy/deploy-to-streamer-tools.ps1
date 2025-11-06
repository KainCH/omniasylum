# Deploy OmniForgeStream to Streamer-Tools-RG
# This script deploys the multi-tenant Twitch stream counter to Azure Container Apps

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerImage,

    [Parameter(Mandatory=$true)]
    [string]$TwitchRedirectUri,

    [Parameter(Mandatory=$true)]
    [string]$FrontendUrl,

    [string]$ResourceGroup = "Streamer-Tools-RG",
    [string]$Location = "southcentralus",
    [string]$BaseName = "omniforgestream",
    [string]$Environment = "prod"
)

Write-Host "🚀 Deploying OmniForgeStream to Azure..." -ForegroundColor Green
Write-Host "Resource Group: $ResourceGroup" -ForegroundColor Cyan
Write-Host "Container Image: $ContainerImage" -ForegroundColor Cyan
Write-Host "Using Managed Identity: bears-stream-UAMI" -ForegroundColor Cyan

# Check if logged into Azure
$account = az account show --query "name" -o tsv
if (-not $account) {
    Write-Host "❌ Not logged into Azure. Please run 'az login' first." -ForegroundColor Red
    exit 1
}

Write-Host "✅ Logged into Azure account: $account" -ForegroundColor Green

# Verify resource group exists
Write-Host "🔍 Verifying resource group exists..." -ForegroundColor Yellow
$rgExists = az group exists --name $ResourceGroup
if ($rgExists -eq "false") {
    Write-Host "❌ Resource group '$ResourceGroup' does not exist!" -ForegroundColor Red
    Write-Host "Expected: /subscriptions/b8a36f4a-bde2-446f-81b5-7a48d5522724/resourceGroups/Streamer-Tools-RG" -ForegroundColor Yellow
    exit 1
}
Write-Host "✅ Resource group $ResourceGroup confirmed" -ForegroundColor Green

# Validate deployment first
Write-Host "🔍 Validating deployment..." -ForegroundColor Yellow
az deployment group what-if `
    --resource-group $ResourceGroup `
    --template-file "main.bicep" `
    --parameters baseName=$BaseName `
                environment=$Environment `
                containerImage=$ContainerImage `
                twitchRedirectUri=$TwitchRedirectUri `
                frontendUrl=$FrontendUrl

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Deployment validation failed" -ForegroundColor Red
    exit 1
}

# Ask for confirmation
$confirm = Read-Host "🤔 Do you want to proceed with the deployment? (y/N)"
if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "⛔ Deployment cancelled" -ForegroundColor Yellow
    exit 0
}

# Deploy the infrastructure
Write-Host "🏗️  Deploying infrastructure..." -ForegroundColor Yellow
$deploymentName = "omniforgestream-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

az deployment group create `
    --name $deploymentName `
    --resource-group $ResourceGroup `
    --template-file "main.bicep" `
    --parameters baseName=$BaseName `
                environment=$Environment `
                containerImage=$ContainerImage `
                twitchRedirectUri=$TwitchRedirectUri `
                frontendUrl=$FrontendUrl

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Deployment completed successfully!" -ForegroundColor Green

    # Get the outputs
    $outputs = az deployment group show --name $deploymentName --resource-group $ResourceGroup --query "properties.outputs" -o json | ConvertFrom-Json

    if ($outputs.containerAppUrl) {
        $appUrl = $outputs.containerAppUrl.value
        Write-Host "🌐 Application URL: $appUrl" -ForegroundColor Cyan
        Write-Host "🔑 Key Vault: $($outputs.keyVaultName.value)" -ForegroundColor Cyan
        Write-Host "💾 Storage Account: $($outputs.storageAccountName.value)" -ForegroundColor Cyan

        Write-Host "`n📋 Next Steps:" -ForegroundColor Yellow
        Write-Host "1. Add Twitch OAuth secrets to Key Vault:" -ForegroundColor White
        Write-Host "   - TWITCH-CLIENT-ID" -ForegroundColor Gray
        Write-Host "   - TWITCH-CLIENT-SECRET" -ForegroundColor Gray
        Write-Host "   - JWT-SECRET" -ForegroundColor Gray
        Write-Host "2. Update Twitch app redirect URI to: $appUrl/auth/twitch/callback" -ForegroundColor White
        Write-Host "3. Test the deployment: $appUrl/api/health" -ForegroundColor White

        # Open Azure portal to resource group
        Write-Host "`n🎯 Opening Azure Portal..." -ForegroundColor Green
        Start-Process "https://portal.azure.com/#@/resource/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$ResourceGroup"
    }
} else {
    Write-Host "❌ Deployment failed!" -ForegroundColor Red
    exit 1
}

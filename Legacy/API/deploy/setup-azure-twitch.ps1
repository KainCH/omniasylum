# Azure Key Vault Setup Script for Twitch Credentials
# Run this AFTER deploying the Bicep template

param(
    [Parameter(Mandatory=$true)]
    [string]$TwitchClientId,

    [Parameter(Mandatory=$true)]
    [string]$TwitchClientSecret,

    [string]$ResourceGroup = "Streamer-Tools-RG",
    [string]$BaseName = "omniforgestream"
)

Write-Host "🔐 Setting up Twitch credentials in Azure Key Vault..." -ForegroundColor Green

# Use the known Key Vault name
$keyVaultName = "forge-steel-vault"

if (-not $keyVaultName) {
    Write-Host "❌ Could not find Key Vault. Make sure the Bicep deployment completed successfully." -ForegroundColor Red
    exit 1
}

Write-Host "✅ Found Key Vault: $keyVaultName" -ForegroundColor Green

# Store Twitch Client ID
Write-Host "📝 Storing Twitch Client ID..." -ForegroundColor Yellow
az keyvault secret set `
    --vault-name $keyVaultName `
    --name "TWITCH-CLIENT-ID" `
    --value $TwitchClientId

# Store Twitch Client Secret
Write-Host "📝 Storing Twitch Client Secret..." -ForegroundColor Yellow
az keyvault secret set `
    --vault-name $keyVaultName `
    --name "TWITCH-CLIENT-SECRET" `
    --value $TwitchClientSecret

Write-Host "✅ Twitch credentials stored successfully in Azure Key Vault!" -ForegroundColor Green
Write-Host ""
Write-Host "🎯 Next steps:" -ForegroundColor Cyan
Write-Host "1. Your app will automatically use these credentials" -ForegroundColor White
Write-Host "2. Update your Twitch app OAuth redirect URI to match your Azure domain" -ForegroundColor White
Write-Host "3. Test the authentication flow" -ForegroundColor White

# Verify Existing Resources in Streamer-Tools-RG
# This script checks for existing resources before deployment

param(
    [string]$ResourceGroup = "Streamer-Tools-RG"
)

Write-Host "🔍 Verifying existing resources in $ResourceGroup..." -ForegroundColor Green

# Check if logged into Azure
$account = az account show --query "name" -o tsv 2>$null
if (-not $account) {
    Write-Host "❌ Not logged into Azure. Please run 'az login' first." -ForegroundColor Red
    exit 1
}

Write-Host "✅ Logged into Azure account: $account" -ForegroundColor Green

# Verify resource group exists
Write-Host "`n📋 Checking Resource Group..." -ForegroundColor Yellow
$rgExists = az group exists --name $ResourceGroup
if ($rgExists -eq "true") {
    $rgDetails = az group show --name $ResourceGroup --query "{name:name, location:location, id:id}" -o json | ConvertFrom-Json
    Write-Host "✅ Resource Group confirmed:" -ForegroundColor Green
    Write-Host "   Name: $($rgDetails.name)" -ForegroundColor Cyan
    Write-Host "   Location: $($rgDetails.location)" -ForegroundColor Cyan
    Write-Host "   ID: $($rgDetails.id)" -ForegroundColor Cyan
} else {
    Write-Host "❌ Resource group '$ResourceGroup' does not exist!" -ForegroundColor Red
    exit 1
}

# Check for User Assigned Managed Identity
Write-Host "`n🔐 Checking for User Assigned Managed Identity..." -ForegroundColor Yellow
$uami = az identity show --name "bears-stream-UAMI" --resource-group $ResourceGroup 2>$null
if ($uami) {
    $uamiDetails = $uami | ConvertFrom-Json
    Write-Host "✅ User Assigned Managed Identity found:" -ForegroundColor Green
    Write-Host "   Name: $($uamiDetails.name)" -ForegroundColor Cyan
    Write-Host "   Principal ID: $($uamiDetails.principalId)" -ForegroundColor Cyan
    Write-Host "   Client ID: $($uamiDetails.clientId)" -ForegroundColor Cyan
} else {
    Write-Host "❌ User Assigned Managed Identity 'bears-stream-UAMI' not found!" -ForegroundColor Red
    Write-Host "   Please create it before deployment." -ForegroundColor Yellow
}

# List existing resources in the resource group
Write-Host "`n📦 Existing resources in $ResourceGroup..." -ForegroundColor Yellow
$resources = az resource list --resource-group $ResourceGroup --query "[].{name:name, type:type, location:location}" -o json | ConvertFrom-Json

if ($resources.Count -eq 0) {
    Write-Host "   No resources found (empty resource group)" -ForegroundColor Gray
} else {
    foreach ($resource in $resources) {
        Write-Host "   📄 $($resource.name) ($($resource.type))" -ForegroundColor Cyan
    }
}

Write-Host "`n🚀 Resource group verification complete!" -ForegroundColor Green
Write-Host "Ready to deploy OmniForgeStream to $ResourceGroup" -ForegroundColor Green

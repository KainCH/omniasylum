param(
    [string]$ResourceGroup = "Streamer-Tools-RG",
    [ValidateSet("dev", "prod")]
    [string]$Environment = "dev",
    [ValidateRange(0, 100)]
    [int]$MaxInactiveRevisions = 5
)

$ErrorActionPreference = "Stop"

$envConfig = @{
    "dev" = @{
        ContainerAppName = "omniforgestream-api-dev"
        DisplayName = "Development"
    }
    "prod" = @{
        ContainerAppName = "omniforgestream-api-prod"
        DisplayName = "Production"
    }
}

$config = $envConfig[$Environment]
$ContainerAppName = $config.ContainerAppName

Write-Host "🧹 Container App Revision Cleanup" -ForegroundColor Cyan
Write-Host "🎯 Environment: $($config.DisplayName) ($Environment)" -ForegroundColor Magenta
Write-Host "📦 Container App: $ContainerAppName" -ForegroundColor Magenta
Write-Host "🗂️  Resource Group: $ResourceGroup" -ForegroundColor Magenta
Write-Host "🧯 Target max inactive revisions: $MaxInactiveRevisions" -ForegroundColor Yellow

# Ensure Azure CLI auth
try {
    az account show --output none
}
catch {
    Write-Host "❌ Azure CLI is not authenticated. Run the 'Azure Login' task first." -ForegroundColor Red
    throw
}

function Get-AppState {
    $raw = az containerapp show --name $ContainerAppName --resource-group $ResourceGroup --output json | ConvertFrom-Json
    return [pscustomobject]@{
        ActiveRevisionsMode = $raw.properties.configuration.activeRevisionsMode
        MaxInactiveRevisions = $raw.properties.configuration.maxInactiveRevisions
        LatestRevisionName = $raw.properties.latestRevisionName
        ProvisioningState = $raw.properties.provisioningState
        RunningStatus = $raw.properties.runningStatus
    }
}

function Show-RevisionSummary {
    $revs = az containerapp revision list --name $ContainerAppName --resource-group $ResourceGroup --output json | ConvertFrom-Json

    $active = @($revs | Where-Object { $_.properties.active -eq $true })
    $inactive = @($revs | Where-Object { $_.properties.active -ne $true })

    Write-Host "\n📊 Revisions Summary" -ForegroundColor Cyan
    Write-Host "   Total:   $($revs.Count)" -ForegroundColor Gray
    Write-Host "   Active:  $($active.Count)" -ForegroundColor Gray
    Write-Host "   Inactive:$($inactive.Count)" -ForegroundColor Gray

    # Show the newest few revisions for quick sanity.
    $top = $revs | Sort-Object { $_.properties.createdTime } -Descending | Select-Object -First 8
    Write-Host "\n🧾 Newest revisions (top 8):" -ForegroundColor Cyan
    foreach ($r in $top) {
        $name = $r.name
        $created = $r.properties.createdTime
        $isActive = $r.properties.active
        $traffic = $r.properties.trafficWeight
        Write-Host "   - $name | created=$created | active=$isActive | trafficWeight=$traffic" -ForegroundColor DarkGray
    }
}

$before = Get-AppState
Write-Host "\nℹ️  Current container app settings" -ForegroundColor Cyan
Write-Host "   activeRevisionsMode: $($before.ActiveRevisionsMode)" -ForegroundColor Gray
Write-Host "   maxInactiveRevisions: $($before.MaxInactiveRevisions)" -ForegroundColor Gray
Write-Host "   latestRevisionName: $($before.LatestRevisionName)" -ForegroundColor Gray
Write-Host "   provisioningState: $($before.ProvisioningState)" -ForegroundColor Gray
Write-Host "   runningStatus: $($before.RunningStatus)" -ForegroundColor Gray

Show-RevisionSummary

Write-Host "\n🔧 Setting max inactive revisions..." -ForegroundColor Cyan
az containerapp update \
    --name $ContainerAppName \
    --resource-group $ResourceGroup \
    --max-inactive-revisions $MaxInactiveRevisions \
    --output none

$after = Get-AppState
Write-Host "✅ Updated maxInactiveRevisions: $($after.MaxInactiveRevisions)" -ForegroundColor Green

Show-RevisionSummary

Write-Host "\n✨ Done. Azure will prune inactive revisions beyond maxInactiveRevisions." -ForegroundColor Green

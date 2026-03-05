param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish/SyncAgent",
    [string]$StorageAccountName = "",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Split-Path -Parent $scriptDir
$projectPath = Join-Path $solutionRoot "src/OmniForge.SyncAgent"

Write-Host "Publishing OmniForge Sync Agent..." -ForegroundColor Cyan

# Resolve output dir relative to script location
$resolvedOutput = Join-Path $scriptDir $OutputDir

# Build single-file self-contained exe
dotnet publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -o $resolvedOutput

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit 1
}

$exePath = Join-Path $resolvedOutput "OmniForge.SyncAgent.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Published exe not found at $exePath"
    exit 1
}

$fileSize = (Get-Item $exePath).Length / 1MB
Write-Host "Published: $exePath ($([math]::Round($fileSize, 1)) MB)" -ForegroundColor Green

# Upload to Azure Blob Storage if account name provided
if ($StorageAccountName) {
    Write-Host "Uploading to Azure Blob Storage ($StorageAccountName)..." -ForegroundColor Cyan

    $containerName = "sync-agent"

    # Create container if it doesn't exist
    az storage container create `
        --name $containerName `
        --account-name $StorageAccountName `
        --auth-mode login `
        2>$null

    # Upload exe
    az storage blob upload `
        --container-name $containerName `
        --name "OmniForge.SyncAgent.exe" `
        --file $exePath `
        --account-name $StorageAccountName `
        --auth-mode login `
        --overwrite

    # Upload version file
    if ($Version) {
        $versionFile = Join-Path $resolvedOutput "agent-version.txt"
        $Version | Out-File -FilePath $versionFile -NoNewline -Encoding utf8

        az storage blob upload `
            --container-name $containerName `
            --name "agent-version.txt" `
            --file $versionFile `
            --account-name $StorageAccountName `
            --auth-mode login `
            --overwrite

        Write-Host "Version $Version uploaded" -ForegroundColor Green
    }

    Write-Host "Upload complete" -ForegroundColor Green
}

Write-Host "Done!" -ForegroundColor Green

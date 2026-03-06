param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish/SyncAgent",
    [string]$StorageAccountName = "omni46jismtjodyuc",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Split-Path -Parent $scriptDir
$projectPath = Join-Path $solutionRoot "src/OmniForge.SyncAgent"

# ── Version management ────────────────────────────────────────────────────────
# If -Version is not supplied, read the local version file and auto-increment
# the patch number (1.2.3 → 1.2.4). The updated version is written back so
# subsequent runs continue from the new number.
$versionFilePath = Join-Path $scriptDir "agent-version.txt"

if ([string]::IsNullOrEmpty($Version)) {
    if (Test-Path $versionFilePath) {
        $existing = (Get-Content $versionFilePath -Raw).Trim()
    } else {
        $existing = "1.0.0"
    }

    if ($existing -match '^(\d+)\.(\d+)\.(\d+)$') {
        $major = [int]$Matches[1]
        $minor = [int]$Matches[2]
        $patch = [int]$Matches[3] + 1
        $Version = "$major.$minor.$patch"
    } else {
        Write-Warning "Could not parse version '$existing' — defaulting to 1.0.0"
        $Version = "1.0.0"
    }

    Write-Host "Auto-incremented version: $Version" -ForegroundColor Cyan
} else {
    Write-Host "Using specified version: $Version" -ForegroundColor Cyan
}

# Persist the version locally before doing anything else so a failed upload
# doesn't leave the counter out of sync on the next run.
$Version | Out-File -FilePath $versionFilePath -NoNewline -Encoding utf8
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "Publishing OmniForge Sync Agent v$Version..." -ForegroundColor Cyan

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

    # Upload version file (always — version is always set by this point)
    $versionFile = Join-Path $resolvedOutput "agent-version.txt"
    $Version | Out-File -FilePath $versionFile -NoNewline -Encoding utf8

    az storage blob upload `
        --container-name $containerName `
        --name "agent-version.txt" `
        --file $versionFile `
        --account-name $StorageAccountName `
        --auth-mode login `
        --overwrite

    Write-Host "Version $Version uploaded to blob" -ForegroundColor Green
    Write-Host "Upload complete" -ForegroundColor Green
}

Write-Host "Done!" -ForegroundColor Green

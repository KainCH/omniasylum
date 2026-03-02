<#
.SYNOPSIS
    Builds and packages OmniForge SceneSync for distribution.

.DESCRIPTION
    Publishes the SceneSync console app as a self-contained single-file executable
    and creates a ready-to-distribute ZIP file and/or Windows Installer (MSI).
    The MSI is built using WiX v6 (requires `wix` dotnet tool installed globally).

.PARAMETER Runtime
    Target runtime identifier. Default: win-x64.
    Examples: win-x64, win-arm64, linux-x64, osx-x64, osx-arm64

.PARAMETER OutputDir
    Directory for the final packages. Default: ../../publish/SceneSync

.PARAMETER SkipZip
    If set, publishes but does not create the ZIP archive.

.PARAMETER SkipMsi
    If set, skips the MSI build step.

.PARAMETER MsiOnly
    If set, only builds the MSI (implies -SkipZip).

.EXAMPLE
    .\package.ps1                         # Build ZIP + MSI
    .\package.ps1 -SkipMsi               # ZIP only (no MSI)
    .\package.ps1 -MsiOnly               # MSI only (no ZIP)
    .\package.ps1 -Runtime win-arm64     # Target ARM64
#>
param(
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "../../publish/SceneSync",
    [switch]$SkipZip,
    [switch]$SkipMsi,
    [switch]$MsiOnly
)

if ($MsiOnly) { $SkipZip = $true }

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = $scriptDir
$projectFile = Join-Path $projectDir "OmniForge.SceneSync.csproj"

# Resolve output paths
$OutputDir = [System.IO.Path]::GetFullPath((Join-Path $scriptDir $OutputDir))
$publishDir = Join-Path $OutputDir "publish-$Runtime"
$zipName = "OmniForge-SceneSync-$Runtime.zip"
$zipPath = Join-Path $OutputDir $zipName

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  OmniForge SceneSync — Package Builder" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Runtime:    $Runtime"
Write-Host "  Project:    $projectFile"
Write-Host "  Publish to: $publishDir"
Write-Host "  ZIP output: $zipPath"
Write-Host ""

# Clean previous output
if (Test-Path $publishDir) {
    Write-Host "🧹 Cleaning previous publish output..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $publishDir
}

# Publish
Write-Host "🔨 Publishing SceneSync ($Runtime)..." -ForegroundColor Green
dotnet publish $projectFile `
    -c Release `
    -r $Runtime `
    --self-contained `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Publish failed!" -ForegroundColor Red
    exit 1
}

# Copy the install script and batch launcher into the publish folder
$installScript = Join-Path $scriptDir "install.ps1"
if (Test-Path $installScript) {
    Copy-Item $installScript -Destination $publishDir
    Write-Host "📄 Included install.ps1"
}

$installBat = Join-Path $scriptDir "Install-SceneSync.bat"
if (Test-Path $installBat) {
    Copy-Item $installBat -Destination $publishDir
    Write-Host "📄 Included Install-SceneSync.bat (double-click installer for Windows)"
}

# Show what was produced
Write-Host ""
Write-Host "📦 Published files:" -ForegroundColor Green
Get-ChildItem $publishDir | ForEach-Object {
    $size = if ($_.Length -gt 1MB) { "{0:N1} MB" -f ($_.Length / 1MB) }
           elseif ($_.Length -gt 1KB) { "{0:N0} KB" -f ($_.Length / 1KB) }
           else { "$($_.Length) B" }
    Write-Host "   $($_.Name)  ($size)"
}

# Create ZIP
if (-not $SkipZip) {
    Write-Host ""
    Write-Host "📦 Creating ZIP: $zipName..." -ForegroundColor Green

    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }

    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

    $zipSize = (Get-Item $zipPath).Length
    $zipSizeMB = "{0:N1} MB" -f ($zipSize / 1MB)
    Write-Host ""
    Write-Host "✅ Package ready: $zipPath ($zipSizeMB)" -ForegroundColor Green
    Write-Host ""
    Write-Host "To distribute:" -ForegroundColor Cyan
    Write-Host "  1. Send the ZIP to the target machine"
    Write-Host "  2. Extract it to any folder"
    Write-Host "  3. Run:  .\install.ps1     (sets up config and optional auto-start)"
    Write-Host "  4. Or just run:  .\OmniForge.SceneSync.exe  directly"
    Write-Host ""
}
else {
    Write-Host ""
    Write-Host "✅ Publish complete at: $publishDir" -ForegroundColor Green
    Write-Host ""
}

# ── MSI Build (Windows only, requires wix dotnet tool) ──────────────────────
if (-not $SkipMsi -and $Runtime -like "win-*") {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  Building Windows Installer (MSI)" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""

    # Check that the wix tool is available
    $wixCmd = Get-Command wix -ErrorAction SilentlyContinue
    if (-not $wixCmd) {
        Write-Host "⚠️  WiX toolset not found. Install it with:" -ForegroundColor Yellow
        Write-Host "     dotnet tool install --global wix" -ForegroundColor Yellow
        Write-Host "     wix extension add WixToolset.UI.wixext -g" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Skipping MSI build." -ForegroundColor Yellow
    }
    else {
        $wxsFile = Join-Path $scriptDir "installer\SceneSync.wxs"
        $msiName = "OmniForge-SceneSync-$Runtime.msi"
        $msiPath = Join-Path $OutputDir $msiName

        if (-not (Test-Path $wxsFile)) {
            Write-Host "❌ WiX source not found at: $wxsFile" -ForegroundColor Red
            exit 1
        }

        # Resolve the publish dir to absolute path for the WiX variable
        $absPublishDir = (Resolve-Path $publishDir).Path

        Write-Host "  WiX source:  $wxsFile"
        Write-Host "  Publish dir: $absPublishDir"
        Write-Host "  MSI output:  $msiPath"
        Write-Host ""

        Write-Host "🔨 Running wix build..." -ForegroundColor Green
        wix build $wxsFile `
            -d "PublishDir=$absPublishDir" `
            -ext WixToolset.UI.wixext `
            -o $msiPath

        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ MSI build failed!" -ForegroundColor Red
            exit 1
        }

        $msiSize = (Get-Item $msiPath).Length
        $msiSizeMB = "{0:N1} MB" -f ($msiSize / 1MB)
        Write-Host ""
        Write-Host "✅ MSI ready: $msiPath ($msiSizeMB)" -ForegroundColor Green
        Write-Host ""
        Write-Host "To install:" -ForegroundColor Cyan
        Write-Host "  1. Copy the .msi to the target machine"
        Write-Host "  2. Double-click to run the installer"
        Write-Host "  3. Edit appsettings.json in the install folder to configure"
        Write-Host "     Default location: %LOCALAPPDATA%\OmniForge\SceneSync"
        Write-Host ""
    }
}
elseif (-not $SkipMsi -and $Runtime -notlike "win-*") {
    Write-Host ""
    Write-Host "ℹ️  MSI build skipped (only available for Windows runtimes)." -ForegroundColor DarkGray
    Write-Host ""
}

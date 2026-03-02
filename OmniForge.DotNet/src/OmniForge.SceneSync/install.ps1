<#
.SYNOPSIS
    Installs OmniForge SceneSync on this machine.

.DESCRIPTION
    Interactive setup script that:
    - Copies SceneSync to a permanent install location
    - Walks you through configuring your server URL and JWT token
    - Optionally sets up OBS/Streamlabs connections
    - Optionally creates a Windows startup shortcut so it runs automatically

.PARAMETER InstallDir
    Where to install. Default: %LOCALAPPDATA%\OmniForge\SceneSync

.PARAMETER NoAutoStart
    Skip the auto-start setup prompt.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -InstallDir "C:\Tools\SceneSync"
#>
param(
    [string]$InstallDir = "",
    [switch]$NoAutoStart
)

$ErrorActionPreference = "Stop"

# ── Defaults ──────────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "OmniForge\SceneSync"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exeName = "OmniForge.SceneSync.exe"
$exeSource = Join-Path $scriptDir $exeName

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  OmniForge SceneSync — Installer" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ── Verify source ────────────────────────────────────────────────
if (-not (Test-Path $exeSource)) {
    Write-Host "❌ Could not find $exeName in the current directory." -ForegroundColor Red
    Write-Host "   Make sure you extracted the ZIP and are running install.ps1 from inside it."
    exit 1
}

# ── Copy files ───────────────────────────────────────────────────
Write-Host "📁 Install location: $InstallDir"
Write-Host ""

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# Copy all files from source to install dir
Write-Host "📦 Copying files..." -ForegroundColor Green
Get-ChildItem $scriptDir -File | Where-Object { $_.Name -ne "install.ps1" } | ForEach-Object {
    Copy-Item $_.FullName -Destination $InstallDir -Force
}
Write-Host "   ✅ Files copied"

# ── Configure appsettings.json ───────────────────────────────────
$settingsPath = Join-Path $InstallDir "appsettings.json"
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json

Write-Host ""
Write-Host "── Configuration ──" -ForegroundColor Cyan
Write-Host ""

# Server URL
$currentUrl = $settings.SceneSync.Server.BaseUrl
$serverUrl = Read-Host "Server URL [$currentUrl]"
if ([string]::IsNullOrWhiteSpace($serverUrl)) { $serverUrl = $currentUrl }
$settings.SceneSync.Server.BaseUrl = $serverUrl

# JWT Token
$currentToken = $settings.SceneSync.Server.AuthToken
$tokenDisplay = if ($currentToken -eq "YOUR_TWITCH_JWT_TOKEN_HERE" -or [string]::IsNullOrWhiteSpace($currentToken)) { "(not set)" } else { "****" + $currentToken.Substring([Math]::Max(0, $currentToken.Length - 8)) }
Write-Host "Current token: $tokenDisplay"
Write-Host "  (Get this from your browser cookies after logging in at stream-tool.cerillia.net)" -ForegroundColor DarkGray
Write-Host "  (DevTools → Application → Cookies → copy the 'token' value)" -ForegroundColor DarkGray
$token = Read-Host "Twitch JWT Token (paste full token, or press Enter to keep current)"
if (-not [string]::IsNullOrWhiteSpace($token)) {
    $settings.SceneSync.Server.AuthToken = $token
}

# OBS
Write-Host ""
$obsEnabled = Read-Host "Enable OBS Studio connection? (Y/n)"
$obsEnabled = if ($obsEnabled -match "^[Nn]") { $false } else { $true }
$settings.SceneSync.OBS.Enabled = $obsEnabled

if ($obsEnabled) {
    $obsPort = Read-Host "OBS WebSocket port [4455]"
    if (-not [string]::IsNullOrWhiteSpace($obsPort)) {
        $settings.SceneSync.OBS.Port = [int]$obsPort
    }
    $obsPass = Read-Host "OBS WebSocket password (Enter for none)"
    $settings.SceneSync.OBS.Password = if ([string]::IsNullOrWhiteSpace($obsPass)) { "" } else { $obsPass }
}

# Streamlabs
Write-Host ""
$slobsEnabled = Read-Host "Enable Streamlabs Desktop connection? (y/N)"
$slobsEnabled = if ($slobsEnabled -match "^[Yy]") { $true } else { $false }
$settings.SceneSync.Streamlabs.Enabled = $slobsEnabled

# Save
$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
Write-Host ""
Write-Host "   ✅ Configuration saved to $settingsPath" -ForegroundColor Green

# ── Auto-start (Windows Startup folder) ─────────────────────────
if (-not $NoAutoStart -and $env:OS -eq "Windows_NT") {
    Write-Host ""
    $autoStart = Read-Host "Start SceneSync automatically when you log in? (y/N)"

    if ($autoStart -match "^[Yy]") {
        $startupFolder = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs\Startup")
        $shortcutPath = Join-Path $startupFolder "OmniForge SceneSync.lnk"
        $targetExe = Join-Path $InstallDir $exeName

        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $targetExe
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Description = "OmniForge SceneSync - Scene change detection for OBS/Streamlabs"
        $shortcut.WindowStyle = 7  # Minimized
        $shortcut.Save()

        Write-Host "   ✅ Startup shortcut created (runs minimized on login)" -ForegroundColor Green
        Write-Host "      $shortcutPath"
    }
}

# ── Done ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  ✅ Installation complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Installed to: $InstallDir" -ForegroundColor White
Write-Host ""
Write-Host "  To run manually:" -ForegroundColor Cyan
Write-Host "    cd `"$InstallDir`""
Write-Host "    .\$exeName"
Write-Host ""
Write-Host "  To edit config later:" -ForegroundColor Cyan
Write-Host "    notepad `"$settingsPath`""
Write-Host ""
Write-Host "  To uninstall:" -ForegroundColor Cyan
Write-Host "    Remove `"$InstallDir`""

# Check if startup shortcut exists
$startupShortcut = Join-Path ([System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs\Startup")) "OmniForge SceneSync.lnk"
if (Test-Path $startupShortcut) {
    Write-Host "    Remove `"$startupShortcut`""
}
Write-Host ""

# Offer to run now
$runNow = Read-Host "Start SceneSync now? (Y/n)"
if (-not ($runNow -match "^[Nn]")) {
    $exePath = Join-Path $InstallDir $exeName
    Write-Host "🚀 Starting SceneSync..." -ForegroundColor Green
    Start-Process -FilePath $exePath -WorkingDirectory $InstallDir
}

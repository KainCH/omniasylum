param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish/SyncAgent",
    [string]$StorageAccountName = "omni46jismtjodyuc",
    # Explicit full version (e.g. "2.1.0"). Takes priority over -BumpMajor/-BumpMinor.
    [string]$Version = "",
    # Bump the major segment and reset minor + build to 0  (1.2.3 → 2.0.0)
    [switch]$BumpMajor,
    # Bump the minor segment and reset build to 0          (1.2.3 → 1.3.0)
    [switch]$BumpMinor,
    [string]$TimestampUrl                = "http://timestamp.acs.microsoft.com",
    # Azure Trusted Signing (Artifact Signing) — replaces Key Vault self-signed cert
    [string]$TrustedSigningEndpoint      = "https://eus.codesigning.azure.net/",
    [string]$TrustedSigningAccountName   = "omni-forge-sign",
    [string]$TrustedSigningCertProfile   = "OmniForgeAgent"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Split-Path -Parent $scriptDir
$projectPath = Join-Path $solutionRoot "src/OmniForge.SyncAgent"

# ── Version management ────────────────────────────────────────────────────────
# Priority:
#   1. -Version "x.y.z"   — use exactly as given
#   2. -BumpMajor          — increment major, reset minor + build to 0
#   3. -BumpMinor          — increment minor, reset build to 0
#   4. (default)           — increment build/patch by 1
$versionFilePath = Join-Path $scriptDir "agent-version.txt"

if (-not [string]::IsNullOrEmpty($Version)) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        Write-Error "-Version must be in 'major.minor.build' format (e.g. '2.0.0'). Got: '$Version'"
        exit 1
    }
    Write-Host "Using specified version: $Version" -ForegroundColor Cyan
} else {
    if (Test-Path $versionFilePath) {
        $existing = (Get-Content $versionFilePath -Raw).Trim()
    } else {
        $existing = "1.0.0"
    }

    if ($existing -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        Write-Warning "Could not parse version '$existing' — defaulting to 1.0.0"
        $existing = "1.0.0"
        $existing -match '^(\d+)\.(\d+)\.(\d+)$' | Out-Null
    }

    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    $build = [int]$Matches[3]

    if ($BumpMajor) {
        $major += 1; $minor = 0; $build = 0
        Write-Host "Bumping major version: $existing → $major.$minor.$build" -ForegroundColor Cyan
    } elseif ($BumpMinor) {
        $minor += 1; $build = 0
        Write-Host "Bumping minor version: $existing → $major.$minor.$build" -ForegroundColor Cyan
    } else {
        $build += 1
        Write-Host "Auto-incremented build: $existing → $major.$minor.$build" -ForegroundColor Cyan
    }

    $Version = "$major.$minor.$build"
}

# Persist the version locally before doing anything else so a failed upload
# doesn't leave the counter out of sync on the next run.
$Version | Out-File -FilePath $versionFilePath -NoNewline -Encoding utf8
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "Publishing OmniForge Sync Agent v$Version..." -ForegroundColor Cyan

# Resolve output dir relative to script location
$resolvedOutput = Join-Path $scriptDir $OutputDir

# Build single-file self-contained exe — stamp the version into the assembly
dotnet publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
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

# ── Azure Trusted Signing (Artifact Signing) ─────────────────────────────────
# Uses signtool.exe + Microsoft.Trusted.Signing.Client DLIB.
# Authentication is via DefaultAzureCredential — picks up 'az login' automatically.
Write-Host "Signing with Azure Trusted Signing ($TrustedSigningAccountName / $TrustedSigningCertProfile)..." -ForegroundColor Cyan

# ── Find signtool.exe (x64 required — DLIB is x64; 32-bit signtool cannot load it) ─────────────────────────────────────
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) {
    $signtool = Get-ChildItem "C:\Program Files\Windows Kits\10\bin" -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signtool) {
    Write-Error "x64 signtool.exe not found. Install the Windows 10/11 SDK (via Visual Studio Installer or standalone SDK)."
    exit 1
}
Write-Host "  signtool: $signtool" -ForegroundColor Gray

# ── Download Microsoft.Trusted.Signing.Client NuGet package (cached) ─────────
$dlibCacheDir = Join-Path $env:LOCALAPPDATA "omni-forge\trusted-signing-client"
$dlibPath     = Get-ChildItem $dlibCacheDir -Filter "Azure.CodeSigning.Dlib.dll" -Recurse -ErrorAction SilentlyContinue |
                    Select-Object -First 1 -ExpandProperty FullName

if (-not $dlibPath) {
    Write-Host "Downloading Microsoft.Trusted.Signing.Client NuGet package..." -ForegroundColor Yellow
    $null = New-Item -ItemType Directory -Force -Path $dlibCacheDir

    # Resolve latest stable version from NuGet
    $indexJson  = Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/microsoft.trusted.signing.client/index.json"
    $pkgVersion = $indexJson.versions | Where-Object { $_ -notmatch "-" } | Select-Object -Last 1
    Write-Host "  Package version: $pkgVersion" -ForegroundColor Gray

    $nupkgUrl  = "https://www.nuget.org/api/v2/package/Microsoft.Trusted.Signing.Client/$pkgVersion"
    $nupkgFile = Join-Path $dlibCacheDir "client.zip"  # nupkg is a zip
    Invoke-WebRequest -Uri $nupkgUrl -OutFile $nupkgFile -UseBasicParsing
    Expand-Archive -Path $nupkgFile -DestinationPath $dlibCacheDir -Force
    Remove-Item $nupkgFile -ErrorAction SilentlyContinue

    $dlibPath = Get-ChildItem $dlibCacheDir -Filter "Azure.CodeSigning.Dlib.dll" -Recurse |
                    Select-Object -First 1 -ExpandProperty FullName
    if (-not $dlibPath) {
        Write-Error "Could not locate Azure.CodeSigning.Dlib.dll after extracting package at $dlibCacheDir"
        exit 1
    }
}
Write-Host "  DLIB: $dlibPath" -ForegroundColor Gray

# ── Build metadata JSON ───────────────────────────────────────────────────────
$metadata = [ordered]@{
    Endpoint               = $TrustedSigningEndpoint
    CodeSigningAccountName = $TrustedSigningAccountName
    CertificateProfileName = $TrustedSigningCertProfile
} | ConvertTo-Json -Compress

$metadataFile = [System.IO.Path]::GetTempFileName()
Set-Content -Path $metadataFile -Value $metadata -Encoding UTF8

# ── Sign ──────────────────────────────────────────────────────────────────────
try {
    & $signtool sign /v /fd sha256 /dlib $dlibPath /dmdf $metadataFile /tr $TimestampUrl /td sha256 $exePath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Code signing failed (exit $LASTEXITCODE). Check signtool output above."
        exit 1
    }
} finally {
    Remove-Item $metadataFile -ErrorAction SilentlyContinue
}

Write-Host "Signed successfully" -ForegroundColor Green
# ─────────────────────────────────────────────────────────────────────────────
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

#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Enhanced deployment script for OmniAsylum Stream Counter with automatic asset cleanup.

.DESCRIPTION
    This script performs a complete deployment pipeline:
    1. Builds the React frontend
    2. Cleans old frontend assets automatically
    3. Copies new frontend files to API directory
    4. Builds and pushes Docker image
    5. Deploys to Azure Container Apps

.PARAMETER SkipFrontend
    Skip the frontend build and copy steps (for backend-only changes)

.PARAMETER SkipDocker
    Skip the Docker build and push steps

.PARAMETER SkipDeploy
    Skip the Azure deployment step

.EXAMPLE
    .\deploy-with-cleanup.ps1
    Runs the full deployment pipeline

.EXAMPLE
    .\deploy-with-cleanup.ps1 -SkipFrontend
    Deploys only backend changes
#>

param(
    [switch]$SkipFrontend,
    [switch]$SkipDocker,
    [switch]$SkipDeploy
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message, [string]$Color = 'Yellow')
    Write-Host $Message -ForegroundColor $Color
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "[STEP] $Message" -ForegroundColor Yellow
}

try {
    Write-Host 'STARTING Enhanced OmniAsylum Deployment Pipeline...' -ForegroundColor Green
    Write-Host "DEPLOYMENT TIME: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray

    $startTime = Get-Date

    if (-not $SkipFrontend) {
        Write-Step 'Step 1: Building React Frontend...'
        Push-Location 'modern-frontend'
        try {
            npm run build
            if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
            Write-Success 'Frontend build completed'
        }
        finally {
            Pop-Location
        }

        Write-Step 'Step 2: Cleaning old assets and deploying new frontend...'

        $assetsPath = 'API/frontend/assets'
        if (Test-Path $assetsPath) {
            $oldFiles = Get-ChildItem $assetsPath
            $oldCount = ($oldFiles | Measure-Object).Count

            if ($oldCount -gt 0) {
                Write-Info "Found $($oldCount) old asset files to clean:"
                $oldFiles | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor DarkGray }

                Remove-Item -Path "$assetsPath/*" -Recurse -Force -ErrorAction SilentlyContinue
                Write-Success "Cleaned $($oldCount) old asset files"
            } else {
                Write-Info 'No old assets to clean'
            }
        } else {
            Write-Info 'No assets folder exists yet'
        }

        Copy-Item -Path 'modern-frontend/dist/*' -Destination 'API/frontend' -Recurse -Force

        $newFiles = Get-ChildItem $assetsPath -ErrorAction SilentlyContinue
        $newCount = ($newFiles | Measure-Object).Count

        Write-Info "New asset files deployed:"
        $newFiles | ForEach-Object { Write-Host "  + $($_.Name)" -ForegroundColor Green }

        Write-Success "Frontend deployment completed ($($newCount) files)"
    } else {
        Write-Info 'Skipping frontend build and deployment'
    }

    if (-not $SkipDocker) {
        Write-Step 'Step 3: Building and pushing Docker image...'

        # Ensure we're authenticated with Azure Container Registry
        Write-Info 'Verifying Azure Container Registry authentication...'
        try {
            $acrLoginResult = az acr login --name omniforgeacr 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Error "ACR login failed: $($acrLoginResult)"
                throw 'Failed to authenticate with Azure Container Registry'
            }
            Write-Success 'Successfully authenticated with Azure Container Registry'
        }
        catch {
            throw "Azure Container Registry authentication failed: $_"
        }
        Push-Location 'API'
        try {
            Write-Info 'Building Docker image...'
            docker build -t omniforgeacr.azurecr.io/omniforgestream-api:latest .
            if ($LASTEXITCODE -ne 0) { throw 'Docker build failed' }

            Write-Info 'Pushing Docker image to Azure Container Registry...'
            docker push omniforgeacr.azurecr.io/omniforgestream-api:latest
            if ($LASTEXITCODE -ne 0) { throw 'Docker push failed' }

            Write-Success 'Docker image built and pushed successfully'
        }
        catch {
            throw "Docker operations failed: $_"
        }
        finally {
            Pop-Location
        }
    } else {
        Write-Info 'Skipping Docker build and push'
    }

    if (-not $SkipDeploy) {
        Write-Step 'Step 4: Deploying to Azure Container Apps...'

        $revision = Get-Date -Format 'MMddHHmm'
        Write-Info "Creating revision: $($revision)"

        $deployResult = az containerapp update `
            --name omniforgestream-api-prod `
            --resource-group Streamer-Tools-RG `
            --image omniforgeacr.azurecr.io/omniforgestream-api:latest `
            --revision-suffix $revision `
            --output json | ConvertFrom-Json

        if ($deployResult) {
            $revisionName = $deployResult.properties.latestRevisionName
            $appUrl = $deployResult.properties.configuration.ingress.fqdn
            $provisioningState = $deployResult.properties.provisioningState
            $runningStatus = $deployResult.properties.runningStatus

            Write-Success "Azure deployment completed!"
            Write-Info "Revision: $($revisionName)"
            Write-Info "Status: $($provisioningState) / $($runningStatus)"
            Write-Host "APPLICATION URL: https://$($appUrl)" -ForegroundColor Cyan

            # Test health endpoint
            Write-Info "Testing application health..."
            Start-Sleep -Seconds 5
            try {
                $healthResponse = Invoke-RestMethod -Uri "https://$($appUrl)/api/health" -TimeoutSec 10
                if ($healthResponse.status -eq 'ok') {
                    Write-Success "Application is healthy and responding"
                    Write-Info "Uptime: $([math]::Round($healthResponse.uptime, 2)) seconds"
                } else {
                    Write-Error "Health check returned unexpected status: $($healthResponse.status)"
                }
            }
            catch {
                Write-Error "Health check failed: $($_.Exception.Message)"
            }
        } else {
            throw 'Azure deployment returned no result'
        }
    } else {
        Write-Info 'Skipping Azure deployment'
    }

    $endTime = Get-Date
    $duration = $endTime - $startTime

    Write-Host "`n🎉 Deployment Pipeline Completed Successfully!" -ForegroundColor Green
    Write-Host "TOTAL TIME: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray
    Write-Host "SUMMARY:" -ForegroundColor Yellow
    if (-not $SkipFrontend) { Write-Host "  [OK] Frontend built and assets cleaned" }
    if (-not $SkipDocker) { Write-Host "  [OK] Docker image updated" }
    if (-not $SkipDeploy) { Write-Host "  [OK] Azure deployment successful" }
}
catch {
    Write-Host "Deployment failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

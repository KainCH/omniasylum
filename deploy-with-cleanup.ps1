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

function Show-RecoveryOptions {
    param([string]$FailureType)

    Write-Host "`n🛟 DEPLOYMENT RECOVERY ASSISTANT" -ForegroundColor Green
    Write-Host "=" * 50 -ForegroundColor Green

    if ($FailureType -eq "Docker") {
        Write-Host "📋 Quick Docker Diagnostics:" -ForegroundColor Yellow
        Write-Host "   docker --version" -ForegroundColor White
        Write-Host "   docker system df" -ForegroundColor White
        Write-Host "   docker system prune -f  # (if low disk space)" -ForegroundColor White
    }
    elseif ($FailureType -eq "Azure") {
        Write-Host "📋 Quick Azure Diagnostics:" -ForegroundColor Yellow
        Write-Host "   az account show" -ForegroundColor White
        Write-Host "   az containerapp show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --query '{state:properties.provisioningState,status:properties.runningStatus}'" -ForegroundColor White
        Write-Host "   az containerapp logs show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --tail 20" -ForegroundColor White
    }

    Write-Host "`n🔄 Retry Options:" -ForegroundColor Cyan
    Write-Host "   • Backend Deploy: run_task('Backend Deploy')" -ForegroundColor White
    Write-Host "   • Fullstack Deploy: run_task('Fullstack Deploy')" -ForegroundColor White
    Write-Host "   • Manual script: .\deploy-with-cleanup.ps1" -ForegroundColor White

    Write-Host "=" * 50 -ForegroundColor Green
}

try {
    Write-Host 'STARTING Enhanced OmniAsylum Deployment Pipeline...' -ForegroundColor Green
    Write-Host "DEPLOYMENT TIME: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray

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

        # Verify Azure CLI authentication first
        Write-Info 'Verifying Azure CLI authentication...'
        try {
            $azAccount = az account show 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Not logged into Azure CLI. Attempting login..."
                az login --scope https://management.azure.com//.default
                if ($LASTEXITCODE -ne 0) {
                    throw 'Azure CLI login failed'
                }
                Write-Success 'Successfully logged into Azure CLI'
            } else {
                $accountInfo = $azAccount | ConvertFrom-Json
                Write-Success "Already logged into Azure CLI as: $($accountInfo.user.name)"
            }
        }
        catch {
            throw "Azure CLI authentication failed: $_"
        }

        # Ensure we're authenticated with Azure Container Registry
        Write-Info 'Verifying Azure Container Registry authentication...'
        try {
            # Always perform ACR login to ensure fresh credentials
            Write-Info 'Performing ACR login...'
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
            Write-Info 'Building Docker image (this may take a few minutes)...'

            # Check if Docker is running first
            try {
                $dockerVersion = docker version --format json 2>&1
                if ($LASTEXITCODE -ne 0) {
                    throw "Docker daemon not running: $dockerVersion"
                }
            }
            catch {
                throw "Docker is not available or not running. Please start Docker Desktop and try again."
            }

            $buildOutput = docker build -t omniforgeacr.azurecr.io/omniforgestream-api:latest . 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host "`n❌ DOCKER BUILD FAILED" -ForegroundColor Red
                Write-Host "Exit Code: $LASTEXITCODE" -ForegroundColor Red
                Write-Host "`n📋 BUILD OUTPUT:" -ForegroundColor Yellow
                Write-Host ($buildOutput | Out-String) -ForegroundColor White
                throw "Docker build failed with exit code $LASTEXITCODE"
            }
            Write-Success 'Docker image built successfully'

            Write-Info 'Pushing Docker image to Azure Container Registry...'
            $pushOutput = docker push omniforgeacr.azurecr.io/omniforgestream-api:latest 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host "`n❌ DOCKER PUSH FAILED" -ForegroundColor Red
                Write-Host "Exit Code: $LASTEXITCODE" -ForegroundColor Red
                Write-Host "`n📋 PUSH OUTPUT:" -ForegroundColor Yellow
                Write-Host ($pushOutput | Out-String) -ForegroundColor White
                throw "Docker push failed with exit code $LASTEXITCODE"
            }
            Write-Success 'Docker image pushed successfully to Azure Container Registry'
            Write-Success 'Docker operations completed successfully'
        }
        catch {
            Write-Host "`n�️  QUICK RECOVERY OPTIONS:" -ForegroundColor Cyan
            Write-Host "   1. Check Docker Desktop is running" -ForegroundColor White
            Write-Host "   2. Clear Docker cache: docker system prune -f" -ForegroundColor White
            Write-Host "   3. Check available disk space" -ForegroundColor White
            Write-Host "   4. Retry deployment" -ForegroundColor White

            # Show basic diagnostics
            Write-Host "`n📊 SYSTEM CHECK:" -ForegroundColor Yellow
            try {
                $dockerInfo = docker system df 2>$null
                if ($dockerInfo) {
                    Write-Host "   Docker Status: ✅ Running" -ForegroundColor Green
                } else {
                    Write-Host "   Docker Status: ❌ Not Running" -ForegroundColor Red
                }
            } catch {
                Write-Host "   Docker Status: ❌ Not Available" -ForegroundColor Red
            }

            try {
                $diskInfo = Get-PSDrive C | Select-Object Used, Free
                $freeGB = [math]::Round($diskInfo.Free / 1GB, 2)
                Write-Host "   Free Disk Space: $freeGB GB" -ForegroundColor White
                if ($freeGB -lt 5) {
                    Write-Host "   ⚠️  WARNING: Low disk space detected" -ForegroundColor Yellow
                }
            } catch {
                Write-Host "   ⚠️  Could not check disk space" -ForegroundColor Yellow
            }

            throw $_
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

        try {
            $deployOutput = az containerapp update `
                --name omniforgestream-api-prod `
                --resource-group Streamer-Tools-RG `
                --image omniforgeacr.azurecr.io/omniforgestream-api:latest `
                --revision-suffix $revision `
                --output json 2>&1

            if ($LASTEXITCODE -ne 0) {
                Write-Host "`n❌ Azure CLI command failed with exit code: $LASTEXITCODE" -ForegroundColor Red
                Write-Host "Raw output:" -ForegroundColor Yellow
                Write-Host $deployOutput -ForegroundColor White
                throw 'Azure Container Apps deployment failed'
            }

            # Simple text-based success check - no JSON parsing
            if ($deployOutput -match '"provisioningState":\s*"Succeeded"' -and $deployOutput -match '"runningStatus":\s*"Running"') {
                Write-Success "Azure deployment completed successfully!"
                Write-Info "Status: Succeeded / Running"
                Write-Host "APPLICATION URL: https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io" -ForegroundColor Cyan
            } else {
                Write-Host "`n❌ Azure deployment may have failed" -ForegroundColor Red
                Write-Host "Raw Azure CLI output:" -ForegroundColor Yellow
                Write-Host $deployOutput -ForegroundColor White
                throw 'Azure deployment failed or status unclear'
            }

            # Test health endpoint regardless of JSON parsing success
            Write-Info "Testing application health..."
            Start-Sleep -Seconds 5
            try {
                $healthResponse = Invoke-RestMethod -Uri "https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io/api/health" -TimeoutSec 10
                if ($healthResponse.status -eq 'ok') {
                    Write-Success "Application is healthy and responding"
                    Write-Info "Uptime: $([math]::Round($healthResponse.uptime, 2)) seconds"
                } else {
                    Write-Warning "Health check returned unexpected status: $($healthResponse.status)"
                    Write-Info "Application may still be starting up..."
                }
            }
            catch {
                Write-Warning "Health check failed: $($_.Exception.Message)"
                Write-Info "This may be normal if the application is still starting up"
            }
        }
        catch {
            Write-Host "`n🚨 DEPLOYMENT FAILURE - Azure Container Apps" -ForegroundColor Red
            Write-Host "=" * 60 -ForegroundColor Red
            Write-Host "❌ ERROR TYPE: Azure Deployment Failure" -ForegroundColor Red
            Write-Host "`n🔍 DIAGNOSIS:" -ForegroundColor Yellow
            Write-Host "   • Failed to deploy to Azure Container Apps" -ForegroundColor White
            Write-Host "   • This could be due to:" -ForegroundColor White
            Write-Host "     - Azure CLI authentication expired" -ForegroundColor Gray
            Write-Host "     - Resource permissions issues" -ForegroundColor Gray
            Write-Host "     - Container image not found in ACR" -ForegroundColor Gray
            Write-Host "     - Azure service issues" -ForegroundColor Gray

            Write-Host "`n🛠️  RECOVERY OPTIONS:" -ForegroundColor Cyan
            Write-Host "   1. Check Azure login: az account show" -ForegroundColor White
            Write-Host "   2. Re-authenticate: az login" -ForegroundColor White
            Write-Host "   3. Verify image in ACR: az acr repository show --name omniforgeacr --image omniforgestream-api:latest" -ForegroundColor White
            Write-Host "   4. Check Container App status: az containerapp show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG" -ForegroundColor White
            Write-Host "   5. View deployment logs: az containerapp logs show --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --tail 50" -ForegroundColor White

            Write-Host "`n🔄 ROLLBACK OPTION:" -ForegroundColor Magenta
            Write-Host "   • If previous deployment was working, you can rollback:" -ForegroundColor White
            Write-Host "   • az containerapp revision list --name omniforgestream-api-prod --resource-group Streamer-Tools-RG" -ForegroundColor White
            Write-Host "   • az containerapp revision activate --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --revision [PREVIOUS_REVISION]" -ForegroundColor White

            Write-Host "`n💡 NEXT STEPS:" -ForegroundColor Green
            Write-Host "   • Check Azure service health: https://status.azure.com" -ForegroundColor White
            Write-Host "   • Verify resource group and subscription access" -ForegroundColor White
            Write-Host "   • If Docker built successfully, try Backend Deploy again" -ForegroundColor White
            Write-Host "=" * 60 -ForegroundColor Red

            throw "Azure deployment failed: $_"
        }
    } else {
        Write-Info 'Skipping Azure deployment'
    }

    Write-Host "`n🎉 Deployment Pipeline Completed Successfully!" -ForegroundColor Green
    Write-Host "SUMMARY:" -ForegroundColor Yellow
    if (-not $SkipFrontend) { Write-Host "  [OK] Frontend built and assets cleaned" }
    if (-not $SkipDocker) { Write-Host "  [OK] Docker image updated" }
    if (-not $SkipDeploy) { Write-Host "  [OK] Azure deployment successful" }
}
catch {
    Write-Host "Deployment failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

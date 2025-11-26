#!/usr/bin/env pwsh
Write-Host "🧹 Starting Docker Cleanup for OmniForge..." -ForegroundColor Cyan

# 1. Remove OmniForge Images
$images = docker images --format "{{.Repository}}:{{.Tag}}" | Where-Object { $_ -like "*omniforge*" }

if ($images) {
    Write-Host "Found $($images.Count) OmniForge images to remove." -ForegroundColor Yellow
    foreach ($image in $images) {
        Write-Host "Removing $image..." -ForegroundColor Gray
        docker rmi $image --force
    }
    Write-Host "✅ OmniForge images removed." -ForegroundColor Green
} else {
    Write-Host "ℹ️  No OmniForge images found." -ForegroundColor Gray
}

# 2. Prune dangling images
Write-Host "🧹 Pruning dangling images..." -ForegroundColor Cyan
docker image prune -f

Write-Host "✨ Cleanup finished!" -ForegroundColor Green

#!/usr/bin/env pwsh
# Twitch OAuth Setup Script for OmniForgeStream

Write-Host "🎮 OmniForgeStream - Twitch OAuth Setup" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "📋 Steps to set up Twitch OAuth:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Go to: https://dev.twitch.tv/console/apps" -ForegroundColor White
Write-Host "2. Click 'Register Your Application'" -ForegroundColor White
Write-Host "3. Fill out the form:" -ForegroundColor White
Write-Host "   - Name: OmniForgeStream (or your app name)" -ForegroundColor Gray
Write-Host "   - OAuth Redirect URLs: http://localhost:3000/auth/twitch/callback" -ForegroundColor Gray
Write-Host "   - Category: Game Integration" -ForegroundColor Gray
Write-Host "4. Click 'Create'" -ForegroundColor White
Write-Host "5. Copy the Client ID from the app page" -ForegroundColor White
Write-Host "6. Click 'New Secret' and copy the Client Secret" -ForegroundColor White
Write-Host ""

Write-Host "🔧 Current Configuration:" -ForegroundColor Green
Write-Host "   API Server: http://localhost:3000" -ForegroundColor Gray
Write-Host "   Frontend: http://localhost:3001" -ForegroundColor Gray
Write-Host "   Callback: http://localhost:3000/auth/twitch/callback" -ForegroundColor Gray
Write-Host ""

$clientId = Read-Host "Enter your Twitch Client ID"
$clientSecret = Read-Host "Enter your Twitch Client Secret" -MaskInput

if ($clientId -and $clientSecret) {
    Write-Host ""
    Write-Host "🔄 Updating .env file..." -ForegroundColor Yellow

    $envFile = ".\.env"
    $content = Get-Content $envFile

    $newContent = @()
    foreach ($line in $content) {
        if ($line -match "^TWITCH_CLIENT_ID=") {
            $newContent += "TWITCH_CLIENT_ID=$clientId"
        }
        elseif ($line -match "^TWITCH_CLIENT_SECRET=") {
            $newContent += "TWITCH_CLIENT_SECRET=$clientSecret"
        }
        else {
            $newContent += $line
        }
    }

    $newContent | Set-Content $envFile

    Write-Host "✅ Environment variables updated!" -ForegroundColor Green
    Write-Host ""
    Write-Host "🚀 Next steps:" -ForegroundColor Cyan
    Write-Host "1. Restart your API server: npm run dev" -ForegroundColor Gray
    Write-Host "2. Open frontend: http://localhost:3001" -ForegroundColor Gray
    Write-Host "3. Click 'Login with Twitch' to test" -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "❌ Setup cancelled. Please run this script again with valid credentials." -ForegroundColor Red
}

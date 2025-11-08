# Fix corrupted emojis in App.jsx
$content = Get-Content -Path "modern-frontend/src/App.jsx" -Raw -Encoding UTF8

# Replace corrupted characters with proper emojis
$content = $content -replace "eventSubStatus\.streamStatus === 'live' \? '�\s*Live'", "eventSubStatus.streamStatus === 'live' ? '🔴 Live'"
$content = $content -replace "�\s*<strong>LIVE", "🔴 <strong>LIVE"
$content = $content -replace "� OBS.*Setup", "🖥️ OBS Setup"
$content = $content -replace "�🎮", "🎮"

# Save with UTF-8 encoding
$content | Set-Content -Path "modern-frontend/src/App.jsx" -Encoding UTF8

Write-Host "✅ Emoji fix completed"

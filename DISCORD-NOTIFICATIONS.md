# Discord Notifications Feature

## Overview
Automatically post to Discord when a streamer goes live on Twitch using Discord webhooks.

## Implementation Summary

### Backend Changes

#### 1. Database Schema (`API/database.js`)
- Added `discordWebhookUrl` field to user schema (stores Discord webhook URL)
- Added `discordNotifications` to default feature flags
- Works with both Azure Table Storage and local JSON storage

#### 2. Stream Monitor (`API/streamMonitor.js`)
- Added `sendDiscordNotification(data)` method
  - Sends rich embed to Discord webhook
  - Includes stream title, game, profile image, and link
  - Twitch purple branding (#9146FF)
- Integrated into `handleStreamOnline()` event
  - Checks if `discordNotifications` feature is enabled
  - Checks if user has configured webhook URL
  - Automatically sends notification when stream goes live

#### 3. API Endpoints (`API/userRoutes.js`)
- `GET /api/user/discord-webhook` - Get webhook configuration
- `PUT /api/user/discord-webhook` - Save webhook URL
- `POST /api/user/discord-webhook/test` - Send test notification

### Frontend Changes

#### 1. New Component (`modern-frontend/src/components/DiscordWebhookSettings.jsx`)
- User-friendly configuration interface
- Setup instructions with step-by-step guide
- Webhook URL input with validation
- Save and Test buttons
- Live preview of Discord message format
- Status indicators (enabled/disabled)

#### 2. Styling (`modern-frontend/src/components/DiscordWebhookSettings.css`)
- Dark theme matching existing UI
- Discord message preview styling
- Responsive button states
- Success/error message styling

#### 3. Admin Dashboard Integration (`modern-frontend/src/components/AdminDashboard.jsx`)
- Component appears when `discordNotifications` feature is enabled
- Integrated into user cards in admin view

## User Setup Process

### Step 1: Enable Feature
Admin enables the `discordNotifications` feature flag for the user.

### Step 2: Create Discord Webhook
1. Open Discord server settings
2. Navigate to: Integrations → Webhooks
3. Click "New Webhook" (or select existing)
4. Choose channel for notifications
5. Copy webhook URL

### Step 3: Configure in App
1. User logs into admin dashboard
2. Expands their user card
3. Sees Discord Notifications section
4. Pastes webhook URL
5. Clicks "Save Webhook"

### Step 4: Test
1. Click "Send Test" button
2. Verify test message appears in Discord channel
3. Done! Will auto-post when stream goes live

## Discord Message Format

When a stream goes live, Discord receives:

```
🔴 **Riress** just went LIVE on Twitch!

┌─────────────────────────────────┐
│ [Profile Image]  Stream Title   │
│                                  │
│ Playing **Game/Category**       │
│                                  │
│ 🎮 Watch Now!                    │
│                                  │
│ Twitch • Just now                │
└─────────────────────────────────┘
```

- **Clickable link**: Entire embed links to `https://twitch.tv/{username}`
- **Rich embed**: Twitch purple accent, profile image thumbnail
- **Dynamic content**: Shows current stream title and game

## Technical Details

### Webhook URL Validation
- Must start with `https://discord.com/api/webhooks/`
- Validated on backend before saving
- Frontend shows error if invalid format

### Error Handling
- Failed webhook POSTs are logged but don't block stream.online event
- User sees error message in test flow
- Webhook failures don't affect stream functionality

### Security
- Webhook URLs stored per-user in database
- JWT authentication required for all endpoints
- No sensitive Discord data stored

### Feature Dependencies
- Requires `discordNotifications` feature flag enabled
- Requires Twitch EventSub WebSocket (stream.online events)
- Requires user to have valid webhook URL configured

## Testing

### Local Testing
1. Enable feature for test user
2. Create Discord webhook in test server
3. Configure webhook URL in admin dashboard
4. Click "Send Test" button
5. Verify message appears in Discord

### Production Testing
1. Go live on Twitch (or use test account)
2. EventSub triggers stream.online event
3. Notification automatically posts to Discord
4. Verify message content and formatting

## Future Enhancements

Possible additions:
- Stream offline notifications
- Custom message templates
- Multiple webhook support (different servers)
- Message customization (add custom text, mentions)
- Stream stats in notifications (viewers, duration)
- Role mentions (@everyone, @here, custom roles)
- Scheduled stream announcements

## Troubleshooting

### Webhook Not Posting
1. Check feature is enabled
2. Verify webhook URL is correct format
3. Check webhook still exists in Discord
4. Test webhook with "Send Test" button
5. Check API logs for error messages

### Invalid Webhook URL
- Must be Discord webhook URL
- Format: `https://discord.com/api/webhooks/ID/TOKEN`
- Copy entire URL from Discord webhook settings

### Test Works But Live Doesn't
- Check EventSub is connected (`/api/twitch/status`)
- Verify stream actually started (check Twitch dashboard)
- Check API logs for stream.online event
- Ensure feature remains enabled

## Files Modified

- `API/database.js` - Schema updates
- `API/streamMonitor.js` - Notification logic
- `API/userRoutes.js` - API endpoints
- `modern-frontend/src/components/DiscordWebhookSettings.jsx` - UI component
- `modern-frontend/src/components/DiscordWebhookSettings.css` - Styling
- `modern-frontend/src/components/AdminDashboard.jsx` - Integration

## API Reference

### GET /api/user/discord-webhook
**Authentication**: Required (JWT)

**Response**:
```json
{
  "webhookUrl": "https://discord.com/api/webhooks/...",
  "enabled": true
}
```

### PUT /api/user/discord-webhook
**Authentication**: Required (JWT)

**Request Body**:
```json
{
  "webhookUrl": "https://discord.com/api/webhooks/..."
}
```

**Response**:
```json
{
  "message": "Discord webhook updated successfully",
  "webhookUrl": "https://discord.com/api/webhooks/..."
}
```

### POST /api/user/discord-webhook/test
**Authentication**: Required (JWT)

**Response**:
```json
{
  "message": "Test notification sent successfully!"
}
```

## Notes
- Discord webhooks have rate limits (30 requests per 60 seconds)
- Webhook URLs should be kept private (treat like passwords)
- Users can delete webhooks from Discord, breaking integration
- Feature is opt-in (must be enabled by admin)

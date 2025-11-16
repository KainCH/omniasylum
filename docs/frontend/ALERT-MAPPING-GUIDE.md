# Alert Event Mapping UI - User Guide

## Overview

The Alert Event Mapping system allows streamers to assign custom alerts to specific Twitch events. When events like follows, subscriptions, or cheers occur, the configured alert will display on the stream overlay.

## Features Implemented

### 1. **Alert Management Interface** ✅
- Create custom alerts with full styling control
- Configure text, colors, duration, sounds
- Enable/disable alerts individually
- Preview alert styles

### 2. **Event Mapping Manager** ✅
- Assign alerts to specific Twitch EventSub events
- Visual grid showing all available events
- One-click mapping changes
- Reset to default mappings

### 3. **Live Preview System** ✅
- Test alerts before going live
- See exactly how alerts will appear on stream
- Sample data for realistic testing
- Full-screen preview with animations

## How to Use

### Step 1: Create Custom Alerts

1. Navigate to **Admin Dashboard**
2. Scroll to **🚨 Alerts Management** section
3. Click **🔽 Show Alerts** to expand
4. Fill out the **Create Custom Alert** form:
   - **Alert Type**: Choose event type (follow, subscription, bits, etc.)
   - **Alert Name**: Give it a descriptive name
   - **Text Prompt**: Enter message with variables
   - **Colors**: Customize background, text, border
   - **Duration**: How long alert displays (in milliseconds)
   - **Visual/Sound Cues**: Optional descriptions

5. Click **Create Alert** button

**Available Variables in Text Prompt:**
- `[User]` - Username triggering the event
- `[Tier]` - Subscription tier (Tier 1, Tier 2, Tier 3, Prime)
- `[Months]` - Total subscription months
- `[Streak]` - Current streak months
- `[Message]` - Resub message text
- `[Bits]` - Bits/cheers amount
- `[Amount]` - Gift sub quantity
- `[Viewers]` - Raid viewer count
- `[X]` - Generic fallback (auto-selects appropriate value)

**Example Alert Prompts:**
```
Follow: "Welcome to the asylum, [User]! 👥"
Subscription: "Thanks for the [Tier] sub, [User]! ⭐"
Gift Sub: "[User] gifted [Amount] subs! 🎁"
Resub: "[User] has been here for [Months] months! 🔄"
Cheer: "[User] cheered [Bits] bits! 💎"
Raid: "[User] raided with [Viewers] viewers! 🚨"
```

### Step 2: Configure Event Mappings

1. In the **Alerts by User** section, find your username
2. Click **🎯 Configure Event Mappings** button
3. A modal will open showing all available events

For each event type:
- **Select Alert**: Choose which alert to display (dropdown)
- **Preview**: See how the alert text will look
- **Test Alert**: Click **🎬 Test Alert** to see full preview

4. Click **💾 Save Event Mappings** when done

### Step 3: Test Your Alerts

**Method 1: In-App Testing**
- In Event Mapping Manager, click **🎬 Test Alert** for any event
- Alert will display full-screen with animations
- Uses sample data to show realistic preview

**Method 2: Live Testing** (requires deployment)
- Go live on Twitch
- Trigger real events (have someone follow, subscribe, etc.)
- Alerts will display on your OBS overlay

### Step 4: Enable Alerts in OBS

1. Add **Browser Source** to your OBS scene
2. URL: `https://your-api-url.com/overlay/{yourTwitchUserId}`
3. Width: 1920, Height: 1080
4. Check "Shutdown source when not visible"
5. Check "Refresh browser when scene becomes active"

## Event Types & Descriptions

| Event            | Icon | Description                        | Default Alert Type |
| ---------------- | ---- | ---------------------------------- | ------------------ |
| **Follow**       | 👥    | User follows the channel           | `follow`           |
| **Subscription** | ⭐    | New subscription (including gifts) | `subscription`     |
| **Gift Sub**     | 🎁    | Community gift subs                | `giftsub`          |
| **Resub**        | 🔄    | Resub with message                 | `resub`            |
| **Cheer**        | 💎    | Bits/cheers from viewers           | `bits`             |
| **Raid**         | 🚨    | Channel receives raid              | `raid`             |

## UI Components

### AlertEventManager Component

**Location**: `modern-frontend/src/components/AlertEventManager.jsx`

**Features:**
- Full CRUD for event mappings
- Real-time preview
- Test alert functionality
- Responsive grid layout
- Dark theme UI

**Props:**
- `userId` - Twitch user ID
- `username` - Display name
- `onClose` - Callback to close modal

### Integration Points

**AdminDashboard.jsx:**
```jsx
<button onClick={() => {
  setEventMappingUser({ userId, username, displayName })
  setShowEventMappingManager(true)
}}>
  🎯 Configure Event Mappings
</button>
```

**API Endpoints Used:**
- `GET /api/alerts/user/:userId` - Fetch user's alerts
- `GET /api/alerts/event-mappings` - Get current mappings
- `PUT /api/alerts/event-mappings` - Update mappings
- `POST /api/alerts/event-mappings/reset` - Reset to defaults

## Styling

The Event Mapping Manager uses a dark theme with purple accents matching the OmniAsylum aesthetic:

- **Background**: Dark grey (#1e1e2e, #2a2a3e)
- **Accent**: Purple (#9146ff)
- **Borders**: Subtle grey (#3a3a4e)
- **Hover Effects**: Glow and transform animations
- **Preview**: Full-screen overlay with blur background

## Troubleshooting

### Alert Not Displaying

1. **Check alert is enabled**
   - In Admin Dashboard, verify alert toggle is ON (green)

2. **Check event mapping**
   - Ensure event is mapped to an alert (not "No Alert")

3. **Check overlay is loaded in OBS**
   - Browser source must be active
   - Check browser console for errors

### Preview Not Working

1. **Check alert exists**
   - Event must be mapped to a valid alert

2. **Browser compatibility**
   - Ensure modern browser (Chrome, Firefox, Edge)
   - Clear cache and reload

### Mappings Not Saving

1. **Check authentication**
   - Token must be valid
   - User must be logged in

2. **Check permissions**
   - User must have access to their own mappings
   - Admin can manage all user mappings

## Advanced Features

### Custom Badge

When an event mapping differs from the default, a **Custom** badge appears:

```jsx
{currentAlertId !== defaultAlertId && (
  <span className="custom-badge">Custom</span>
)}
```

### Variable Processing

The preview system processes variables in real-time:

```javascript
processed = text.replace(/\[User\]/g, username)
processed = processed.replace(/\[Tier\]/g, getTierName(tier))
// etc...
```

### Animation System

Alerts use CSS keyframe animations:

```css
@keyframes asylumPulse {
  0% { opacity: 0; transform: scale(0.8); }
  15% { opacity: 1; transform: scale(1.05); }
  85% { opacity: 1; transform: scale(1); }
  100% { opacity: 1; }
}
```

## Next Steps

### Testing
1. Build the React app: `npm run build`
2. Deploy to Azure
3. Test with real Twitch events
4. Adjust alert timings and styles as needed

### Enhancements (Future)
- Sound file upload support
- Image/GIF support in alerts
- Animation selection (slide, fade, bounce, etc.)
- Alert queue system (prevent overlapping)
- Analytics (which alerts get triggered most)
- A/B testing different alert styles

## File Structure

```
modern-frontend/
├── src/
│   ├── components/
│   │   ├── AlertEventManager.jsx      # New component
│   │   ├── AlertEventManager.css      # Styling
│   │   ├── AdminDashboard.jsx         # Modified (added integration)
│   │   └── ...
│   └── ...
API/
├── overlayRoutes.js                   # Modified (added Socket.io listeners)
├── database.js                        # Event mapping methods
├── alertRoutes.js                     # Event mapping endpoints
├── streamMonitor.js                   # EventSub subscriptions
└── server.js                          # Socket.io event broadcast
```

## API Contract

### GET /api/alerts/event-mappings
**Response:**
```json
{
  "mappings": {
    "channel.follow": "follow-alert-id",
    "channel.subscribe": "sub-alert-id"
  },
  "defaultMappings": {
    "channel.follow": "follow",
    "channel.subscribe": "subscription"
  },
  "availableEvents": [
    "channel.follow",
    "channel.subscribe",
    "channel.subscription.gift",
    "channel.subscription.message",
    "channel.cheer",
    "channel.raid"
  ]
}
```

### PUT /api/alerts/event-mappings
**Request:**
```json
{
  "channel.follow": "custom-follow-alert-id",
  "channel.subscribe": "custom-sub-alert-id"
}
```

**Response:**
```json
{
  "success": true,
  "mappings": { /* updated mappings */ }
}
```

## Browser Support

- ✅ Chrome 90+
- ✅ Firefox 88+
- ✅ Edge 90+
- ✅ Safari 14+
- ❌ Internet Explorer (not supported)

## Performance

- Event mappings cached in component state
- Preview renders instantly (no API calls)
- Test alerts use CSS animations (GPU accelerated)
- Minimal re-renders with proper React hooks

## Security

- All API calls require JWT authentication
- User can only modify their own mappings
- Admin can view/modify all user mappings
- Input sanitization on backend

---

**Ready to use!** Build the frontend and start configuring your stream alerts! 🎉

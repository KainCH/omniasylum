# Twitch EventSub Integration Guide

## Overview

This application integrates with Twitch EventSub WebSocket to receive real-time events for follows, subscriptions, gift subs, resubs, and cheers/bits. Each event can trigger customizable alerts.

## Event Types Supported

| Event Type | EventSub Subscription          | Alert Type     | Description                                 |
| ---------- | ------------------------------ | -------------- | ------------------------------------------- |
| Follow     | `channel.follow`               | `follow`       | User follows the channel                    |
| Subscribe  | `channel.subscribe`            | `subscription` | User subscribes (new or gift)               |
| Gift Sub   | `channel.subscription.gift`    | `giftsub`      | User gifts subs to community                |
| Resub      | `channel.subscription.message` | `resub`        | User resubscribes with message              |
| Cheer/Bits | `channel.cheer`                | `bits`         | User cheers bits                            |
| Raid       | `channel.raid`                 | `raid`         | Channel receives raid (already implemented) |

## Architecture

### 1. **Event Flow**

```
Twitch EventSub → streamMonitor.js → server.js → Socket.io → Frontend/Overlay
                         ↓
                    database.js (Event Mappings)
                         ↓
                    Custom Alert Configuration
```

### 2. **Components**

#### **streamMonitor.js**
- Creates EventSub WebSocket listener per authenticated user
- Subscribes to events using user's access token
- Handles incoming events and retrieves alert configuration
- Emits events to server.js

**Key Methods:**
```javascript
subscribeToUser(userId, username, accessToken)  // Create subscriptions
handleSubscribeEvent(event)                      // Process subscription
handleSubGiftEvent(event)                        // Process gift subs
handleSubMessageEvent(event)                     // Process resubs
handleCheerEvent(event)                          // Process cheers
unsubscribeFromUser(userId)                      // Clean up subscriptions
```

#### **database.js**
- Manages event-to-alert mappings
- Stores default and custom mappings per user
- Retrieves alert configuration for event types

**Key Methods:**
```javascript
getDefaultEventMappings()                        // Default mappings
initializeEventMappings(userId)                  // Set up user defaults
getEventMappings(userId)                         // Get user's mappings
saveEventMappings(userId, mappings)              // Save custom mappings
getAlertForEvent(userId, eventType)              // Get alert config
```

#### **alertRoutes.js**
- REST API for managing event mappings
- Validates mappings and ensures alert types exist

**Endpoints:**
```javascript
GET    /api/alerts/event-mappings                // Get current mappings
PUT    /api/alerts/event-mappings                // Update mappings
POST   /api/alerts/event-mappings/reset          // Reset to defaults
```

#### **server.js**
- Receives events from streamMonitor
- Broadcasts to connected clients via Socket.io
- Sends to user-specific rooms (`user:${userId}`)

**Socket.io Events:**
```javascript
streamMonitor.on('newSubscription', ...)         // Subscribe event
streamMonitor.on('newGiftSub', ...)              // Gift sub event
streamMonitor.on('newResub', ...)                // Resub event
streamMonitor.on('newCheer', ...)                // Cheer event
```

## Data Structures

### Event Mappings (Database)

```javascript
{
  "channel.follow": "follow",
  "channel.subscribe": "subscription",
  "channel.subscription.gift": "giftsub",
  "channel.subscription.message": "resub",
  "channel.cheer": "bits",
  "channel.raid": "raid"
}
```

### Alert Configuration

```javascript
{
  "id": "alert-123",
  "userId": "12345678",
  "name": "Default Subscription Alert",
  "type": "subscription",
  "enabled": true,
  "message": "Thanks for subscribing {username}!",
  "soundUrl": "/sounds/subscribe.mp3",
  "soundVolume": 0.5,
  "imageUrl": "/images/sub-badge.png",
  "imagePosition": "center",
  "textColor": "#FFFFFF",
  "backgroundColor": "#9147FF",
  "fontSize": 48,
  "fontFamily": "Arial",
  "displayDuration": 5000,
  "animationIn": "fadeIn",
  "animationOut": "fadeOut",
  "customCSS": "",
  "createdAt": "2024-01-01T00:00:00.000Z",
  "updatedAt": "2024-01-01T00:00:00.000Z"
}
```

### Socket.io Event Payloads

#### Subscription Event
```javascript
{
  userId: "12345678",
  username: "streamer_name",
  subscriber: "subscriber_name",
  tier: "1000",  // 1000=Tier 1, 2000=Tier 2, 3000=Tier 3
  isGift: false,
  timestamp: "2024-01-01T12:00:00.000Z",
  alertConfig: { /* Alert object */ }
}
```

#### Gift Sub Event
```javascript
{
  userId: "12345678",
  username: "streamer_name",
  gifter: "gifter_name",
  amount: 5,
  tier: "1000",
  timestamp: "2024-01-01T12:00:00.000Z",
  alertConfig: { /* Alert object */ }
}
```

#### Resub Event
```javascript
{
  userId: "12345678",
  username: "streamer_name",
  subscriber: "subscriber_name",
  tier: "1000",
  months: 12,
  streakMonths: 6,
  message: "Love this stream!",
  timestamp: "2024-01-01T12:00:00.000Z",
  alertConfig: { /* Alert object */ }
}
```

#### Cheer Event
```javascript
{
  userId: "12345678",
  username: "streamer_name",
  cheerer: "cheerer_name",
  bits: 100,
  message: "Great content! cheer100",
  isAnonymous: false,
  timestamp: "2024-01-01T12:00:00.000Z",
  alertConfig: { /* Alert object */ }
}
```

## API Usage

### Get Event Mappings

```bash
GET /api/alerts/event-mappings
Authorization: Bearer <jwt_token>
```

**Response:**
```json
{
  "mappings": {
    "channel.follow": "follow",
    "channel.subscribe": "subscription"
  },
  "defaultMappings": { /* ... */ },
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

### Update Event Mappings

```bash
PUT /api/alerts/event-mappings
Authorization: Bearer <jwt_token>
Content-Type: application/json

{
  "channel.follow": "custom-follow-alert",
  "channel.subscribe": "custom-sub-alert"
}
```

**Response:**
```json
{
  "success": true,
  "mappings": { /* updated mappings */ }
}
```

### Reset to Defaults

```bash
POST /api/alerts/event-mappings/reset
Authorization: Bearer <jwt_token>
```

**Response:**
```json
{
  "success": true,
  "mappings": { /* default mappings */ }
}
```

## Frontend Integration

### Socket.io Client Setup

```javascript
import io from 'socket.io-client';

const socket = io('https://your-api-url.com', {
  auth: {
    token: jwtToken
  }
});

// Listen for subscription alerts
socket.on('newSubscription', (data) => {
  console.log('New subscriber:', data.subscriber);
  displayAlert(data.alertConfig, data);
});

// Listen for gift sub alerts
socket.on('newGiftSub', (data) => {
  console.log('Gift subs:', data.gifter, data.amount);
  displayAlert(data.alertConfig, data);
});

// Listen for resub alerts
socket.on('newResub', (data) => {
  console.log('Resub:', data.subscriber, data.months, 'months');
  displayAlert(data.alertConfig, data);
});

// Listen for cheer alerts
socket.on('newCheer', (data) => {
  console.log('Cheer:', data.cheerer, data.bits, 'bits');
  displayAlert(data.alertConfig, data);
});

// Generic custom alert listener
socket.on('customAlert', (data) => {
  // data.type = 'subscription' | 'giftsub' | 'resub' | 'bits' | etc.
  displayCustomAlert(data);
});
```

### Display Alert Function

```javascript
function displayAlert(alertConfig, eventData) {
  if (!alertConfig || !alertConfig.enabled) return;

  // Replace placeholders in message
  let message = alertConfig.message;
  message = message.replace('{username}', eventData.subscriber || eventData.gifter || eventData.cheerer);
  message = message.replace('{amount}', eventData.amount || eventData.bits || '');
  message = message.replace('{months}', eventData.months || '');
  message = message.replace('{tier}', getTierName(eventData.tier));

  // Create alert element
  const alertElement = document.createElement('div');
  alertElement.className = `alert-container ${alertConfig.animationIn}`;
  alertElement.style.cssText = `
    background-color: ${alertConfig.backgroundColor};
    color: ${alertConfig.textColor};
    font-size: ${alertConfig.fontSize}px;
    font-family: ${alertConfig.fontFamily};
  `;
  alertElement.textContent = message;

  // Add image if configured
  if (alertConfig.imageUrl) {
    const img = document.createElement('img');
    img.src = alertConfig.imageUrl;
    img.className = `alert-image ${alertConfig.imagePosition}`;
    alertElement.appendChild(img);
  }

  // Play sound if configured
  if (alertConfig.soundUrl) {
    const audio = new Audio(alertConfig.soundUrl);
    audio.volume = alertConfig.soundVolume;
    audio.play();
  }

  // Display alert
  document.body.appendChild(alertElement);

  // Remove after duration
  setTimeout(() => {
    alertElement.className = `alert-container ${alertConfig.animationOut}`;
    setTimeout(() => alertElement.remove(), 1000);
  }, alertConfig.displayDuration);
}

function getTierName(tier) {
  const tiers = {
    '1000': 'Tier 1',
    '2000': 'Tier 2',
    '3000': 'Tier 3'
  };
  return tiers[tier] || 'Prime';
}
```

## Configuration UI Example

```javascript
function EventMappingManager() {
  const [mappings, setMappings] = useState({});
  const [availableEvents, setAvailableEvents] = useState([]);

  // Fetch current mappings
  useEffect(() => {
    fetch('/api/alerts/event-mappings', {
      headers: { 'Authorization': `Bearer ${token}` }
    })
      .then(res => res.json())
      .then(data => {
        setMappings(data.mappings);
        setAvailableEvents(data.availableEvents);
      });
  }, []);

  // Update mapping
  const updateMapping = async (eventType, alertType) => {
    const newMappings = { ...mappings, [eventType]: alertType };

    const response = await fetch('/api/alerts/event-mappings', {
      method: 'PUT',
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(newMappings)
    });

    const data = await response.json();
    setMappings(data.mappings);
  };

  // Reset to defaults
  const resetMappings = async () => {
    const response = await fetch('/api/alerts/event-mappings/reset', {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${token}` }
    });

    const data = await response.json();
    setMappings(data.mappings);
  };

  return (
    <div>
      <h2>Event to Alert Mappings</h2>
      {availableEvents.map(event => (
        <div key={event}>
          <label>{event}</label>
          <select
            value={mappings[event] || ''}
            onChange={(e) => updateMapping(event, e.target.value)}
          >
            <option value="">No Alert</option>
            {/* Populate with user's alerts */}
          </select>
        </div>
      ))}
      <button onClick={resetMappings}>Reset to Defaults</button>
    </div>
  );
}
```

## Testing

### 1. **Test Event Subscriptions**

Monitor server logs when user authenticates:
```
✅ EventSub listener created for user: streamer_name
✅ Subscribed to channel.subscribe for user streamer_name
✅ Subscribed to channel.subscription.gift for user streamer_name
✅ Subscribed to channel.subscription.message for user streamer_name
✅ Subscribed to channel.cheer for user streamer_name
```

### 2. **Test with Twitch CLI**

```bash
# Install Twitch CLI
scoop install twitch-cli

# Trigger subscribe event
twitch event trigger subscribe -F https://your-api.com/webhooks/callback

# Trigger gift sub event
twitch event trigger subscription-gift -F https://your-api.com/webhooks/callback

# Trigger cheer event
twitch event trigger cheer -F https://your-api.com/webhooks/callback
```

### 3. **Manual Testing**

1. Login to application as streamer
2. Open browser overlay in separate window
3. Perform test actions on Twitch channel:
   - Have someone follow (or use test account)
   - Subscribe (or use Twitch's test subscription)
   - Gift subs
   - Cheer bits

## Troubleshooting

### Events Not Received

1. **Check EventSub listener status:**
   ```javascript
   // In streamMonitor.js, verify listener is created
   console.log('Active listeners:', this.listeners.size);
   ```

2. **Verify access token has required scopes:**
   - `moderator:read:followers` (for follows)
   - `channel:read:subscriptions` (for subs)
   - No special scope for cheers (part of chat)

3. **Check Twitch EventSub connection:**
   - Look for WebSocket connection logs
   - Verify no authentication errors

### Alerts Not Displaying

1. **Check alert configuration:**
   ```bash
   GET /api/alerts/event-mappings
   ```

2. **Verify alert is enabled:**
   ```javascript
   const alert = await database.getAlertForEvent(userId, 'channel.subscribe');
   console.log('Alert enabled:', alert?.enabled);
   ```

3. **Check Socket.io connection:**
   ```javascript
   // In frontend
   socket.on('connect', () => console.log('Connected'));
   socket.on('disconnect', () => console.log('Disconnected'));
   ```

### Database Issues

1. **Check event mappings initialization:**
   ```javascript
   // Should run on user first login
   await database.initializeEventMappings(userId);
   ```

2. **Verify mappings exist:**
   ```javascript
   const mappings = await database.getEventMappings(userId);
   console.log('User mappings:', mappings);
   ```

## Security Considerations

- ✅ **User-scoped tokens**: Each EventSub listener uses the user's access token
- ✅ **Room isolation**: Socket.io broadcasts only to `user:${userId}` rooms
- ✅ **Authentication**: All API endpoints require valid JWT
- ✅ **Data isolation**: Event mappings stored per user in database
- ✅ **Token refresh**: Automatic refresh of expired Twitch tokens

## Future Enhancements

- [ ] Channel points redemption handling (`channel.channel_points_custom_reward_redemption.add`)
- [ ] Sub tier upgrade events
- [ ] Hype train events
- [ ] Poll/prediction events
- [ ] Mod actions (ban, timeout, etc.)
- [ ] Stream markers on events
- [ ] Analytics dashboard for event frequency
- [ ] A/B testing different alert styles
- [ ] Event replay/history viewer

## Resources

- [Twitch EventSub Documentation](https://dev.twitch.tv/docs/eventsub/)
- [Twurple EventSub WebSocket Docs](https://twurple.js.org/docs/eventsub-ws/)
- [Socket.io Documentation](https://socket.io/docs/v4/)
- [Azure Table Storage Docs](https://learn.microsoft.com/en-us/azure/storage/tables/)

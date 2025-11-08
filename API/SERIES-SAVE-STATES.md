# Series Save States Feature

## Overview

The Series Save States feature allows broadcasters to save and reload counter values for different stream series or gameplay sessions. This is perfect for episodic content where you want to maintain continuity across multiple streams.

## Use Cases

- **Game Series**: Save counter progress for "Elden Ring Episode 5" and reload it next stream
- **Challenge Runs**: Track different playthroughs separately (e.g., "Nuzlocke Run", "Hardcore Mode")
- **Multi-Game Streams**: Switch between different games with their own counter states
- **Session Comparison**: Compare death/swear counts across different sessions

## API Endpoints

### Save Current Counter State
```http
POST /api/counters/series/save
Authorization: Bearer <JWT>
Content-Type: application/json

{
  "seriesName": "Elden Ring Episode 5",
  "description": "Fighting Malenia" (optional)
}
```

**Response:**
```json
{
  "message": "Series saved successfully",
  "save": {
    "seriesId": "1699564823456_Elden_Ring_Episode_5",
    "seriesName": "Elden Ring Episode 5",
    "description": "Fighting Malenia",
    "deaths": 42,
    "swears": 87,
    "bits": 150,
    "savedAt": "2024-11-06T12:30:00.000Z"
  }
}
```

### Load a Series Save
```http
POST /api/counters/series/load
Authorization: Bearer <JWT>
Content-Type: application/json

{
  "seriesId": "1699564823456_Elden_Ring_Episode_5"
}
```

**Response:**
```json
{
  "message": "Series loaded successfully",
  "counters": {
    "deaths": 42,
    "swears": 87,
    "bits": 150,
    "lastUpdated": "2024-11-06T14:00:00.000Z"
  },
  "seriesInfo": {
    "seriesName": "Elden Ring Episode 5",
    "description": "Fighting Malenia",
    "savedAt": "2024-11-06T12:30:00.000Z"
  }
}
```

### List All Series Saves
```http
GET /api/counters/series/list
Authorization: Bearer <JWT>
```

**Response:**
```json
{
  "count": 3,
  "saves": [
    {
      "seriesId": "1699564823456_Elden_Ring_Episode_5",
      "seriesName": "Elden Ring Episode 5",
      "description": "Fighting Malenia",
      "deaths": 42,
      "swears": 87,
      "bits": 150,
      "savedAt": "2024-11-06T12:30:00.000Z"
    },
    {
      "seriesId": "1699450000000_Dark_Souls_3_Ep_2",
      "seriesName": "Dark Souls 3 Ep 2",
      "description": "",
      "deaths": 28,
      "swears": 56,
      "bits": 0,
      "savedAt": "2024-11-05T18:15:00.000Z"
    }
  ]
}
```

### Delete a Series Save
```http
DELETE /api/counters/series/:seriesId
Authorization: Bearer <JWT>
```

**Response:**
```json
{
  "message": "Series save deleted successfully"
}
```

## Twitch Chat Commands

### Broadcaster/Mod Commands

#### Save Current State
```
!saveseries <series name>
```
**Example:**
```
!saveseries Elden Ring Episode 5
```
**Bot Response:**
```
💾 Series saved: "Elden Ring Episode 5" (Deaths: 42, Swears: 87)
```

#### Load a Save State
```
!loadseries <series ID>
```
**Example:**
```
!loadseries 1699564823456_Elden_Ring_Episode_5
```
**Bot Response:**
```
📂 Series loaded: "Elden Ring Episode 5" (Deaths: 42, Swears: 87)
```

#### List Available Saves
```
!listseries
```
**Bot Response:**
```
📋 Recent saves: 1. "Elden Ring Episode 5" (11/6/2024) - ID: 1699564823456_Elden_Ring_Episode_5 | 2. "Dark Souls 3 Ep 2" (11/5/2024) - ID: 1699450000000_Dark_Souls_3_Ep_2 | Total: 3
```

#### Delete a Save
```
!deleteseries <series ID>
```
**Example:**
```
!deleteseries 1699564823456_Elden_Ring_Episode_5
```
**Bot Response:**
```
🗑️  Series save deleted: 1699564823456_Elden_Ring_Episode_5
```

## How It Works

### Database Storage

**Azure Table Storage (Production):**
- Table: `seriessaves`
- Partition Key: `twitchUserId`
- Row Key: `seriesId` (timestamp + sanitized series name)

**Local JSON (Development):**
- File: `API/data/series_saves.json`
- Structure:
```json
{
  "twitchUserId": {
    "seriesId": {
      "partitionKey": "twitchUserId",
      "rowKey": "seriesId",
      "seriesName": "Elden Ring Episode 5",
      "description": "Fighting Malenia",
      "deaths": 42,
      "swears": 87,
      "bits": 150,
      "savedAt": "2024-11-06T12:30:00.000Z"
    }
  }
}
```

### Multi-Tenant Isolation

- All saves are scoped to the user's `twitchUserId`
- Users can only access their own series saves
- No cross-contamination between different streamers

### Real-Time Sync

When a series is loaded:
1. Counter values are restored from the save
2. WebSocket event broadcasts to all connected devices
3. Overlay, browser sources, and mobile apps all update instantly

## Workflow Example

### Starting a New Series Episode

1. **Before Stream:**
   - Check available saves: `!listseries`
   - Load previous episode: `!loadseries 1699564823456_Elden_Ring_Episode_5`
   - Counters restore to: Deaths: 42, Swears: 87

2. **During Stream:**
   - Play game, counters increment normally
   - End stream with: Deaths: 67, Swears: 103

3. **After Stream:**
   - Save progress: `!saveseries Elden Ring Episode 6`
   - New save created with current counter values

### Managing Multiple Series

**Save different games:**
```
!saveseries Elden Ring Episode 5
!saveseries Dark Souls 3 Run 2
!saveseries Hollow Knight Hardcore
```

**Switch between series:**
```
!listseries
!loadseries <appropriate_series_id>
```

## Best Practices

### Naming Conventions

✅ **Good:**
- `Elden Ring Episode 5`
- `Nuzlocke Run - Session 3`
- `DS3 SL1 Attempt 2`

❌ **Avoid:**
- Very long names (>100 characters)
- Special characters that make IDs hard to read
- Generic names like "Stream 1", "Save 1"

### Save Management

- **Save at the end of each stream** to preserve progress
- **Delete old saves** you no longer need
- **Use descriptive names** so you remember what each save is for
- **Check !listseries** before starting a series stream

### Series ID Format

Series IDs are automatically generated:
```
<timestamp>_<sanitized_series_name>
```

Example:
- Series Name: `Elden Ring Episode 5`
- Series ID: `1699564823456_Elden_Ring_Episode_5`

## Error Handling

### Common Errors

**Series not found:**
```
❌ Series save not found. Use !listseries to see available saves.
```
Solution: Check the series ID is correct

**Invalid series name:**
```
❌ Series name is required
```
Solution: Provide a name after `!saveseries`

**Failed to save:**
```
❌ Failed to save series. Please try again.
```
Solution: Check database connection, try again

## Integration with Frontend

The React frontend includes a dedicated Series Save Manager UI accessible from the user portal.

### Accessing the Series Manager

1. Login to your user portal
2. Click the **💾 Series Saves** button
3. The Series Save Manager modal will open

### UI Features

**Save Current State:**
- Click "💾 Save Current State"
- Enter a series name (required)
- Optionally add a description
- Click "Save Series"

**View Saved Series:**
- All your series saves are listed with:
  - Series name and description
  - Death/Swear/Bits counts
  - Save date and timestamp
  - Series ID

**Load a Series:**
- Click "📂 Load" on any series
- Confirm the action
- Your counters will be restored
- All connected devices update instantly

**Delete a Series:**
- Click "🗑️" (trash icon) on any series
- Confirm deletion
- The save is permanently removed

### API Integration Example

The frontend uses these API calls:

```javascript
// Save current state
const saveSeries = async (seriesName, description) => {
  const response = await fetch('/api/counters/series/save', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    },
    body: JSON.stringify({ seriesName, description })
  });
  return await response.json();
};

// Load a save
const loadSeries = async (seriesId) => {
  const response = await fetch('/api/counters/series/load', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    },
    body: JSON.stringify({ seriesId })
  });
  return await response.json();
};

// List saves
const listSeries = async () => {
  const response = await fetch('/api/counters/series/list', {
    headers: {
      'Authorization': `Bearer ${token}`
    }
  });
  return await response.json();
};

// Delete a save
const deleteSeries = async (seriesId) => {
  const response = await fetch(`/api/counters/series/${seriesId}`, {
    method: 'DELETE',
    headers: {
      'Authorization': `Bearer ${token}`
    }
  });
  return await response.json();
};
```

## Testing

### Local Testing

1. **Ensure test data directory exists:**
```powershell
cd API
npm run task "Ensure Test Data"
```

2. **Start the server:**
```powershell
npm run dev
```

3. **Test via Twitch chat:**
```
!saveseries Test Series 1
!listseries
!loadseries <series_id_from_list>
!deleteseries <series_id>
```

### API Testing

```bash
# Save series
curl -X POST http://localhost:3000/api/counters/series/save \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"seriesName": "Test Series", "description": "Testing"}'

# List series
curl http://localhost:3000/api/counters/series/list \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"

# Load series
curl -X POST http://localhost:3000/api/counters/series/load \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"seriesId": "YOUR_SERIES_ID"}'
```

## Future Enhancements

Potential improvements:
- **Auto-save on stream end** - Automatically save with timestamp
- **Series templates** - Pre-configured counter starting values
- **Series notes** - Add detailed notes to each save
- **Export/import** - Share series saves between users
- **Series history** - Track all changes to a series over time
- **Series analytics** - Compare stats across different episodes

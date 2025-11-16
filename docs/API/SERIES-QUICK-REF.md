# Series Save States - Quick Reference

## What is it?
Save and reload your counter values for different stream series or gameplay sessions. Perfect for episodic content!

## Quick Commands

### Save Your Progress
```
!saveseries Elden Ring Episode 5
```
Saves current death/swear counts with a name.

### See Your Saves
```
!listseries
```
Shows your 3 most recent saves with their IDs.

### Load a Previous Save
```
!loadseries 1699564823456_Elden_Ring_Episode_5
```
Restores counters from that save.

### Delete Old Saves
```
!deleteseries 1699564823456_Elden_Ring_Episode_5
```
Removes a save you don't need anymore.

## Typical Workflow

### Starting Episode 1
```
Stream starts → Play game → Deaths: 15
End stream → !saveseries Elden Ring Episode 1
```

### Continuing Episode 2
```
New stream starts → !listseries
Copy the series ID for Episode 1
!loadseries <id>
Counters restore to Deaths: 15
Continue playing → Deaths: 27
End stream → !saveseries Elden Ring Episode 2
```

### Switching Games Mid-Stream
```
Playing Elden Ring → Deaths: 42
!saveseries Elden Ring Session
Switch to Dark Souls → Deaths: 28
!saveseries Dark Souls Session

Later: !loadseries <elden_ring_id>
Back to Deaths: 42!
```

## Tips

✅ **Good Series Names:**
- "Elden Ring Episode 5"
- "DS3 SL1 Run - Session 3"
- "Nuzlocke Attempt 2"

❌ **Avoid:**
- Very long names
- Generic names like "Save 1"

## Need Help?

Full documentation: [SERIES-SAVE-STATES.md](SERIES-SAVE-STATES.md)

Bot not responding? Make sure you're a mod or broadcaster!

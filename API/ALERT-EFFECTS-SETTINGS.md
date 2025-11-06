# Alert Effects Settings System

## Overview

The alert effects settings system provides granular control over which visual and audio effects are displayed in stream overlays. Settings are stored in the browser's localStorage and synchronized between the dashboard and overlay.

## Components

### 1. AsylumEffects Settings (Backend)
**Location**: `API/frontend/asylum-effects.js`

**Settings Object**:
```javascript
{
  enableSound: true,           // 🔊 Sound effects (WAV files)
  enableAnimations: true,      // 🎬 CSS keyframe animations
  enableParticles: true,       // ✨ Canvas particle systems
  enableScreenEffects: true,   // 📺 Screen shake, flicker, red alert
  enableSVGFilters: true,      // 🌫️ SVG masks and filters
  enableTextEffects: true      // 🔤 Text scrambling and glitch
}
```

**Methods**:
- `loadSettings()` - Loads settings from localStorage on init
- `saveSettings()` - Saves settings to localStorage
- `toggleSetting(name, enabled)` - Updates individual setting
- `getSettings()` - Returns copy of current settings
- `resetSettings()` - Restores all defaults
- `setVolume(0-1)` - Adjusts audio volume (stored separately)

**LocalStorage Keys**:
- `asylumEffectsSettings` - JSON string of settings object
- `asylumEffectsVolume` - Integer 0-100 for volume percentage

### 2. AlertEffectsSettings Component (Frontend UI)
**Location**: `modern-frontend/src/components/AlertEffectsSettings.jsx`

**Features**:
- Toggle switches for all 6 effect types
- Volume slider (0-100%)
- Real-time localStorage sync
- Reset to defaults button
- Visual feedback with icons and descriptions

**Props**:
- `onClose` - Function to call when closing the modal

### 3. Integration in Main App
**Location**: `modern-frontend/src/App.jsx`

**Added**:
- Import: `AlertEffectsSettings` component
- State: `showAlertEffectsSettings`
- Button: "🎭 Alert Effects" in dashboard
- Modal: Full-screen overlay with settings component

## How It Works

### Flow Diagram
```
User Dashboard → Toggle Setting → localStorage → Overlay Reload → AsylumEffects.loadSettings()
```

### Step-by-Step

1. **User Opens Dashboard**
   - Clicks "🎭 Alert Effects" button
   - Modal opens with AlertEffectsSettings component

2. **User Adjusts Settings**
   - Toggles effects on/off
   - Adjusts volume slider
   - Changes save to localStorage immediately

3. **Overlay Loads**
   - asylum-effects.js constructor calls init()
   - init() calls loadSettings()
   - Settings loaded from localStorage
   - Effects system configured

4. **Alert Triggers**
   - triggerEffect() receives alert data
   - Checks each setting before applying effects
   - Only enabled effects are displayed

### Effect Checks in triggerEffect()

```javascript
// Sound
if (this.settings.enableSound && effects.soundTrigger) {
  this.playSound(effects.soundTrigger);
}

// Animations
if (this.settings.enableAnimations && effects.animation) {
  // Apply CSS animation
}

// SVG Filters
if (this.settings.enableSVGFilters && effects.svgMask) {
  // Apply SVG mask/filter
}

// Particles
if (this.settings.enableParticles && effects.particle) {
  // Create particle system
}

// Screen Effects
if (this.settings.enableScreenEffects) {
  if (effects.screenShake) { /* shake */ }
  if (effects.screenFlicker) { /* flicker */ }
  if (effects.redAlert) { /* red alert */ }
}

// Text Effects
if (this.settings.enableTextEffects && effects.textScramble) {
  // Apply text scramble
}
```

## Testing

### Test Page
**Location**: `API/frontend/effects-test.html`

**Access**: http://localhost:3000/effects-test.html

**Features**:
- Interactive toggle controls for all settings
- Volume slider with live feedback
- Test buttons for all 7 default alert types
- Real-time settings display (JSON)
- Syncs with same localStorage as overlay

### Test Workflow

1. **Start API Server**:
   ```powershell
   cd "c:\Game Data\Coding Projects\doc-omni\API"
   npm start
   ```

2. **Open Test Page**:
   - Navigate to http://localhost:3000/effects-test.html

3. **Test Individual Effects**:
   - Disable "Sound Effects" → Click "Test Follow" → No sound plays
   - Disable "Particles" → Click "Test Subscribe" → No sparks appear
   - Disable "Screen Effects" → Click "Test Raid" → No screen shake
   - Disable "SVG Filters" → Click "Test Follow" → No fog effect

4. **Test Volume Control**:
   - Set volume to 0% → Click any test → No sound
   - Set volume to 100% → Click any test → Full volume
   - Ensure sound is enabled first

5. **Test Persistence**:
   - Change settings on test page
   - Refresh page → Settings persist
   - Open overlay in OBS → Same settings apply

## Default Alert Effects

| Event      | Animation      | SVG Mask           | Particles | Screen     | Sound            |
| ---------- | -------------- | ------------------ | --------- | ---------- | ---------------- |
| Follow     | doorCreak      | fog                | dust      | shake      | doorCreak.wav    |
| Subscribe  | electricPulse  | glassDistortion    | sparks    | flicker    | electroshock.wav |
| Resub      | typewriter     | paperTexture       | ink       | -          | typewriter.wav   |
| Bits       | pillScatter    | -                  | pills     | colorShift | pillRattle.wav   |
| Raid       | sirenFlash     | -                  | chaos     | redAlert   | alarm.wav        |
| Gift Sub   | heartbeatPulse | silhouette         | hearts    | -          | heartMonitor.wav |
| Hype Train | wheelchairRoll | hallwayPerspective | smoke     | -          | hypeTrain.wav    |

## User Instructions

### For Streamers

1. **Access Settings**:
   - Login to dashboard (http://localhost:3000 or Azure URL)
   - Click "🎭 Alert Effects" button

2. **Customize Effects**:
   - Toggle any effect type on/off
   - Adjust volume to preference
   - Settings save automatically

3. **Reset to Defaults**:
   - Click "🔄 Reset to Defaults" button
   - All toggles enabled, volume 70%

4. **Apply to Overlay**:
   - Settings sync automatically
   - Refresh OBS browser source if needed
   - Changes apply to next alert

### For Developers

**Adding New Effect Types**:

1. Add setting to asylum-effects.js constructor:
   ```javascript
   this.settings = {
     // ...existing settings
     enableNewEffect: true
   };
   ```

2. Add check in triggerEffect():
   ```javascript
   if (this.settings.enableNewEffect && effects.newEffect) {
     // Apply new effect
   }
   ```

3. Add toggle to AlertEffectsSettings.jsx:
   ```javascript
   {
     key: 'enableNewEffect',
     icon: '🆕',
     title: 'New Effect',
     description: 'Description of new effect'
   }
   ```

## Troubleshooting

### Settings Not Persisting
**Problem**: Settings reset on page refresh
**Solution**: Check browser allows localStorage, not in private mode

### Settings Not Syncing to Overlay
**Problem**: Dashboard settings don't affect overlay
**Solution**: Both use same localStorage domain, refresh overlay browser source

### Volume Not Working
**Problem**: Volume slider changes but sound still plays at full volume
**Solution**: Ensure "Sound Effects" toggle is enabled, check browser audio permissions

### Effects Still Playing When Disabled
**Problem**: Disabled effects still appear
**Solution**: Hard refresh overlay (Ctrl+Shift+R), clear localStorage and reload

### localStorage Full
**Problem**: Settings fail to save
**Solution**: Clear other localStorage data, browser quota exceeded

## Performance Considerations

- **Disable Particles**: Significant performance boost on low-end systems
- **Disable SVG Filters**: Reduces GPU usage
- **Disable Screen Effects**: Eliminates shake/flicker overhead
- **Lower Volume**: Doesn't affect performance, just audio playback

## Files Modified/Created

### Created:
- ✅ `modern-frontend/src/components/AlertEffectsSettings.jsx` - Settings UI component
- ✅ `modern-frontend/src/components/AlertEffectsSettings.css` - Component styles
- ✅ `API/frontend/effects-test.html` - Interactive test page

### Modified:
- ✅ `API/frontend/asylum-effects.js` - Added settings system and checks
- ✅ `modern-frontend/src/App.jsx` - Integrated settings modal
- ✅ `API/database.js` - Default alerts have effects objects (already done)

## Next Steps

1. **Build React Frontend**:
   ```powershell
   cd "c:\Game Data\Coding Projects\doc-omni\modern-frontend"
   npm run build
   ```

2. **Copy to API**:
   ```powershell
   Copy-Item -Path "dist\*" -Destination "..\API\frontend" -Recurse -Force
   ```

3. **Test Locally**:
   - Start API server
   - Login to dashboard
   - Test settings UI
   - Verify overlay respects settings

4. **Deploy to Azure**:
   ```powershell
   cd "c:\Game Data\Coding Projects\doc-omni\API"
   docker build -t omniforgeacr.azurecr.io/omniforgestream-api:latest .
   docker push omniforgeacr.azurecr.io/omniforgestream-api:latest
   az containerapp update --name omniforgestream-api-prod --resource-group Streamer-Tools-RG --image omniforgeacr.azurecr.io/omniforgestream-api:latest --revision-suffix $(Get-Date -Format "MMddHHmm")
   ```

## Summary

The alert effects settings system provides complete control over visual and audio effects with:
- ✅ 6 toggle controls (sound, animations, particles, screen, SVG, text)
- ✅ Volume slider (0-100%)
- ✅ localStorage persistence
- ✅ Dashboard UI component
- ✅ Test page for verification
- ✅ Automatic sync between dashboard and overlay
- ✅ Reset to defaults functionality
- ✅ Real-time effect filtering in triggerEffect()

All effects can now be customized per user preference while maintaining the asylum theme experience! 🎭

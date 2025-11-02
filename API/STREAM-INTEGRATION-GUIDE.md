# 🎬 Stream Integration Guide - OmniAsylum Counter System

This guide explains how to set up the new auto-popup features and stream overlay integration.

## 🎯 New Features Overview

### 1. **💎 Stream Bits Counter**
- Separate bits counter that tracks donations during current stream
- Resets automatically when stream starts (or manually)
- Configurable bit thresholds for different effects
- Optional auto-increment of death/swear counters based on bit amounts
- Celebration effects and thank you messages

### 2. **📺 Stream Overlay (Browser Source)**
- Real-time counter display for OBS/streaming software
- Animated pop-ups when counters change
- Bits celebration effects
- Transparent background, perfect for overlays

### 3. **🎉 Alert Animations**
- Animated notifications when counters update
- Custom animations for different events
- Configurable display duration and styles

### 4. **💜 Subscriber Celebrations**
- Animated celebrations for new subscribers
- Special effects for resubs showing month count
- Gift subscription recognition
- Floating hearts and banner notifications

## 🔧 Setup Instructions

### Step 1: Enable Features (Admin Required)

As an admin, enable these features for streamers:

1. Login to admin dashboard
2. Find the streamer in user management
3. Enable the following features:
   - ✅ **Bits Auto-Counter** - For automatic bit detection
   - ✅ **Stream Overlay** - For OBS browser source
   - ✅ **Alert Animations** - For pop-up notifications

### Step 2: Get Your Overlay URL

Each streamer gets a unique overlay URL:
```
http://your-domain.com/overlay/YOUR_TWITCH_USER_ID
```

**To find your Twitch User ID:**
1. Go to https://www.streamweasels.com/tools/convert-twitch-username-to-user-id/
2. Enter your Twitch username
3. Copy the User ID number

### Step 3: Add to OBS/Streaming Software

#### For OBS Studio:
1. **Add Browser Source**:
   - Click "+" in Sources
   - Select "Browser"
   - Name it "Counter Overlay"

2. **Configure Browser Source**:
   - **URL**: `http://localhost:3000/overlay/YOUR_USER_ID`
   - **Width**: 400
   - **Height**: 300
   - **Custom CSS**: Leave blank
   - **Shutdown source when not visible**: ✅
   - **Refresh browser when scene becomes active**: ✅

3. **Position the Overlay**:
   - Drag to desired screen position
   - Resize as needed
   - The background is transparent

#### For Streamlabs OBS:
1. **Add Browser Source**:
   - Click "+" in Sources
   - Select "Browser Source"

2. **Settings**:
   - **URL**: Your overlay URL
   - **Width**: 400, **Height**: 300
   - **Custom CSS**: Not needed

#### For XSplit:
1. **Add Web Source**:
   - Right-click scene
   - Add source → Web page
   - Enter your overlay URL

## 🎮 How It Works

### Bits Integration
When a viewer donates bits in your chat:
- **10+ bits**: Bot thanks the viewer in chat
- **All bits**: Animated celebration particles fall on overlay
- **No Counter Changes**: Bits do NOT automatically increment death/swear counters
- **Manual Control**: Use mod commands to change counters manually

### Manual Counter Commands (Mods/Broadcaster Only)
- `!death+` or `!d+` - Increment deaths
- `!death-` or `!d-` - Decrement deaths
- `!swear+` or `!s+` - Increment swears
- `!swear-` or `!s-` - Decrement swears
- `!resetcounters` - Reset death/swear counters to zero
- `!startstream` - Start new stream session (resets bits counter)
- `!endstream` - End current stream session
- `!resetbits` - Reset bits counter to zero### Public Commands (Anyone)
- `!deaths` - Show current death count
- `!swears` - Show current swear count
- `!stats` - Show all stats

### Overlay Features
- **Real-time Updates**: Changes instantly when counters update
- **Animated Alerts**: Pop-up notifications for counter changes
- **Bits Celebrations**: Special effects for bit donations
- **Responsive Design**: Works on any screen size
- **Transparent Background**: Blends seamlessly with your stream

## 🎨 Customization Options

### Overlay Positioning
The overlay is designed to be flexible:
- **Top Corner**: Shows counters without blocking gameplay
- **Bottom Bar**: Horizontal layout option
- **Side Panel**: Vertical stack of counters

### Animation Settings
Admins can configure:
- Animation duration (2-5 seconds)
- Celebration intensity (particle count)
- Alert frequency (prevent spam)

## 🔧 Technical Requirements

### Twitch Permissions
Your bot needs these Twitch scopes:
- `chat:read` - Read chat messages ✅
- `chat:edit` - Send chat responses ✅
- `bits:read` - Detect bit donations (NEW)

### Network Requirements
- Overlay must be accessible from your streaming computer
- If using cloud hosting, ensure URL is publicly accessible
- Local development: `http://localhost:3000/overlay/YOUR_ID`

## 📊 Analytics Integration

When analytics feature is enabled:
- Track total bits received
- Monitor counter trends over time
- View most active chatters
- Export data for insights

## 🚨 Troubleshooting

### Overlay Not Showing
1. **Check URL**: Ensure correct user ID in URL
2. **Feature Enabled**: Verify "Stream Overlay" is enabled for user
3. **Browser Source**: Refresh the browser source in OBS
4. **Network**: Check if overlay URL loads in regular browser

### Bits Not Working
1. **Feature Enabled**: Verify "Bits Auto-Counter" is enabled
2. **Bot Connected**: Ensure Twitch bot is connected and authenticated
3. **Permissions**: Check if bot has bits:read scope
4. **Test**: Use small bit donation to test (minimum 50 bits)

### No Animations
1. **Feature Check**: Verify "Alert Animations" is enabled
2. **Browser Support**: Some older browsers may not support CSS animations
3. **Performance**: Lower particle count if stream performance is affected

## 🎯 Best Practices

### For Streamers
1. **Test First**: Test overlay in a preview scene before going live
2. **Position Carefully**: Don't block important gameplay elements
3. **Inform Viewers**: Let viewers know about bit integration
4. **Moderate Bits**: Set reasonable bit thresholds for counter increments

### For Mods
1. **Use Commands Sparingly**: Don't spam counter commands
2. **Coordinate**: Communicate with other mods about counter management
3. **Monitor Chat**: Watch for viewers asking about commands

## 🔮 Future Features

Coming soon:
- Custom counter types (beyond deaths/swears)
- Sound effects for counter changes
- Integration with StreamElements/StreamLabs
- Mobile app for remote counter control
- Custom overlay themes and colors

## 📞 Support

If you need help:
1. Check this guide first
2. Contact your system administrator (riress)
3. Submit feature requests via admin dashboard
4. Report bugs with detailed reproduction steps

---

**Happy Streaming! 🎮✨**

Remember: The overlay enhances your stream experience while keeping viewers engaged with interactive counter elements.

# OmniAsylum

Stream tooling for the Doctor - A comprehensive collection of streaming utilities and overlays.

## 🎮 Projects

### Stream Counter

A real-time event counter for tracking deaths and swears during stream gameplay.

## ✨ Features

### Death & Swear Counter

- **Dual Counters**: Track deaths and swears independently
- **Persistent Storage**: Data saved to browser localStorage (survives page refresh)
- **Keyboard Shortcuts**:
  - **D** - Increment deaths
  - **S** - Increment swears
- **Quick Controls**:
  - Click **+** to increment
  - Click **−** to decrement
  - Click **Reset All** to clear both counters
- **Export Functionality**: Download counter data as JSON for records
- **Live Statistics**:
  - Total combined events counter
  - Last updated timestamp
- **Sound Feedback**: Audio cues when incrementing counters
- **Beautiful UI**:
  - Professional gradient background
  - Color-coded counters (red for deaths, orange for swears)
  - Responsive design (mobile-friendly)
  - Smooth animations and hover effects

## 📁 Project Structure

```markdown
doc-omni/
├── index.html           # Main counter interface
├── mobile.html          # Remote mobile control page
├── styles.css           # Main counter styling
├── mobile-styles.css    # Mobile control styling
├── script.js            # Counter logic and remote command handling
├── mobile-script.js     # Mobile control logic
├── LICENSE              # Project license
└── README.md            # This file
```

## 🚀 Usage

### Main Counter Display

1. Open `index.html` in your web browser (on stream computer)
2. Use as a Browser Source in OBS:
   - Add Browser Source to your OBS scene
   - Set the URL or file path to `index.html`
   - Adjust width/height as needed
3. Counter automatically saves after each change

### Remote Mobile Control

1. Open `mobile.html` on your phone/tablet (or separate device)
2. Both pages sync automatically via browser storage
3. Use mobile buttons to control the main counter remotely
4. Real-time connection status indicator
5. Sync log shows all commands sent

## 🎯 Quick Start

### Main Counter

#### Manual Counting

- Click the **+** button next to Deaths or Swears
- Click the **−** button to decrement

#### Keyboard Shortcuts

- Press **D** to add a death
- Press **S** to add a swear
- Great for hands-free updates during streams

### Mobile Remote Control

#### Setup

1. Open `index.html` in browser/OBS on your stream computer
2. Open `mobile.html` on your phone (same network or even different network)
3. Watch the status indicator show "Connected"

#### Use Mobile Control

- Tap **+** buttons to increment deaths or swears
- Tap **−** buttons to decrement
- Tap **Quick D+** or **Quick S+** for rapid one-touch updates
- Tap **Reset All** to clear all counters

#### Features

- **Live Counter Display** - See current values on mobile
- **Connection Status** - Green dot = connected
- **Sync Log** - View all commands sent to main counter
- **Audio Feedback** - Beep when button is pressed

## 💾 Data Persistence

Your counter values are automatically saved to your browser's local storage. This means:

- Counters persist across page refreshes
- Each browser/device has separate counts (use mobile on same browser for sync)
- Mobile and main counter sync via localStorage
- Clear browser data to reset (manual override)

## 🔄 How Sync Works

Both pages communicate through browser localStorage:

1. **Command Flow**: Mobile → sends command → Main counter executes
2. **Update Flow**: Main counter → stores new values → Mobile reads and displays
3. **Connection**: Both pages must be open on same device/browser for sync
4. **Fallback**: Polling mechanism checks for updates every 500ms

## 🎨 Customization

To customize colors or styling:

1. Edit `styles.css`
2. Modify the color values in the `.counter-box` and `.btn` classes
3. Adjust sizes by modifying font-size and dimensions

## 📝 Future Enhancements

Potential features for future versions:

- [ ] Cloud synchronization
- [ ] Multiple counter types
- [ ] Webhook integration for custom alerts
- [ ] Overlay animations
- [ ] Sound effect customization
- [ ] Counter history tracking

## 📄 License

See LICENSE file for details.

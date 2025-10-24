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
├── index.html          # Main counter interface
├── styles.css          # Styling and responsive design
├── script.js           # Counter logic and functionality
├── LICENSE             # Project license
└── README.md           # This file
```

## 🚀 Usage

1. Open `index.html` in your web browser
2. Use as a Browser Source in OBS:
   - Add Browser Source to your OBS scene
   - Set the URL or file path to `index.html`
   - Adjust width/height as needed
3. Counter automatically saves after each change

## 🎯 Quick Start

### Manual Counting

- Click the **+** button next to Deaths or Swears
- Click the **−** button to decrement

### Keyboard Shortcuts

- Press **D** to add a death
- Press **S** to add a swear
- Great for hands-free updates during streams

### Data Management

- **Export**: Click "Export Data" to download your session history
- **Reset**: Click "Reset All" to clear counters (confirmation required)

## 💾 Data Persistence

Your counter values are automatically saved to your browser's local storage. This means:

- Counters persist across page refreshes
- Each browser/device has separate counts
- Clear browser data to reset (manual override)

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

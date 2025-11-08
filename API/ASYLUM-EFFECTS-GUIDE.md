# 🎭 Asylum Alert Visual Effects System

## Overview

The **Asylum Effects System** brings your stream alerts to life with advanced CSS keyframes, SVG masks, canvas-based particle systems, and dynamic WebSocket-triggered animations. Each default alert is enhanced with asylum-themed visual effects that match Omni's dark, psychological horror aesthetic.

---

## 🎨 **Default Alert Effects Breakdown**

### **1. Follow Alert - "Door Creak"**
**Visual:** A door creaks open slowly
**Animation:** `doorCreak` - Slow horizontal reveal with scaling
**Effects:**
- SVG fog mask for atmospheric haze
- Dust particle system drifting through air
- Screen shake on appearance
- Sound: Door creaking + distant footsteps

**CSS Keyframes:**
```css
@keyframes doorCreak {
  0% { transform: translateX(-100%) scaleX(0.1); opacity: 0; }
  50% { transform: translateX(-20%) scaleX(0.8); opacity: 0.7; }
  100% { transform: translateX(0) scaleX(1); opacity: 1; }
}
```

---

### **2. Subscription Alert - "Electric Pulse"**
**Visual:** Restraints snap shut with electric shocks
**Animation:** `electricPulse` - Sharp flickering with glow pulses
**Effects:**
- Glass distortion SVG filter
- Spark particle system
- Screen flicker overlay
- High-intensity purple glow
- Sound: Electroshock buzz + echoing scream

**CSS Keyframes:**
```css
@keyframes electricPulse {
  0%, 100% { filter: brightness(1) drop-shadow(0 0 0px #9147ff); }
  10% { filter: brightness(2) drop-shadow(0 0 20px #9147ff); }
  15% { filter: brightness(0.3); }
  20% { filter: brightness(2.5) drop-shadow(0 0 30px #9147ff); }
}
```

---

### **3. Resub Alert - "Typewriter"**
**Visual:** Case file slams shut on desk
**Animation:** `typewriter` - Character-by-character text reveal
**Effects:**
- Paper texture SVG pattern
- Ink splatter particles
- Text scramble effect (decoding animation)
- Sound: Pen scribble + typewriter ding

**CSS Keyframes:**
```css
@keyframes typewriter {
  from { width: 0; }
  to { width: 100%; }
}
```

**Text Scramble:**
```javascript
// Decoding effect - random characters gradually resolve to real text
'█▓▒░!@#$%^&*()' → 'Case file reopened'
```

---

### **4. Bits Alert - "Pill Scatter"**
**Visual:** Pills scatter across the floor
**Animation:** `pillScatter` - Bouncing with rotation
**Effects:**
- Multi-colored pill particles (red, blue, green)
- Bounce physics with gravity
- Color shift animation (hue rotation)
- Sound: Pill bottle rattle + distorted laugh

**CSS Keyframes:**
```css
@keyframes pillScatter {
  0% { transform: translateY(-100px) rotate(0deg); opacity: 0; }
  20% { transform: translateY(0px) rotate(180deg); opacity: 1; }
  40% { transform: translateY(-30px) rotate(270deg); }
  100% { transform: translateY(0px) rotate(540deg); }
}
```

---

### **5. Raid Alert - "Siren Flash"**
**Visual:** Sirens blare, lights flicker red
**Animation:** `sirenFlash` - Rapid red/black flashing
**Effects:**
- Full-screen red alert overlay
- Chaos particle system (100 particles)
- Screen shake + screen flicker combo
- Multi-colored warning particles
- Sound: Asylum alarm + overlapping voices

**CSS Keyframes:**
```css
@keyframes sirenFlash {
  0%, 100% { background-color: #1a0000; }
  10%, 30%, 50%, 70%, 90% { background-color: #ff0000; box-shadow: inset 0 0 100px #ff0000; }
  20%, 40%, 60%, 80% { background-color: #330000; }
}
```

---

### **6. Gift Sub Alert - "Heartbeat Pulse"**
**Visual:** Nurse silhouette behind glass
**Animation:** `heartbeatPulse` - Rhythmic scaling pulse
**Effects:**
- Glass distortion filter
- Heartbeat particle system (pink hearts)
- Canvas-drawn EKG heartbeat line
- Shadowy silhouette figure appearing
- Sound: Heart monitor flatline

**CSS Keyframes:**
```css
@keyframes heartbeatPulse {
  0%, 100% { transform: scale(1); filter: drop-shadow(0 0 0px #88ddff); }
  10% { transform: scale(1.1); filter: drop-shadow(0 0 15px #88ddff); }
  30% { transform: scale(1.15); filter: drop-shadow(0 0 20px #88ddff); }
}
```

**Canvas Heartbeat Line:**
```javascript
// EKG pattern drawn across screen
ctx.moveTo(x, centerY);
ctx.lineTo(x + 15, centerY - 40);  // Spike up
ctx.lineTo(x + 20, centerY + 30);  // Spike down
ctx.lineTo(x + 25, centerY - 20);  // Smaller spike
ctx.lineTo(x + 30, centerY);       // Return to baseline
```

---

### **7. Hype Train Alert - "Wheelchair Roll"**
**Visual:** Wheelchair rolls down a hallway
**Animation:** `wheelchairRoll` - Perspective movement left-to-right
**Effects:**
- Hallway perspective gradient
- Smoke particle system
- Screen shake
- Crescendo effect (building intensity)
- Sound: Rising heartbeat + train screech

**CSS Keyframes:**
```css
@keyframes wheelchairRoll {
  0% { transform: translateX(-150%) perspective(500px) rotateY(-45deg); opacity: 0; }
  60% { transform: translateX(0%) perspective(500px) rotateY(0deg); opacity: 1; }
  100% { transform: translateX(100%) perspective(500px) rotateY(20deg); opacity: 0; }
}
```

---

## 🛠️ **Technical Implementation**

### **SVG Filters & Masks**

```html
<svg style="position: absolute; width: 0; height: 0;">
  <defs>
    <!-- Fog Effect -->
    <filter id="fog-filter">
      <feTurbulence type="fractalNoise" baseFrequency="0.01" numOctaves="5">
        <animate attributeName="baseFrequency" from="0.01" to="0.03" dur="10s" repeatCount="indefinite"/>
      </feTurbulence>
      <feDisplacementMap in="SourceGraphic" scale="50"/>
      <feGaussianBlur stdDeviation="3"/>
    </filter>

    <!-- Glass Distortion -->
    <filter id="glass-distortion">
      <feTurbulence type="turbulence" baseFrequency="0.05" numOctaves="2"/>
      <feDisplacementMap in="SourceGraphic" scale="20"/>
    </filter>

    <!-- Heartbeat Pulse -->
    <filter id="heartbeat-pulse">
      <feGaussianBlur stdDeviation="0">
        <animate values="0;3;0" dur="1.2s" repeatCount="indefinite"/>
      </feGaussianBlur>
    </filter>
  </defs>
</svg>
```

---

### **Canvas Particle System**

```javascript
class ParticleSystem {
  createParticles() {
    for (let i = 0; i < count; i++) {
      this.particles.push({
        x: Math.random() * width,
        y: Math.random() * height,
        vx: (Math.random() - 0.5) * 4,
        vy: (Math.random() - 0.5) * 4,
        size: Math.random() * 5 + 2,
        opacity: Math.random(),
        color: this.getParticleColor()
      });
    }
  }

  animate() {
    this.particles.forEach(particle => {
      particle.x += particle.vx;
      particle.y += particle.vy;

      // Gravity for pills/dust
      if (type === 'pills') particle.vy += 0.1;

      // Draw particle
      ctx.beginPath();
      ctx.arc(particle.x, particle.y, particle.size, 0, Math.PI * 2);
      ctx.fill();
    });
  }
}
```

---

### **WebSocket Event Triggering**

```javascript
// Server-side (server.js)
streamMonitor.on('newSubscription', (data) => {
  io.to(`user:${data.userId}`).emit('customAlert', {
    type: 'subscription',
    username: data.username,
    data: { tier: data.tier },
    alertConfig: alertWithEffects
  });
});

// Client-side (overlay)
socket.on('customAlert', (data) => {
  displayCustomAlert(data);
  if (data.alertConfig.effects) {
    asylumEffects.triggerEffect(data.alertConfig);
  }
});
```

---

## 📋 **Effect Configuration Schema**

Each alert template includes an `effects` object:

```javascript
{
  id: 'subscription',
  effects: {
    animation: 'electricPulse',      // CSS keyframe name
    svgMask: 'glassDistortion',      // SVG filter ID
    particle: 'sparks',              // Particle type
    screenFlicker: true,             // Boolean toggles
    screenShake: false,
    glowIntensity: 'high',           // 'low' | 'medium' | 'high'
    soundTrigger: 'electroshock.mp3' // Audio file
  }
}
```

---

## 🎯 **Available Effect Types**

### **Animations:**
- `doorCreak` - Slow horizontal reveal
- `electricPulse` - Flickering with glow
- `typewriter` - Text reveal
- `pillScatter` - Bouncing rotation
- `sirenFlash` - Red alert flash
- `heartbeatPulse` - Rhythmic pulse
- `wheelchairRoll` - Perspective movement

### **SVG Masks:**
- `fog` - Animated fog/smoke
- `glassDistortion` - Ripple effect
- `heartbeatPulse` - Pulsing blur
- `paperTexture` - Old paper pattern
- `hallwayPerspective` - Depth gradient

### **Particle Systems:**
- `dust` - Brown floating particles
- `sparks` - Yellow electric sparks
- `ink` - Black ink splatters
- `pills` - Multi-colored capsules
- `heartbeats` - Pink heart shapes
- `smoke` - Gray smoke clouds
- `chaos` - Red/orange/yellow mix

### **Screen Effects:**
- `screenShake` - Camera shake
- `screenFlicker` - Black flash
- `redAlert` - Full-screen red pulse
- `silhouette` - Shadow figure
- `textScramble` - Glitch decode
- `colorShift` - Hue rotation

---

## 🚀 **Usage**

### **In OBS:**
1. Add Browser Source
2. URL: `https://your-domain.com/overlay/YOUR_USER_ID`
3. Width: `1920`, Height: `1080`
4. **Check:** ✅ Shutdown source when not visible
5. **Check:** ✅ Refresh browser when scene becomes active
6. **Custom CSS:** (Optional) Override effects

### **Testing Alerts:**
```bash
# Via admin dashboard
Admin → User → "🎯 Configure Event Mappings" → Test Alert

# Via user portal
User Dashboard → "🎯 Manage Alerts" → Create Alert → Preview
```

---

## 🎨 **Customization Examples**

### **Custom Alert with Effects:**

```javascript
{
  name: 'Custom Follow',
  type: 'follow',
  textPrompt: '🔓 [User] escaped the cells!',
  backgroundColor: '#0d0d0d',
  textColor: '#00ff00',
  duration: 5000,
  effects: {
    animation: 'doorCreak',
    svgMask: 'fog',
    particle: 'smoke',
    screenShake: true
  }
}
```

### **Override CSS in OBS:**

```css
/* Make alerts larger */
.alert-container {
  font-size: 32px !important;
  min-width: 600px !important;
}

/* Slow down door creak */
@keyframes doorCreak {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(0); }
  animation-duration: 8s !important;
}
```

---

## 🐛 **Troubleshooting**

**Effects not showing:**
- Check browser console for errors
- Verify `asylum-effects.js` is loaded
- Confirm WebSocket connection established

**Performance issues:**
- Reduce particle count in `asylum-effects.js`
- Disable SVG filters for low-end systems
- Lower OBS browser source FPS to 30

**Alerts not triggering:**
- Verify EventSub subscriptions active
- Check event mappings in user settings
- Ensure alert is enabled in database

---

## 📊 **Performance Metrics**

| Effect Type      | CPU Usage | Memory   | FPS Impact |
| ---------------- | --------- | -------- | ---------- |
| CSS Keyframes    | Low       | 2-5 MB   | <1%        |
| SVG Filters      | Medium    | 10-15 MB | 2-5%       |
| Particle Systems | High      | 20-40 MB | 5-10%      |
| Canvas Effects   | High      | 15-30 MB | 5-8%       |

**Recommended OBS Settings:**
- Browser Source FPS: 60
- Hardware Acceleration: Enabled
- Shutdown when not visible: Enabled

---

## 🎭 **Future Enhancements**

- [ ] Audio file integration for sound effects
- [ ] 3D WebGL effects for hype train
- [ ] Custom particle shapes (syringes, files, keys)
- [ ] Layered fog with depth parallax
- [ ] Interactive alert elements (clicking triggers bonus effects)
- [ ] Alert queuing system for multiple simultaneous events

---

**Created by:** OmniAsylum Stream Team
**Last Updated:** November 2025
**Version:** 2.0 - Advanced Visual Effects

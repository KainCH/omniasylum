/**
 * Asylum Alert Visual Effects System
 * Advanced CSS keyframes, SVG masks, canvas filters, and particle systems
 * for OmniAsylum stream alerts
 */

class AsylumEffects {
  constructor() {
    this.activeEffects = [];
    this.particleSystems = [];
    this.canvas = null;
    this.ctx = null;
    this.svgDefs = null;
    this.init();
  }

  init() {
    // Create effects canvas
    this.createCanvas();
    // Create SVG definitions for masks and filters
    this.createSVGDefs();
    // Inject CSS keyframes
    this.injectKeyframes();
  }

  createCanvas() {
    this.canvas = document.createElement('canvas');
    this.canvas.id = 'asylum-effects-canvas';
    this.canvas.style.position = 'fixed';
    this.canvas.style.top = '0';
    this.canvas.style.left = '0';
    this.canvas.style.width = '100vw';
    this.canvas.style.height = '100vh';
    this.canvas.style.pointerEvents = 'none';
    this.canvas.style.zIndex = '9998';
    this.canvas.width = window.innerWidth;
    this.canvas.height = window.innerHeight;
    document.body.appendChild(this.canvas);
    this.ctx = this.canvas.getContext('2d');

    // Resize handler
    window.addEventListener('resize', () => {
      this.canvas.width = window.innerWidth;
      this.canvas.height = window.innerHeight;
    });
  }

  createSVGDefs() {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.style.position = 'absolute';
    svg.style.width = '0';
    svg.style.height = '0';

    svg.innerHTML = `
      <defs>
        <!-- Fog/Smoke Effect -->
        <filter id="fog-filter">
          <feTurbulence type="fractalNoise" baseFrequency="0.01" numOctaves="5">
            <animate attributeName="baseFrequency" from="0.01" to="0.03" dur="10s" repeatCount="indefinite"/>
          </feTurbulence>
          <feDisplacementMap in="SourceGraphic" scale="50"/>
          <feGaussianBlur stdDeviation="3"/>
        </filter>

        <!-- Glass Distortion Effect -->
        <filter id="glass-distortion">
          <feTurbulence type="turbulence" baseFrequency="0.05" numOctaves="2" result="turbulence"/>
          <feDisplacementMap in2="turbulence" in="SourceGraphic" scale="20" xChannelSelector="R" yChannelSelector="G"/>
          <feGaussianBlur stdDeviation="0.5"/>
        </filter>

        <!-- Heartbeat Pulse Filter -->
        <filter id="heartbeat-pulse">
          <feGaussianBlur in="SourceGraphic" stdDeviation="0">
            <animate attributeName="stdDeviation" values="0;3;0" dur="1.2s" repeatCount="indefinite"/>
          </feGaussianBlur>
          <feColorMatrix type="saturate" values="1">
            <animate attributeName="values" values="1;1.5;1" dur="1.2s" repeatCount="indefinite"/>
          </feColorMatrix>
        </filter>

        <!-- Paper Texture Mask -->
        <pattern id="paper-texture" x="0" y="0" width="100" height="100" patternUnits="userSpaceOnUse">
          <rect width="100" height="100" fill="#f0ebe5"/>
          <circle cx="10" cy="10" r="1" fill="#d0c0a0" opacity="0.3"/>
          <circle cx="50" cy="30" r="1.5" fill="#d0c0a0" opacity="0.2"/>
          <circle cx="80" cy="70" r="1" fill="#d0c0a0" opacity="0.3"/>
        </pattern>

        <!-- Hallway Perspective Gradient -->
        <linearGradient id="hallway-gradient" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" style="stop-color:#000000;stop-opacity:0.9"/>
          <stop offset="50%" style="stop-color:#1a1a1a;stop-opacity:0.5"/>
          <stop offset="100%" style="stop-color:#000000;stop-opacity:0.9"/>
        </linearGradient>
      </defs>
    `;

    document.body.appendChild(svg);
    this.svgDefs = svg;
  }

  injectKeyframes() {
    const style = document.createElement('style');
    style.innerHTML = `
      /* Door Creak - Slow reveal */
      @keyframes doorCreak {
        0% { transform: translateX(-100%) scaleX(0.1); opacity: 0; }
        20% { transform: translateX(-50%) scaleX(0.5); opacity: 0.3; }
        50% { transform: translateX(-20%) scaleX(0.8); opacity: 0.7; }
        100% { transform: translateX(0) scaleX(1); opacity: 1; }
      }

      /* Electric Pulse - Sharp flicker with glow */
      @keyframes electricPulse {
        0%, 100% { filter: brightness(1) drop-shadow(0 0 0px #9147ff); }
        10% { filter: brightness(2) drop-shadow(0 0 20px #9147ff); }
        15% { filter: brightness(0.3) drop-shadow(0 0 0px #9147ff); }
        20% { filter: brightness(2.5) drop-shadow(0 0 30px #9147ff); }
        25% { filter: brightness(0.5) drop-shadow(0 0 5px #9147ff); }
        35% { filter: brightness(1.8) drop-shadow(0 0 15px #9147ff); }
        50% { filter: brightness(1) drop-shadow(0 0 10px #9147ff); }
      }

      /* Typewriter - Character by character reveal */
      @keyframes typewriter {
        from { width: 0; }
        to { width: 100%; }
      }

      /* Pill Scatter - Bouncing pills */
      @keyframes pillScatter {
        0% { transform: translateY(-100px) rotate(0deg); opacity: 0; }
        20% { transform: translateY(0px) rotate(180deg); opacity: 1; }
        40% { transform: translateY(-30px) rotate(270deg); }
        60% { transform: translateY(0px) rotate(360deg); }
        80% { transform: translateY(-10px) rotate(450deg); }
        100% { transform: translateY(0px) rotate(540deg); opacity: 1; }
      }

      /* Siren Flash - Red alert flicker */
      @keyframes sirenFlash {
        0%, 100% { background-color: #1a0000; box-shadow: inset 0 0 0px #ff0000; }
        10%, 30%, 50%, 70%, 90% { background-color: #ff0000; box-shadow: inset 0 0 100px #ff0000; }
        20%, 40%, 60%, 80% { background-color: #330000; box-shadow: inset 0 0 50px #ff0000; }
      }

      /* Heartbeat Pulse - Scale pulse with glow */
      @keyframes heartbeatPulse {
        0%, 100% { transform: scale(1); filter: drop-shadow(0 0 0px #88ddff); }
        10% { transform: scale(1.1); filter: drop-shadow(0 0 15px #88ddff); }
        20% { transform: scale(1); filter: drop-shadow(0 0 5px #88ddff); }
        30% { transform: scale(1.15); filter: drop-shadow(0 0 20px #88ddff); }
        40% { transform: scale(1); filter: drop-shadow(0 0 0px #88ddff); }
      }

      /* Wheelchair Roll - Left to right with perspective */
      @keyframes wheelchairRoll {
        0% { transform: translateX(-150%) perspective(500px) rotateY(-45deg); opacity: 0; }
        30% { transform: translateX(-50%) perspective(500px) rotateY(-20deg); opacity: 0.5; }
        60% { transform: translateX(0%) perspective(500px) rotateY(0deg); opacity: 1; }
        100% { transform: translateX(100%) perspective(500px) rotateY(20deg); opacity: 0; }
      }

      /* Screen Shake */
      @keyframes screenShake {
        0%, 100% { transform: translate(0, 0); }
        10% { transform: translate(-5px, 2px); }
        20% { transform: translate(5px, -2px); }
        30% { transform: translate(-3px, 3px); }
        40% { transform: translate(3px, -3px); }
        50% { transform: translate(-2px, 2px); }
        60% { transform: translate(2px, -2px); }
        70% { transform: translate(-1px, 1px); }
        80% { transform: translate(1px, -1px); }
        90% { transform: translate(-1px, 0); }
      }

      /* Flicker Effect */
      @keyframes flicker {
        0%, 100% { opacity: 1; }
        41% { opacity: 1; }
        42% { opacity: 0.5; }
        43% { opacity: 1; }
        45% { opacity: 0.3; }
        46% { opacity: 1; }
        82% { opacity: 1; }
        83% { opacity: 0.7; }
        87% { opacity: 1; }
      }

      /* Fade In */
      @keyframes fadeIn {
        from { opacity: 0; }
        to { opacity: 1; }
      }

      /* Glitch Text */
      @keyframes glitchText {
        0% { text-shadow: 0 0 0 transparent; }
        10% { text-shadow: -2px 0 0 #ff0000, 2px 0 0 #00ff00; }
        20% { text-shadow: 0 0 0 transparent; }
        30% { text-shadow: 3px 0 0 #00ff00, -3px 0 0 #ff0000; }
        40% { text-shadow: 0 0 0 transparent; }
      }

      /* Color Shift */
      @keyframes colorShift {
        0% { filter: hue-rotate(0deg); }
        25% { filter: hue-rotate(90deg); }
        50% { filter: hue-rotate(180deg); }
        75% { filter: hue-rotate(270deg); }
        100% { filter: hue-rotate(360deg); }
      }
    `;
    document.head.appendChild(style);
  }

  // ==================== EFFECT TRIGGERS ====================

  triggerEffect(alertData) {
    const effects = alertData.effects || {};

    // Apply main animation
    if (effects.animation) {
      this.applyAnimation(effects.animation, alertData);
    }

    // Apply SVG mask/filter
    if (effects.svgMask && effects.svgMask !== 'none') {
      this.applySVGMask(effects.svgMask, alertData);
    }

    // Trigger particle system
    if (effects.particle) {
      this.createParticleSystem(effects.particle, alertData);
    }

    // Screen effects
    if (effects.screenShake) {
      this.triggerScreenShake();
    }

    if (effects.screenFlicker) {
      this.triggerScreenFlicker();
    }

    if (effects.redAlert) {
      this.triggerRedAlert(alertData.duration);
    }

    // Special effects
    if (effects.heartbeatLine) {
      this.drawHeartbeatLine(alertData.duration);
    }

    if (effects.silhouette) {
      this.showSilhouette(alertData.duration);
    }

    if (effects.textScramble) {
      this.scrambleText(alertData);
    }
  }

  applyAnimation(animationName, alertData) {
    const alertElement = document.querySelector('.alert-container');
    if (alertElement) {
      alertElement.style.animation = `${animationName} ${alertData.duration / 1000}s ease-out`;
    }
  }

  applySVGMask(maskType, alertData) {
    const alertElement = document.querySelector('.alert-container');
    if (alertElement) {
      switch(maskType) {
        case 'fog':
          alertElement.style.filter = 'url(#fog-filter)';
          break;
        case 'glassDistortion':
          alertElement.style.filter = 'url(#glass-distortion)';
          break;
        case 'heartbeatPulse':
          alertElement.style.filter = 'url(#heartbeat-pulse)';
          break;
        case 'paperTexture':
          alertElement.style.background = 'url(#paper-texture)';
          break;
      }
    }
  }

  createParticleSystem(particleType, alertData) {
    const system = new ParticleSystem(this.ctx, particleType, alertData.duration);
    this.particleSystems.push(system);
    system.start();
  }

  triggerScreenShake() {
    document.body.style.animation = 'screenShake 0.5s ease-in-out';
    setTimeout(() => {
      document.body.style.animation = '';
    }, 500);
  }

  triggerScreenFlicker() {
    const overlay = document.createElement('div');
    overlay.style.position = 'fixed';
    overlay.style.top = '0';
    overlay.style.left = '0';
    overlay.style.width = '100vw';
    overlay.style.height = '100vh';
    overlay.style.backgroundColor = '#000';
    overlay.style.animation = 'flicker 2s ease-in-out';
    overlay.style.zIndex = '9997';
    overlay.style.pointerEvents = 'none';
    document.body.appendChild(overlay);

    setTimeout(() => {
      overlay.remove();
    }, 2000);
  }

  triggerRedAlert(duration) {
    const overlay = document.createElement('div');
    overlay.style.position = 'fixed';
    overlay.style.top = '0';
    overlay.style.left = '0';
    overlay.style.width = '100vw';
    overlay.style.height = '100vh';
    overlay.style.animation = `sirenFlash ${duration / 1000}s ease-in-out`;
    overlay.style.zIndex = '9997';
    overlay.style.pointerEvents = 'none';
    document.body.appendChild(overlay);

    setTimeout(() => {
      overlay.remove();
    }, duration);
  }

  drawHeartbeatLine(duration) {
    const width = this.canvas.width;
    const height = this.canvas.height;
    const centerY = height / 2;

    let x = 0;
    const interval = setInterval(() => {
      this.ctx.strokeStyle = '#88ddff';
      this.ctx.lineWidth = 3;
      this.ctx.beginPath();

      // Draw heartbeat pattern
      this.ctx.moveTo(x, centerY);
      this.ctx.lineTo(x + 10, centerY);
      this.ctx.lineTo(x + 15, centerY - 40);
      this.ctx.lineTo(x + 20, centerY + 30);
      this.ctx.lineTo(x + 25, centerY - 20);
      this.ctx.lineTo(x + 30, centerY);
      this.ctx.lineTo(x + 100, centerY);

      this.ctx.stroke();

      x += 2;

      if (x > width) {
        x = 0;
        this.ctx.clearRect(0, 0, width, height);
      }
    }, 16);

    setTimeout(() => {
      clearInterval(interval);
      this.ctx.clearRect(0, 0, width, height);
    }, duration);
  }

  showSilhouette(duration) {
    const silhouette = document.createElement('div');
    silhouette.style.position = 'fixed';
    silhouette.style.bottom = '0';
    silhouette.style.right = '10%';
    silhouette.style.width = '200px';
    silhouette.style.height = '400px';
    silhouette.style.background = 'linear-gradient(to top, #000 0%, rgba(0,0,0,0.8) 50%, transparent 100%)';
    silhouette.style.clipPath = 'polygon(40% 0%, 60% 0%, 70% 30%, 65% 50%, 75% 100%, 25% 100%, 35% 50%, 30% 30%)';
    silhouette.style.filter = 'blur(2px)';
    silhouette.style.opacity = '0';
    silhouette.style.animation = 'fadeIn 1s ease-in forwards';
    silhouette.style.zIndex = '9999';
    silhouette.style.pointerEvents = 'none';

    document.body.appendChild(silhouette);

    setTimeout(() => {
      silhouette.style.animation = 'fadeIn 1s ease-out reverse';
      setTimeout(() => silhouette.remove(), 1000);
    }, duration - 1000);
  }

  scrambleText(alertData) {
    const textElement = document.querySelector('.alert-text');
    if (!textElement) return;

    const originalText = textElement.textContent;
    const chars = '█▓▒░!@#$%^&*()_+-=[]{}|;:,.<>?';
    let iterations = 0;
    const maxIterations = 20;

    const interval = setInterval(() => {
      textElement.textContent = originalText
        .split('')
        .map((char, index) => {
          if (index < iterations) {
            return originalText[index];
          }
          return chars[Math.floor(Math.random() * chars.length)];
        })
        .join('');

      iterations += 1;

      if (iterations > maxIterations) {
        clearInterval(interval);
        textElement.textContent = originalText;
      }
    }, 50);
  }
}

// ==================== PARTICLE SYSTEM ====================

class ParticleSystem {
  constructor(ctx, type, duration) {
    this.ctx = ctx;
    this.type = type;
    this.duration = duration;
    this.particles = [];
    this.animationId = null;
  }

  start() {
    this.createParticles();
    this.animate();
  }

  createParticles() {
    const count = this.type === 'chaos' ? 100 : 50;
    const width = this.ctx.canvas.width;
    const height = this.ctx.canvas.height;

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

  getParticleColor() {
    switch(this.type) {
      case 'dust': return '#8b7355';
      case 'sparks': return '#ffff00';
      case 'ink': return '#000000';
      case 'pills': return ['#ff6b6b', '#4dabf7', '#51cf66'][Math.floor(Math.random() * 3)];
      case 'heartbeats': return '#ff0066';
      case 'smoke': return '#666666';
      case 'chaos': return ['#ff0000', '#ff6600', '#ffff00'][Math.floor(Math.random() * 3)];
      default: return '#ffffff';
    }
  }

  animate() {
    const startTime = Date.now();

    const loop = () => {
      const elapsed = Date.now() - startTime;

      if (elapsed > this.duration) {
        this.ctx.clearRect(0, 0, this.ctx.canvas.width, this.ctx.canvas.height);
        return;
      }

      this.ctx.clearRect(0, 0, this.ctx.canvas.width, this.ctx.canvas.height);

      this.particles.forEach(particle => {
        // Update position
        particle.x += particle.vx;
        particle.y += particle.vy;

        // Gravity for certain particle types
        if (this.type === 'pills' || this.type === 'dust') {
          particle.vy += 0.1;
        }

        // Bounce off edges
        if (particle.x < 0 || particle.x > this.ctx.canvas.width) particle.vx *= -0.8;
        if (particle.y < 0 || particle.y > this.ctx.canvas.height) particle.vy *= -0.8;

        // Draw particle
        this.ctx.save();
        this.ctx.globalAlpha = particle.opacity;
        this.ctx.fillStyle = particle.color;

        if (this.type === 'pills') {
          // Draw pill shape
          this.ctx.fillRect(particle.x, particle.y, particle.size * 2, particle.size);
        } else if (this.type === 'heartbeats') {
          // Draw heart shape
          this.ctx.beginPath();
          this.ctx.arc(particle.x, particle.y, particle.size, 0, Math.PI * 2);
          this.ctx.fill();
        } else {
          // Draw circle
          this.ctx.beginPath();
          this.ctx.arc(particle.x, particle.y, particle.size, 0, Math.PI * 2);
          this.ctx.fill();
        }

        this.ctx.restore();

        // Fade out over time
        particle.opacity -= 0.005;
      });

      // Remove dead particles
      this.particles = this.particles.filter(p => p.opacity > 0);

      this.animationId = requestAnimationFrame(loop);
    };

    loop();
  }
}

// Export singleton instance
window.asylumEffects = new AsylumEffects();

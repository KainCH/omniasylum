/**
 * Asylum Alert Visual Effects System
 * Advanced CSS keyframes, SVG masks, canvas filters, and particle systems
 * for OmniAsylum stream alerts
 */

class AsylumEffects {
  // ==================== CONSTANTS ====================
  static get CONSTANTS() {
    return {
      // Audio Configuration
      DEFAULT_AUDIO_VOLUME: 0.7,
      MAX_RETRY_ATTEMPTS: 3,
      RETRY_TIMEOUT_MS: 5000,
      CACHE_EXPIRY_HOURS: 24,
      CACHE_AUTO_SAVE_INTERVAL_MS: 30000,

      // Performance Settings
      LOG_LEVELS: ['minimal', 'normal', 'verbose'],
      STORAGE_KEYS: {
        SETTINGS: 'asylumEffectsSettings',
        LOG_LEVEL: 'omni_log_level',
        AUDIO_CACHE: 'omni_asylum_audio_cache'
      }
    };
  }

  constructor() {
    this.activeEffects = [];
    this.particleSystems = [];
    this.canvas = null;
    this.ctx = null;
    this.svgDefs = null;
    this.audioCache = {};
    this.audioVolume = AsylumEffects.CONSTANTS.DEFAULT_AUDIO_VOLUME;

    // Audio retry management to prevent infinite loops
    this.audioRetryAttempts = new Map(); // Track retry attempts per sound
    this.maxRetryAttempts = AsylumEffects.CONSTANTS.MAX_RETRY_ATTEMPTS;
    this.retryTimeout = AsylumEffects.CONSTANTS.RETRY_TIMEOUT_MS;

    // Logging configuration for performance optimization
    this.logLevel = this.getLogLevel(); // 'verbose', 'normal', 'minimal'
    this.enableAudioLogging = this.logLevel !== 'minimal';

    // Performance optimizations
    this.debouncedCacheSave = this.debounce(this.saveAudioCacheState.bind(this), 1000);
    this.cacheModified = false;

    // Effect toggles - can be enabled/disabled
    this.settings = {
      enableSound: true,
      enableAnimations: true,
      enableParticles: true,
      enableScreenEffects: true,
      enableSVGFilters: true,
      enableTextEffects: true
    };

    this.init();
  }

  init() {
    // Create effects canvas
    this.createCanvas();
    // Create SVG definitions for masks and filters
    this.createSVGDefs();
    // Inject CSS keyframes
    this.injectKeyframes();
    // Preload sound effects
    this.preloadSounds();
    // Load settings from localStorage if available
    this.loadSettings();
  }

  // ==================== SETTINGS MANAGEMENT ====================

  /**
   * Load settings from localStorage with validation
   * Falls back to defaults if loading fails or data is invalid
   */
  loadSettings() {
    try {
      const saved = localStorage.getItem(AsylumEffects.CONSTANTS.STORAGE_KEYS.SETTINGS);
      if (saved) {
        const parsed = JSON.parse(saved);
        this.settings = this.validateSettings({ ...this.getDefaultSettings(), ...parsed });
        this.log('normal', 'Loaded and validated effect settings:', this.settings);
      }
    } catch (error) {
      this.warn('minimal', 'Failed to load settings, using defaults:', error);
      this.settings = this.getDefaultSettings();
    }
  }

  /**
   * Save current settings to localStorage
   * @returns {boolean} Success status
   */
  saveSettings() {
    try {
      localStorage.setItem(AsylumEffects.CONSTANTS.STORAGE_KEYS.SETTINGS, JSON.stringify(this.settings));
      this.log('normal', 'Saved effect settings:', this.settings);
    } catch (error) {
      this.warn('minimal', 'Failed to save settings:', error);
    }
  }

  toggleSetting(settingName, enabled) {
    if (this.settings.hasOwnProperty(settingName)) {
      this.settings[settingName] = enabled;
      this.saveSettings();
      this.log('normal', `${settingName}: ${enabled ? 'ENABLED' : 'DISABLED'}`);
      return true;
    }
    this.warn('minimal', `Unknown setting: ${settingName}`);
    return false;
  }

  getSettings() {
    return { ...this.settings };
  }

  // Get default settings structure
  getDefaultSettings() {
    return {
      enableSound: true,
      enableAnimations: true,
      enableParticles: true,
      enableScreenEffects: true,
      enableSVGFilters: true,
      enableTextEffects: true
    };
  }

  // Validate and sanitize settings object
  validateSettings(settings) {
    const defaults = this.getDefaultSettings();
    const validated = {};

    // Only include known settings with boolean values
    for (const key in defaults) {
      validated[key] = typeof settings[key] === 'boolean' ? settings[key] : defaults[key];
    }

    return validated;
  }

  resetSettings() {
    this.settings = this.getDefaultSettings();
    this.saveSettings();
    this.log('normal', 'Reset all settings to defaults');
  }

  /*
   * LOGGING SYSTEM - Performance Optimized
   * Available methods:
   * - setLogLevel('verbose'|'normal'|'minimal') - Change logging level
   * - showLogStatus() - Display current configuration
   * - log(level, message) - Conditional logging
   * - warn(level, message) - Conditional warnings
   */

  // Get logging level for performance optimization
  getLogLevel() {
    // Check localStorage for log level preference
    try {
      const saved = localStorage.getItem(AsylumEffects.CONSTANTS.STORAGE_KEYS.LOG_LEVEL);
      if (saved && AsylumEffects.CONSTANTS.LOG_LEVELS.includes(saved)) {
        return saved;
      }
    } catch (error) {
      // localStorage not available or error - use default
    }

    // Default to 'normal' for production, 'verbose' for development
    return window.location.hostname === 'localhost' ? 'verbose' : 'normal';
  }

  // Optimized logging method
  log(level, message, ...args) {
    const levels = { minimal: 0, normal: 1, verbose: 2 };
    const currentLevel = levels[this.logLevel] || 1;
    const messageLevel = levels[level] || 1;

    if (messageLevel <= currentLevel) {
      console.log(message, ...args);
    }
  }

  // Optimized warning method
  warn(level, message, ...args) {
    const levels = { minimal: 0, normal: 1, verbose: 2 };
    const currentLevel = levels[this.logLevel] || 1;
    const messageLevel = levels[level] || 1;

    if (messageLevel <= currentLevel) {
      console.warn(message, ...args);
    }
  }

  // Set logging level dynamically
  setLogLevel(level) {
    if (AsylumEffects.CONSTANTS.LOG_LEVELS.includes(level)) {
      this.logLevel = level;
      this.enableAudioLogging = level !== 'minimal';
      try {
        localStorage.setItem(AsylumEffects.CONSTANTS.STORAGE_KEYS.LOG_LEVEL, level);
        console.log(`Log level set to: ${level}`);
      } catch (error) {
        console.warn('Failed to save log level preference:', error);
      }
    } else {
      console.warn(`Invalid log level. Use: ${AsylumEffects.CONSTANTS.LOG_LEVELS.join(', ')}`);
    }
  }

  // Show current logging configuration
  showLogStatus() {
    console.log(`Current logging configuration:
      Level: ${this.logLevel}
      Audio logging: ${this.enableAudioLogging ? 'enabled' : 'disabled'}
      Available methods: setLogLevel('verbose'|'normal'|'minimal')`);
  }

  /**
   * Show comprehensive system status for debugging
   */
  showSystemStatus() {
    const cacheCount = Object.keys(this.audioCache).length;
    const activeEffectCount = this.activeEffects.length;
    const retryCount = this.audioRetryAttempts.size;

    console.log(`OmniAsylum Effects System Status:
      Audio Cache: ${cacheCount} sounds loaded
      Active Effects: ${activeEffectCount}
      Retry Tracking: ${retryCount} active attempts
      Volume: ${(this.audioVolume * 100).toFixed(0)}%
      Settings: ${JSON.stringify(this.settings, null, 2)}
      Log Level: ${this.logLevel}
      Constants: Available via AsylumEffects.CONSTANTS`);
  }

  // ==================== UTILITY HELPERS ====================

  /**
   * Debounce function to limit how often a function can be called
   * @param {Function} func - Function to debounce
   * @param {number} wait - Milliseconds to wait
   * @returns {Function} Debounced function
   */
  debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
      const later = () => {
        clearTimeout(timeout);
        func(...args);
      };
      clearTimeout(timeout);
      timeout = setTimeout(later, wait);
    };
  }

  // ==================== INPUT VALIDATION HELPERS ====================

  // Validate audio volume (0.0 - 1.0)
  validateVolume(volume) {
    const vol = parseFloat(volume);
    return !isNaN(vol) && vol >= 0 && vol <= 1 ? vol : AsylumEffects.CONSTANTS.DEFAULT_AUDIO_VOLUME;
  }

  // Validate effect name exists in settings
  validateEffectName(effectName) {
    return typeof effectName === 'string' && this.settings.hasOwnProperty(effectName);
  }

  // Sanitize string input for safe usage
  sanitizeString(input, maxLength = 100) {
    if (typeof input !== 'string') return '';
    return input.slice(0, maxLength).replace(/[<>]/g, '');
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

  // ==================== AUDIO SYSTEM ====================

  // Enhanced preloadSounds with cache restoration
  preloadSounds() {
    this.log('normal', 'Initializing audio system...');

    // First try to restore from cache
    const restored = this.restoreAudioCacheState();

    // Then load any missing sounds
    const soundFiles = [
      'doorCreak.wav',
      'electroshock.wav',
      'typewriter.wav',
      'pillRattle.mp3',
      'alarm.wav',
      'heartMonitor.wav',
      'hypeTrain.wav'
    ];

    soundFiles.forEach(filename => {
      const key = filename.replace('.wav', '').replace('.mp3', '');

      // Skip if already loaded from cache
      if (this.audioCache[key] && this.audioCache[key].readyState >= 3) {
        this.log('verbose', `Sound already cached: ${filename}`);
        return;
      }

      const audio = new Audio(`/sounds/${filename}`);
      audio.volume = this.audioVolume;
      audio.preload = 'auto';
      this.audioCache[key] = audio;

      audio.addEventListener('canplaythrough', () => {
        this.log('verbose', `Preloaded sound: ${filename}`);
        this.saveAudioCacheState(); // Save state after each load
        this.log('verbose', 'Saved audio cache state after loading:', filename);
      }, { once: true });

      audio.addEventListener('error', (e) => {
        this.warn('normal', `Failed to load sound: ${filename}`, e);
        delete this.audioCache[key]; // Clean up failed entry
      });
    });

    // Check cache health after 3 seconds
    setTimeout(() => {
      const health = this.checkAudioCacheHealth();
      if (!health.healthy) {
        this.warn('normal', 'Audio cache unhealthy, attempting recovery...', health);
        this.recoverAudioCache(health);
      }
    }, 3000);
  }

  // Enhanced playSound with retry tracking to prevent infinite loops
  playSound(soundTrigger, retryCount = 0) {
    // Input validation
    const sanitizedTrigger = this.sanitizeString(soundTrigger);
    if (!sanitizedTrigger) {
      this.warn('minimal', 'Invalid sound trigger provided');
      return;
    }

    // Check retry limits to prevent infinite loops
    const retryKey = `${sanitizedTrigger}_${Date.now()}`;
    if (retryCount >= AsylumEffects.CONSTANTS.MAX_RETRY_ATTEMPTS) {
      this.warn('minimal', `Max retry attempts (${AsylumEffects.CONSTANTS.MAX_RETRY_ATTEMPTS}) reached for: ${sanitizedTrigger}`);
      this.audioRetryAttempts.delete(retryKey);
      return;
    }

    // Check if audio cache is empty and recover if needed
    if (Object.keys(this.audioCache).length === 0) {
      this.log('normal', 'Audio cache empty, attempting recovery...');
      const restored = this.restoreAudioCacheState();

      if (!restored && retryCount === 0) {
        this.warn('minimal', 'Failed to restore audio cache, sound will not play:', soundTrigger);
        return;
      }

      // Queue the sound to play once cache is restored (with retry tracking)
      setTimeout(() => this.playSound(soundTrigger, retryCount + 1), 1000);
      return;
    }

    // Extract sound name without extension
    const soundName = soundTrigger.replace('.wav', '').replace('.mp3', '');
    const audio = this.audioCache[soundName];

    if (audio && audio.readyState >= 3) {
      // Audio is ready - play it and clear any retry tracking
      this.audioRetryAttempts.delete(retryKey);

      // Clone the audio to allow overlapping sounds
      const soundInstance = audio.cloneNode();
      soundInstance.volume = this.audioVolume;

      // Play with error handling for browser autoplay policies
      const playPromise = soundInstance.play();

      if (playPromise !== undefined) {
        playPromise
          .then(() => {
            this.log('verbose', `Playing sound: ${soundTrigger}`);
          })
          .catch(error => {
            this.warn('normal', `Autoplay prevented for: ${soundTrigger}`, error);
            // User interaction required - this is expected on first load
          });
      }
    } else if (audio && audio.readyState < 3) {
      // Audio exists but not ready, wait for it to load (with timeout)
      console.log(`Audio loading, will play when ready: ${soundTrigger}`);

      const timeoutId = setTimeout(() => {
        console.warn(`Audio load timeout for: ${soundTrigger}`);
        this.playSound(soundTrigger, retryCount + 1);
      }, this.retryTimeout);

      audio.addEventListener('canplaythrough', () => {
        clearTimeout(timeoutId);
        this.playSound(soundTrigger, retryCount);
      }, { once: true });

    } else {
      // Sound not found in cache - try to load it
      console.warn(`Sound not found in cache: ${soundTrigger}`);

      // Only attempt loading if we haven't exceeded retry limits
      if (retryCount < this.maxRetryAttempts) {
        this.priorityLoadSound(soundName, `/sounds/${soundName}.wav`);

        // Queue the sound to play once loaded (with retry tracking)
        setTimeout(() => this.playSound(soundTrigger, retryCount + 1), 1500);
      } else {
        console.error(`Failed to load sound after ${this.maxRetryAttempts} attempts: ${soundTrigger}`);
      }
    }
  }

  setVolume(volume) {
    this.audioVolume = Math.max(0, Math.min(1, volume)); // Clamp between 0-1

    // Update all cached audio elements
    Object.values(this.audioCache).forEach(audio => {
      audio.volume = this.audioVolume;
    });

    console.log(`Volume set to: ${Math.round(this.audioVolume * 100)}%`);
  }

  // ==================== AUDIO CACHE PERSISTENCE ====================

  // Save audio cache state to localStorage
  saveAudioCacheState() {
    try {
      const cacheState = {
        timestamp: Date.now(),
        sounds: Object.keys(this.audioCache).map(key => ({
          key: key,
          loaded: this.audioCache[key] && this.audioCache[key].readyState >= 3,
          src: this.audioCache[key] ? this.audioCache[key].src : null
        })),
        volume: this.audioVolume,
        settings: this.settings
      };

      localStorage.setItem(AsylumEffects.CONSTANTS.STORAGE_KEYS.AUDIO_CACHE, JSON.stringify(cacheState));
      this.log('verbose', `Audio cache saved: ${cacheState.sounds.length} sounds at ${new Date(cacheState.timestamp).toLocaleTimeString()}`);
    } catch (error) {
      this.warn('normal', 'Failed to save audio cache state:', error);
    }
  }

  // Restore audio cache from localStorage
  restoreAudioCacheState() {
    try {
      const saved = localStorage.getItem(AsylumEffects.CONSTANTS.STORAGE_KEYS.AUDIO_CACHE);
      if (!saved) return false;

      const cacheState = JSON.parse(saved);
      const age = Date.now() - cacheState.timestamp;

      // Cache is valid for configured hours
      const maxCacheAge = AsylumEffects.CONSTANTS.CACHE_EXPIRY_HOURS * 60 * 60 * 1000;
      if (age > maxCacheAge) {
        this.log('normal', 'Audio cache expired, will reload fresh');
        localStorage.removeItem(AsylumEffects.CONSTANTS.STORAGE_KEYS.AUDIO_CACHE);
        return false;
      }

      this.log('normal',
        `Restoring audio cache: ${cacheState.sounds?.length || 0} sounds, volume=${cacheState.volume || AsylumEffects.CONSTANTS.DEFAULT_AUDIO_VOLUME}, cache age=${Math.round(age / 1000)}s`
      );

      // Restore volume and settings
      this.audioVolume = cacheState.volume || AsylumEffects.CONSTANTS.DEFAULT_AUDIO_VOLUME;
      this.settings = { ...this.settings, ...(cacheState.settings || {}) };

      // Priority reload of previously cached sounds
      cacheState.sounds.forEach(sound => {
        if (sound.loaded && sound.src) {
          this.priorityLoadSound(sound.key, sound.src);
        }
      });

      return true;
    } catch (error) {
      console.warn('Failed to restore audio cache:', error);
      return false;
    }
  }

  // Priority load a specific sound (faster than normal preload)
  priorityLoadSound(key, src) {
    const audio = new Audio(src);
    audio.volume = this.audioVolume;
    audio.preload = 'auto';

    this.audioCache[key] = audio;

    audio.addEventListener('canplaythrough', () => {
      this.log('verbose', `Priority restored sound: ${key}`);
      this.saveAudioCacheState(); // Update cache state
    }, { once: true });

    audio.addEventListener('error', (e) => {
      this.warn('normal', `Priority load failed for: ${key}`, e);
      delete this.audioCache[key]; // Remove failed entry
    });
  }

  // Load audio with fallback support (eliminates code duplication)
  loadAudioWithFallback(key, primaryUrl, fallbackUrl = null) {
    const audio = new Audio(primaryUrl);
    audio.volume = this.audioVolume;
    audio.preload = 'auto';
    this.audioCache[key] = audio;

    audio.addEventListener('canplaythrough', () => {
      const fileType = primaryUrl.includes('.mp3') ? 'mp3' : 'wav';
      console.log(`Recovered sound (${fileType}): ${key}`);
      this.saveAudioCacheState();
    }, { once: true });

    audio.addEventListener('error', (e) => {
      if (fallbackUrl) {
        console.warn(`Primary audio load failed for ${key}, trying fallback...`);
        // Remove failed attempt and try fallback
        delete this.audioCache[key];
        this.loadAudioWithFallback(key, fallbackUrl, null);
      } else {
        console.error(`All audio load attempts failed for: ${key}`, e);
        delete this.audioCache[key];
      }
    });
  }

  // Check if audio cache is healthy and complete
  checkAudioCacheHealth() {
    const expectedSounds = [
      'doorCreak', 'electroshock', 'typewriter', 'pillRattle',
      'alarm', 'heartMonitor', 'hypeTrain'
    ];

    const cachedSounds = Object.keys(this.audioCache);
    const healthySounds = cachedSounds.filter(key => {
      const audio = this.audioCache[key];
      return audio && audio.readyState >= 3; // HAVE_FUTURE_DATA or better
    });

    const healthPercentage = (healthySounds.length / expectedSounds.length) * 100;

    console.log(`Audio cache health: ${healthPercentage.toFixed(1)}% (${healthySounds.length}/${expectedSounds.length})`);

    return {
      healthy: healthPercentage >= 80, // 80% threshold
      percentage: healthPercentage,
      missing: expectedSounds.filter(sound => !cachedSounds.includes(sound)),
      failed: cachedSounds.filter(key => {
        const audio = this.audioCache[key];
        return !audio || audio.readyState < 3;
      })
    };
  }

  // Recover unhealthy audio cache
  recoverAudioCache(healthStatus) {
    console.log('Starting audio cache recovery...');

    // Reload failed sounds using helper method with fallback
    healthStatus.failed.forEach(key => {
      console.log(`Recovering failed sound: ${key}`);
      delete this.audioCache[key];

      // Use helper method with .wav primary and .mp3 fallback
      this.loadAudioWithFallback(key, `/sounds/${key}.wav`, `/sounds/${key}.mp3`);
    });

    // Load missing sounds using helper method with fallback
    healthStatus.missing.forEach(key => {
      console.log(`Loading missing sound: ${key}`);
      this.loadAudioWithFallback(key, `/sounds/${key}.wav`, `/sounds/${key}.mp3`);
    });
  }

  // ==================== EFFECT TRIGGERS ====================

  triggerEffect(alertData) {
    const effects = alertData.effects || {};

    // Play sound effect first (if enabled)
    if (this.settings.enableSound && effects.soundTrigger) {
      this.playSound(effects.soundTrigger);
    }

    // Apply main animation (if enabled)
    if (this.settings.enableAnimations && effects.animation) {
      this.applyAnimation(effects.animation, alertData);
    }

    // Apply SVG mask/filter (if enabled)
    if (this.settings.enableSVGFilters && effects.svgMask && effects.svgMask !== 'none') {
      this.applySVGMask(effects.svgMask, alertData);
    }

    // Trigger particle system (if enabled)
    if (this.settings.enableParticles && effects.particle) {
      this.createParticleSystem(effects.particle, alertData);
    }

    // Screen effects (if enabled)
    if (this.settings.enableScreenEffects) {
      if (effects.screenShake) {
        this.triggerScreenShake();
      }

      if (effects.screenFlicker) {
        this.triggerScreenFlicker();
      }
    }

    if (this.settings.enableScreenEffects && effects.redAlert) {
      this.triggerRedAlert(alertData.duration);
    }

    // Special effects
    if (effects.heartbeatLine) {
      this.drawHeartbeatLine(alertData.duration);
    }

    if (effects.silhouette) {
      this.showSilhouette(alertData.duration);
    }

    if (this.settings.enableTextEffects && effects.textScramble) {
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

// Auto-create AsylumEffects instance when DOM is ready
(function() {
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeAsylumEffects);
  } else {
    initializeAsylumEffects();
  }

  function initializeAsylumEffects() {
    console.log('Initializing AsylumEffects with cache persistence...');
    window.asylumEffects = new AsylumEffects();

    // Global method for testing audio cache
    window.testAsylumAudio = function() {
      if (window.asylumEffects) {
        const health = window.asylumEffects.checkAudioCacheHealth();
        console.log('AsylumEffects Audio Health Test:', health);

        // Test play a sound
        window.asylumEffects.playSound('heartMonitor');
      }
    };
  }
})();

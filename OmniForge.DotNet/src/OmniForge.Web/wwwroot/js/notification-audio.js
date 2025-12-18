class NotificationAudioManager {
    constructor() {
        this.audioCache = {};
        this.volume = 0.8; // Higher volume for OBS capture
        this.notificationSounds = {
            follow: 'heartMonitor.wav',
            subscription: 'hypeTrain.wav',
            resub: 'typewriter.wav',
            giftsub: 'pillRattle.mp3',
            bits: 'electroshock.wav',
            milestone: 'alarm.wav'
        };
        // Auto-init ONLY if we are on the overlay page
        if (window.location.pathname.startsWith('/overlay')) {
            if (document.readyState === 'complete') {
                this.init();
            } else {
                window.addEventListener('load', () => this.init());
            }
        }
    }

    async init() {
        if (this.initialized) return;
        this.initialized = true;

        console.log('OBS Audio Manager initializing for Overlay...');

        // Try to restore from cache first
        const restored = this.restoreNotificationCacheState();

        // Then preload any missing sounds
        this.preloadNotificationSounds();

        // Health check after 2 seconds
        setTimeout(() => this.checkNotificationHealth(), 2000);
    }

    // Restore notification cache from localStorage
    restoreNotificationCacheState() {
        try {
            const saved = localStorage.getItem('omni_notification_audio_cache');
            if (!saved) return false;

            const cacheState = JSON.parse(saved);
            const age = Date.now() - cacheState.timestamp;

            if (age > 24 * 60 * 60 * 1000) {
                localStorage.removeItem('omni_notification_audio_cache');
                return false;
            }

            console.log('Restoring notification cache...');
            this.volume = cacheState.volume || 0.8;

            // Priority restore notifications
            cacheState.notifications.forEach(notif => {
                if (notif.loaded) {
                    this.priorityLoadNotification(notif.eventType, notif.soundFile);
                }
            });

            return true;
        } catch (error) {
            console.warn('Failed to restore notification cache:', error);
            return false;
        }
    }

    // Priority load a specific notification sound
    priorityLoadNotification(eventType, soundFile) {
        const audio = new Audio('/sounds/' + soundFile);
        audio.preload = 'auto';
        audio.volume = this.volume;
        audio.crossOrigin = 'anonymous';
        audio.setAttribute('controls', 'false');
        audio.setAttribute('autoplay', 'false');

        this.audioCache[eventType] = audio;

        audio.addEventListener('canplaythrough', () => {
            console.log('Priority restored notification:', eventType);
            this.saveNotificationCacheState();
        }, { once: true });

        audio.addEventListener('error', (e) => {
            console.warn('Priority notification load failed:', eventType, e);
            delete this.audioCache[eventType];
        });

        audio.style.display = 'none';
        document.body.appendChild(audio);
    }

    // Save notification cache state to localStorage
    saveNotificationCacheState() {
        try {
            const cacheState = {
                timestamp: Date.now(),
                notifications: Object.keys(this.audioCache).map(eventType => ({
                    eventType: eventType,
                    soundFile: this.notificationSounds[eventType],
                    loaded: this.audioCache[eventType] && this.audioCache[eventType].readyState >= 3
                })),
                volume: this.volume
            };

            localStorage.setItem('omni_notification_audio_cache', JSON.stringify(cacheState));
            console.log('Notification cache state saved');
        } catch (error) {
            console.warn('Failed to save notification cache:', error);
        }
    }

    // Check notification cache health
    checkNotificationHealth() {
        const expectedNotifications = Object.keys(this.notificationSounds);
        const cachedNotifications = Object.keys(this.audioCache);
        const healthyNotifications = cachedNotifications.filter(eventType => {
            const audio = this.audioCache[eventType];
            return audio && audio.readyState >= 3;
        });

        const healthPercentage = (healthyNotifications.length / expectedNotifications.length) * 100;
        console.log('Notification cache health: ' + healthPercentage.toFixed(1) + '% (' + healthyNotifications.length + '/' + expectedNotifications.length + ')');

        return {
            healthy: healthPercentage >= 80,
            percentage: healthPercentage,
            missing: expectedNotifications.filter(eventType => !cachedNotifications.includes(eventType)),
            failed: cachedNotifications.filter(eventType => {
                const audio = this.audioCache[eventType];
                return !audio || audio.readyState < 3;
            })
        };
    }

    preloadNotificationSounds() {
        Object.entries(this.notificationSounds).forEach(([eventType, soundFile]) => {
            // Skip if already loaded from cache
            if (this.audioCache[eventType] && this.audioCache[eventType].readyState >= 3) {
                console.log('Sound already cached:', eventType);
                return;
            }

            // Create dedicated audio element for OBS capture
            const audio = new Audio('/sounds/' + soundFile);
            audio.preload = 'auto';
            audio.volume = this.volume;
            audio.crossOrigin = 'anonymous'; // Ensure OBS can access

            // Set audio attributes for better OBS compatibility
            audio.setAttribute('controls', 'false');
            audio.setAttribute('autoplay', 'false');

            this.audioCache[eventType] = audio;

            audio.addEventListener('canplaythrough', () => {
                console.log('OBS notification sound loaded:', eventType, soundFile);
                this.saveNotificationCacheState(); // Save state after each load
            }, { once: true });

            audio.addEventListener('error', (e) => {
                console.warn('Failed to load notification sound for OBS:', eventType, soundFile, e);
                delete this.audioCache[eventType]; // Clean up failed entry
            });

            // Add to DOM for OBS Browser Source audio capture
            audio.style.display = 'none';
            document.body.appendChild(audio);
        });

        console.log('All notification sounds prepared for OBS capture');
    }

    async playNotification(eventType, data = {}) {
        // If the overlay isn't visible, don't play audio.
        // OBS can throttle hidden browser sources; queued JS events may flush on resume.
        if (document.visibilityState !== 'visible') {
            console.log('🔇 Notification suppressed because overlay is not visible:', eventType);
            return;
        }

        // Suppress audio briefly after resume to avoid replaying buffered alerts.
        if (window.omniOverlayResumeSuppressUntil && Date.now() < window.omniOverlayResumeSuppressUntil) {
            console.log('🔇 Notification suppressed during resume window:', eventType);
            return;
        }

        if (window.omniSilenceUntil && Date.now() < window.omniSilenceUntil) {
            console.log('🔇 Notification sound suppressed during silence window:', eventType);
            return;
        }

        if (!this.notificationSounds[eventType]) {
            console.warn('⚠️ No sound configured for event type:', eventType);
            return;
        }

        console.log('🔊 Playing OBS notification:', eventType);

        try {
            const audio = this.audioCache[eventType];
            if (!audio) {
                console.warn('⚠️ Audio not cached for:', eventType);
                return;
            }

            // Reset audio to beginning and set volume
            audio.currentTime = 0;
            audio.volume = this.volume;

            // Simple play for OBS Browser Source capture
            const playPromise = audio.play();

            if (playPromise !== undefined) {
                playPromise.then(() => {
                    console.log('✅ OBS notification played:', eventType);
                }).catch(error => {
                    console.warn('⚠️ OBS audio playback failed:', eventType, error);
                    // Try user interaction workaround
                    this.handleAudioPlaybackError(eventType, error);
                });
            }

        } catch (error) {
            console.warn('⚠️ OBS notification system failed:', error);
        }
    }

    handleAudioPlaybackError(eventType, error) {
        // Common issue: Browser autoplay policy
        if (error.name === 'NotAllowedError') {
            console.log('🔇 Autoplay blocked for:', eventType, '- User interaction required');

            // Create a one-time click handler to enable audio
            const enableAudio = () => {
                console.log('👆 User interaction detected, enabling audio...');
                this.audioCache[eventType].play().then(() => {
                    console.log('✅ Audio enabled after user interaction');
                }).catch(e => console.warn('⚠️ Audio still blocked:', e));

                document.removeEventListener('click', enableAudio);
                document.removeEventListener('keydown', enableAudio);
            };

            document.addEventListener('click', enableAudio, { once: true });
            document.addEventListener('keydown', enableAudio, { once: true });
        }
    }

    // Test method for OBS audio verification
    testNotificationAudio(eventType = 'follow') {
        console.log('🧪 Testing OBS audio capture for:', eventType);
        this.playNotification(eventType, { test: true });
    }

    setVolume(volume) {
        this.volume = Math.max(0, Math.min(1, volume));
        console.log('🔊 Setting OBS audio volume to:', this.volume);

        Object.values(this.audioCache).forEach(audio => {
            audio.volume = this.volume;
        });
        console.log('✅ OBS audio volume updated:', this.volume);
    }

    // Get current audio status for debugging
    getAudioStatus() {
        return {
            volume: this.volume,
            sounds: Object.keys(this.notificationSounds),
            cacheStatus: Object.keys(this.audioCache).map(key => ({
                event: key,
                loaded: this.audioCache[key] ? this.audioCache[key].readyState >= 3 : false,
                file: this.notificationSounds[key]
            }))
        };
    }
}

// Initialize and expose globally
window.notificationAudio = new NotificationAudioManager();

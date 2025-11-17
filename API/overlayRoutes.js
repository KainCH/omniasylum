const express = require('express');
const database = require('./database');
const path = require('path');

const router = express.Router();

/**
 * Stream overlay page for OBS browser source
 * GET /overlay/:userId
 */
router.get('/:userId', async (req, res) => {
  try {
    const user = await database.getUser(req.params.userId);

    if (!user) {
      return res.status(404).send('User not found');
    }

    // Check if user has overlay feature enabled
    const hasFeature = await database.hasFeature(req.params.userId, 'streamOverlay');
    if (!hasFeature) {
      return res.status(403).send('Stream overlay not enabled for this user');
    }

    // Get current stream status for initial state
    const streamStatus = user.streamStatus || 'offline';

    // Get user's overlay settings (for customization, not enabling/disabling)
    const overlaySettings = await database.getUserOverlaySettings(req.params.userId);

    const counters = await database.getCounters(req.params.userId);

    // Generate dynamic overlay HTML with user settings
    const overlayHTML = `
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>${user.displayName} - Stream Counter Overlay</title>
    <link href="https://fonts.googleapis.com/css2?family=Creepster&display=swap" rel="stylesheet">
    <style>
        body {
            margin: 0;
            padding: 20px;
            font-family: 'Arial', sans-serif;
            background: transparent;
            color: ${overlaySettings.theme.textColor};
            overflow: hidden;
        }

        .counter-overlay {
            display: flex;
            flex-direction: column;
            gap: 15px;
            position: fixed;
            ${overlaySettings.position.includes('top') ? 'top: 20px;' : 'bottom: 20px;'}
            ${overlaySettings.position.includes('right') ? 'right: 20px;' : 'left: 20px;'}
        }

        .counter-item {
            background: ${overlaySettings.theme.backgroundColor};
            padding: 15px 25px;
            border-radius: 10px;
            border: 2px solid ${overlaySettings.theme.borderColor};
            backdrop-filter: blur(5px);
            display: flex;
            align-items: center;
            gap: 15px;
            min-width: 200px;
            transition: ${overlaySettings.animations.enabled ? 'all 0.3s ease' : 'none'};
        }

        .counter-item:hover {
            ${overlaySettings.animations.enabled ? 'transform: scale(1.05);' : ''}
            ${overlaySettings.animations.enabled ? `box-shadow: 0 5px 20px ${overlaySettings.theme.borderColor}30;` : ''}
        }

        .counter-icon {
            font-size: 2rem;
            width: 40px;
            text-align: center;
        }

        .counter-info {
            flex: 1;
        }

        .counter-label {
            font-size: 1rem;
            color: ${overlaySettings.theme.borderColor};
            margin-bottom: 5px;
            font-weight: bold;
        }

        .counter-value {
            font-size: 2rem;
            font-weight: bold;
            color: ${overlaySettings.theme.textColor};
            text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.8);
        }

        /* Bits Goal Progress */
        .bits-goal-progress {
            margin-top: 8px;
        }

        .goal-label {
            font-size: 0.8rem;
            color: ${overlaySettings.theme.borderColor};
            margin-bottom: 4px;
            opacity: 0.9;
        }

        .goal-bar {
            width: 100%;
            height: 6px;
            background: rgba(255, 255, 255, 0.2);
            border-radius: 3px;
            overflow: hidden;
        }

        .goal-fill {
            height: 100%;
            background: linear-gradient(90deg, #9146ff, #772ce8);
            border-radius: 3px;
            transition: width 0.3s ease;
            box-shadow: 0 0 8px rgba(145, 70, 255, 0.5);
        }

        /* Alert animations */
        .counter-alert {
            position: fixed;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            background: linear-gradient(135deg, #8b4513, #d4af37);
            color: white;
            padding: 30px 50px;
            border-radius: 20px;
            font-size: 3rem;
            font-weight: bold;
            text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.8);
            z-index: 1000;
            opacity: 0;
            transform: translate(-50%, -50%) scale(0.5);
            transition: all 0.5s cubic-bezier(0.68, -0.55, 0.265, 1.55);
        }

        .counter-alert.show {
            opacity: 1;
            transform: translate(-50%, -50%) scale(1);
        }

        .counter-alert.hide {
            opacity: 0;
            transform: translate(-50%, -50%) scale(1.2);
        }

        /* Pulse animation for counter updates */
        .counter-pulse {
            animation: pulse 0.6s cubic-bezier(0.25, 0.46, 0.45, 0.94);
        }

        @keyframes pulse {
            0% { transform: scale(1); }
            50% { transform: scale(1.1); }
            100% { transform: scale(1); }
        }

        /* Bits celebration effect */
        .bits-celebration {
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            pointer-events: none;
            z-index: 999;
        }

        .bit-particle {
            position: absolute;
            width: 10px;
            height: 10px;
            background: #d4af37;
            border-radius: 50%;
            animation: bitsFall 3s linear forwards;
        }

        @keyframes bitsFall {
            0% {
                transform: translateY(-20px) rotate(0deg);
                opacity: 1;
            }
            100% {
                transform: translateY(100vh) rotate(360deg);
                opacity: 0;
            }
        }

        /* Subscriber celebration effects */
        .sub-celebration {
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            pointer-events: none;
            z-index: 1001;
        }

        .sub-heart {
            position: absolute;
            font-size: 2rem;
            color: #9146ff;
            animation: subHeartFloat 4s ease-out forwards;
        }

        @keyframes subHeartFloat {
            0% {
                transform: translateY(100vh) scale(0);
                opacity: 1;
            }
            20% {
                transform: translateY(70vh) scale(1.2);
                opacity: 1;
            }
            100% {
                transform: translateY(-20vh) scale(0.8);
                opacity: 0;
            }
        }

        @keyframes sparkleFloat {
            0% {
                transform: translateY(0) rotate(0deg) scale(0);
                opacity: 1;
            }
            50% {
                transform: translateY(-50px) rotate(180deg) scale(1.2);
                opacity: 1;
            }
            100% {
                transform: translateY(-100px) rotate(360deg) scale(0);
                opacity: 0;
            }
        }

        .sub-banner {
            position: fixed;
            top: 20px;
            left: 50%;
            transform: translateX(-50%);
            background: linear-gradient(135deg, #9146ff, #ff6b6b);
            color: white;
            padding: 20px 40px;
            border-radius: 15px;
            font-size: 1.5rem;
            font-weight: bold;
            text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.8);
            z-index: 1002;
            opacity: 0;
            transform: translateX(-50%) translateY(-100px) scale(0.5);
            transition: all 0.5s cubic-bezier(0.68, -0.55, 0.265, 1.55);
        }

        .sub-banner.show {
            opacity: 1;
            transform: translateX(-50%) translateY(0) scale(1);
        }

        .sub-banner.hide {
            opacity: 0;
            transform: translateX(-50%) translateY(-100px) scale(0.5);
        }



        .counters-container {
            display: flex;
            flex-direction: column;
            gap: 15px;
            position: fixed;
            ${overlaySettings.position.includes('top') ? 'top: 20px;' : 'bottom: 20px;'}
            ${overlaySettings.position.includes('right') ? 'right: 20px;' : 'left: 20px;'}
            opacity: ${streamStatus === 'live' ? '1' : '0'};
            transition: opacity 0.8s ease-in-out;
        }
    </style>
</head>
<body>
    <!-- Counters Container - fades in when stream goes live -->
    <div class="counters-container" id="counters-container">
        ${overlaySettings.counters.deaths ? `
        <div class="counter-item" id="deaths-counter">
            <div class="counter-icon">💀</div>
            <div class="counter-info">
                <div class="counter-label">Deaths</div>
                <div class="counter-value" id="deaths-value">${counters.deaths || 0}</div>
            </div>
        </div>` : ''}

        ${overlaySettings.counters.swears ? `
        <div class="counter-item" id="swears-counter">
            <div class="counter-icon">🤬</div>
            <div class="counter-info">
                <div class="counter-label">Swears</div>
                <div class="counter-value" id="swears-value">${counters.swears || 0}</div>
            </div>
        </div>` : ''}

        ${overlaySettings.counters.screams ? `
        <div class="counter-item" id="screams-counter">
            <div class="counter-icon">😱</div>
            <div class="counter-info">
                <div class="counter-label">Screams</div>
                <div class="counter-value" id="screams-value">${counters.screams || 0}</div>
            </div>
        </div>` : ''}

        ${overlaySettings.counters.bits ? `
        <div class="counter-item" id="bits-counter">
            <div class="counter-icon">💎</div>
            <div class="counter-info">
                <div class="counter-label">Stream Bits</div>
                <div class="counter-value" id="bits-value">${counters.bits || 0}</div>
                ${overlaySettings.bitsGoal ? `
                <div class="bits-goal-progress">
                    <div class="goal-label">Goal: ${overlaySettings.bitsGoal.current || 0} / ${overlaySettings.bitsGoal.target || 1000}</div>
                    <div class="goal-bar">
                        <div class="goal-fill" style="width: ${Math.min(100, ((overlaySettings.bitsGoal.current || 0) / (overlaySettings.bitsGoal.target || 1000)) * 100)}%"></div>
                    </div>
                </div>` : ''}
            </div>
        </div>` : ''}
    </div>

    <!-- Alert popup -->
    <div class="counter-alert" id="alert-popup"></div>

    <!-- Bits celebration container -->
    <div class="bits-celebration" id="bits-celebration"></div>

    <!-- Subscriber celebration container -->
    <div class="sub-celebration" id="sub-celebration"></div>

    <!-- Subscriber banner -->
    <div class="sub-banner" id="sub-banner"></div>

    <script src="/socket.io/socket.io.js"></script>
    <script src="/asylum-effects.js"></script>
    <script>
        const userId = '${req.params.userId}';
        const overlaySettings = ${JSON.stringify(overlaySettings)};
        const socket = io();

        // Notification Audio Manager for Twitch Events - OBS Compatible
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
                this.init();
            }

            async init() {
                console.log('🔊 OBS Audio Manager initializing...');
                // Preload notification sounds for OBS Browser Source
                this.preloadNotificationSounds();
            }

            preloadNotificationSounds() {
                Object.entries(this.notificationSounds).forEach(([eventType, soundFile]) => {
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
                        console.log('🔊 OBS notification sound loaded:', eventType, soundFile);
                    }, { once: true });

                    audio.addEventListener('error', (e) => {
                        console.warn('⚠️ Failed to load notification sound for OBS:', eventType, soundFile, e);
                    });

                    // Add to DOM for OBS Browser Source audio capture
                    audio.style.display = 'none';
                    document.body.appendChild(audio);
                });

                console.log('🔊 All notification sounds prepared for OBS capture');
            }

            async playNotification(eventType, data = {}) {
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

        // Initialize notification audio manager
        const notificationAudio = new NotificationAudioManager();

        // Window message handler for OBS audio control
        window.addEventListener('message', (event) => {
            // Security: Only accept messages from same origin
            if (event.origin !== window.location.origin) {
                return;
            }

            const { type, payload } = event.data;

            switch (type) {
                case 'SET_AUDIO_VOLUME':
                    if (payload && typeof payload.volume === 'number') {
                        notificationAudio.setVolume(payload.volume);
                        console.log('✅ OBS audio volume updated via control panel:', payload.volume);
                    }
                    break;

                case 'TEST_NOTIFICATION_AUDIO':
                    if (payload && payload.eventType) {
                        console.log('🧪 Testing OBS notification audio:', payload.eventType);
                        notificationAudio.testNotificationAudio(payload.eventType);
                    }
                    break;

                case 'GET_AUDIO_STATUS':
                    // Return current audio status for debugging
                    const audioStatus = notificationAudio.getAudioStatus();
                    console.log('📊 OBS audio status:', audioStatus);
                    break;

                default:
                    console.log('🔊 Unknown OBS audio message type:', type);
            }
        });

        // Helper function to get size-based styles
        function getSizeStyles(size) {
            const sizes = {
                small: {
                    fontSize: '14px',
                    counterFontSize: '1.5rem',
                    iconFontSize: '1.5rem',
                    padding: '10px 20px',
                    minWidth: '180px',
                    iconWidth: '30px'
                },
                medium: {
                    fontSize: '16px',
                    counterFontSize: '2rem',
                    iconFontSize: '2rem',
                    padding: '15px 25px',
                    minWidth: '200px',
                    iconWidth: '40px'
                },
                large: {
                    fontSize: '20px',
                    counterFontSize: '2.5rem',
                    iconFontSize: '2.5rem',
                    padding: '20px 30px',
                    minWidth: '250px',
                    iconWidth: '50px'
                }
            };
            return sizes[size] || sizes.medium;
        }

        // Apply size-based styles on load
        const sizeStyles = getSizeStyles(overlaySettings.size || 'medium');
        const style = document.createElement('style');
        style.setAttribute('data-size-styles', 'true');
        style.textContent = \`
            .counter-item {
                padding: \${sizeStyles.padding} !important;
                min-width: \${sizeStyles.minWidth} !important;
            }
            .counter-icon {
                font-size: \${sizeStyles.iconFontSize} !important;
                width: \${sizeStyles.iconWidth} !important;
            }
            .counter-label {
                font-size: \${sizeStyles.fontSize} !important;
            }
            .counter-value {
                font-size: \${sizeStyles.counterFontSize} !important;
            }
        \`;
        document.head.appendChild(style);

        // Join user's room for real-time updates
        socket.emit('joinRoom', userId);

        // Helper function to switch overlay content smoothly
        function switchOverlayContent(status) {
            console.log('🔄 Switching overlay to status:', status);

            const countersContainer = document.getElementById('counters-container');

            // Fade counters in when stream goes live, fade out otherwise
            if (status === 'live') {
                console.log('✨ Stream is live - fading counters into existence');
                countersContainer.style.opacity = '1';
            } else {
                console.log('🌙 Stream not live - fading counters out');
                countersContainer.style.opacity = '0';
            }

            // Trigger OBS browser source refresh
            document.body.style.transform = 'translateZ(0)';
            setTimeout(() => document.body.style.transform = 'none', 100);
        }

        // Update counter display
        function updateCounter(type, value, change = null) {
            const element = document.getElementById(type + '-value');
            const counterItem = document.getElementById(type + '-counter');

            if (element) {
                element.textContent = value;

                // Add pulse effect if animations are enabled
                if (overlaySettings.animations.enabled) {
                    counterItem.classList.remove('counter-pulse');
                    setTimeout(() => counterItem.classList.add('counter-pulse'), 10);
                    setTimeout(() => counterItem.classList.remove('counter-pulse'), 600);
                }

                // Show alert if there was a change and alerts are enabled
                if (change !== null && change !== 0 && overlaySettings.animations.showAlerts) {
                    showAlert(type, change, value);
                }
            }
        }

        // Update bits goal progress
        function updateBitsGoal(bitsGoal) {
            const goalLabel = document.querySelector('.goal-label');
            const goalFill = document.querySelector('.goal-fill');

            if (goalLabel && goalFill && bitsGoal) {
                goalLabel.textContent = \`Goal: \${bitsGoal.current || 0} / \${bitsGoal.target || 1000}\`;
                const percentage = Math.min(100, ((bitsGoal.current || 0) / (bitsGoal.target || 1000)) * 100);
                goalFill.style.width = percentage + '%';

                // Add glow effect if goal is reached
                if (bitsGoal.current >= bitsGoal.target) {
                    goalFill.style.boxShadow = '0 0 12px rgba(145, 70, 255, 0.8)';

                    // Show goal reached celebration
                    if (overlaySettings.animations.celebrationEffects) {
                        showBitsGoalCelebration();
                    }
                } else {
                    goalFill.style.boxShadow = '0 0 8px rgba(145, 70, 255, 0.5)';
                }
            }
        }

        // Show bits goal celebration
        function showBitsGoalCelebration() {
            const alert = document.getElementById('alert-popup');
            if (alert) {
                alert.innerHTML = '🎯 BITS GOAL REACHED! 💎';
                alert.classList.add('show');

                // Add sparkle effect
                createSparkles();

                setTimeout(() => {
                    alert.classList.add('hide');
                    setTimeout(() => {
                        alert.classList.remove('show', 'hide');
                    }, 500);
                }, 5000);
            }
        }

        // Create sparkle effects for goal celebration
        function createSparkles() {
            const sparkleContainer = document.createElement('div');
            sparkleContainer.style.cssText = \`
                position: fixed;
                top: 0;
                left: 0;
                width: 100vw;
                height: 100vh;
                pointer-events: none;
                z-index: 999;
            \`;
            document.body.appendChild(sparkleContainer);

            for (let i = 0; i < 20; i++) {
                const sparkle = document.createElement('div');
                sparkle.textContent = '✨';
                sparkle.style.cssText = \`
                    position: absolute;
                    font-size: \${Math.random() * 2 + 1}rem;
                    left: \${Math.random() * 100}vw;
                    top: \${Math.random() * 100}vh;
                    animation: sparkleFloat \${Math.random() * 3 + 2}s ease-out forwards;
                \`;
                sparkleContainer.appendChild(sparkle);
            }

            setTimeout(() => {
                document.body.removeChild(sparkleContainer);
            }, 5000);
        }

        // Show animated alert
        function showAlert(type, change, newValue) {
            const alert = document.getElementById('alert-popup');
            const icon = type === 'deaths' ? '💀' : '🤬';
            const action = change > 0 ? '+' + change : change.toString();

            alert.innerHTML = icon + ' ' + action + ' (' + newValue + ')';
            alert.classList.add('show');

            setTimeout(() => {
                alert.classList.add('hide');
                setTimeout(() => {
                    alert.classList.remove('show', 'hide');
                }, 500);
            }, 2000);
        }

        // Bits celebration effect
        function celebrateBits(amount) {
            if (!overlaySettings.animations.celebrationEffects) return;

            const container = document.getElementById('bits-celebration');
            const particleCount = Math.min(amount, 50); // Limit particles for performance

            for (let i = 0; i < particleCount; i++) {
                setTimeout(() => {
                    const particle = document.createElement('div');
                    particle.className = 'bit-particle';
                    particle.style.left = Math.random() * 100 + '%';
                    particle.style.animationDelay = Math.random() * 0.5 + 's';
                    container.appendChild(particle);

                    setTimeout(() => {
                        if (container.contains(particle)) {
                            container.removeChild(particle);
                        }
                    }, 3000);
                }, i * 50);
            }
        }

        // Subscriber celebration effects
        function celebrateSubscriber(username, type = 'sub', months = null) {
            if (!overlaySettings.animations.celebrationEffects) return;

            const container = document.getElementById('sub-celebration');
            const banner = document.getElementById('sub-banner');

            // Show banner
            let bannerText = '';
            if (type === 'sub') {
                bannerText = '💜 Welcome ' + username + '! 💜';
            } else if (type === 'resub') {
                bannerText = '💜 ' + username + ' - ' + months + ' months! 💜';
            } else if (type === 'gift') {
                bannerText = '💜 Gift Sub for ' + username + '! 💜';
            }

            banner.textContent = bannerText;
            banner.classList.add('show');

            // Create floating hearts
            for (let i = 0; i < 15; i++) {
                setTimeout(() => {
                    const heart = document.createElement('div');
                    heart.className = 'sub-heart';
                    heart.textContent = '💜';
                    heart.style.left = Math.random() * 100 + '%';
                    heart.style.animationDelay = Math.random() * 0.5 + 's';
                    container.appendChild(heart);

                    setTimeout(() => {
                        if (container.contains(heart)) {
                            container.removeChild(heart);
                        }
                    }, 4000);
                }, i * 100);
            }

            // Hide banner after 5 seconds
            setTimeout(() => {
                banner.classList.add('hide');
                setTimeout(() => {
                    banner.classList.remove('show', 'hide');
                }, 500);
            }, 5000);
        }

        // Display custom alert with asylum theme
        function displayCustomAlert(alertData) {
            if (!overlaySettings.animations.enabled) return;

            const { type, username, data, alertConfig } = alertData;

            // Create alert container if it doesn't exist
            let alertContainer = document.getElementById('custom-alert-container');
            if (!alertContainer) {
                alertContainer = document.createElement('div');
                alertContainer.id = 'custom-alert-container';
                alertContainer.style.cssText = \`
                    position: fixed;
                    top: 50%;
                    left: 50%;
                    transform: translate(-50%, -50%);
                    z-index: 9999;
                    pointer-events: none;
                    opacity: 0;
                    transition: opacity 0.5s ease-in-out;
                \`;
                document.body.appendChild(alertContainer);
            }

            // Process text prompt with variables
            let processedText = alertConfig.textPrompt;
            processedText = processedText.replace(/\\[User\\]/g, username);
            processedText = processedText.replace(/\\[X\\]/g, data.viewers || data.amount || data.bits || data.months || '');

            // Additional variable replacements for different event types
            if (data.tier) {
                const tierNames = { '1000': 'Tier 1', '2000': 'Tier 2', '3000': 'Tier 3', 'Prime': 'Prime' };
                processedText = processedText.replace(/\\[Tier\\]/g, tierNames[data.tier] || 'Prime');
            }
            if (data.months) {
                processedText = processedText.replace(/\\[Months\\]/g, data.months);
            }
            if (data.streakMonths) {
                processedText = processedText.replace(/\\[Streak\\]/g, data.streakMonths);
            }
            if (data.message) {
                processedText = processedText.replace(/\\[Message\\]/g, data.message);
            }
            if (data.bits) {
                processedText = processedText.replace(/\\[Bits\\]/g, data.bits);
            }
            if (data.amount) {
                processedText = processedText.replace(/\\[Amount\\]/g, data.amount);
            }
            if (data.viewers) {
                processedText = processedText.replace(/\\[Viewers\\]/g, data.viewers);
            }

            // Create alert element
            const alertElement = document.createElement('div');
            alertElement.className = 'alert-container';
            alertElement.style.cssText = \`
                background: \${alertConfig.backgroundColor || '#1a0d0d'};
                color: \${alertConfig.textColor || '#ffffff'};
                border: 3px solid \${alertConfig.borderColor || '#666666'};
                padding: 20px 30px;
                border-radius: 10px;
                font-family: 'Creepster', cursive;
                font-size: 24px;
                text-align: center;
                box-shadow: 0 0 20px rgba(0, 0, 0, 0.8), inset 0 0 20px rgba(255, 255, 255, 0.1);
                min-width: 400px;
                max-width: 600px;
            \`;

            // Add visual cue if provided
            if (alertConfig.visualCue) {
                const visualElement = document.createElement('div');
                visualElement.style.cssText = \`
                    font-size: 14px;
                    opacity: 0.8;
                    margin-bottom: 10px;
                    font-style: italic;
                    color: \${alertConfig.textColor || '#ffffff'}aa;
                \`;
                visualElement.textContent = alertConfig.visualCue;
                alertElement.appendChild(visualElement);
            }

            // Add main text
            const textElement = document.createElement('div');
            textElement.className = 'alert-text';
            textElement.textContent = processedText;
            alertElement.appendChild(textElement);

            // Add sound description if provided
            if (alertConfig.soundDescription) {
                const soundElement = document.createElement('div');
                soundElement.style.cssText = \`
                    font-size: 12px;
                    opacity: 0.6;
                    margin-top: 10px;
                    font-style: italic;
                \`;
                soundElement.textContent = '♪ ' + alertConfig.soundDescription;
                alertElement.appendChild(soundElement);
            }

            // Add CSS animation keyframes if not already added
            if (!document.getElementById('asylum-alert-styles')) {
                const styles = document.createElement('style');
                styles.id = 'asylum-alert-styles';
                styles.textContent = \`
                    @keyframes asylumPulse {
                        0% {
                            opacity: 0;
                            transform: scale(0.8) rotateZ(-2deg);
                            filter: blur(2px);
                        }
                        15% {
                            opacity: 1;
                            transform: scale(1.05) rotateZ(1deg);
                            filter: blur(0);
                        }
                        85% {
                            opacity: 1;
                            transform: scale(1) rotateZ(-0.5deg);
                        }
                        100% {
                            opacity: 0;
                            transform: scale(0.9) rotateZ(2deg);
                            filter: blur(1px);
                        }
                    }

                    @import url('https://fonts.googleapis.com/css2?family=Creepster&display=swap');
                \`;
                document.head.appendChild(styles);
            }

            // Clear previous alert
            alertContainer.innerHTML = '';
            alertContainer.appendChild(alertElement);

            // Trigger advanced visual effects if effects are defined
            if (alertConfig.effects && window.asylumEffects) {
                window.asylumEffects.triggerEffect(alertConfig);
            }

            // Show alert with fade in
            alertContainer.style.opacity = '1';

            // Auto-hide after duration
            setTimeout(() => {
                alertContainer.style.opacity = '0';
                setTimeout(() => {
                    if (alertContainer.contains(alertElement)) {
                        alertContainer.removeChild(alertElement);
                    }
                }, 500);
            }, alertConfig.duration || 4000);
        }

        // Simplified alert for backwards compatibility
        function showCustomAlert(type, data) {
            const alertMessages = {
                follow: \`👥 \${data.follower} has joined the asylum!\`,
                raid: \`🚨 \${data.raider} has breached the ward with \${data.viewers} intruders!\`
            };

            const alert = document.getElementById('alert-popup');
            if (alert && alertMessages[type]) {
                alert.innerHTML = alertMessages[type];
                alert.classList.add('show');

                setTimeout(() => {
                    alert.classList.add('hide');
                    setTimeout(() => {
                        alert.classList.remove('show', 'hide');
                    }, 500);
                }, 4000);
            }
        }

        // Listen for counter updates
        socket.on('counterUpdate', (data) => {
            console.log('📊 Overlay received counterUpdate:', data);

            // Backend sends data with structure: { userId, counters: {deaths, swears, screams, bits}, change }
            const counters = data.counters || data;  // Handle both formats

            if (overlaySettings.counters.deaths) {
                updateCounter('deaths', counters.deaths || 0);
            }
            if (overlaySettings.counters.swears) {
                updateCounter('swears', counters.swears || 0);
            }
            if (overlaySettings.counters.screams) {
                updateCounter('screams', counters.screams || 0);
            }
            if (overlaySettings.counters.bits) {
                updateCounter('bits', counters.bits || 0);

                // Update bits goal if enabled and present
                if (overlaySettings.bitsGoal && overlaySettings.bitsGoal.enabled) {
                    updateBitsGoal(counters.bits, overlaySettings.bitsGoal.target);
                }
            }
        });

        // Listen for bits goal updates
        socket.on('bitsGoalUpdate', (data) => {
            if (data.userId === userId) {
                console.log('🎯 Bits goal update received:', data);

                if (overlaySettings.bitsGoal && overlaySettings.bitsGoal.enabled) {
                    updateBitsGoal(data.current, data.target);

                    // Update local settings
                    overlaySettings.bitsGoal.current = data.current;
                    overlaySettings.bitsGoal.target = data.target;

                    // Show celebration if completed
                    if (data.completed && data.current >= data.target) {
                        showBitsGoalCelebration();
                    }
                }
            }
        });

        // Listen for bits goal completion
        socket.on('bitsGoalComplete', (data) => {
            if (data.userId === userId) {
                console.log('🎉 Bits goal completed!', data);

                if (overlaySettings.bitsGoal && overlaySettings.bitsGoal.enabled) {
                    // Update the goal display
                    updateBitsGoal(data.current, data.target);

                    // Show celebration animation
                    showBitsGoalCelebration();

                    // Update local settings
                    overlaySettings.bitsGoal.current = data.current;
                }
            }
        });

        // Listen for bits events
        socket.on('bitsReceived', (data) => {
            if (data.userId === userId) {
                celebrateBits(data.amount);

                // Show bits alert
                const alert = document.getElementById('alert-popup');
                alert.innerHTML = '💎 ' + data.amount + ' bits from ' + data.username + '!';
                alert.classList.add('show');

                setTimeout(() => {
                    alert.classList.add('hide');
                    setTimeout(() => {
                        alert.classList.remove('show', 'hide');
                    }, 500);
                }, 3000);
            }
        });

        // Listen for subscriber events (LEGACY - keeping for compatibility)
        socket.on('newSubscriber', (data) => {
            if (data.userId === userId) {
                celebrateSubscriber(data.subscriber || data.username, 'sub');
            }
        });

        socket.on('resub', (data) => {
            if (data.userId === userId) {
                celebrateSubscriber(data.username, 'resub', data.months);
            }
        });

        socket.on('giftSub', (data) => {
            if (data.userId === userId) {
                celebrateSubscriber(data.recipient, 'gift');
            }
        });

        // Listen for follow events
        socket.on('newFollower', (data) => {
            if (data.userId === userId) {
                console.log('👥 New follower received:', data.follower);

                // Play notification audio
                notificationAudio.playNotification('follow', {
                    follower: data.follower
                });

                if (data.alertConfig) {
                    displayCustomAlert({
                        type: 'follow',
                        username: data.follower,
                        data: { follower: data.follower },
                        alertConfig: data.alertConfig,
                        timestamp: data.timestamp
                    });
                } else {
                    // Fallback to simple alert
                    showCustomAlert('follow', data);
                }
            }
        });

        // Listen for raid events
        socket.on('raidReceived', (data) => {
            if (data.userId === userId) {
                showCustomAlert('raid', data);
            }
        });

        // Listen for custom alerts
        socket.on('customAlert', (data) => {
            if (data.userId === userId) {
                displayCustomAlert(data);
            }
        });

        // Listen for new subscription events
        socket.on('newSubscription', (data) => {
            if (data.userId === userId) {
                console.log('⭐ New subscription received:', data.subscriber, 'Tier', data.tier);

                // Play notification audio
                notificationAudio.playNotification('subscription', {
                    subscriber: data.subscriber,
                    tier: data.tier,
                    isGift: data.isGift
                });

                if (data.alertConfig) {
                    displayCustomAlert({
                        type: 'subscription',
                        username: data.subscriber,
                        data: { tier: data.tier, isGift: data.isGift },
                        alertConfig: data.alertConfig,
                        timestamp: data.timestamp
                    });
                } else {
                    // Fallback to simple celebration
                    celebrateSubscriber(data.subscriber, 'sub');
                }
            }
        });

        // Listen for gift sub events
        socket.on('newGiftSub', (data) => {
            if (data.userId === userId) {
                console.log('🎁 Gift sub received:', data.gifter, 'gifted', data.amount, 'subs');

                // Play notification audio
                notificationAudio.playNotification('giftsub', {
                    gifter: data.gifter,
                    amount: data.amount,
                    tier: data.tier
                });

                if (data.alertConfig) {
                    displayCustomAlert({
                        type: 'giftsub',
                        username: data.gifter,
                        data: { amount: data.amount, tier: data.tier },
                        alertConfig: data.alertConfig,
                        timestamp: data.timestamp
                    });
                } else {
                    // Fallback to simple celebration
                    celebrateSubscriber(data.gifter, 'gift');
                }
            }
        });

        // Listen for resub events
        socket.on('newResub', (data) => {
            if (data.userId === userId) {
                console.log('🔄 Resub received:', data.subscriber, data.months, 'months');

                // Play notification audio
                notificationAudio.playNotification('resub', {
                    subscriber: data.subscriber,
                    months: data.months,
                    tier: data.tier
                });

                if (data.alertConfig) {
                    displayCustomAlert({
                        type: 'resub',
                        username: data.subscriber,
                        data: { months: data.months, streakMonths: data.streakMonths, message: data.message, tier: data.tier },
                        alertConfig: data.alertConfig,
                        timestamp: data.timestamp
                    });
                } else {
                    // Fallback to simple celebration
                    celebrateSubscriber(data.subscriber, 'resub', data.months);
                }
            }
        });

        // Listen for cheer/bits events
        socket.on('newCheer', (data) => {
            if (data.userId === userId) {
                console.log('💎 Bits received:', data.cheerer, 'cheered', data.bits, 'bits');

                // Play notification audio
                notificationAudio.playNotification('bits', {
                    cheerer: data.cheerer,
                    bits: data.bits,
                    isAnonymous: data.isAnonymous
                });

                if (data.alertConfig) {
                    displayCustomAlert({
                        type: 'bits',
                        username: data.cheerer,
                        data: { bits: data.bits, message: data.message, isAnonymous: data.isAnonymous },
                        alertConfig: data.alertConfig,
                        timestamp: data.timestamp
                    });
                } else {
                    // Fallback to simple bits animation
                    triggerBitsAnimation(data.bits);
                }
            }
        });

        // Listen for bits use events (new channel.bits.use EventSub)
        socket.on('newBitsUse', (data) => {
            if (data.userId === userId) {
                console.log('💎 Bits used:', data.user, 'used', data.bits, 'bits via', data.eventType);

                // Play notification audio
                notificationAudio.playNotification('bits', {
                    cheerer: data.user,
                    bits: data.bits,
                    isAnonymous: data.isAnonymous,
                    eventType: data.eventType
                });

                if (data.alertConfig) {
                    displayCustomAlert({
                        type: 'bits',
                        username: data.user,
                        data: {
                            bits: data.bits,
                            message: data.message,
                            isAnonymous: data.isAnonymous,
                            eventType: data.eventType
                        },
                        alertConfig: data.alertConfig,
                        timestamp: data.timestamp
                    });
                } else {
                    // Fallback to simple bits animation
                    triggerBitsAnimation(data.bits, data.eventType);
                }
            }
        });

        // Listen for milestone achievements
        socket.on('milestoneReached', (data) => {
            if (data.userId === userId) {
                console.log('🎯 Milestone reached:', data.counterType, data.milestone);

                // Play milestone notification audio
                notificationAudio.playNotification('milestone', {
                    counterType: data.counterType,
                    milestone: data.milestone,
                    newValue: data.newValue
                });

                // Show milestone alert
                const alert = document.getElementById('alert-popup');
                if (alert) {
                    const emoji = data.counterType === 'deaths' ? '💀' : '🤬';
                    const counterName = data.counterType === 'deaths' ? 'Deaths' : 'Swears';
                    alert.innerHTML = emoji + ' MILESTONE! ' + data.milestone + ' ' + counterName + ' Reached! ' + emoji;
                    alert.classList.add('show');

                    setTimeout(() => {
                        alert.classList.add('hide');
                        setTimeout(() => {
                            alert.classList.remove('show', 'hide');
                        }, 500);
                    }, 4000);
                }
            }
        });

        // Listen for stream status changes (live/offline/prepping)
        socket.on('streamStatusChanged', (data) => {
            if (data.userId === userId) {
                console.log('🔄 Stream status changed to:', data.streamStatus);
                switchOverlayContent(data.streamStatus);
            }
        });

        // Listen for stream events
        socket.on('streamStarted', (data) => {
            if (data.userId === userId) {
                // Show stream started notification
                const alert = document.getElementById('alert-popup');
                alert.innerHTML = '🎬 Stream Started - Bits Counter Reset!';
                alert.classList.add('show');

                setTimeout(() => {
                    alert.classList.add('hide');
                    setTimeout(() => {
                        alert.classList.remove('show', 'hide');
                    }, 500);
                }, 3000);

                // Update all counters
                updateCounter('deaths', data.counters.deaths, 0);
                updateCounter('swears', data.counters.swears, 0);
                updateCounter('bits', data.counters.bits, data.change?.bits);
            }
        });

        // Listen for overlay settings updates (size, position, theme, etc.)
        socket.on('overlaySettingsUpdate', (newSettings) => {
            console.log('🎨 Overlay settings updated:', newSettings);

            // Update size if changed
            if (newSettings.size && newSettings.size !== overlaySettings.size) {
                overlaySettings.size = newSettings.size;
                const newSizeStyles = getSizeStyles(newSettings.size);

                // Update dynamic styles
                const existingStyle = document.querySelector('style[data-size-styles]');
                if (existingStyle) {
                    existingStyle.remove();
                }

                const style = document.createElement('style');
                style.setAttribute('data-size-styles', 'true');
                style.textContent = \`
                    .counter-item {
                        padding: \${newSizeStyles.padding} !important;
                        min-width: \${newSizeStyles.minWidth} !important;
                    }
                    .counter-icon {
                        font-size: \${newSizeStyles.iconFontSize} !important;
                        width: \${newSizeStyles.iconWidth} !important;
                    }
                    .counter-label {
                        font-size: \${newSizeStyles.fontSize} !important;
                    }
                    .counter-value {
                        font-size: \${newSizeStyles.counterFontSize} !important;
                    }
                \`;
                document.head.appendChild(style);
            }

            // Update position if changed
            if (newSettings.position && newSettings.position !== overlaySettings.position) {
                overlaySettings.position = newSettings.position;
                const overlay = document.querySelector('.counter-overlay');
                if (overlay) {
                    overlay.style.top = newSettings.position.includes('top') ? '20px' : 'auto';
                    overlay.style.bottom = newSettings.position.includes('bottom') ? '20px' : 'auto';
                    overlay.style.left = newSettings.position.includes('left') ? '20px' : 'auto';
                    overlay.style.right = newSettings.position.includes('right') ? '20px' : 'auto';
                }
            }

            // Update theme colors if changed
            if (newSettings.theme) {
                Object.assign(overlaySettings.theme, newSettings.theme);
                document.querySelectorAll('.counter-item').forEach(item => {
                    item.style.backgroundColor = overlaySettings.theme.backgroundColor;
                    item.style.borderColor = overlaySettings.theme.borderColor;
                    item.style.color = overlaySettings.theme.textColor;
                });
            }
        });

        // Initialize overlay with current stream status
        socket.on('connect', () => {
            console.log('🔌 Connected to server, initializing overlay...');
            console.log('🔌 Socket connected - Transport:', socket.io.engine.transport.name);
            // Request current stream status to show correct initial state
            socket.emit('getStreamStatus', userId);
        });

        // Monitor connection status
        socket.on('disconnect', (reason) => {
            console.log('❌ Socket disconnected:', reason);
        });

        socket.on('connect_error', (error) => {
            console.error('❌ Connection error:', error);
        });

        socket.on('reconnect', (attemptNumber) => {
            console.log('🔄 Reconnected on attempt:', attemptNumber);
        });

        // Handle initial stream status response
        socket.on('streamStatus', (data) => {
            if (data.userId === userId) {
                console.log('📊 Initial stream status:', data.streamStatus);
                switchOverlayContent(data.streamStatus || 'offline');
            }
        });

        // Keep connection alive with frequent pings
        setInterval(() => {
            if (socket.connected) {
                socket.emit('ping');
                console.log('📡 Overlay keepalive ping sent');
            }
        }, 6000); // 6 seconds to match server ping interval
    </script>
</body>
</html>
    `;

    res.send(overlayHTML);

  } catch (error) {
    console.error('Error serving overlay:', error);
    res.status(500).send('Internal server error');
  }
});

/**
 * Get overlay settings for a user
 * GET /api/overlay/:userId/settings
 */
router.get('/:userId/settings', async (req, res) => {
  try {
    const user = await database.getUser(req.params.userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    const hasFeature = await database.hasFeature(req.params.userId, 'streamOverlay');

    res.json({
      overlayEnabled: hasFeature,
      overlayUrl: hasFeature ? `${req.protocol}://${req.get('host')}/overlay/${req.params.userId}` : null,
      alertAnimations: await database.hasFeature(req.params.userId, 'alertAnimations'),
      bitsIntegration: await database.hasFeature(req.params.userId, 'bitsIntegration')
    });

  } catch (error) {
    console.error('Error getting overlay settings:', error);
    res.status(500).json({ error: 'Failed to get overlay settings' });
  }
});

/**
 * Overlay setup instructions page
 * GET /overlay/setup/:userId
 */
router.get('/setup/:userId', async (req, res) => {
  try {
    const { userId } = req.params;
    const user = await database.getUser(userId);

    if (!user) {
      return res.status(404).send('User not found');
    }

    const baseUrl = process.env.NODE_ENV === 'production'
      ? 'https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io'
      : `http://localhost:${process.env.PORT || 3000}`;

    const overlayUrl = `${baseUrl}/overlay/${userId}`;

    const html = `
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>OBS Overlay Setup - ${user.displayName || user.username}</title>
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }

        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: #333;
            line-height: 1.6;
            min-height: 100vh;
        }

        .container {
            max-width: 1200px;
            margin: 0 auto;
            padding: 20px;
        }

        .header {
            text-align: center;
            color: white;
            margin-bottom: 30px;
        }

        .header h1 {
            font-size: 2.5rem;
            margin-bottom: 10px;
            text-shadow: 2px 2px 4px rgba(0,0,0,0.3);
        }

        .header p {
            font-size: 1.2rem;
            opacity: 0.9;
        }

        .card {
            background: white;
            border-radius: 15px;
            padding: 30px;
            margin-bottom: 25px;
            box-shadow: 0 10px 30px rgba(0,0,0,0.2);
            border-left: 5px solid #9146ff;
        }

        .card h2 {
            color: #9146ff;
            margin-bottom: 20px;
            font-size: 1.8rem;
            display: flex;
            align-items: center;
            gap: 10px;
        }

        .card h3 {
            color: #555;
            margin-bottom: 15px;
            margin-top: 25px;
        }

        .step {
            background: #f8f9fa;
            border-radius: 10px;
            padding: 20px;
            margin-bottom: 20px;
            border-left: 4px solid #28a745;
        }

        .step-number {
            background: #28a745;
            color: white;
            width: 30px;
            height: 30px;
            border-radius: 50%;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            font-weight: bold;
            margin-right: 15px;
        }

        .url-container {
            background: #2d3748;
            color: #e2e8f0;
            padding: 15px;
            border-radius: 8px;
            font-family: 'Courier New', monospace;
            word-break: break-all;
            margin: 10px 0;
            position: relative;
        }

        .copy-btn {
            position: absolute;
            top: 10px;
            right: 10px;
            background: #9146ff;
            color: white;
            border: none;
            padding: 8px 12px;
            border-radius: 5px;
            cursor: pointer;
            font-size: 12px;
            transition: background 0.3s;
        }

        .copy-btn:hover {
            background: #7c3aed;
        }

        .warning {
            background: #fff3cd;
            border: 1px solid #ffeeba;
            border-radius: 8px;
            padding: 15px;
            margin: 15px 0;
            color: #856404;
        }

        .success {
            background: #d4edda;
            border: 1px solid #c3e6cb;
            border-radius: 8px;
            padding: 15px;
            margin: 15px 0;
            color: #155724;
        }

        .feature-list {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
            gap: 15px;
            margin: 20px 0;
        }

        .feature {
            background: #f8f9fa;
            padding: 15px;
            border-radius: 8px;
            border-left: 3px solid #9146ff;
        }

        .feature strong {
            color: #9146ff;
        }

        .preview-btn {
            background: #9146ff;
            color: white;
            padding: 12px 24px;
            border: none;
            border-radius: 8px;
            cursor: pointer;
            font-size: 16px;
            text-decoration: none;
            display: inline-block;
            margin: 10px 0;
            transition: background 0.3s;
        }

        .preview-btn:hover {
            background: #7c3aed;
            color: white;
        }

        .settings-link {
            background: #28a745;
            color: white;
            padding: 12px 24px;
            border-radius: 8px;
            text-decoration: none;
            display: inline-block;
            margin: 10px 10px 10px 0;
            transition: background 0.3s;
        }

        .settings-link:hover {
            background: #218838;
            color: white;
        }

        .emoji {
            font-size: 1.2em;
        }

        ol li, ul li {
            margin-bottom: 8px;
        }

        .troubleshooting {
            background: #f8f9fa;
            border-radius: 10px;
            padding: 20px;
            margin-top: 20px;
        }

        .troubleshooting h4 {
            color: #dc3545;
            margin-bottom: 15px;
        }

        @media (max-width: 768px) {
            .container {
                padding: 10px;
            }

            .header h1 {
                font-size: 2rem;
            }

            .card {
                padding: 20px;
            }
        }

        /* Audio-specific styling */
        .audio-info {
            background: rgba(76, 175, 80, 0.1);
            border: 1px solid rgba(76, 175, 80, 0.3);
            border-radius: 8px;
            padding: 12px;
            margin: 10px 0;
            font-size: 14px;
        }

        .audio-explanation {
            background: #f8f9fa;
            border-radius: 8px;
            padding: 20px;
            margin: 15px 0;
        }

        .audio-path {
            background: white;
            border-left: 4px solid #4CAF50;
            padding: 12px 16px;
            margin: 10px 0;
            border-radius: 0 8px 8px 0;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }

        .audio-control-btn {
            display: inline-block;
            background: linear-gradient(135deg, #4CAF50, #45a049);
            color: white;
            padding: 12px 24px;
            text-decoration: none;
            border-radius: 8px;
            font-weight: bold;
            margin: 15px 0;
            transition: all 0.3s ease;
            box-shadow: 0 4px 8px rgba(0,0,0,0.2);
        }

        .audio-control-btn:hover {
            transform: translateY(-2px);
            box-shadow: 0 6px 12px rgba(0,0,0,0.3);
        }

        .audio-features {
            background: #f8f9fa;
            border-radius: 8px;
            padding: 15px;
            margin: 15px 0;
        }

        .obs-audio-settings {
            background: rgba(33, 150, 243, 0.05);
            border: 1px solid rgba(33, 150, 243, 0.2);
            border-radius: 8px;
            padding: 20px;
            margin: 15px 0;
        }

        .audio-troubleshoot {
            background: rgba(255, 152, 0, 0.1);
            border: 1px solid rgba(255, 152, 0, 0.3);
            border-radius: 8px;
            padding: 15px;
            margin: 15px 0;
        }

        .audio-troubleshoot ol {
            margin: 10px 0;
            padding-left: 20px;
        }

        .audio-troubleshoot li {
            margin: 8px 0;
        }

        .sound-list {
            background: #f8f9fa;
            border-radius: 8px;
            padding: 15px;
            margin: 15px 0;
        }

        .sound-list ul {
            list-style: none;
            padding: 0;
        }

        .sound-list li {
            background: white;
            border-radius: 6px;
            padding: 10px 15px;
            margin: 8px 0;
            border-left: 4px solid #6c5ce7;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1><span class="emoji">🎮</span> OBS Overlay Setup Guide</h1>
            <p>Setup instructions for <strong>${user.displayName || user.username}</strong></p>
        </div>

        <div class="card">
            <h2><span class="emoji">📺</span> Your Overlay URL</h2>
            <p>Use this URL in OBS Studio to add your stream overlay:</p>
            <div class="url-container">
                <button class="copy-btn" onclick="copyUrl()">Copy</button>
                ${overlayUrl}
            </div>
            <div class="success">
                <strong><span class="emoji">✅</span> Ready to use!</strong> This URL is unique to your account and updates in real-time.
            </div>
            <a href="${overlayUrl}" target="_blank" class="preview-btn">
                <span class="emoji">👀</span> Preview Overlay
            </a>
            <a href="${baseUrl}/" class="settings-link">
                <span class="emoji">⚙️</span> Customize Settings
            </a>
        </div>

        <div class="card">
            <h2><span class="emoji">🚀</span> Quick Setup (5 Minutes)</h2>

            <div class="step">
                <span class="step-number">1</span>
                <strong>Open OBS Studio</strong><br>
                Launch OBS Studio on your computer. If you don't have it installed, download it from <a href="https://obsproject.com" target="_blank">obsproject.com</a>
            </div>

            <div class="step">
                <span class="step-number">2</span>
                <strong>Add Browser Source</strong><br>
                In your scene, click the <strong>+</strong> button in Sources → Select <strong>Browser Source</strong> → Create new or use existing
            </div>

            <div class="step">
                <span class="step-number">3</span>
                <strong>Configure Browser Source</strong><br>
                <ul>
                    <li><strong>URL:</strong> Paste your overlay URL from above</li>
                    <li><strong>Width:</strong> 1920 (or your stream resolution width)</li>
                    <li><strong>Height:</strong> 1080 (or your stream resolution height)</li>
                    <li><strong>✅ Shutdown source when not visible</strong></li>
                    <li><strong>✅ Refresh browser when scene becomes active</strong></li>
                </ul>
            </div>

            <div class="step">
                <span class="step-number">4</span>
                <strong>Configure Audio (Important!)</strong><br>
                <ul>
                    <li><strong>✅ Control audio via OBS</strong> - Enables audio capture</li>
                    <li><strong>Monitor and Output</strong> - Audio goes to stream (recommended)</li>
                    <li><strong>Don't mute</strong> the browser source in OBS Audio Mixer</li>
                </ul>
                <div class="audio-info">
                    <strong>🔊 Dual Audio System:</strong> Your overlay plays notification sounds both to your stream (captured by OBS) AND to your desktop speakers for immediate feedback!
                </div>
            </div>

            <div class="step">
                <span class="step-number">5</span>
                <strong>Position Your Overlay</strong><br>
                Click OK, then drag and resize the overlay in your OBS preview to position it where you want on your stream
            </div>

            <div class="step">
                <span class="step-number">6</span>
                <strong>Test Audio & Start Streaming!</strong><br>
                Visit the <a href="${baseUrl}/overlay/${userId}/audio-control" target="_blank"><strong>Audio Control Panel</strong></a> to test sounds and adjust volume. Your overlay will show live counter updates when you use chat commands like <strong>!death+</strong>
            </div>
        </div>

        <div class="card">
            <h2><span class="emoji">🎨</span> Overlay Features</h2>
            <p>Your overlay includes these awesome features:</p>

            <div class="feature-list">
                <div class="feature">
                    <strong><span class="emoji">💀</span> Death Counter</strong><br>
                    Tracks deaths with chat commands and manual updates
                </div>
                <div class="feature">
                    <strong><span class="emoji">🤬</span> Swear Counter</strong><br>
                    Counts profanity with customizable detection
                </div>
                <div class="feature">
                    <strong><span class="emoji">💎</span> Bits & Channel Points</strong><br>
                    Shows recent donations and redemptions
                </div>
                <div class="feature">
                    <strong><span class="emoji">🎯</span> Real-time Updates</strong><br>
                    Instant updates via WebSocket connection
                </div>
                <div class="feature">
                    <strong><span class="emoji">🎨</span> Customizable Themes</strong><br>
                    Change colors, position, animations, and more
                </div>
                <div class="feature">
                    <strong><span class="emoji">📱</span> Multi-Device Sync</span><br>
                    Update from dashboard, mobile, or chat commands
                </div>
            </div>
        </div>

        <div class="card">
            <h2><span class="emoji">�</span> Audio Setup & Control</h2>

            <h3>🎵 How Overlay Audio Works</h3>
            <p>Your overlay features an advanced <strong>dual audio system</strong>:</p>

            <div class="audio-explanation">
                <div class="audio-path">
                    <strong>Stream Audio:</strong> OBS Browser Source automatically captures notification sounds for your viewers
                </div>
                <div class="audio-path">
                    <strong>Desktop Audio:</strong> Same sounds play on your speakers simultaneously for immediate feedback
                </div>
            </div>

            <h3>🎚️ Audio Control Panel</h3>
            <p>Fine-tune your audio experience:</p>
            <a href="${baseUrl}/overlay/${userId}/audio-control" target="_blank" class="audio-control-btn">
                <span class="emoji">🎛️</span> Open Audio Control Panel
            </a>

            <div class="audio-features">
                <ul>
                    <li><strong>Master Volume:</strong> Control overall notification volume (0-100%)</li>
                    <li><strong>Desktop Audio Toggle:</strong> Enable/disable sounds on your speakers</li>
                    <li><strong>Desktop Volume:</strong> Independent volume for your speakers (% of stream volume)</li>
                    <li><strong>Sound Testing:</strong> Test all notification types instantly</li>
                    <li><strong>Real-time Control:</strong> Changes apply immediately to your overlay</li>
                </ul>
            </div>

            <h3>🎮 OBS Browser Source Audio Setup</h3>
            <div class="obs-audio-settings">
                <p><strong>🚨 REQUIRED for notification sounds in stream:</strong></p>
                <ol>
                    <li><strong>✅ Enable "Control audio via OBS"</strong> in Browser Source properties</li>
                    <li><strong>✅ Unmute browser source</strong> in OBS Audio Mixer</li>
                    <li><strong>✅ Set to "Monitor and Output"</strong> in Advanced Audio Properties</li>
                    <li><strong>✅ Restart browser source</strong> after changes</li>
                </ol>

                <div class="audio-troubleshoot">
                    <p><strong>🔧 Troubleshooting - No Audio in Stream:</strong></p>
                    <ol>
                        <li><strong>Browser Source Properties:</strong> Right-click → Properties → ✅ Check "Control audio via OBS"</li>
                        <li><strong>Audio Mixer:</strong> Browser source should show as "Browser Source" (not muted 🔇)</li>
                        <li><strong>Advanced Audio:</strong> Click gear ⚙️ → Advanced Audio Properties → Set "Audio Monitoring" to "Monitor and Output"</li>
                        <li><strong>Refresh Source:</strong> Right-click browser source → Refresh</li>
                        <li><strong>Test Audio:</strong> Use Audio Control Panel below to verify overlay is playing sounds</li>
                        <li><strong>Browser Issues:</strong> Try refreshing the browser source or restarting OBS</li>
                    </ol>

                    <p><strong>⚠️ Common Issue:</strong> If you don't see your overlay browser source in the Audio Mixer, the "Control audio via OBS" setting is not enabled.</p>
                </div>
            </div>

            <h3>🎯 Notification Sounds</h3>
            <p>Your overlay includes these notification sounds:</p>
            <div class="sound-list">
                <ul>
                    <li><strong>🫶 Follow:</strong> Heart Monitor (heartMonitor.wav)</li>
                    <li><strong>⭐ Subscription:</strong> Hype Train (hypeTrain.wav)</li>
                    <li><strong>🔄 Resub:</strong> Typewriter (typewriter.wav)</li>
                    <li><strong>🎁 Gift Sub:</strong> Pill Rattle (pillRattle.mp3)</li>
                    <li><strong>💎 Bits:</strong> Electroshock (electroshock.wav)</li>
                    <li><strong>🏆 Milestone:</strong> Alarm (alarm.wav)</li>
                </ul>
            </div>
        </div>

        <div class="card">
            <h2><span class="emoji">�💬</span> Chat Commands</h2>
            <h3>Viewer Commands (Everyone can use):</h3>
            <ul>
                <li><strong>!deaths</strong> - Show current death count</li>
                <li><strong>!swears</strong> - Show current swear count</li>
                <li><strong>!stats</strong> - Show all counters</li>
            </ul>

            <h3>Moderator Commands (Streamer & Mods only):</h3>
            <ul>
                <li><strong>!death+</strong> or <strong>!d+</strong> - Add a death</li>
                <li><strong>!death-</strong> or <strong>!d-</strong> - Remove a death</li>
                <li><strong>!swear+</strong> or <strong>!s+</strong> - Add a swear</li>
                <li><strong>!swear-</strong> or <strong>!s-</strong> - Remove a swear</li>
                <li><strong>!resetcounters</strong> - Reset all counters to 0</li>
            </ul>

            <div class="warning">
                <strong><span class="emoji">⚠️</span> Note:</strong> Chat commands require you to have the <strong>chatCommands</strong> feature enabled in your settings.
            </div>
        </div>

        <div class="card">
            <h2><span class="emoji">⚙️</span> Customization Options</h2>
            <p>Personalize your overlay through the settings dashboard:</p>

            <ul>
                <li><strong>Position:</strong> Top-left, top-right, bottom-left, bottom-right</li>
                <li><strong>Theme:</strong> Custom colors for text, background, and borders</li>
                <li><strong>Animations:</strong> Enable/disable smooth transitions and effects</li>
                <li><strong>Counters:</strong> Show/hide specific counters (deaths, swears, bits)</li>
                <li><strong>Size & Opacity:</strong> Adjust overlay visibility</li>
                <li><strong>Update Frequency:</strong> Real-time or batched updates</li>
            </ul>

            <a href="${baseUrl}/" class="settings-link">
                <span class="emoji">🎛️</span> Open Settings Dashboard
            </a>
        </div>

        <div class="card">
            <h2><span class="emoji">🔧</span> Advanced Setup</h2>

            <h3>Multiple Scenes:</h3>
            <p>You can add the same overlay to multiple OBS scenes. It will automatically show/hide based on your settings.</p>

            <h3>Mobile Dashboard:</h3>
            <p>Access <strong>${baseUrl}/</strong> on your phone to update counters during your stream without alt-tabbing.</p>

            <h3>Hotkeys:</h3>
            <p>Set up OBS hotkeys to show/hide the overlay source during specific moments in your stream.</p>

            <div class="troubleshooting">
                <h4><span class="emoji">🩺</span> Troubleshooting</h4>
                <ul>
                    <li><strong>Overlay not showing?</strong> Check that the URL is correct and your internet connection is stable</li>
                    <li><strong>No audio in stream?</strong> Ensure "Control audio via OBS" is checked and browser source isn't muted in OBS mixer</li>
                    <li><strong>Sounds not playing on desktop?</strong> Visit the <a href="${baseUrl}/overlay/${userId}/audio-control" target="_blank">Audio Control Panel</a> to test and enable desktop audio</li>
                    <li><strong>Audio too quiet/loud?</strong> Use Audio Control Panel to adjust master volume and desktop volume separately</li>
                    <li><strong>Counters not updating?</strong> Verify that chat commands are enabled in your feature settings</li>
                    <li><strong>Overlay looks wrong?</strong> Try refreshing the browser source in OBS (right-click → Refresh)</li>
                    <li><strong>Performance issues?</strong> Reduce animation effects or update frequency in settings</li>
                    <li><strong>Still having problems?</strong> Check the <a href="${baseUrl}/api/health" target="_blank">API health status</a></li>
                </ul>
            </div>
        </div>

        <div class="card">
            <h2><span class="emoji">🎯</span> Pro Tips</h2>
            <ul>
                <li><strong>Test Audio First:</strong> Use the Audio Control Panel to test all sounds before going live</li>
                <li><strong>Set Desktop Volume:</strong> Keep desktop audio at 80% or lower to avoid feedback</li>
                <li><strong>Check OBS Mixer:</strong> Ensure browser source shows audio activity when notifications play</li>
                <li><strong>Test First:</strong> Preview your overlay before going live to ensure everything looks right</li>
                <li><strong>Backup Scenes:</strong> Create multiple OBS scenes with different overlay configurations</li>
                <li><strong>Monitor Performance:</strong> Keep an eye on OBS CPU usage - disable animations if needed</li>
                <li><strong>Engage Viewers:</strong> Encourage chat to use viewer commands to check your stats</li>
                <li><strong>Audio Balance:</strong> Adjust notification volume so it complements, not overpowers, your game audio</li>
                <li><strong>Regular Updates:</strong> Check for new features in the dashboard settings periodically</li>
            </ul>
        </div>

        <div class="success">
            <strong><span class="emoji">🎉</span> You're all set!</strong> Your overlay is ready to make your stream more engaging.
            Happy streaming, ${user.displayName || user.username}!
        </div>
    </div>

    <script>
        function copyUrl() {
            const urlText = '${overlayUrl}';
            navigator.clipboard.writeText(urlText).then(() => {
                const btn = document.querySelector('.copy-btn');
                const originalText = btn.textContent;
                btn.textContent = 'Copied!';
                btn.style.background = '#28a745';
                setTimeout(() => {
                    btn.textContent = originalText;
                    btn.style.background = '#9146ff';
                }, 2000);
            });
        }
    </script>
</body>
</html>`;

    res.send(html);
  } catch (error) {
    console.error('Error serving overlay setup instructions:', error);
    res.status(500).send('Error loading setup instructions');
  }
});

/**
 * Audio Control Panel for testing dual audio setup
 * GET /overlay/:userId/audio-control
 */
router.get('/:userId/audio-control', async (req, res) => {
  try {
    const user = await database.getUser(req.params.userId);

    if (!user) {
      return res.status(404).send('User not found');
    }

    const baseUrl = process.env.NODE_ENV === 'production'
      ? 'https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io'
      : `http://localhost:${process.env.PORT || 3000}`;

    const overlayUrl = `${baseUrl}/overlay/${req.params.userId}`;

    const html = `
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Audio Control Panel - ${user.displayName || user.username}</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: 'Segoe UI', sans-serif;
            background: #1a1a1a;
            color: #fff;
            padding: 20px;
        }
        .container { max-width: 800px; margin: 0 auto; }
        .header { text-align: center; margin-bottom: 30px; }
        .card {
            background: #2a2a2a;
            border-radius: 12px;
            padding: 20px;
            margin-bottom: 20px;
            border: 1px solid #333;
        }
        .control-group {
            margin-bottom: 20px;
            padding: 15px;
            background: #333;
            border-radius: 8px;
        }
        .control-group h3 { margin-bottom: 10px; color: #4CAF50; }
        .slider-container {
            display: flex;
            align-items: center;
            gap: 15px;
            margin: 10px 0;
        }
        .slider {
            flex: 1;
            height: 6px;
            border-radius: 3px;
            background: #555;
            outline: none;
            -webkit-appearance: none;
        }
        .slider::-webkit-slider-thumb {
            appearance: none;
            width: 20px;
            height: 20px;
            border-radius: 50%;
            background: #4CAF50;
            cursor: pointer;
        }
        .btn {
            padding: 10px 20px;
            background: #4CAF50;
            color: white;
            border: none;
            border-radius: 6px;
            cursor: pointer;
            margin: 5px;
        }
        .btn:hover { background: #45a049; }
        .btn.test { background: #2196F3; }
        .btn.test:hover { background: #1976D2; }
        .toggle {
            display: inline-block;
            width: 60px;
            height: 30px;
            background: #555;
            border-radius: 15px;
            position: relative;
            cursor: pointer;
        }
        .toggle.active { background: #4CAF50; }
        .toggle:after {
            content: '';
            position: absolute;
            width: 26px;
            height: 26px;
            border-radius: 50%;
            background: white;
            top: 2px;
            left: 2px;
            transition: 0.3s;
        }
        .toggle.active:after { left: 32px; }
        .status {
            background: #1a1a1a;
            border-radius: 6px;
            padding: 10px;
            font-family: monospace;
            font-size: 12px;
            max-height: 200px;
            overflow-y: auto;
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🔊 Audio Control Panel</h1>
            <p>Test and configure dual audio system for <strong>${user.displayName || user.username}</strong></p>
            <p><small>Overlay URL: ${overlayUrl}</small></p>
        </div>

        <div class="card">
            <h2>🎚️ Audio Settings</h2>

            <div class="control-group">
                <h3>Master Volume</h3>
                <div class="slider-container">
                    <span>0%</span>
                    <input type="range" class="slider" id="masterVolume" min="0" max="100" value="70">
                    <span>100%</span>
                    <span id="masterVolumeValue">70%</span>
                </div>
            </div>

            <div class="control-group">
                <h3>Desktop Audio</h3>
                <div class="slider-container">
                    <span>Enable Desktop Audio:</span>
                    <div class="toggle active" id="desktopAudioToggle"></div>
                    <span id="desktopAudioStatus">Enabled</span>
                </div>
                <div class="slider-container">
                    <span>Desktop Volume (% of stream):</span>
                    <input type="range" class="slider" id="desktopVolumeMultiplier" min="0" max="200" value="80">
                    <span id="desktopVolumeValue">80%</span>
                </div>
            </div>
        </div>

        <div class="card">
            <h2>🧪 Test Audio</h2>
            <p>Test different notification sounds:</p>
            <div style="margin: 15px 0;">
                <button class="btn test" onclick="testSound('follow')">🫶 Follow</button>
                <button class="btn test" onclick="testSound('subscription')">⭐ Subscription</button>
                <button class="btn test" onclick="testSound('resub')">🔄 Resub</button>
                <button class="btn test" onclick="testSound('giftsub')">🎁 Gift Sub</button>
                <button class="btn test" onclick="testSound('bits')">💎 Bits</button>
                <button class="btn test" onclick="testSound('milestone')">🏆 Milestone</button>
            </div>
        </div>

        <div class="card">
            <h2>📊 Status Log</h2>
            <div class="status" id="statusLog">
                Audio control panel ready...<br>
            </div>
            <button class="btn" onclick="clearLog()">Clear Log</button>
        </div>

        <div class="card">
            <h2>ℹ️ Instructions</h2>
            <p>1. <strong>Open your overlay in OBS</strong> as a Browser Source</p>
            <p>2. <strong>Adjust settings above</strong> and test different sounds</p>
            <p>3. <strong>Stream Audio:</strong> Captured by OBS Browser Source</p>
            <p>4. <strong>Desktop Audio:</strong> Plays on your speakers for immediate feedback</p>
            <p>5. <strong>Changes apply instantly</strong> to your overlay</p>
        </div>
    </div>

    <script>
        const overlayUrl = '${overlayUrl}';
        let overlayWindow = null;

        // Get overlay window reference
        function getOverlayWindow() {
            if (!overlayWindow || overlayWindow.closed) {
                overlayWindow = window.open(overlayUrl, 'overlay', 'width=400,height=600');
            }
            return overlayWindow;
        }

        // Send message to overlay
        function sendToOverlay(type, payload) {
            const overlay = getOverlayWindow();
            if (overlay) {
                overlay.postMessage({ type, payload }, window.location.origin);
                log('📤 Sent to overlay: ' + type, payload);
            } else {
                log('❌ Could not communicate with overlay window');
            }
        }

        // Log function
        function log(message, data = null) {
            const statusLog = document.getElementById('statusLog');
            const timestamp = new Date().toLocaleTimeString();
            const logEntry = timestamp + ' - ' + message + (data ? ' ' + JSON.stringify(data) : '') + '<br>';
            statusLog.innerHTML += logEntry;
            statusLog.scrollTop = statusLog.scrollHeight;
        }

        // Clear log
        function clearLog() {
            document.getElementById('statusLog').innerHTML = 'Log cleared...<br>';
        }

        // Test sound
        function testSound(eventType) {
            sendToOverlay('TEST_NOTIFICATION_AUDIO', { eventType });
            log('🧪 Testing sound: ' + eventType);
        }

        // Master volume control
        document.getElementById('masterVolume').addEventListener('input', (e) => {
            const volume = e.target.value / 100;
            document.getElementById('masterVolumeValue').textContent = e.target.value + '%';
            sendToOverlay('SET_AUDIO_VOLUME', { volume });
        });

        // Desktop audio toggle
        document.getElementById('desktopAudioToggle').addEventListener('click', (e) => {
            const toggle = e.target;
            const enabled = !toggle.classList.contains('active');

            if (enabled) {
                toggle.classList.add('active');
                document.getElementById('desktopAudioStatus').textContent = 'Enabled';
            } else {
                toggle.classList.remove('active');
                document.getElementById('desktopAudioStatus').textContent = 'Disabled';
            }

            sendToOverlay('SET_DESKTOP_AUDIO', { enabled });
        });

        // Desktop volume multiplier
        document.getElementById('desktopVolumeMultiplier').addEventListener('input', (e) => {
            const multiplier = e.target.value / 100;
            document.getElementById('desktopVolumeValue').textContent = e.target.value + '%';
            sendToOverlay('SET_DESKTOP_VOLUME_MULTIPLIER', { multiplier });
        });

        // Initialize
        log('🚀 Audio Control Panel initialized');
        log('📖 Click "Test Audio" buttons to test different sounds');
        log('🔊 Adjust settings and test with your OBS overlay');
    </script>
</body>
</html>`;

    res.send(html);
  } catch (error) {
    console.error('Error serving audio control panel:', error);
    res.status(500).send('Error loading audio control panel');
  }
});

module.exports = router;

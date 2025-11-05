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

    // Check stream status
    const streamStatus = user.streamStatus || 'offline';

    // Show different messages based on stream status
    if (streamStatus === 'offline') {
      return res.status(200).send(`
        <!DOCTYPE html>
        <html><head><title>Stream Offline</title></head>
        <body style="background: transparent; color: white; font-family: Arial; padding: 20px;">
          <div style="background: rgba(0,0,0,0.7); padding: 20px; border-radius: 10px; border: 2px solid #d4af37; text-align: center;">
            <h3 style="color: #d4af37; margin-top: 0;">🎬 Stream Offline</h3>
            <p>Overlay will activate when you start prepping.</p>
            <p><small>Use "Start Prepping" button in admin dashboard</small></p>
          </div>
        </body></html>
      `);
    } else if (streamStatus === 'prepping') {
      return res.status(200).send(`
        <!DOCTYPE html>
        <html><head><title>Getting Ready</title></head>
        <body style="background: transparent; color: white; font-family: Arial; padding: 20px;">
          <div style="background: rgba(0,0,0,0.7); padding: 20px; border-radius: 10px; border: 2px solid #ffa500; text-align: center;">
            <h3 style="color: #ffa500; margin-top: 0;">🎭 Getting Ready</h3>
            <p>Stream prep in progress...</p>
            <p><small>Overlay will show counters when you go live</small></p>
          </div>
        </body></html>
      `);
    } else if (streamStatus === 'ending') {
      return res.status(200).send(`
        <!DOCTYPE html>
        <html><head><title>Stream Ending</title></head>
        <body style="background: transparent; color: white; font-family: Arial; padding: 20px;">
          <div style="background: rgba(0,0,0,0.7); padding: 20px; border-radius: 10px; border: 2px solid #ff6b6b; text-align: center;">
            <h3 style="color: #ff6b6b; margin-top: 0;">🏁 Stream Ending</h3>
            <p>Thanks for watching!</p>
            <p><small>Stream concluded</small></p>
          </div>
        </body></html>
      `);
    }

    // If status is 'live', show the full overlay with counters

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
    </style>
</head>
<body>
    <div class="counter-overlay">
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

        ${overlaySettings.counters.bits ? `
        <div class="counter-item" id="bits-counter">
            <div class="counter-icon">💎</div>
            <div class="counter-info">
                <div class="counter-label">Stream Bits</div>
                <div class="counter-value" id="bits-value">${counters.bits || 0}</div>
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
    <script>
        const userId = '${req.params.userId}';
        const overlaySettings = ${JSON.stringify(overlaySettings)};
        const socket = io();

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
            processedText = processedText.replace(/\\[X\\]/g, data.viewers || data.amount || '');

            // Create alert element
            const alertElement = document.createElement('div');
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
                animation: asylumPulse \${alertConfig.duration || 4000}ms ease-in-out;
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

            // Show alert
            alertContainer.appendChild(alertElement);
            alertContainer.style.opacity = '1';

            // Remove alert after duration
            setTimeout(() => {
                alertContainer.style.opacity = '0';
                setTimeout(() => {
                    if (alertContainer.contains(alertElement)) {
                        alertContainer.removeChild(alertElement);
                    }
                }, 500);
            }, alertConfig.duration || 4000);

            // Play sound if configured (placeholder for future implementation)
            if (alertConfig.sound) {
                console.log(\`🔊 Playing sound: \${alertConfig.sound}\`);
                // TODO: Implement sound playback
            }
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

            // Backend sends counters directly as {deaths: X, swears: Y, bits: Z}
            if (overlaySettings.counters.deaths) {
                updateCounter('deaths', data.deaths || 0);
            }
            if (overlaySettings.counters.swears) {
                updateCounter('swears', data.swears || 0);
            }
            if (overlaySettings.counters.bits) {
                updateCounter('bits', data.bits || 0);
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

        // Listen for subscriber events
        socket.on('newSubscriber', (data) => {
            if (data.userId === userId) {
                celebrateSubscriber(data.username, 'sub');
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
                showCustomAlert('follow', data);
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

        // Keep connection alive
        setInterval(() => {
            socket.emit('ping');
        }, 30000);
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
                <strong>Position Your Overlay</strong><br>
                Click OK, then drag and resize the overlay in your OBS preview to position it where you want on your stream
            </div>

            <div class="step">
                <span class="step-number">5</span>
                <strong>Start Streaming!</strong><br>
                Your overlay will now show live counter updates. Test by using chat commands like <strong>!death+</strong> or updating counters in your dashboard
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
            <h2><span class="emoji">💬</span> Chat Commands</h2>
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
                <li><strong>Test First:</strong> Preview your overlay before going live to ensure everything looks right</li>
                <li><strong>Backup Scenes:</strong> Create multiple OBS scenes with different overlay configurations</li>
                <li><strong>Monitor Performance:</strong> Keep an eye on OBS CPU usage - disable animations if needed</li>
                <li><strong>Engage Viewers:</strong> Encourage chat to use viewer commands to check your stats</li>
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

module.exports = router;

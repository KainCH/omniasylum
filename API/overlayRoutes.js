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

    // Get user's overlay settings
    const overlaySettings = await database.getUserOverlaySettings(req.params.userId);

    // If overlay is disabled in settings, show disabled message
    if (!overlaySettings.enabled) {
      return res.status(200).send(`
        <!DOCTYPE html>
        <html><head><title>Overlay Disabled</title></head>
        <body style="background: transparent; color: white; font-family: Arial; padding: 20px;">
          <div style="background: rgba(0,0,0,0.7); padding: 20px; border-radius: 10px; border: 2px solid #d4af37;">
            Overlay is currently disabled. Enable it in your settings.
          </div>
        </body></html>
      `);
    }

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

        // Listen for counter updates
        socket.on('counterUpdate', (data) => {
            if (data.userId === userId) {
                if (overlaySettings.counters.deaths) {
                    updateCounter('deaths', data.counters.deaths, data.change?.deaths);
                }
                if (overlaySettings.counters.swears) {
                    updateCounter('swears', data.counters.swears, data.change?.swears);
                }
                if (overlaySettings.counters.bits) {
                    updateCounter('bits', data.counters.bits, data.change?.bits);
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

module.exports = router;

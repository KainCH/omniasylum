window.overlayInterop = {
    init: function() {
        // If OBS scene switching causes the browser source to be hidden/throttled,
        // WebSocket messages and timers can effectively "catch up" when visible again.
        // That can replay old alert audio. We add a short suppression window on resume.
        const getResumeSilenceMs = () => {
            const configured = Number(window.omniResumeSilenceMs);
            return Number.isFinite(configured) && configured >= 0 ? configured : 1500;
        };

        const bumpResumeSilence = (reason) => {
            const ms = getResumeSilenceMs();
            const until = Date.now() + ms;

            window.omniOverlayResumeSuppressUntil = until;

            // Also extend the general silence window so all audio systems agree.
            if (!window.omniSilenceUntil || window.omniSilenceUntil < until) {
                window.omniSilenceUntil = until;
            }

            console.log(`🔇 Resume suppression active for ${ms}ms (${reason})`);
        };

        if (!window.__omniOverlayVisibilityHandlerRegistered) {
            window.__omniOverlayVisibilityHandlerRegistered = true;

            document.addEventListener('visibilitychange', () => {
                if (document.visibilityState === 'visible') {
                    bumpResumeSilence('visibilitychange');
                }
            });
        }

        if (window.asylumEffects) {
            console.log("AsylumEffects initialized via Interop");
        } else {
            console.error("AsylumEffects not found!");
        }

        // Initialize audio if not already done
        if (!window.notificationAudio) {
            console.warn("NotificationAudio not found! Audio will not play.");
        }

        // Silence alerts briefly on initial load and on resume.
        // overlay.html sets an initial silence window, but other entry points may not.
        const initialMs = Number(window.omniSilenceInitialMs) || 3000;
        const initialUntil = Date.now() + initialMs;
        if (!window.omniSilenceUntil || window.omniSilenceUntil < initialUntil) {
            window.omniSilenceUntil = initialUntil;
        }

        bumpResumeSilence('init');
    },

    triggerAlert: function(type, payload) {
        // If the page is not visible, ignore alerts (prevents backlog replay on resume).
        if (document.visibilityState !== 'visible') {
            console.log("🔇 Alert suppressed because overlay is not visible:", type);
            return;
        }

        // Suppress alerts briefly after resume to avoid playing buffered messages.
        if (window.omniOverlayResumeSuppressUntil && Date.now() < window.omniOverlayResumeSuppressUntil) {
            console.log("🔇 Alert suppressed during resume window:", type);
            return;
        }

        // Skip playing alerts if we're still in the initial silence window
        if (window.omniSilenceUntil && Date.now() < window.omniSilenceUntil) {
            console.log("🔇 Alert suppressed during initial silence window:", type);
            return;
        }
        console.log("Triggering alert:", type, payload);

        // Interaction banners are UI-only and should not require an alert definition.
        if (type === 'interactionBanner') {
            const text = payload?.textPrompt || payload?.text || payload?.message;
            if (text) {
                this.showInteractionBanner(text, payload?.duration || 5000);
            }
            return;
        }

        // Play audio if available
        if (window.notificationAudio) {
            // Map alert types to audio types if needed
            const audioType = type === 'giftsub' ? 'giftsub' :
                              type === 'resub' ? 'resub' :
                              type === 'subscription' ? 'subscription' :
                              type === 'bits' ? 'bits' :
                              type === 'follow' ? 'follow' :
                              type === 'milestone' ? 'milestone' :
                              type === 'paypal_donation' ? 'donation' :
                              type === 'paypalDonation' ? 'donation' : type;

            window.notificationAudio.playNotification(audioType, payload);
        }

        // Handle specific types that need custom logic
        if (type === 'bits') {
            this.triggerBitsCelebration(payload.amount || payload.bits || 50);
        } else if (type === 'subscription' || type === 'resub' || type === 'giftsub') {
            this.triggerSubCelebration();
            if (payload.textPrompt) {
                this.showSubBanner(payload.textPrompt);
            }
        } else if (type === 'paypal_donation' || type === 'paypalDonation') {
            // PayPal donation celebration
            this.triggerDonationCelebration(payload.amount || 10);
            if (payload.textPrompt) {
                this.showDonationBanner(payload.textPrompt);
            }
        } else if (type === 'milestone') {
             // Milestone specific logic if any
        }

        // Use AsylumEffects for the main alert popup
        if (window.asylumEffects) {
            window.asylumEffects.triggerEffect({
                textPrompt: payload.textPrompt || payload.name,
                backgroundColor: payload.backgroundColor,
                textColor: payload.textColor,
                borderColor: payload.borderColor,
                duration: payload.duration || 5000,
                soundTrigger: payload.sound, // AsylumEffects might handle its own sound too
                effects: payload.effects || {}
            });
        }
    },

    triggerBitsCelebration: function(amount) {
        const container = document.getElementById('bits-celebration');
        if (!container) return;

        const particleCount = Math.min(amount || 50, 50);

        for (let i = 0; i < particleCount; i++) {
            const particle = document.createElement('div');
            particle.className = 'bit-particle';
            particle.style.left = Math.random() * 100 + 'vw';
            particle.style.animationDuration = (Math.random() * 2 + 2) + 's';
            particle.style.opacity = Math.random();
            container.appendChild(particle);

            setTimeout(() => {
                particle.remove();
            }, 4000);
        }
    },

    triggerSubCelebration: function() {
        const container = document.getElementById('sub-celebration');
        if (!container) return;

        for (let i = 0; i < 20; i++) {
            const heart = document.createElement('div');
            heart.className = 'sub-heart';
            heart.innerHTML = '💜';
            heart.style.left = Math.random() * 100 + 'vw';
            heart.style.animationDelay = Math.random() * 2 + 's';
            container.appendChild(heart);

            setTimeout(() => {
                heart.remove();
            }, 5000);
        }
    },

    showSubBanner: function(text) {
        const banner = document.getElementById('sub-banner');
        if (!banner) return;

        banner.textContent = text;
        banner.classList.remove('hide');
        banner.classList.add('show');

        setTimeout(() => {
            banner.classList.remove('show');
            banner.classList.add('hide');
        }, 5000);
    },

    triggerDonationCelebration: function(amount) {
        // Create or get donation celebration container
        let container = document.getElementById('donation-celebration');
        if (!container) {
            container = document.createElement('div');
            container.id = 'donation-celebration';
            container.className = 'donation-celebration';
            document.body.appendChild(container);
        }

        // Scale particle count based on donation amount (5-50 particles)
        const particleCount = Math.min(Math.max(Math.floor(amount / 2), 5), 50);

        // Money symbols to rain down
        const moneySymbols = ['💵', '💰', '💸', '🤑', '💲'];

        for (let i = 0; i < particleCount; i++) {
            const particle = document.createElement('div');
            particle.className = 'money-particle';
            particle.textContent = moneySymbols[Math.floor(Math.random() * moneySymbols.length)];
            particle.style.left = Math.random() * 100 + 'vw';
            particle.style.animationDuration = (Math.random() * 2 + 2) + 's';
            particle.style.animationDelay = (Math.random() * 1) + 's';
            particle.style.fontSize = (Math.random() * 1.5 + 1) + 'rem';
            container.appendChild(particle);

            setTimeout(() => {
                particle.remove();
            }, 5000);
        }

        // Add keyframes if not exists
        if (!document.getElementById('money-rain-keyframes')) {
            const style = document.createElement('style');
            style.id = 'money-rain-keyframes';
            style.textContent = `
                .donation-celebration {
                    position: fixed;
                    top: 0;
                    left: 0;
                    width: 100vw;
                    height: 100vh;
                    pointer-events: none;
                    z-index: 1000;
                    overflow: hidden;
                }
                .money-particle {
                    position: absolute;
                    top: -50px;
                    animation: moneyRain linear forwards;
                    filter: drop-shadow(0 0 5px rgba(0, 255, 0, 0.5));
                }
                @keyframes moneyRain {
                    0% {
                        transform: translateY(0) rotate(0deg) scale(1);
                        opacity: 1;
                    }
                    50% {
                        transform: translateY(50vh) rotate(180deg) scale(1.2);
                        opacity: 1;
                    }
                    100% {
                        transform: translateY(110vh) rotate(360deg) scale(0.8);
                        opacity: 0;
                    }
                }
            `;
            document.head.appendChild(style);
        }
    },

    showDonationBanner: function(text) {
        // Use the interaction banner for donation messages
        const banner = document.getElementById('interaction-banner');
        if (!banner) return;

        banner.textContent = text;
        banner.style.background = 'linear-gradient(135deg, #1a472a 0%, #2d5a3f 100%)';
        banner.style.borderColor = '#4CAF50';
        banner.classList.remove('hide');
        banner.classList.add('show');

        if (banner.__omniDonationTimer) {
            clearTimeout(banner.__omniDonationTimer);
        }

        banner.__omniDonationTimer = setTimeout(() => {
            banner.classList.remove('show');
            banner.classList.add('hide');
            // Reset banner style
            banner.style.background = '';
            banner.style.borderColor = '';
        }, 6000);
    },

    showInteractionBanner: function(text, durationMs) {
        const banner = document.getElementById('interaction-banner');
        if (!banner) return;

        const safeDuration = Number(durationMs);
        const duration = Number.isFinite(safeDuration) ? Math.max(500, safeDuration) : 5000;

        banner.textContent = text;
        banner.classList.remove('hide');
        banner.classList.add('show');

        if (banner.__omniInteractionTimer) {
            clearTimeout(banner.__omniInteractionTimer);
        }

        banner.__omniInteractionTimer = setTimeout(() => {
            banner.classList.remove('show');
            banner.classList.add('hide');
        }, duration);
    },

    triggerBitsGoalCelebration: function() {
        const alert = document.getElementById('alert-popup');
        if (alert) {
            alert.innerHTML = '🎯 BITS GOAL REACHED! 💎';
            alert.classList.add('show');

            // Add sparkle effect
            this.createSparkles();

            setTimeout(() => {
                alert.classList.add('hide');
                setTimeout(() => {
                    alert.classList.remove('show', 'hide');
                }, 500);
            }, 5000);
        }
    },

    createSparkles: function() {
        const sparkleContainer = document.createElement('div');
        sparkleContainer.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            width: 100vw;
            height: 100vh;
            pointer-events: none;
            z-index: 999;
        `;
        document.body.appendChild(sparkleContainer);

        for (let i = 0; i < 20; i++) {
            const sparkle = document.createElement('div');
            sparkle.textContent = '✨';
            sparkle.style.cssText = `
                position: absolute;
                font-size: ${Math.random() * 2 + 1}rem;
                left: ${Math.random() * 100}vw;
                top: ${Math.random() * 100}vh;
                animation: sparkleFloat ${Math.random() * 3 + 2}s ease-out forwards;
            `;
            sparkleContainer.appendChild(sparkle);
        }

        // Add keyframes if not exists
        if (!document.getElementById('sparkle-keyframes')) {
            const style = document.createElement('style');
            style.id = 'sparkle-keyframes';
            style.textContent = `
                @keyframes sparkleFloat {
                    0% { transform: translateY(0) rotate(0deg) scale(0); opacity: 1; }
                    50% { transform: translateY(-50px) rotate(180deg) scale(1.2); opacity: 1; }
                    100% { transform: translateY(-100px) rotate(360deg) scale(0); opacity: 0; }
                }
            `;
            document.head.appendChild(style);
        }

        setTimeout(() => {
            document.body.removeChild(sparkleContainer);
        }, 5000);
    },

    updateOverlaySettings: function(settings) {
        console.log('Updating overlay settings:', settings);

        // Update size styles
        if (settings.size) {
            const sizes = {
                small: { padding: '10px 20px', minWidth: '180px', iconFontSize: '1.5rem', fontSize: '14px', counterFontSize: '1.5rem', iconWidth: '30px' },
                medium: { padding: '15px 25px', minWidth: '200px', iconFontSize: '2rem', fontSize: '16px', counterFontSize: '2rem', iconWidth: '40px' },
                large: { padding: '20px 30px', minWidth: '250px', iconFontSize: '2.5rem', fontSize: '20px', counterFontSize: '2.5rem', iconWidth: '50px' }
            };
            const s = sizes[settings.size] || sizes.medium;

            let style = document.getElementById('overlay-size-style');
            if (!style) {
                style = document.createElement('style');
                style.id = 'overlay-size-style';
                document.head.appendChild(style);
            }

            style.textContent = `
                .counter-item { padding: ${s.padding} !important; min-width: ${s.minWidth} !important; }
                .counter-icon { font-size: ${s.iconFontSize} !important; width: ${s.iconWidth} !important; }
                .counter-label { font-size: ${s.fontSize} !important; }
                .counter-value { font-size: ${s.counterFontSize} !important; }
            `;
        }
    },

    triggerDeath: function(newCount) {
        if (window.asylumEffects) {
            window.asylumEffects.triggerEffect({
                textPrompt: `DEATH COUNT: ${newCount}`,
                backgroundColor: '#3e1a1a',
                textColor: '#ff0000',
                duration: 5000,
                effects: {
                    animation: 'sirenFlash',
                    particle: 'chaos',
                    redAlert: true,
                    soundTrigger: 'alarm.wav'
                }
            });
        }
    },
    triggerSwear: function(newCount) {
        if (window.asylumEffects) {
            window.asylumEffects.triggerEffect({
                textPrompt: `SWEAR JAR: ${newCount}`,
                backgroundColor: '#1a1a3e',
                textColor: '#00ffff',
                duration: 4000,
                effects: {
                    animation: 'electricPulse',
                    svgMask: 'glassDistortion',
                    particle: 'sparks',
                    screenFlicker: true,
                    soundTrigger: 'electroshock.wav'
                }
            });
        }
    }
};

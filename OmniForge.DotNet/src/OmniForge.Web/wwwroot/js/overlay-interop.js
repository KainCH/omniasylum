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
                              type === 'milestone' ? 'milestone' : type;

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
        } else if (type === 'milestone') {
             // Milestone specific logic if any
        }

        // Show the main alert popup with visual cue and text
        this.showAlertPopup(payload);

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

    showAlertPopup: function(payload) {
        const popup = document.getElementById('alert-popup');
        const title = document.getElementById('alert-title');
        const message = document.getElementById('alert-message');
        const image = document.getElementById('alert-image');

        if (!popup || !title || !message) return;

        // Update text content
        title.textContent = payload.name || 'ALERT'; // Or use type?
        message.textContent = payload.textPrompt || payload.message || '';

        // Update visual cue (image)
        if (image) {
            // Validate visualCue is a likely URL/path and not a description
            const visualCue = payload.visualCue;
            const isValidImage = visualCue &&
                               (visualCue.startsWith('http') ||
                                visualCue.startsWith('/') ||
                                /\.(png|jpg|jpeg|gif|webp)$/i.test(visualCue));

            if (isValidImage) {
                image.src = visualCue;
                image.classList.add('show');
            } else {
                if (visualCue) {
                    console.warn('Skipping invalid visualCue (looks like text/description):', visualCue);
                }
                image.classList.remove('show');
                image.src = '';
            }
        }

        // Apply colors if provided
        if (payload.backgroundColor) popup.style.backgroundColor = payload.backgroundColor;
        if (payload.borderColor) popup.style.borderColor = payload.borderColor;
        if (payload.textColor) {
            title.style.color = payload.textColor;
            message.style.color = payload.textColor;
        }

        // Show popup
        popup.classList.add('show');

        // Hide after duration
        const duration = payload.duration || 5000;

        if (popup.__hideTimer) clearTimeout(popup.__hideTimer);

        popup.__hideTimer = setTimeout(() => {
            popup.classList.remove('show');
        }, duration);
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

        // Track flags used by overlay-websocket.js visibility logic.
        try {
            window.omniOverlayOfflinePreview = (settings?.offlinePreview === true || settings?.OfflinePreview === true);
            window.omniOverlayTimerForceVisible = (settings?.timerManualRunning === true || settings?.TimerManualRunning === true);
        } catch (e) {}

        // Toggle timer element (no box; transparent). This allows portal toggles to
        // show/hide the timer without requiring a full overlay reload.
        const ensureTimer = () => {
            let timer = document.querySelector('.overlay-timer');
            if (!timer) {
                timer = document.createElement('div');
                timer.className = 'overlay-timer';

                // Base styling (transparent, OBS-friendly, always top-center)
                timer.style.position = 'fixed';
                timer.style.background = 'transparent';
                timer.style.pointerEvents = 'none';
                timer.style.zIndex = '1100';
                timer.style.fontFamily = "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace";
                timer.style.fontWeight = '800';
                timer.style.fontVariantNumeric = 'tabular-nums';
                timer.style.letterSpacing = '0.03em';
                timer.style.textShadow = '0 0 6px rgba(0,0,0,0.95), 0 0 10px rgba(0,0,0,0.75)';

                const value = document.createElement('span');
                value.className = 'timer-value';
                value.textContent = '00:00';
                value.style.fontSize = '42px';

                timer.appendChild(value);
                document.body.appendChild(timer);
            }

            // Remove any legacy label element if it exists from older versions
            const legacyLabel = timer.querySelector('.timer-label');
            if (legacyLabel && legacyLabel.parentNode) {
                legacyLabel.parentNode.removeChild(legacyLabel);
            }

            // Provide duration to overlay-websocket.js (minutes)
            const durationMinutes = Number(settings?.timerDurationMinutes ?? settings?.TimerDurationMinutes ?? 0);
            timer.dataset.durationMinutes = Number.isFinite(durationMinutes) ? String(durationMinutes) : '0';

            // Apply colors from dedicated timer color first, then fall back to theme
            const explicitTimerColor = settings?.timerTextColor ?? settings?.TimerTextColor;
            const themeColor = settings?.theme?.textColor;
            const valueColor = (explicitTimerColor && String(explicitTimerColor).trim().length > 0)
                ? String(explicitTimerColor).trim()
                : (themeColor ? String(themeColor) : null);

            if (valueColor) {
                const valueEl = timer.querySelector('.timer-value');
                if (valueEl) valueEl.style.color = valueColor;
            }

            // Top-center placement requirement. Scale matches overlay scale.
            const scale = Number(settings?.scale);
            const scaleValue = Number.isFinite(scale) && scale > 0 ? scale : 1;
            timer.style.top = '20px';
            timer.style.left = '50%';
            timer.style.right = '';
            timer.style.bottom = '';
            timer.style.transform = `translateX(-50%) scale(${scaleValue})`;
            timer.style.transformOrigin = 'top center';

            // Match current overlay visibility if available
            const overlay = document.querySelector('.counter-overlay');
            if (overlay && overlay.style && overlay.style.opacity) {
                timer.style.opacity = overlay.style.opacity;
            }

            // Force visible in preview/offline-preview/manual-running modes
            if (window.omniOverlayPreview === true || window.omniOverlayOfflinePreview === true || window.omniOverlayTimerForceVisible === true) {
                timer.style.opacity = '1';
            }

            return timer;
        };

        // In preview mode, always render the timer element so it can be tested.
        const isPreview = window.omniOverlayPreview === true;

        if (isPreview || (settings && (settings.timerEnabled === true || settings.TimerEnabled === true))) {
            ensureTimer();
        } else {
            const timer = document.querySelector('.overlay-timer');
            if (timer && timer.parentNode) {
                timer.parentNode.removeChild(timer);
            }
        }

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

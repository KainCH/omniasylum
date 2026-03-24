window.overlayInterop = {
    init: function() {
        const isDebugEnabled = () => window.omniOverlayDebug === true;

        if (!window.__omniOverlayVisibilityHandlerRegistered) {
            window.__omniOverlayVisibilityHandlerRegistered = true;

            document.addEventListener('visibilitychange', () => {
                if (document.visibilityState !== 'visible') return;

                // If an alert arrived while hidden, flush it once.
                try {
                    const pending = window.__omniOverlayPendingAlert;
                    if (pending && pending.type) {
                        window.__omniOverlayPendingAlert = null;
                        this.triggerAlert(pending.type, pending.payload);
                    }
                } catch (e) {
                    // Ignore flush errors; overlay should keep running.
                }
            });
        }

        if (window.asylumEffects) {
            if (isDebugEnabled()) {
                console.log("AsylumEffects initialized via Interop");
            }
        } else {
            console.error("AsylumEffects not found!");
        }

        // Initialize audio if not already done
        if (!window.notificationAudio && isDebugEnabled()) {
            console.warn("NotificationAudio not found! Audio will not play.");
        }

        // Intentionally do not silence alerts on init.
    },

    triggerAlert: function(type, payload) {
        const isDebugEnabled = () => window.omniOverlayDebug === true;

        const now = Date.now();

        // Normalize payload to an object so null/strings don't crash the overlay.
        const safePayload = (payload && typeof payload === 'object')
            ? payload
            : { textPrompt: payload != null ? String(payload) : '' };

        const queuePendingAlert = (reason, flushAtMs) => {
            try {
                window.__omniOverlayPendingAlert = {
                    type,
                    payload: safePayload,
                    receivedAtMs: now,
                    reason
                };
            } catch (e) {
                // Ignore queue errors; overlay should keep running.
            }
        };

        // If the page is not visible, queue the latest alert and flush when visible.
        if (document.visibilityState !== 'visible') {
            if (isDebugEnabled()) {
                console.log("🔇 Alert queued because overlay is not visible:", type);
            }
            queuePendingAlert('hidden');
            return;
        }
        if (isDebugEnabled()) {
            console.log("Triggering alert:", type, safePayload);
        }

        // Skip entire alert during reconnect silence window
        if (window.omniSuppressNotificationAudioResume === true) {
            if (isDebugEnabled()) {
                console.log("Alert suppressed during reconnect silence window:", type);
            }
            return;
        }

        // Interaction banners are UI-only and should not require an alert definition.
        if (type === 'interactionBanner') {
            const text = safePayload?.textPrompt || safePayload?.text;
            if (text) {
                this.showInteractionBanner(text, safePayload?.duration || 5000);
            }
            return;
        }

        // Play audio via DOM-embedded element for reliable OBS/Streamlabs browser source capture.
        // Uses the server-preloaded sound cache when available; falls back to a new DOM-embedded element.
        {
            const effectsJson = safePayload.effects;
            const soundTrigger = (typeof effectsJson === 'object' && effectsJson?.soundTrigger)
                || (typeof effectsJson === 'string' && (() => { try { return JSON.parse(effectsJson)?.soundTrigger; } catch (e) { return null; } })())
                || null;
            if (soundTrigger && typeof soundTrigger === 'string' && soundTrigger.includes('.')) {
                try {
                    const cached = window.__omniAlertSoundCache?.[soundTrigger];
                    let audio;
                    if (cached && cached.readyState >= 2) {
                        // Reuse the preloaded, DOM-embedded element — already primed in the browser source pipeline.
                        audio = cached;
                        audio.currentTime = 0;
                        audio.volume = 0.8;
                    } else {
                        // Cache miss: create a new DOM-embedded element and store it for future alerts.
                        // Remove any previous stale element for this sound before adding the new one.
                        const stale = window.__omniAlertSoundCache?.[soundTrigger];
                        if (stale && stale.parentNode) stale.parentNode.removeChild(stale);
                        audio = new Audio(`/sounds/${soundTrigger}`);
                        audio.volume = 0.8;
                        audio.style.display = 'none';
                        document.body.appendChild(audio);
                        if (!window.__omniAlertSoundCache) window.__omniAlertSoundCache = {};
                        window.__omniAlertSoundCache[soundTrigger] = audio;
                    }
                    const playPromise = audio.play();
                    if (playPromise !== undefined) {
                        playPromise.catch(err => {
                            if (isDebugEnabled()) console.warn('Alert audio blocked:', soundTrigger, err.name);
                        });
                    }
                } catch (e) { if (isDebugEnabled()) console.warn('Alert audio error:', e); }
            }
        }

        // Handle specific types that need custom logic
        if (type === 'bits') {
            this.triggerBitsCelebration(safePayload.amount || safePayload.bits || 50);
        } else if (type === 'subscription' || type === 'resub' || type === 'giftsub' || type === 'community_sub_gift') {
            this.triggerSubCelebration();
            if (safePayload.textPrompt) {
                this.showSubBanner(safePayload.textPrompt);
            }
        } else if (type === 'milestone') {
             // Milestone specific logic if any
        }

        // Show the main alert popup with visual cue and text
        this.showAlertPopup(safePayload);

        // Use AsylumEffects for visual effects only; sound is handled above via DOM-embedded audio.
        if (window.asylumEffects) {
            // Normalize effects to an object — if the server sent it as a JSON string, parse it first
            // so that object-spreading produces proper effect properties rather than per-character keys.
            let rawEffects = safePayload.effects || {};
            if (typeof rawEffects === 'string') {
                try { rawEffects = JSON.parse(rawEffects); } catch { rawEffects = {}; }
            }
            const effectsForAsylum = { ...rawEffects };
            delete effectsForAsylum.soundTrigger;
            window.asylumEffects.triggerEffect({
                textPrompt: safePayload.textPrompt || safePayload.name,
                backgroundColor: safePayload.backgroundColor,
                textColor: safePayload.textColor,
                borderColor: safePayload.borderColor,
                duration: safePayload.duration || 5000,
                effects: effectsForAsylum
            });
        }
    },

    showAlertPopup: function(payload) {
        const safePayload = (payload && typeof payload === 'object') ? payload : { textPrompt: payload != null ? String(payload) : '' };
        const popup = document.getElementById('alert-popup');
        const title = document.getElementById('alert-title');
        const message = document.getElementById('alert-message');
        const image = document.getElementById('alert-image');

        if (!popup || !title || !message) return;

        // Update text content
        title.textContent = safePayload.name || 'ALERT'; // Or use type?
        // Intentionally do NOT fall back to payload.message.
        // payload.message often contains raw Twitch chat/resub text and we do not want to render chat on the overlay.
        message.textContent = safePayload.textPrompt || '';

        // Update visual cue (image)
        if (image) {
            // Validate visualCue is a likely URL/path and not a description
            const visualCue = safePayload.visualCue;
            const isValidImage = visualCue &&
                               (visualCue.startsWith('http') ||
                                visualCue.startsWith('/') ||
                                /\.(png|jpg|jpeg|gif|webp)$/i.test(visualCue));

            if (isValidImage) {
                image.src = visualCue;
                image.classList.add('show');
            } else {
                if (visualCue) {
                    if (isDebugEnabled()) {
                        console.warn('Skipping invalid visualCue (looks like text/description):', visualCue);
                    }
                }
                image.classList.remove('show');
                image.src = '';
            }
        }

        // Apply colors if provided
        if (safePayload.backgroundColor) popup.style.backgroundColor = safePayload.backgroundColor;
        if (safePayload.borderColor) popup.style.borderColor = safePayload.borderColor;
        if (safePayload.textColor) {
            title.style.color = safePayload.textColor;
            message.style.color = safePayload.textColor;
        }

        // Show popup
        popup.classList.add('show');

        // Hide after duration
        const duration = safePayload.duration || 5000;

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
        // Reuse the existing alert popup markup; do NOT replace innerHTML (it breaks later alerts).
        const popup = document.getElementById('alert-popup');
        const title = document.getElementById('alert-title');
        const message = document.getElementById('alert-message');
        const image = document.getElementById('alert-image');

        if (!popup || !title || !message) return;

        if (image) {
            image.classList.remove('show');
            image.src = '';
        }

        title.textContent = 'BITS GOAL';
        message.textContent = '🎯 BITS GOAL REACHED! 💎';

        popup.classList.add('show');

        // Add sparkle effect
        this.createSparkles();

        if (popup.__hideTimer) clearTimeout(popup.__hideTimer);
        popup.__hideTimer = setTimeout(() => {
            popup.classList.remove('show');
        }, 5000);
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
        if (window.omniOverlayDebug === true) {
            console.log('Updating overlay settings:', settings);
        }

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
                timer.style.pointerEvents = 'none'; // Intentional: keep overlay click-through in OBS/Streamlabs
                timer.style.zIndex = '1100';
                timer.style.transition = 'opacity 0.6s ease';
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

            // Ensure fade is applied even if the timer was server-rendered (Blazor overlay page).
            if (!timer.style.transition) {
                timer.style.transition = 'opacity 0.6s ease';
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
            const valueColor = (explicitTimerColor != null && String(explicitTimerColor).trim().length > 0)
                ? String(explicitTimerColor).trim()
                : (themeColor ? String(themeColor) : null);

            const valueEl = timer.querySelector('.timer-value');
            if (valueEl) {
                valueEl.style.fontSize = '42px';
                if (valueColor) valueEl.style.color = valueColor;
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

            // If a countdown completed, keep it hidden until restarted.
            if (window.omniOverlayTimerExpired === true) {
                timer.style.opacity = '0';
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

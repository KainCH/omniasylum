export function connect(url, dotNetHelper) {
    const socket = new WebSocket(url);

    const pad2 = (n) => String(n).padStart(2, '0');

    const isDebugEnabled = () => {
        try {
            const urlParams = new URLSearchParams(window.location.search);
            return urlParams.get('debug') === 'true' || window.omniOverlayDebug === true;
        } catch (e) {
            return window.omniOverlayDebug === true;
        }
    };

    // Stream timer state (client-side; avoids Blazor re-rendering every second)
    let timerIntervalId = null;
    let streamStartMs = null;
    let manualStartMs = null;
    let manualRunning = false;
    let timerDurationSeconds = 0;
    let timerExpired = false;

    // Initialize duration from DOM if present (helps on first load before settingsUpdate arrives)
    try {
        const timerEl = document.querySelector('.overlay-timer');
        if (timerEl && timerEl.dataset && timerEl.dataset.durationMinutes) {
            const minutes = Number(timerEl.dataset.durationMinutes);
            timerDurationSeconds = Number.isFinite(minutes) && minutes > 0 ? Math.floor(minutes * 60) : 0;
        }
    } catch (e) {}

    const parseToMs = (value) => {
        // Most common: ISO string. Be defensive with alternate shapes.
        let parsed = null;
        if (typeof value === 'string') {
            const ms = Date.parse(value);
            parsed = Number.isFinite(ms) ? ms : null;
        } else if (typeof value === 'number') {
            parsed = Number.isFinite(value) ? value : null;
        } else if (typeof value === 'object' && value && value.dateTime) {
            const ms = Date.parse(value.dateTime);
            parsed = Number.isFinite(ms) ? ms : null;
        }
        return parsed;
    };

    const updateTimerDisplay = () => {
        const element = document.querySelector('.overlay-timer .timer-value');
        if (!element) return;

        const effectiveStartMs = (manualRunning && manualStartMs)
            ? manualStartMs
            : streamStartMs;

        if (!effectiveStartMs) {
            element.textContent = '00:00';
            return;
        }

        const elapsedMs = Math.max(0, Date.now() - effectiveStartMs);
        const totalSeconds = Math.floor(elapsedMs / 1000);

        // If a duration is configured, show countdown; otherwise show elapsed.
        if (timerDurationSeconds > 0) {
            const remainingSeconds = Math.max(0, timerDurationSeconds - totalSeconds);
            if (remainingSeconds <= 0) {
                element.textContent = '00:00';

                // Countdown completed: fade back to hidden.
                timerExpired = true;
                window.omniOverlayTimerExpired = true;
                stopTimerInterval();

                const timerEl = document.querySelector('.overlay-timer');
                if (timerEl) timerEl.style.opacity = '0';
                return;
            }

            // Clear any previous expired state once we have time remaining again.
            timerExpired = false;
            window.omniOverlayTimerExpired = false;

            const minutes = Math.floor(remainingSeconds / 60);
            const seconds = remainingSeconds % 60;
            element.textContent = `${pad2(minutes)}:${pad2(seconds)}`;
            return;
        }

        // Elapsed timer mode
        timerExpired = false;
        window.omniOverlayTimerExpired = false;

        const displaySeconds = totalSeconds;

        const minutes = Math.floor(displaySeconds / 60);
        const seconds = displaySeconds % 60;
        element.textContent = `${pad2(minutes)}:${pad2(seconds)}`;
    };

    const startTimerInterval = () => {
        if (timerIntervalId) return;
        timerIntervalId = setInterval(updateTimerDisplay, 1000);
    };

    const stopTimerInterval = () => {
        if (!timerIntervalId) return;
        clearInterval(timerIntervalId);
        timerIntervalId = null;
    };

    const setStreamStarted = (value) => {
        if (!value) {
            streamStartMs = null;
            if (!manualRunning) {
                stopTimerInterval();
                updateTimerDisplay();
            }
            return;
        }

        const parsed = parseToMs(value);
        if (parsed) {
            streamStartMs = parsed;
            timerExpired = false;
            window.omniOverlayTimerExpired = false;
            // If manual mode is not running, stream start should drive the timer.
            if (!manualRunning) {
                startTimerInterval();
                updateTimerDisplay();
            }
        }
    };

    const setManualTimer = (running, startUtcValue) => {
        manualRunning = running === true;
        window.omniOverlayTimerForceVisible = manualRunning;

        // Any manual start/stop resets completion state.
        timerExpired = false;
        window.omniOverlayTimerExpired = false;

        if (!manualRunning) {
            manualStartMs = null;
            // Resume stream-driven timer if we have stream start.
            if (streamStartMs) {
                startTimerInterval();
                updateTimerDisplay();
            } else {
                stopTimerInterval();
                updateTimerDisplay();
            }
            return;
        }

        const parsed = parseToMs(startUtcValue);
        manualStartMs = parsed || Date.now();
        startTimerInterval();
        updateTimerDisplay();
    };

    const normalizeOverlaySettings = (data) => {
        // We have two payload shapes in the wild:
        // 1) WebSocketOverlayNotifier => method: "settingsUpdate", data: OverlaySettings
        // 2) SignalROverlayNotifier   => method: "overlaySettingsUpdate", data: { overlaySettings: OverlaySettings }
        if (!data) return null;
        if (data.overlaySettings) return data.overlaySettings;
        if (data.settings) return data.settings;
        return data;
    };

    socket.onopen = function(e) {
        console.log("[WebSocket] Connection established");
    };

    socket.onmessage = function(event) {
        try {
            const message = JSON.parse(event.data);
            const method = message.method;
            const data = message.data;

            if (method === "counterUpdate") {
                // Update DOM directly to avoid Blazor Circuit dependency
                updateCounter("deaths", data.deaths);
                updateCounter("swears", data.swears);
                updateCounter("screams", data.screams);
                updateCounter("bits", data.bits);

                // Update stream timer if the payload includes streamStarted (may be null)
                if (data && (Object.prototype.hasOwnProperty.call(data, 'streamStarted') || Object.prototype.hasOwnProperty.call(data, 'StreamStarted')))
                {
                    setStreamStarted(data.streamStarted ?? data.StreamStarted);
                }
                // Also update Blazor state if connected, but don't crash if not
                try { dotNetHelper.invokeMethodAsync("OnCounterUpdate", data); } catch (e) {}
            } else if (method === "streamStatusUpdate") {
                updateStreamStatus(data.streamStatus);
                try { dotNetHelper.invokeMethodAsync("OnStreamStatusUpdate", data.streamStatus); } catch (e) {}
            } else if (method === "customAlert") {
                // Some customAlert types are control-plane updates (e.g. game switch), not user-facing alerts.
                if (isDebugEnabled()) {
                    console.log('[WebSocket] customAlert:', data && data.alertType ? data.alertType : '(unknown)', data && data.data ? data.data : {});
                }
                const alertType = data && data.alertType;
                const alertPayload = data && data.data ? data.data : {};

                // Suppress control-plane customAlert types (e.g. chatCommandsUpdated), matching static overlay.html behavior.
                if (alertType === 'chatCommandsUpdated') {
                    if (isDebugEnabled()) {
                        console.log('[WebSocket] Suppressing control-plane customAlert:', alertType);
                    }
                    return;
                }

                // Trigger alert directly via JS
                if (alertType) {
                    triggerAlert(alertType, alertPayload, dotNetHelper);
                }
            } else if (method === "bitsGoalUpdate") {
                try { dotNetHelper.invokeMethodAsync("OnBitsGoalUpdate", data); } catch (e) {}
            } else if (method === "bitsGoalComplete") {
                try { dotNetHelper.invokeMethodAsync("OnBitsGoalComplete", data); } catch (e) {}
                if (window.overlayInterop) window.overlayInterop.triggerBitsGoalCelebration();
            } else if (method === "milestoneReached") {
                try { dotNetHelper.invokeMethodAsync("OnMilestoneReached", data); } catch (e) {}
                if (window.overlayInterop) window.overlayInterop.triggerAlert('milestone', data);
            } else if (method === "streamStarted") {
                // streamStarted typically includes the full counter payload
                if (data && (data.streamStarted || data.StreamStarted)) {
                    setStreamStarted(data.streamStarted ?? data.StreamStarted);
                }
                try { dotNetHelper.invokeMethodAsync("OnStreamStarted", data); } catch (e) {}
            } else if (method === "streamEnded") {
                // Clear timer on stream end
                setStreamStarted(null);
            } else if (method === "overlaySettingsUpdate" || method === "settingsUpdate") {
                const settings = normalizeOverlaySettings(data);
                if (settings) {
                    // First: apply overlay settings to ensure the timer element is created/unhidden
                    // before we start the timer interval (prevents "running while hidden" flashes).
                    if (window.overlayInterop && window.overlayInterop.updateOverlaySettings) {
                        window.overlayInterop.updateOverlaySettings(settings);
                    }

                    // Update timer duration for countdown mode (minutes -> seconds)
                    const minutes = Number(settings.timerDurationMinutes ?? settings.TimerDurationMinutes ?? 0);
                    timerDurationSeconds = Number.isFinite(minutes) && minutes > 0 ? Math.floor(minutes * 60) : 0;

                    // Manual timer mode (start/stop)
                    const running = (settings.timerManualRunning ?? settings.TimerManualRunning) === true;
                    const startUtc = settings.timerManualStartUtc ?? settings.TimerManualStartUtc;
                    setManualTimer(running, startUtc);

                    try { dotNetHelper.invokeMethodAsync("OnOverlaySettingsUpdate", settings); } catch (e) {}
                }
            } else {
                // Standard alerts
                triggerAlert(method, data, dotNetHelper);
            }
        } catch (e) {
            console.error("[WebSocket] Error parsing message", e);
        }
    };

    socket.onclose = function(event) {
        if (event.wasClean) {
            console.log(`[WebSocket] Connection closed cleanly, code=${event.code} reason=${event.reason}`);
        } else {
            console.log('[WebSocket] Connection died');
            // Optional: Implement reconnect logic here
            setTimeout(() => connect(url, dotNetHelper), 5000);
        }
    };

    socket.onerror = function(error) {
        console.log(`[WebSocket] Error: ${error.message}`);
    };
}

function updateCounter(type, value) {
    const element = document.querySelector(`.counter-item.${type} .counter-value`);
    if (element) {
        element.textContent = value;
    }
}

function updateStreamStatus(status) {
    const overlay = document.querySelector('.counter-overlay');
    if (overlay) {
        const shouldShow = status === 'live' || (window.omniOverlayPreview === true) || (window.omniOverlayOfflinePreview === true);
        overlay.style.opacity = shouldShow ? '1' : '0';
    }

    const timer = document.querySelector('.overlay-timer');
    if (timer) {
        const shouldShowTimer = status === 'live'
            || (window.omniOverlayPreview === true)
            || (window.omniOverlayOfflinePreview === true)
            || (window.omniOverlayTimerForceVisible === true);

        const expired = window.omniOverlayTimerExpired === true;
        timer.style.opacity = (!expired && shouldShowTimer) ? '1' : '0';
    }
}

async function triggerAlert(type, data, dotNetHelper) {
    // Try to use Blazor to get full alert config if possible, as it has the DB data
    try {
        await dotNetHelper.invokeMethodAsync("OnAlert", type, data);
    } catch (e) {
        console.warn("[WebSocket] Blazor circuit disconnected, cannot trigger complex alert via server. Fallback needed if we want offline alerts.");
        // If we wanted fully offline alerts, we'd need to pass the full alert config in the WebSocket message
        // or cache it on the client side. For now, we rely on Blazor for the config lookup.
    }
}

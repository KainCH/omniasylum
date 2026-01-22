export function connect(url, dotNetHelper) {
    const socket = new WebSocket(url);

    // Stream timer state (client-side; avoids Blazor re-rendering every second)
    let timerIntervalId = null;
    let streamStartMs = null;

    const updateTimerDisplay = () => {
        const element = document.querySelector('.overlay-timer .timer-value');
        if (!element) return;

        if (!streamStartMs) {
            element.textContent = '00:00:00';
            return;
        }

        const elapsedMs = Math.max(0, Date.now() - streamStartMs);
        const totalSeconds = Math.floor(elapsedMs / 1000);
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;

        const pad2 = (n) => String(n).padStart(2, '0');
        element.textContent = `${pad2(hours)}:${pad2(minutes)}:${pad2(seconds)}`;
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
            stopTimerInterval();
            updateTimerDisplay();
            return;
        }

        // Most common: ISO string. Be defensive with alternate shapes.
        let parsed = null;
        if (typeof value === 'string') {
            const ms = Date.parse(value);
            parsed = Number.isFinite(ms) ? ms : null;
        } else if (typeof value === 'number') {
            parsed = Number.isFinite(value) ? value : null;
        } else if (typeof value === 'object' && value.dateTime) {
            const ms = Date.parse(value.dateTime);
            parsed = Number.isFinite(ms) ? ms : null;
        }

        if (parsed) {
            streamStartMs = parsed;
            startTimerInterval();
            updateTimerDisplay();
        }
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
                // Trigger alert directly via JS
                triggerAlert(data.alertType, data.data, dotNetHelper);
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
                    try { dotNetHelper.invokeMethodAsync("OnOverlaySettingsUpdate", settings); } catch (e) {}
                    if (window.overlayInterop && window.overlayInterop.updateOverlaySettings) {
                        window.overlayInterop.updateOverlaySettings(settings);
                    }
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
        overlay.style.opacity = status === 'live' ? '1' : '0';
    }

    const timer = document.querySelector('.overlay-timer');
    if (timer) {
        timer.style.opacity = status === 'live' ? '1' : '0';
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

const isDebugEnabled = () => {
    try {
        const urlParams = new URLSearchParams(window.location.search);
        return urlParams.get('debug') === 'true' || window.omniOverlayDebug === true;
    } catch (e) {
        return window.omniOverlayDebug === true;
    }
};

const log = (...args) => {
    if (isDebugEnabled()) console.log(...args);
};

const warn = (...args) => {
    if (isDebugEnabled()) console.warn(...args);
};

let activeEventSource = null;
let staleCheckTimerId = null;
let isUnloading = false;
let currentDotNetHelper = null;
let currentUserId = null;
let currentTier = null;

// Stream heartbeat tracking
let lastStreamHeartbeatAtMs = null;
let lastLiveSignalAtMs = null;
let inferredEnded = false;

const getLiveSignalTtlMs = () => {
    const configured = Number(window.omniLiveSignalTtlMs);
    return Number.isFinite(configured) && configured > 0 ? configured : 120000;
};

const clearTimers = () => {
    if (staleCheckTimerId) {
        clearInterval(staleCheckTimerId);
        staleCheckTimerId = null;
    }
};

// Timer state (client-side; avoids Blazor re-rendering every second)
let timerIntervalId = null;
let streamStartMs = null;
let manualStartMs = null;
let manualRunning = false;
let timerDurationSeconds = 0;
let timerExpired = false;

const pad2 = (n) => String(n).padStart(2, '0');

const parseToMs = (value) => {
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

    if (timerDurationSeconds > 0) {
        const remainingSeconds = Math.max(0, timerDurationSeconds - totalSeconds);
        if (remainingSeconds <= 0) {
            element.textContent = '00:00';
            if (!timerExpired) {
                try {
                    if (window.notificationAudio && typeof window.notificationAudio.playNotification === 'function') {
                        window.notificationAudio.playNotification('timerFinished');
                    }
                } catch (e) {}
            }
            timerExpired = true;
            window.omniOverlayTimerExpired = true;
            stopTimerInterval();
            const timerEl = document.querySelector('.overlay-timer');
            if (timerEl) timerEl.style.opacity = '0';
            return;
        }
        timerExpired = false;
        window.omniOverlayTimerExpired = false;
        const minutes = Math.floor(remainingSeconds / 60);
        const seconds = remainingSeconds % 60;
        element.textContent = `${pad2(minutes)}:${pad2(seconds)}`;
        return;
    }

    timerExpired = false;
    window.omniOverlayTimerExpired = false;
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
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
        if (!manualRunning) {
            startTimerInterval();
            updateTimerDisplay();
        }
    }
};

const setManualTimer = (running, startUtcValue) => {
    manualRunning = running === true;
    window.omniOverlayTimerForceVisible = manualRunning;
    timerExpired = false;
    window.omniOverlayTimerExpired = false;

    if (!manualRunning) {
        manualStartMs = null;
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
    if (!data) return null;
    if (data.overlaySettings) return data.overlaySettings;
    if (data.settings) return data.settings;
    return data;
};

function updateCounter(type, value) {
    const element = document.querySelector(`.counter-item.${type} .counter-value`);
    if (element) {
        element.textContent = value;
    }
}

function updateCustomCounters(customCounters) {
    if (!customCounters) return;
    for (const [key, value] of Object.entries(customCounters)) {
        const element = document.querySelector(`.counter-item[data-counter="${key.toLowerCase()}"] .counter-value`);
        if (element) {
            element.textContent = value;
        }
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
    try {
        await dotNetHelper.invokeMethodAsync("OnAlert", type, data);
    } catch (e) {
        warn("[SSE] Blazor circuit disconnected, cannot trigger alert via server.");
    }
}

function handleSseEvent(eventType, data, dotNetHelper) {
    if (eventType === 'counterUpdate') {
        updateCounter("deaths", data.deaths);
        updateCounter("swears", data.swears);
        updateCounter("screams", data.screams);
        updateCounter("bits", data.bits);
        if (data.customCounters) updateCustomCounters(data.customCounters);
        if (data && (Object.prototype.hasOwnProperty.call(data, 'streamStarted') || Object.prototype.hasOwnProperty.call(data, 'StreamStarted'))) {
            setStreamStarted(data.streamStarted ?? data.StreamStarted);
        }
        try { dotNetHelper.invokeMethodAsync("OnCounterUpdate", data); } catch (e) {}

    } else if (eventType === 'streamStatusUpdate') {
        const status = data && data.streamStatus ? data.streamStatus : 'offline';
        lastStreamHeartbeatAtMs = Date.now();
        if (status === 'live' || status === 'prepping') {
            lastLiveSignalAtMs = Date.now();
            inferredEnded = false;
        }
        updateStreamStatus(status);
        try { dotNetHelper.invokeMethodAsync("OnStreamStatusUpdate", status); } catch (e) {}

    } else if (eventType === 'customAlert') {
        const alertType = data && data.alertType;
        const alertPayload = data && data.data ? data.data : {};
        if (alertType === 'chatCommandsUpdated') return;
        if (alertType) triggerAlert(alertType, alertPayload, dotNetHelper);

    } else if (eventType === 'bitsGoalUpdate') {
        try { dotNetHelper.invokeMethodAsync("OnBitsGoalUpdate", data); } catch (e) {}

    } else if (eventType === 'bitsGoalComplete') {
        try { dotNetHelper.invokeMethodAsync("OnBitsGoalComplete", data); } catch (e) {}
        if (window.overlayInterop) window.overlayInterop.triggerBitsGoalCelebration();

    } else if (eventType === 'milestoneReached') {
        try { dotNetHelper.invokeMethodAsync("OnMilestoneReached", data); } catch (e) {}
        if (window.overlayInterop) window.overlayInterop.triggerAlert('milestone', data);

    } else if (eventType === 'streamStarted') {
        if (data && (data.streamStarted || data.StreamStarted)) {
            setStreamStarted(data.streamStarted ?? data.StreamStarted);
        }
        try { dotNetHelper.invokeMethodAsync("OnStreamStarted", data); } catch (e) {}

    } else if (eventType === 'streamEnded') {
        setStreamStarted(null);
        inferredEnded = false;
        lastLiveSignalAtMs = null;
        updateStreamStatus('offline');
        try { dotNetHelper.invokeMethodAsync("OnStreamStatusUpdate", 'offline'); } catch (e) {}

    } else if (eventType === 'overlaySettingsUpdate' || eventType === 'settingsUpdate') {
        const settings = normalizeOverlaySettings(data);
        if (settings) {
            if (window.overlayInterop && window.overlayInterop.updateOverlaySettings) {
                window.overlayInterop.updateOverlaySettings(settings);
            }
            const minutes = Number(settings.timerDurationMinutes ?? settings.TimerDurationMinutes ?? 0);
            timerDurationSeconds = Number.isFinite(minutes) && minutes > 0 ? Math.floor(minutes * 60) : 0;
            const running = (settings.timerManualRunning ?? settings.TimerManualRunning) === true;
            const startUtc = settings.timerManualStartUtc ?? settings.TimerManualStartUtc;
            setManualTimer(running, startUtc);
            try { dotNetHelper.invokeMethodAsync("OnOverlaySettingsUpdate", settings); } catch (e) {}
        }

    } else {
        // Standard alerts
        triggerAlert(eventType, data, dotNetHelper);
    }
}

function handleInitBundle(data, dotNetHelper) {
    // Apply settings
    if (data.settings) {
        if (window.overlayInterop && window.overlayInterop.updateOverlaySettings) {
            window.overlayInterop.updateOverlaySettings(data.settings);
        }
        const minutes = Number(data.settings.timerDurationMinutes ?? data.settings.TimerDurationMinutes ?? 0);
        timerDurationSeconds = Number.isFinite(minutes) && minutes > 0 ? Math.floor(minutes * 60) : 0;
        const running = (data.settings.timerManualRunning ?? data.settings.TimerManualRunning) === true;
        const startUtc = data.settings.timerManualStartUtc ?? data.settings.TimerManualStartUtc;
        setManualTimer(running, startUtc);
        try { dotNetHelper.invokeMethodAsync("OnOverlaySettingsUpdate", data.settings); } catch (e) {}
    }

    // Apply counters
    if (data.counters) {
        updateCounter("deaths", data.counters.deaths ?? data.counters.Deaths ?? 0);
        updateCounter("swears", data.counters.swears ?? data.counters.Swears ?? 0);
        updateCounter("screams", data.counters.screams ?? data.counters.Screams ?? 0);
        updateCounter("bits", data.counters.bits ?? data.counters.Bits ?? 0);
        if (data.counters.customCounters) updateCustomCounters(data.counters.customCounters);
        try { dotNetHelper.invokeMethodAsync("OnCounterUpdate", data.counters); } catch (e) {}
    }

    // Stream status
    if (data.streamStatus) {
        updateStreamStatus(data.streamStatus);
        try { dotNetHelper.invokeMethodAsync("OnStreamStatusUpdate", data.streamStatus); } catch (e) {}
    }

    // Stream started timestamp
    if (data.streamStarted) {
        setStreamStarted(data.streamStarted);
    }

    // Bits goal
    if (data.bitsGoal) {
        try { dotNetHelper.invokeMethodAsync("OnBitsGoalUpdate", data.bitsGoal); } catch (e) {}
    }
}

export function disconnect() {
    isUnloading = true;
    clearTimers();
    stopTimerInterval();
    if (activeEventSource) {
        activeEventSource.close();
        activeEventSource = null;
    }
}

export function connect(userId, dotNetHelper, tier) {
    if (!window.__omniOverlaySseUnloadHookRegistered) {
        window.__omniOverlaySseUnloadHookRegistered = true;
        window.addEventListener('beforeunload', () => {
            isUnloading = true;
            disconnect();
        });
    }

    isUnloading = false;
    currentDotNetHelper = dotNetHelper;
    currentUserId = userId;
    currentTier = tier || null;

    // Close existing connection
    if (activeEventSource) {
        activeEventSource.close();
        activeEventSource = null;
    }
    clearTimers();

    // Initialize duration from DOM if present
    try {
        const timerEl = document.querySelector('.overlay-timer');
        if (timerEl && timerEl.dataset && timerEl.dataset.durationMinutes) {
            const minutes = Number(timerEl.dataset.durationMinutes);
            timerDurationSeconds = Number.isFinite(minutes) && minutes > 0 ? Math.floor(minutes * 60) : 0;
        }
    } catch (e) {}

    // Build SSE URL
    let sseUrl = `/api/v2/overlay/events?userId=${encodeURIComponent(userId)}`;
    if (tier) {
        sseUrl += `&tier=${encodeURIComponent(tier)}`;
    }

    const eventSource = new EventSource(sseUrl);
    activeEventSource = eventSource;
    inferredEnded = false;
    lastStreamHeartbeatAtMs = Date.now();

    eventSource.addEventListener('connected', async (e) => {
        log('[SSE] Connected');
        try {
            const connData = JSON.parse(e.data);
            const connectionId = connData.connectionId;

            // Signal ready to get init bundle
            let readyUrl = `/api/v2/overlay/ready?userId=${encodeURIComponent(userId)}&connectionId=${encodeURIComponent(connectionId)}`;
            const response = await fetch(readyUrl, { method: 'POST' });
            if (!response.ok) {
                warn('[SSE] Ready signal failed:', response.status);
            }
        } catch (err) {
            warn('[SSE] Error handling connected event:', err);
        }
    });

    eventSource.addEventListener('init', (e) => {
        log('[SSE] Init bundle received');
        try {
            const data = JSON.parse(e.data);
            handleInitBundle(data, dotNetHelper);
        } catch (err) {
            warn('[SSE] Error parsing init bundle:', err);
        }
    });

    // Standard overlay events
    const eventTypes = [
        'counterUpdate', 'streamStatusUpdate', 'customAlert',
        'bitsGoalUpdate', 'bitsGoalComplete', 'milestoneReached',
        'streamStarted', 'streamEnded', 'overlaySettingsUpdate', 'settingsUpdate',
        'follow', 'subscription', 'resub', 'giftsub', 'bits', 'raid',
        'interactionBanner'
    ];

    for (const eventType of eventTypes) {
        eventSource.addEventListener(eventType, (e) => {
            try {
                const data = JSON.parse(e.data);
                handleSseEvent(eventType, data, dotNetHelper);
            } catch (err) {
                warn(`[SSE] Error handling ${eventType}:`, err);
            }
        });
    }

    eventSource.onerror = () => {
        if (isUnloading) return;
        warn('[SSE] Connection error — EventSource will auto-reconnect');
    };

    // Live signal TTL stale check
    staleCheckTimerId = setInterval(() => {
        if (isUnloading) return;
        if (!activeEventSource || activeEventSource.readyState === EventSource.CLOSED) return;

        const ttlMs = getLiveSignalTtlMs();
        const inferredEndMs = ttlMs * 2;

        if (!lastLiveSignalAtMs) return;

        const ageMs = Date.now() - lastLiveSignalAtMs;
        if (ageMs >= inferredEndMs && !inferredEnded) {
            inferredEnded = true;
            warn('[SSE] Live signal TTL exceeded; inferring stream ended. ageMs=', ageMs, 'ttlMs=', ttlMs);
            try { setStreamStarted(null); } catch (e) {}
            try { updateStreamStatus('offline'); } catch (e) {}
            try { dotNetHelper.invokeMethodAsync("OnStreamStatusUpdate", 'offline'); } catch (e) {}
        }
    }, 10000);
}

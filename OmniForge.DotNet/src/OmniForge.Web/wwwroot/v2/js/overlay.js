/**
 * OmniForge V2 Overlay — SSE client
 *
 * Thin display client. Server is the source of truth for all config and events.
 * Uses Server-Sent Events (EventSource) with a two-step handshake:
 *   1. EventSource connects → server sends event: connected { connectionId }
 *   2. Client sends POST /ready with connectionId → server sends event: init { full config }
 *   3. Server pushes incremental events: counter, alert, config, stream, bitsgoal, banner, template
 */
(function () {
  'use strict';

  const urlParams = new URLSearchParams(window.location.search);
  const userId = urlParams.get('userId');
  const preview = urlParams.get('preview') === 'true';
  const debug = urlParams.get('debug') === 'true';

  // Expose flags for shared interop scripts
  window.omniOverlayPreview = preview;
  window.omniOverlayDebug = debug;

  // Silence console in non-debug mode (OBS noise reduction)
  if (!debug && !window.__omniOverlayConsoleSilenced) {
    window.__omniOverlayConsoleSilenced = true;
    try {
      console.log = function () { };
      console.info = function () { };
      console.debug = function () { };
    } catch (e) { /* ignore */ }
  }

  // --- State ---
  let currentSettings = null;
  let lastCounterData = null;
  let customCountersConfig = {};
  let knownServerInstanceId = null;

  // Timer state
  let timerIntervalId = null;
  let streamStartMs = null;
  let timerDurationSeconds = 0;
  let manualRunning = false;
  let manualStartMs = null;
  let timerExpired = false;

  // Visibility state
  let lastKnownIsLive = false;
  let lastOnlineSignalAtMs = null;
  const LIVE_SIGNAL_TTL_MS = 120000; // 2 minutes

  // --- Utility ---
  const isLiveSignalFresh = () => lastOnlineSignalAtMs && (Date.now() - lastOnlineSignalAtMs) < LIVE_SIGNAL_TTL_MS;

  const parseToMs = (value) => {
    if (!value) return null;
    if (typeof value === 'string') {
      const ms = Date.parse(value);
      return Number.isFinite(ms) ? ms : null;
    }
    if (typeof value === 'number') {
      if (!Number.isFinite(value)) return null;
      const now = Date.now();
      const diffMs = Math.abs(value - now);
      const diffSec = Math.abs(value * 1000 - now);
      return diffSec < diffMs ? value * 1000 : value;
    }
    if (typeof value === 'object' && value.dateTime) {
      const ms = Date.parse(value.dateTime);
      return Number.isFinite(ms) ? ms : null;
    }
    return null;
  };

  const pad2 = (n) => String(n).padStart(2, '0');
  const formatMmSs = (seconds) => {
    const safe = Math.max(0, Number(seconds) || 0);
    return `${pad2(Math.floor(safe / 60))}:${pad2(safe % 60)}`;
  };

  // --- Visibility ---
  const shouldForceVisible = () => preview || window.omniOverlayOfflinePreview === true || manualRunning;

  const updateOverlayContainerVisibility = () => {
    const container = document.getElementById('overlay-container');
    if (!container) return;
    const shouldShow = shouldForceVisible() || isLiveSignalFresh();
    container.classList.toggle('visible', shouldShow);
  };

  const updateTimerVisibility = () => {
    const timerEl = document.querySelector('.overlay-timer');
    if (!timerEl) return;
    window.omniOverlayTimerExpired = timerExpired;
    timerEl.style.opacity = (!timerExpired && (shouldForceVisible() || isLiveSignalFresh())) ? '1' : '0';
  };

  const updateTimerDisplay = () => {
    const valueEl = document.querySelector('.overlay-timer .timer-value');
    if (!valueEl) return;

    const start = (manualRunning && manualStartMs) ? manualStartMs : streamStartMs;
    if (!start) { valueEl.textContent = '00:00'; return; }

    const elapsedSeconds = Math.floor(Math.max(0, Date.now() - start) / 1000);

    if (timerDurationSeconds > 0) {
      const remaining = Math.max(0, timerDurationSeconds - elapsedSeconds);
      valueEl.textContent = formatMmSs(remaining);
      if (remaining <= 0 && !timerExpired) {
        try { window.notificationAudio?.playNotification('timerFinished'); } catch (e) { /* */ }
        timerExpired = true;
        window.omniOverlayTimerExpired = true;
        stopTimerInterval();
        updateTimerVisibility();
      }
      return;
    }

    // Elapsed mode
    timerExpired = false;
    window.omniOverlayTimerExpired = false;
    valueEl.textContent = formatMmSs(elapsedSeconds);
  };

  const startTimerInterval = () => {
    if (timerIntervalId) return;
    timerIntervalId = setInterval(() => {
      updateTimerDisplay();
      updateTimerVisibility();
      updateOverlayContainerVisibility();
    }, 1000);
  };

  const stopTimerInterval = () => {
    if (!timerIntervalId) return;
    clearInterval(timerIntervalId);
    timerIntervalId = null;
  };

  const applyTimerSettings = (settings) => {
    if (!settings) return;
    const durationMinutes = Number(settings.timerDurationMinutes ?? settings.TimerDurationMinutes ?? 0);
    timerDurationSeconds = Number.isFinite(durationMinutes) && durationMinutes > 0 ? Math.floor(durationMinutes * 60) : 0;

    manualRunning = (settings.timerManualRunning ?? settings.TimerManualRunning) === true;
    window.omniOverlayTimerForceVisible = manualRunning;

    if (manualRunning) {
      const parsed = parseToMs(settings.timerManualStartUtc ?? settings.TimerManualStartUtc);
      const newStartMs = parsed || Date.now();
      const isNewSession = !manualStartMs || Math.abs(newStartMs - manualStartMs) > 1000;
      if (isNewSession) { timerExpired = false; window.omniOverlayTimerExpired = false; }
      manualStartMs = newStartMs;
      startTimerInterval();
    } else {
      manualStartMs = null;
    }

    updateTimerVisibility();
    updateTimerDisplay();
  };

  // --- Rendering ---
  function applySettings(settings) {
    const container = document.getElementById('overlay-container');
    const interactionBanner = document.getElementById('interaction-banner');

    const rawScale = settings ? (settings.scale ?? settings.Scale) : 1;
    const rawPosition = settings ? (settings.position || settings.Position) : 'top-right';
    const scale = rawScale != null ? Number(rawScale) : 1;
    const position = rawPosition ? String(rawPosition) : 'top-right';

    window.omniOverlayOfflinePreview = settings?.offlinePreview === true || settings?.OfflinePreview === true;

    container.style.top = '';
    container.style.bottom = '';
    container.style.left = '';
    container.style.right = '';
    if (interactionBanner) { interactionBanner.style.top = ''; interactionBanner.style.bottom = ''; }

    let translate = '';
    switch (position) {
      case 'top-left':
        container.style.top = '20px'; container.style.left = '20px'; container.style.transformOrigin = 'top left';
        if (interactionBanner) interactionBanner.style.bottom = '20px';
        break;
      case 'top-right':
        container.style.top = '20px'; container.style.right = '20px'; container.style.transformOrigin = 'top right';
        if (interactionBanner) interactionBanner.style.bottom = '20px';
        break;
      case 'bottom-left':
        container.style.bottom = '20px'; container.style.left = '20px'; container.style.transformOrigin = 'bottom left';
        if (interactionBanner) interactionBanner.style.top = '20px';
        break;
      case 'bottom-right':
        container.style.bottom = '20px'; container.style.right = '20px'; container.style.transformOrigin = 'bottom right';
        if (interactionBanner) interactionBanner.style.top = '20px';
        break;
      case 'top-center':
        container.style.top = '20px'; container.style.left = '50%'; translate = 'translateX(-50%)'; container.style.transformOrigin = 'top center';
        if (interactionBanner) interactionBanner.style.bottom = '20px';
        break;
      case 'bottom-center':
        container.style.bottom = '20px'; container.style.left = '50%'; translate = 'translateX(-50%)'; container.style.transformOrigin = 'bottom center';
        if (interactionBanner) interactionBanner.style.top = '20px';
        break;
      default:
        container.style.top = '20px'; container.style.right = '20px'; container.style.transformOrigin = 'top right';
        if (interactionBanner) interactionBanner.style.bottom = '20px';
        break;
    }

    const safeScale = Number.isFinite(scale) && scale > 0 ? scale : 1;
    container.style.transform = translate ? `${translate} scale(${safeScale})` : `scale(${safeScale})`;
  }

  function renderCounters(data) {
    const container = document.getElementById('overlay-container');
    if (!data) return;

    lastCounterData = data;

    // Handle visibility from stream data
    const hasStreamStartedField = Object.prototype.hasOwnProperty.call(data, 'streamStarted') || Object.prototype.hasOwnProperty.call(data, 'StreamStarted');
    if (hasStreamStartedField) {
      const raw = data.streamStarted !== undefined ? data.streamStarted : data.StreamStarted;
      if (raw !== null) {
        const parsedStart = parseToMs(raw);
        if (parsedStart) {
          lastKnownIsLive = true;
          lastOnlineSignalAtMs = Date.now();
        }
      }
    }

    updateOverlayContainerVisibility();

    if (!manualRunning) {
      if (isLiveSignalFresh() && streamStartMs) {
        startTimerInterval();
      } else {
        stopTimerInterval();
      }
      updateTimerVisibility();
      updateTimerDisplay();
    }

    container.replaceChildren();

    const deaths = data.deaths !== undefined ? data.deaths : data.Deaths;
    const swears = data.swears !== undefined ? data.swears : data.Swears;
    const screams = data.screams !== undefined ? data.screams : data.Screams;
    const bits = data.bits !== undefined ? data.bits : data.Bits;
    const customCounters = data.customCounters || data.CustomCounters || {};

    const countersConfig = currentSettings?.counters || currentSettings?.Counters || {};
    const showDeaths = (countersConfig.deaths !== undefined ? countersConfig.deaths : countersConfig.Deaths) !== false;
    const showSwears = (countersConfig.swears !== undefined ? countersConfig.swears : countersConfig.Swears) !== false;
    const showScreams = (countersConfig.screams !== undefined ? countersConfig.screams : countersConfig.Screams) !== false;
    const showBits = countersConfig.bits === true || countersConfig.Bits === true;

    if (showDeaths && deaths !== undefined) createCounterItem(container, 'DEATHS', deaths, '\u{1F480}');
    if (showSwears && swears !== undefined) createCounterItem(container, 'SWEARS', swears, '\u{1F92C}');
    if (showScreams && screams !== undefined) createCounterItem(container, 'SCREAMS', screams, '\u{1F631}');
    if (showBits && bits !== undefined) createCounterItem(container, 'BITS', bits, '\u{1F48E}');

    // Render custom counters
    const defs = customCountersConfig || {};
    const entries = Object.entries(defs);
    if (entries.length) {
      entries.sort((a, b) => {
        const nameA = (a[1]?.name || a[1]?.Name || a[0]).toString().toLowerCase();
        const nameB = (b[1]?.name || b[1]?.Name || b[0]).toString().toLowerCase();
        return nameA.localeCompare(nameB);
      });
      for (const [counterId, def] of entries) {
        const label = def?.name || def?.Name || counterId;
        const icon = def?.icon || def?.Icon || '\u{1F522}';
        const value = (customCounters[counterId] ?? customCounters[counterId.toLowerCase()]) ?? 0;
        createCustomCounterItem(container, counterId, label, value, icon);
      }
    }
  }

  function createCounterItem(container, label, value, icon) {
    const safeValue = (value === null || value === undefined || isNaN(Number(value))) ? 0 : value;
    const div = document.createElement('div');
    div.className = 'counter-item';

    const iconDiv = document.createElement('div');
    iconDiv.className = 'counter-icon';
    iconDiv.textContent = icon;

    const infoDiv = document.createElement('div');
    infoDiv.className = 'counter-info';

    const labelDiv = document.createElement('div');
    labelDiv.className = 'counter-label';
    labelDiv.textContent = label;

    const valueDiv = document.createElement('div');
    valueDiv.className = 'counter-value';
    valueDiv.textContent = safeValue;

    infoDiv.appendChild(labelDiv);
    infoDiv.appendChild(valueDiv);
    div.appendChild(iconDiv);
    div.appendChild(infoDiv);
    container.appendChild(div);
  }

  function createCustomCounterItem(container, counterId, label, value, icon) {
    const safeValue = (value === null || value === undefined || isNaN(Number(value))) ? '0' : String(value);
    const div = document.createElement('div');
    div.className = 'counter-item custom';
    div.setAttribute('data-counter-id', counterId);

    const iconDiv = document.createElement('div');
    iconDiv.className = 'counter-icon';
    iconDiv.textContent = icon;

    const infoDiv = document.createElement('div');
    infoDiv.className = 'counter-info';

    const labelDiv = document.createElement('div');
    labelDiv.className = 'counter-label';
    labelDiv.textContent = label;

    const valueDiv = document.createElement('div');
    valueDiv.className = 'counter-value';
    valueDiv.textContent = safeValue;

    infoDiv.appendChild(labelDiv);
    infoDiv.appendChild(valueDiv);
    div.appendChild(iconDiv);
    div.appendChild(infoDiv);

    div.addEventListener('click', async () => {
      try {
        await fetch(`/api/counters/${userId}/custom/${encodeURIComponent(counterId)}/increment`, { method: 'POST' });
      } catch (err) {
        console.error('Failed to increment custom counter:', counterId, err);
      }
    });

    container.appendChild(div);
  }

  // --- Stream status ---
  function setStreamStatus(data) {
    if (!data) return;
    const status = typeof data === 'string' ? data : (data.status || data.streamStatus);
    const streamStarted = data.streamStarted;

    if (typeof status === 'string') {
      const normalized = status.toLowerCase();
      const isLive = normalized === 'live';
      lastKnownIsLive = isLive;

      if (isLive) {
        lastOnlineSignalAtMs = Date.now();
        if (streamStarted) {
          const parsed = parseToMs(streamStarted);
          if (parsed) streamStartMs = parsed;
        }
        if (!manualRunning && streamStartMs) startTimerInterval();
      } else if (normalized === 'offline') {
        lastOnlineSignalAtMs = null;
        if (!manualRunning) {
          streamStartMs = null;
          stopTimerInterval();
          updateTimerDisplay();
        }
      }
    }

    updateOverlayContainerVisibility();
    updateTimerVisibility();
  }

  // --- Alert handling ---
  function triggerAlert(data) {
    if (!data) return;
    const alertType = data.type || data.alertType || 'alert';

    // Control-plane updates (silent)
    if (alertType === 'chatCommandsUpdated' || alertType === 'customCountersUpdated' || alertType === 'customCounterUpdate') {
      if (alertType === 'customCountersUpdated' && data.data?.counters) {
        customCountersConfig = data.data.counters;
        if (lastCounterData) renderCounters(lastCounterData);
      }
      if (alertType === 'customCounterUpdate') {
        const counterId = data.data?.counterId ?? data.counterId;
        const value = data.data?.value ?? data.value;
        if (counterId != null && value != null) {
          if (!lastCounterData) lastCounterData = {};
          if (!lastCounterData.customCounters) lastCounterData.customCounters = {};
          lastCounterData.customCounters[counterId] = value;
          renderCounters(lastCounterData);
        }
      }
      return;
    }

    // Interaction banner
    if (alertType === 'interactionBanner' || alertType === 'banner') {
      if (window.overlayInterop) {
        window.overlayInterop.triggerAlert('interactionBanner', data);
      }
      return;
    }

    // Standard alert
    if (window.overlayInterop) {
      window.overlayInterop.triggerAlert(alertType, data.data || data);
    }
  }

  function showBanner(data) {
    if (!data) return;
    if (window.overlayInterop) {
      const text = data.text || data.message || '';
      const duration = data.duration || 8000;
      window.overlayInterop.triggerAlert('interactionBanner', { text, duration, type: data.type || 'interaction' });
    }
  }

  function updateBitsGoal(data) {
    if (!data) return;
    if (data.completed === true && window.overlayInterop?.triggerBitsGoalCelebration) {
      window.overlayInterop.triggerBitsGoalCelebration();
    }
  }

  // --- Init ---
  if (!userId) {
    const message = document.createElement('div');
    message.setAttribute('style', 'color: red; background: black; padding: 20px;');
    message.textContent = 'Error: No userId provided in URL query parameter.';
    document.body.replaceChildren(message);
    return;
  }

  // Apply safe default position
  currentSettings = { position: 'top-right', scale: 1 };
  applySettings(currentSettings);

  // Initialize interop
  if (window.overlayInterop) {
    window.overlayInterop.init();
    if (preview && window.overlayInterop.updateOverlaySettings) {
      window.overlayInterop.updateOverlaySettings(currentSettings);
    }
  }

  // --- SSE Connection ---
  const eventsUrl = `/api/v2/overlay/events?userId=${encodeURIComponent(userId)}`;
  let source = null;

  function connectSSE() {
    source = new EventSource(eventsUrl);

    // Step 1: Server assigns connectionId
    source.addEventListener('connected', (e) => {
      const { connectionId } = JSON.parse(e.data);
      console.log('SSE connected, connectionId:', connectionId);

      // Activate silence window to suppress backlog burst on connect/reconnect
      const RECONNECT_SILENCE_MS = 10000;
      window.omniSuppressNotificationAudioResume = true;
      window.omniOverlayResumeSuppressUntil = Date.now() + RECONNECT_SILENCE_MS;
      window.omniSilenceUntil = Date.now() + RECONNECT_SILENCE_MS;
      setTimeout(() => { window.omniSuppressNotificationAudioResume = false; }, RECONNECT_SILENCE_MS);

      // Step 2: Signal ready after DOM is set up
      fetch(`/api/v2/overlay/ready?userId=${encodeURIComponent(userId)}&connectionId=${encodeURIComponent(connectionId)}`, {
        method: 'POST'
      }).catch(err => console.error('Failed to send ready signal:', err));
    });

    // Step 3: Server sends full config bundle
    source.addEventListener('init', (e) => {
      const config = JSON.parse(e.data);
      console.log('SSE init received');

      // Server instance ID for restart detection
      if (config.serverInstanceId) {
        if (knownServerInstanceId === null) {
          knownServerInstanceId = config.serverInstanceId;
        } else if (config.serverInstanceId !== knownServerInstanceId) {
          console.log('Server restart detected, reloading...');
          window.location.reload();
          return;
        }
      }

      // Apply settings
      if (config.settings) {
        currentSettings = config.settings;
        applySettings(config.settings);
        if (window.overlayInterop?.updateOverlaySettings) {
          window.overlayInterop.updateOverlaySettings(config.settings);
        }
        applyTimerSettings(config.settings);
      }

      // Load custom counters config
      customCountersConfig = config.customCountersConfig || {};

      // Render counters
      if (config.counters) {
        renderCounters(config.counters);
      }

      // Set stream status
      if (config.streamStatus) {
        setStreamStatus({
          status: config.streamStatus,
          streamStarted: config.streamStarted || config.counters?.streamStarted
        });
      }

      // Cache alert templates (for future use by the effects system)
      if (config.alerts && window.overlayInterop) {
        // Alert templates are stored server-side and enriched before delivery.
        // No local caching needed — the server sends fully enriched alert events.
      }
    });

    // --- Incremental events ---

    source.addEventListener('counter', (e) => {
      renderCounters(JSON.parse(e.data));
    });

    source.addEventListener('alert', (e) => {
      triggerAlert(JSON.parse(e.data));
    });

    source.addEventListener('config', (e) => {
      const settings = JSON.parse(e.data);
      currentSettings = settings;
      applySettings(settings);
      if (window.overlayInterop?.updateOverlaySettings) {
        window.overlayInterop.updateOverlaySettings(settings);
      }
      applyTimerSettings(settings);
      if (lastCounterData) renderCounters(lastCounterData);
    });

    source.addEventListener('stream', (e) => {
      setStreamStatus(JSON.parse(e.data));
    });

    source.addEventListener('bitsgoal', (e) => {
      updateBitsGoal(JSON.parse(e.data));
    });

    source.addEventListener('banner', (e) => {
      showBanner(JSON.parse(e.data));
    });

    source.addEventListener('template', (e) => {
      // Template changes are handled by the effects system.
      // No action needed in the overlay client for now.
      if (window.omniOverlayDebug) {
        console.log('Template update received:', e.data);
      }
    });

    source.onerror = () => {
      // EventSource auto-reconnects. On reconnect, server sends new event: connected.
      // Nothing to do here — the handshake resets automatically.
      console.warn('SSE connection error, auto-reconnecting...');
    };
  }

  connectSSE();

  // Watchdog: auto-hide if monitor stops sending live heartbeats
  setInterval(() => {
    updateOverlayContainerVisibility();
    if (!manualRunning) {
      if (isLiveSignalFresh() && streamStartMs) {
        startTimerInterval();
      } else {
        stopTimerInterval();
      }
      updateTimerVisibility();
    }
  }, 5000);

})();

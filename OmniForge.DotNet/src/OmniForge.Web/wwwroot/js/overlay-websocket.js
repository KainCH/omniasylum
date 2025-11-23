export function connect(url, dotNetHelper) {
    const socket = new WebSocket(url);

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
                // Also update Blazor state if connected, but don't crash if not
                try { dotNetHelper.invokeMethodAsync("OnCounterUpdate", data); } catch (e) {}
            } else if (method === "streamStatusUpdate") {
                updateStreamStatus(data.streamStatus);
                try { dotNetHelper.invokeMethodAsync("OnStreamStatusUpdate", data.streamStatus); } catch (e) {}
            } else if (method === "customAlert") {
                // Trigger alert directly via JS
                triggerAlert(data.alertType, data.data, dotNetHelper);
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

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
                dotNetHelper.invokeMethodAsync("OnCounterUpdate", data);
            } else if (method === "streamStatusUpdate") {
                dotNetHelper.invokeMethodAsync("OnStreamStatusUpdate", data.streamStatus);
            } else if (method === "customAlert") {
                dotNetHelper.invokeMethodAsync("OnCustomAlert", data.alertType, data.data);
            } else {
                // Assume it's a standard alert type like "newFollower", "newSubscriber" etc.
                dotNetHelper.invokeMethodAsync("OnAlert", method, data);
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

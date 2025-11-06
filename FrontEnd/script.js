// Configuration
let socket = null;
let isAuthenticated = false;
let connectionStatus = 'disconnected';

// Counter state
let deathCount = 0;
let swearsCount = 0;

// Initialize the application
init();

async function init() {
    // Check if user is authenticated
    try {
        const response = await fetch('/api/health');
        if (response.ok) {
            console.log('✅ Server connection established');
            checkAuth();
        }
    } catch (error) {
        console.error('❌ Failed to connect to server:', error);
        showAuthPrompt();
    }
}

async function checkAuth() {
    try {
        const response = await fetch('/api/counters', {
            credentials: 'include'
        });

        if (response.ok) {
            const data = await response.json();
            console.log('✅ User authenticated');
            isAuthenticated = true;
            initializeSocket();
            updateCountersFromData(data);
        } else if (response.status === 401) {
            console.log('🔐 User not authenticated');
            showAuthPrompt();
        }
    } catch (error) {
        console.error('❌ Auth check failed:', error);
        showAuthPrompt();
    }
}

function showAuthPrompt() {
    document.body.innerHTML = `
        <div style="text-align: center; padding: 50px; font-family: Arial, sans-serif;">
            <h1>🎮 OmniForgeStream Counter</h1>
            <p>Please log in with your Twitch account to use the stream counter.</p>
            <button onclick="window.location.href='/auth/twitch'"
                    style="padding: 15px 30px; font-size: 18px; background: #9146ff; color: white; border: none; border-radius: 5px; cursor: pointer;">
                Login with Twitch
            </button>
        </div>
    `;
}

function initializeSocket() {
    // Connect to WebSocket
    socket = io('/', {
        transports: ['websocket', 'polling']
    });

    socket.on('connect', () => {
        console.log('✅ WebSocket connected');
        connectionStatus = 'connected';
        updateConnectionStatus();
    });

    socket.on('disconnect', () => {
        console.log('❌ WebSocket disconnected');
        connectionStatus = 'disconnected';
        updateConnectionStatus();
    });

    socket.on('counterUpdate', (data) => {
        console.log('📊 Counter update received:', data);
        updateCountersFromData(data);
    });

    socket.on('error', (error) => {
        console.error('❌ Socket error:', error);
    });
}

function updateConnectionStatus() {
    // This could update a status indicator if we add one to the UI
    console.log(`Connection status: ${connectionStatus}`);
}

function updateCountersFromData(data) {
    if (data) {
        deathCount = data.deaths || 0;
        swearsCount = data.swears || 0;
        updateDisplay();
    }
}

// Death counter functions
function incrementDeaths() {
    if (socket && socket.connected) {
        socket.emit('incrementDeaths');
        playSound('death');
    } else {
        console.error('❌ Not connected to server');
    }
}

function decrementDeaths() {
    if (socket && socket.connected) {
        socket.emit('decrementDeaths');
    } else {
        console.error('❌ Not connected to server');
    }
}

// Swears counter functions
function incrementSwears() {
    if (socket && socket.connected) {
        socket.emit('incrementSwears');
        playSound('swear');
    } else {
        console.error('❌ Not connected to server');
    }
}

function decrementSwears() {
    if (socket && socket.connected) {
        socket.emit('decrementSwears');
    } else {
        console.error('❌ Not connected to server');
    }
}

// Reset all counters
function resetCounters() {
    if (confirm('Are you sure you want to reset all counters?')) {
        if (socket && socket.connected) {
            socket.emit('resetCounters');
        } else {
            console.error('❌ Not connected to server');
        }
    }
}

// Export data
function exportData() {
    const data = {
        deaths: deathCount,
        swears: swearsCount,
        total: deathCount + swearsCount,
        timestamp: new Date().toLocaleString()
    };

    const json = JSON.stringify(data, null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `stream-counter-${Date.now()}.json`;
    a.click();
    URL.revokeObjectURL(url);
}

// Update all display elements
function updateDisplay() {
    const deathElement = document.getElementById('deathCount');
    const swearsElement = document.getElementById('swearsCount');
    const totalElement = document.getElementById('totalEvents');
    const updatedElement = document.getElementById('lastUpdated');

    if (deathElement) deathElement.textContent = deathCount;
    if (swearsElement) swearsElement.textContent = swearsCount;
    if (totalElement) totalElement.textContent = deathCount + swearsCount;
    if (updatedElement) updatedElement.textContent = new Date().toLocaleTimeString();
}

// Play sound on increment (optional)
function playSound(type) {
    // Create a simple beep using Web Audio API
    const audioContext = new (window.AudioContext || window.webkitAudioContext)();
    const oscillator = audioContext.createOscillator();
    const gainNode = audioContext.createGain();

    oscillator.connect(gainNode);
    gainNode.connect(audioContext.destination);

    if (type === 'death') {
        oscillator.frequency.value = 200;
        gainNode.gain.setValueAtTime(0.1, audioContext.currentTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.2);
        oscillator.start(audioContext.currentTime);
        oscillator.stop(audioContext.currentTime + 0.2);
    } else if (type === 'swear') {
        oscillator.frequency.value = 400;
        gainNode.gain.setValueAtTime(0.1, audioContext.currentTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.1);
        oscillator.start(audioContext.currentTime);
        oscillator.stop(audioContext.currentTime + 0.1);
    }
}

// Keyboard shortcuts
document.addEventListener('keydown', (e) => {
    if (e.key === 'd' || e.key === 'D') {
        incrementDeaths();
    } else if (e.key === 's' || e.key === 'S') {
        incrementSwears();
    } else if (e.key === 'r' || e.key === 'R') {
        resetCounters();
    }
});

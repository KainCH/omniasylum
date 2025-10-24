// Configuration
const STORAGE_PREFIX = 'streamCounter_';
const COMMAND_STORAGE_KEY = STORAGE_PREFIX + 'command';
const UPDATE_STORAGE_KEY = STORAGE_PREFIX + 'update';

// Initialize
let lastCommand = null;
updateDisplay();
checkForUpdates();

// Listen for storage changes from main counter window
window.addEventListener('storage', (e) => {
    if (e.key === UPDATE_STORAGE_KEY || e.key === 'deathCount' || e.key === 'swearsCount') {
        updateDisplay();
        logSync('📥 Update received from counter');
    }
});

// Check for updates periodically
setInterval(checkForUpdates, 500);

// Send command to main counter via localStorage
function sendCommand(command) {
    try {
        const timestamp = Date.now();
        const commandData = {
            command: command,
            timestamp: timestamp,
            id: Math.random()
        };
        
        localStorage.setItem(COMMAND_STORAGE_KEY, JSON.stringify(commandData));
        lastCommand = command;
        
        logSync(`📤 Command: ${formatCommand(command)}`);
        playFeedback();
        
        // Update display after sending command
        setTimeout(updateDisplay, 100);
    } catch (error) {
        console.error('Error sending command:', error);
        logSync('❌ Failed to send command');
    }
}

// Check for updates from the counter
function checkForUpdates() {
    try {
        const deathCount = parseInt(localStorage.getItem('deathCount') || '0');
        const swearsCount = parseInt(localStorage.getItem('swearsCount') || '0');
        
        updateCounterDisplay(deathCount, swearsCount);
        updateConnectionStatus(true);
    } catch (error) {
        console.error('Error checking updates:', error);
        updateConnectionStatus(false);
    }
}

// Update the display with current values
function updateDisplay() {
    checkForUpdates();
}

// Update counter values on screen
function updateCounterDisplay(deaths, swears) {
    const deathsEl = document.getElementById('currentDeaths');
    const swearsEl = document.getElementById('currentSwears');
    
    if (deathsEl) deathsEl.textContent = deaths;
    if (swearsEl) swearsEl.textContent = swears;
}

// Update connection status indicator
function updateConnectionStatus(connected) {
    const statusDot = document.getElementById('statusDot');
    const statusText = document.getElementById('statusText');
    
    if (connected) {
        statusDot.classList.remove('disconnected');
        statusText.textContent = 'Connected';
    } else {
        statusDot.classList.add('disconnected');
        statusText.textContent = 'Disconnected';
    }
}

// Format command name for display
function formatCommand(command) {
    const names = {
        'incrementDeaths': 'Deaths +',
        'decrementDeaths': 'Deaths −',
        'incrementSwears': 'Swears +',
        'decrementSwears': 'Swears −',
        'resetCounters': 'Reset All'
    };
    return names[command] || command;
}

// Play audio feedback
function playFeedback() {
    try {
        const audioContext = new (window.AudioContext || window.webkitAudioContext)();
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();
        
        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);
        
        oscillator.frequency.value = 800;
        gainNode.gain.setValueAtTime(0.1, audioContext.currentTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.1);
        
        oscillator.start(audioContext.currentTime);
        oscillator.stop(audioContext.currentTime + 0.1);
    } catch (error) {
        console.error('Audio feedback error:', error);
    }
}

// Logging system
let logMessages = [];
const MAX_LOG_ENTRIES = 8;

function logSync(message) {
    const timestamp = new Date().toLocaleTimeString();
    const entry = `[${timestamp}] ${message}`;
    
    logMessages.unshift(entry);
    if (logMessages.length > MAX_LOG_ENTRIES) {
        logMessages.pop();
    }
    
    updateLogDisplay();
}

function updateLogDisplay() {
    const logContent = document.getElementById('syncLog');
    if (!logContent) return;
    
    logContent.innerHTML = logMessages
        .map(msg => `<div class="log-entry">${msg}</div>`)
        .join('');
    
    logContent.scrollTop = logContent.scrollHeight;
}

function clearLog() {
    logMessages = [];
    updateLogDisplay();
}

// Initialize
logSync('🚀 Mobile control ready');
updateConnectionStatus(true);

// Fallback update for when storage event doesn't fire
setInterval(() => {
    checkForUpdates();
}, 1000);

// Configuration
const COMMAND_STORAGE_KEY = 'streamCounter_command';
const UPDATE_STORAGE_KEY = 'streamCounter_update';
let lastProcessedCommand = null;

// Initialize counters from localStorage
let deathCount = localStorage.getItem('deathCount') ? parseInt(localStorage.getItem('deathCount')) : 0;
let swearsCount = localStorage.getItem('swearsCount') ? parseInt(localStorage.getItem('swearsCount')) : 0;

// Initialize UI
updateDisplay();

// Listen for commands from mobile control
window.addEventListener('storage', (e) => {
    if (e.key === COMMAND_STORAGE_KEY && e.newValue) {
        try {
            const commandData = JSON.parse(e.newValue);
            if (commandData.id !== lastProcessedCommand) {
                processRemoteCommand(commandData.command);
                lastProcessedCommand = commandData.id;
            }
        } catch (error) {
            console.error('Error processing remote command:', error);
        }
    }
});

// Check for commands periodically (fallback for same-window updates)
setInterval(checkForRemoteCommands, 500);

// Death counter functions
function incrementDeaths() {
    deathCount++;
    saveAndUpdate();
    playSound('death');
}

function decrementDeaths() {
    if (deathCount > 0) {
        deathCount--;
        saveAndUpdate();
    }
}

// Swears counter functions
function incrementSwears() {
    swearsCount++;
    saveAndUpdate();
    playSound('swear');
}

function decrementSwears() {
    if (swearsCount > 0) {
        swearsCount--;
        saveAndUpdate();
    }
}

// Reset all counters
function resetCounters() {
    if (confirm('Are you sure you want to reset all counters?')) {
        deathCount = 0;
        swearsCount = 0;
        saveAndUpdate();
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

// Save to localStorage and update display
function saveAndUpdate() {
    localStorage.setItem('deathCount', deathCount);
    localStorage.setItem('swearsCount', swearsCount);
    localStorage.setItem('lastUpdated', new Date().toLocaleTimeString());
    updateDisplay();
}

// Update all display elements
function updateDisplay() {
    document.getElementById('deathCount').textContent = deathCount;
    document.getElementById('swearsCount').textContent = swearsCount;
    document.getElementById('totalEvents').textContent = deathCount + swearsCount;
    
    const lastUpdated = localStorage.getItem('lastUpdated');
    document.getElementById('lastUpdated').textContent = lastUpdated || 'Never';
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

// Process commands from mobile control
function processRemoteCommand(command) {
    switch (command) {
        case 'incrementDeaths':
            incrementDeaths();
            break;
        case 'decrementDeaths':
            decrementDeaths();
            break;
        case 'incrementSwears':
            incrementSwears();
            break;
        case 'decrementSwears':
            decrementSwears();
            break;
        case 'resetCounters':
            // Reset without confirmation when triggered remotely
            deathCount = 0;
            swearsCount = 0;
            saveAndUpdate();
            break;
    }
    
    // Notify mobile control of update
    notifyMobileControl();
}

// Check for pending commands (fallback mechanism)
function checkForRemoteCommands() {
    try {
        const commandStr = localStorage.getItem(COMMAND_STORAGE_KEY);
        if (commandStr) {
            const commandData = JSON.parse(commandStr);
            if (commandData.id !== lastProcessedCommand) {
                processRemoteCommand(commandData.command);
                lastProcessedCommand = commandData.id;
            }
        }
    } catch (error) {
        console.error('Error checking remote commands:', error);
    }
}

// Notify mobile control of updates
function notifyMobileControl() {
    try {
        localStorage.setItem(UPDATE_STORAGE_KEY, Date.now().toString());
    } catch (error) {
        console.error('Error notifying mobile control:', error);
    }
}

// Keyboard shortcuts
document.addEventListener('keydown', (e) => {
    if (e.key === 'd' || e.key === 'D') {
        incrementDeaths();
    } else if (e.key === 's' || e.key === 'S') {
        incrementSwears();
    }
});

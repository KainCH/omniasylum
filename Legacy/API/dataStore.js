const fs = require('fs');
const path = require('path');

// Data file path
const DATA_DIR = path.join(__dirname, 'data');
const DATA_FILE = process.env.DATA_FILE || path.join(DATA_DIR, 'counters.json');

// Ensure data directory exists
if (!fs.existsSync(DATA_DIR)) {
  fs.mkdirSync(DATA_DIR, { recursive: true });
}

// Default data structure
const defaultData = {
  deaths: 0,
  swears: 0,
  lastUpdated: new Date().toISOString(),
  createdAt: new Date().toISOString()
};

// Initialize data file if it doesn't exist
function initializeDataFile() {
  if (!fs.existsSync(DATA_FILE)) {
    saveData(defaultData);
    console.log('✅ Data file initialized');
  }
}

// Read data from file
function getData() {
  try {
    if (!fs.existsSync(DATA_FILE)) {
      initializeDataFile();
    }
    const fileContent = fs.readFileSync(DATA_FILE, 'utf8');
    return JSON.parse(fileContent);
  } catch (error) {
    console.error('Error reading data file:', error);
    return defaultData;
  }
}

// Save data to file
function saveData(data) {
  try {
    const dataToSave = {
      ...data,
      lastUpdated: new Date().toISOString()
    };
    fs.writeFileSync(DATA_FILE, JSON.stringify(dataToSave, null, 2), 'utf8');
    return dataToSave;
  } catch (error) {
    console.error('Error saving data file:', error);
    throw error;
  }
}

// Increment deaths counter
function incrementDeaths() {
  const data = getData();
  data.deaths += 1;
  return saveData(data);
}

// Decrement deaths counter
function decrementDeaths() {
  const data = getData();
  if (data.deaths > 0) {
    data.deaths -= 1;
  }
  return saveData(data);
}

// Increment swears counter
function incrementSwears() {
  const data = getData();
  data.swears += 1;
  return saveData(data);
}

// Decrement swears counter
function decrementSwears() {
  const data = getData();
  if (data.swears > 0) {
    data.swears -= 1;
  }
  return saveData(data);
}

// Reset all counters
function resetCounters() {
  const data = getData();
  data.deaths = 0;
  data.swears = 0;
  return saveData(data);
}

// Get total events
function getTotalEvents() {
  const data = getData();
  return data.deaths + data.swears;
}

// Initialize on module load
initializeDataFile();

module.exports = {
  getData,
  saveData,
  incrementDeaths,
  decrementDeaths,
  incrementSwears,
  decrementSwears,
  resetCounters,
  getTotalEvents
};

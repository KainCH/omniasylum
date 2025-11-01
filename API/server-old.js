const express = require('express');
const http = require('http');
const socketIo = require('socket.io');
const cors = require('cors');
const path = require('path');
require('dotenv').config();

const dataStore = require('./dataStore');
const twitchService = require('./twitchService');

// Initialize Express app
const app = express();
const server = http.createServer(app);

// Configure Socket.io with CORS
const io = socketIo(server, {
  cors: {
    origin: process.env.CORS_ORIGIN || '*',
    methods: ['GET', 'POST']
  }
});

// Middleware
app.use(cors());
app.use(express.json());

// API Routes

// Get current counter state
app.get('/api/counters', (req, res) => {
  try {
    const data = dataStore.getData();
    res.json(data);
  } catch (error) {
    console.error('Error fetching counters:', error);
    res.status(500).json({ error: 'Failed to fetch counters' });
  }
});

// Increment death counter
app.post('/api/counters/deaths/increment', (req, res) => {
  try {
    const data = dataStore.incrementDeaths();
    io.emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error incrementing deaths:', error);
    res.status(500).json({ error: 'Failed to increment deaths' });
  }
});

// Decrement death counter
app.post('/api/counters/deaths/decrement', (req, res) => {
  try {
    const data = dataStore.decrementDeaths();
    io.emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error decrementing deaths:', error);
    res.status(500).json({ error: 'Failed to decrement deaths' });
  }
});

// Increment swear counter
app.post('/api/counters/swears/increment', (req, res) => {
  try {
    const data = dataStore.incrementSwears();
    io.emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error incrementing swears:', error);
    res.status(500).json({ error: 'Failed to increment swears' });
  }
});

// Decrement swear counter
app.post('/api/counters/swears/decrement', (req, res) => {
  try {
    const data = dataStore.decrementSwears();
    io.emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error decrementing swears:', error);
    res.status(500).json({ error: 'Failed to decrement swears' });
  }
});

// Reset all counters
app.post('/api/counters/reset', (req, res) => {
  try {
    const data = dataStore.resetCounters();
    io.emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error resetting counters:', error);
    res.status(500).json({ error: 'Failed to reset counters' });
  }
});

// Export counter data
app.get('/api/counters/export', (req, res) => {
  try {
    const data = dataStore.getData();
    const exportData = {
      ...data,
      exportedAt: new Date().toISOString()
    };
    res.json(exportData);
  } catch (error) {
    console.error('Error exporting data:', error);
    res.status(500).json({ error: 'Failed to export data' });
  }
});

// Twitch API routes (future implementation)
app.get('/api/twitch/status', (req, res) => {
  res.json({ 
    connected: twitchService.isConnected(),
    features: ['chat_commands', 'channel_points', 'clips']
  });
});

// Health check endpoint
app.get('/api/health', (req, res) => {
  res.json({ 
    status: 'ok', 
    timestamp: new Date().toISOString(),
    uptime: process.uptime()
  });
});

// WebSocket connection handling
io.on('connection', (socket) => {
  console.log(`Client connected: ${socket.id}`);

  // Send current state to newly connected client
  socket.emit('counterUpdate', dataStore.getData());

  // Handle client commands
  socket.on('incrementDeaths', () => {
    const data = dataStore.incrementDeaths();
    io.emit('counterUpdate', data);
  });

  socket.on('decrementDeaths', () => {
    const data = dataStore.decrementDeaths();
    io.emit('counterUpdate', data);
  });

  socket.on('incrementSwears', () => {
    const data = dataStore.incrementSwears();
    io.emit('counterUpdate', data);
  });

  socket.on('decrementSwears', () => {
    const data = dataStore.decrementSwears();
    io.emit('counterUpdate', data);
  });

  socket.on('resetCounters', () => {
    const data = dataStore.resetCounters();
    io.emit('counterUpdate', data);
  });

  socket.on('disconnect', () => {
    console.log(`Client disconnected: ${socket.id}`);
  });
});

// Start server
const PORT = process.env.PORT || 3000;
server.listen(PORT, () => {
  console.log(`🚀 Server running on port ${PORT}`);
  console.log(`📊 Counter API available at http://localhost:${PORT}/api/counters`);
  console.log(`🔌 WebSocket server ready for connections`);
  
  // Initialize Twitch service if configured
  if (process.env.TWITCH_CLIENT_ID && process.env.TWITCH_CLIENT_SECRET) {
    twitchService.initialize();
    
    // Listen for Twitch chat commands (mod-only counter controls)
    twitchService.on('incrementDeaths', (user) => {
      const data = dataStore.incrementDeaths();
      io.emit('counterUpdate', data);
      console.log(`💀 Deaths incremented by mod: ${user}`);
    });

    twitchService.on('decrementDeaths', (user) => {
      const data = dataStore.decrementDeaths();
      io.emit('counterUpdate', data);
      console.log(`💀 Deaths decremented by mod: ${user}`);
    });

    twitchService.on('incrementSwears', (user) => {
      const data = dataStore.incrementSwears();
      io.emit('counterUpdate', data);
      console.log(`🤬 Swears incremented by mod: ${user}`);
    });

    twitchService.on('decrementSwears', (user) => {
      const data = dataStore.decrementSwears();
      io.emit('counterUpdate', data);
      console.log(`🤬 Swears decremented by mod: ${user}`);
    });

    twitchService.on('resetCounters', (user) => {
      const data = dataStore.resetCounters();
      io.emit('counterUpdate', data);
      console.log(`🔄 Counters reset by mod: ${user}`);
    });
  } else {
    console.log('⚠️  Twitch integration not configured (add credentials to .env)');
  }
});

// Graceful shutdown
process.on('SIGTERM', () => {
  console.log('SIGTERM signal received: closing HTTP server');
  server.close(() => {
    console.log('HTTP server closed');
  });
});

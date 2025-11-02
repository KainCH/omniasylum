const express = require('express');
const http = require('http');
const socketIo = require('socket.io');
const cors = require('cors');
const cookieParser = require('cookie-parser');
require('dotenv').config();

const database = require('./database');
const keyVault = require('./keyVault');
const twitchService = require('./multiTenantTwitchService');
const authRoutes = require('./authRoutes');
const counterRoutes = require('./counterRoutes');
const adminRoutes = require('./adminRoutes');
const { verifySocketAuth } = require('./authMiddleware');

// Initialize Express app
const app = express();
const server = http.createServer(app);

// Configure Socket.io with CORS
const io = socketIo(server, {
  cors: {
    origin: process.env.CORS_ORIGIN || '*',
    methods: ['GET', 'POST'],
    credentials: true
  }
});

// Make io available to routes
app.set('io', io);

// Middleware
app.use(cors({
  origin: process.env.CORS_ORIGIN || '*',
  credentials: true
}));
app.use(express.json());
app.use(cookieParser());

// Serve static frontend files
const path = require('path');
const frontendPath = process.env.NODE_ENV === 'production'
  ? path.join(__dirname, 'frontend')
  : path.join(__dirname, '..', 'modern-frontend', 'dist');
app.use(express.static(frontendPath));

// Health check endpoint (unauthenticated)
app.get('/api/health', (req, res) => {
  res.json({
    status: 'ok',
    timestamp: new Date().toISOString(),
    uptime: process.uptime(),
    keyVault: keyVault.isUsingKeyVault(),
    dbMode: process.env.DB_MODE || 'local'
  });
});

// Frontend routes
app.get('/', (req, res) => {
  res.sendFile(path.join(frontendPath, 'index.html'));
});

app.get('/mobile', (req, res) => {
  res.sendFile(path.join(frontendPath, 'mobile.html'));
});

// Authentication routes
app.use('/auth', authRoutes);

// Counter routes (requires authentication)
app.use('/api/counters', counterRoutes);

// Admin routes (requires admin role)
app.use('/api/admin', adminRoutes);

// Twitch status endpoint
app.get('/api/twitch/status', (req, res) => {
  res.json({
    initialized: true,
    connectedUsers: twitchService.getConnectedUsers().length
  });
});

// WebSocket connection handling with authentication
io.use(verifySocketAuth);

io.on('connection', (socket) => {
  const userId = socket.userId;
  console.log(`Client connected: ${socket.displayName} (${userId})`);

  // Join user-specific room for targeted broadcasts
  socket.join(`user:${userId}`);

  // Send current state to newly connected client
  database.getCounters(userId).then(data => {
    socket.emit('counterUpdate', data);
  });

  // Handle client commands
  socket.on('incrementDeaths', async () => {
    try {
      const data = await database.incrementDeaths(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error incrementing deaths:', error);
      socket.emit('error', { message: 'Failed to increment deaths' });
    }
  });

  socket.on('decrementDeaths', async () => {
    try {
      const data = await database.decrementDeaths(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error decrementing deaths:', error);
    }
  });

  socket.on('incrementSwears', async () => {
    try {
      const data = await database.incrementSwears(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error incrementing swears:', error);
    }
  });

  socket.on('decrementSwears', async () => {
    try {
      const data = await database.decrementSwears(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error decrementing swears:', error);
    }
  });

  socket.on('resetCounters', async () => {
    try {
      const data = await database.resetCounters(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error resetting counters:', error);
    }
  });

  // Connect user's Twitch bot when they connect
  socket.on('connectTwitch', async () => {
    try {
      const success = await twitchService.connectUser(userId);
      socket.emit('twitchConnected', { success });
    } catch (error) {
      console.error('Error connecting Twitch:', error);
      socket.emit('twitchConnected', { success: false, error: error.message });
    }
  });

  socket.on('disconnect', () => {
    console.log(`Client disconnected: ${socket.displayName} (${userId})`);
    socket.leave(`user:${userId}`);
  });
});

// Twitch chat command handlers
twitchService.on('incrementDeaths', async ({ userId, username }) => {
  try {
    const data = await database.incrementDeaths(userId);
    io.to(`user:${userId}`).emit('counterUpdate', data);
    console.log(`💀 Deaths incremented by ${username}`);
  } catch (error) {
    console.error('Error handling Twitch increment deaths:', error);
  }
});

twitchService.on('decrementDeaths', async ({ userId, username }) => {
  try {
    const data = await database.decrementDeaths(userId);
    io.to(`user:${userId}`).emit('counterUpdate', data);
    console.log(`💀 Deaths decremented by ${username}`);
  } catch (error) {
    console.error('Error handling Twitch decrement deaths:', error);
  }
});

twitchService.on('incrementSwears', async ({ userId, username }) => {
  try {
    const data = await database.incrementSwears(userId);
    io.to(`user:${userId}`).emit('counterUpdate', data);
    console.log(`🤬 Swears incremented by ${username}`);
  } catch (error) {
    console.error('Error handling Twitch increment swears:', error);
  }
});

twitchService.on('decrementSwears', async ({ userId, username }) => {
  try {
    const data = await database.decrementSwears(userId);
    io.to(`user:${userId}`).emit('counterUpdate', data);
    console.log(`🤬 Swears decremented by ${username}`);
  } catch (error) {
    console.error('Error handling Twitch decrement swears:', error);
  }
});

twitchService.on('resetCounters', async ({ userId, username }) => {
  try {
    const data = await database.resetCounters(userId);
    io.to(`user:${userId}`).emit('counterUpdate', data);
    console.log(`🔄 Counters reset by ${username}`);
  } catch (error) {
    console.error('Error handling Twitch reset:', error);
  }
});

// Handle public commands (anyone can use)
twitchService.on('publicCommand', async ({ userId, channel, username, command }) => {
  try {
    const counters = await database.getCounters(userId);
    let message = '';

    if (command === '!deaths') {
      message = `💀 Current deaths: ${counters.deaths}`;
    } else if (command === '!swears') {
      message = `🤬 Current swears: ${counters.swears}`;
    } else if (command === '!stats') {
      const total = counters.deaths + counters.swears;
      message = `📊 Stats - Deaths: ${counters.deaths} | Swears: ${counters.swears} | Total: ${total}`;
    }

    if (message) {
      await twitchService.sendMessage(userId, message);
    }
  } catch (error) {
    console.error('Error handling public command:', error);
  }
});

// Start server
const PORT = process.env.PORT || 3000;

async function startServer() {
  try {
    // Initialize Key Vault
    await keyVault.initialize();

    // Initialize database
    await database.initialize();

    // Initialize Twitch service
    await twitchService.initialize();

    // Start HTTP server
    server.listen(PORT, () => {
      console.log('');
      console.log('╔════════════════════════════════════════════════════════╗');
      console.log('║        🎮 OmniAsylum API Server Started 🎮           ║');
      console.log('╠════════════════════════════════════════════════════════╣');
      console.log(`║  Port:          ${PORT.toString().padEnd(39)} ║`);
      console.log(`║  Environment:   ${(process.env.NODE_ENV || 'development').padEnd(39)} ║`);
      console.log(`║  Database:      ${(process.env.DB_MODE || 'local').padEnd(39)} ║`);
      console.log(`║  Key Vault:     ${(keyVault.isUsingKeyVault() ? 'Azure' : 'Local ENV').padEnd(39)} ║`);
      console.log('╠════════════════════════════════════════════════════════╣');
      console.log(`║  API:           http://localhost:${PORT}/api/health`.padEnd(56) + '║');
      console.log(`║  Auth:          http://localhost:${PORT}/auth/twitch`.padEnd(56) + '║');
      console.log(`║  WebSocket:     ws://localhost:${PORT}`.padEnd(56) + '║');
      console.log('╚════════════════════════════════════════════════════════╝');
      console.log('');
    });
  } catch (error) {
    console.error('❌ Failed to start server:', error);
    process.exit(1);
  }
}

// Graceful shutdown
process.on('SIGTERM', async () => {
  console.log('SIGTERM signal received: closing HTTP server');
  await twitchService.disconnectAll();
  server.close(() => {
    console.log('HTTP server closed');
    process.exit(0);
  });
});

process.on('SIGINT', async () => {
  console.log('\nSIGINT signal received: closing HTTP server');
  await twitchService.disconnectAll();
  server.close(() => {
    console.log('HTTP server closed');
    process.exit(0);
  });
});

// Start the server
startServer();

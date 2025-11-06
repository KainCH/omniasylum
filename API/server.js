const express = require('express');
const http = require('http');
const socketIo = require('socket.io');
const cors = require('cors');
const cookieParser = require('cookie-parser');
require('dotenv').config();

const database = require('./database');
const keyVault = require('./keyVault');
const twitchService = require('./multiTenantTwitchService');
const streamMonitor = require('./streamMonitor');
const authRoutes = require('./authRoutes');
const counterRoutes = require('./counterRoutes');
const adminRoutes = require('./adminRoutes');
const overlayRoutes = require('./overlayRoutes');
const streamRoutes = require('./streamRoutes');
const userRoutes = require('./userRoutes');
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
  },
  transports: ['websocket', 'polling'],
  allowEIO3: true,
  path: '/socket.io/'
});

// Add connection debugging
io.engine.on('connection_error', (err) => {
  console.log('❌ Socket.io connection error:', err.req);
  console.log('❌ Socket.io error details:', err.code, err.message, err.context);
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

// User routes (requires authentication)
app.use('/api/user', userRoutes);
app.use('/api', userRoutes); // For /api/overlay-settings

// Admin routes (requires admin role)
app.use('/api/admin', adminRoutes);

// Overlay routes (public for OBS browser sources)
app.use('/overlay', overlayRoutes);

// Stream management routes (requires authentication)
app.use('/api/stream', streamRoutes);

// Channel point reward routes (requires authentication)
const channelPointRoutes = require('./channelPointRoutes');
app.use('/api/rewards', channelPointRoutes);

// Alert management routes (requires authentication)
const alertRoutes = require('./alertRoutes');
app.use('/api/alerts', alertRoutes);

// Twitch status endpoint
app.get('/api/twitch/status', (req, res) => {
  res.json({
    initialized: true,
    connectedUsers: twitchService.getConnectedUsers().length
  });
});

// WebSocket connection handling with authentication
console.log('🔌 Setting up Socket.io authentication middleware');
io.use(verifySocketAuth);

io.on('connection', (socket) => {
  const userId = socket.userId;
  console.log(`✅ Client connected: ${socket.displayName} (${userId || 'unauthenticated'})`);
  console.log(`🔌 Socket ID: ${socket.id}, Transport: ${socket.conn.transport.name}`);

  // Join user-specific room for authenticated users
  if (userId) {
    socket.join(`user:${userId}`);
  }

  // Log transport upgrade
  socket.conn.on('upgrade', (transport) => {
    console.log(`🚀 Transport upgraded to: ${transport.name}`);
  });

  // Handle disconnect
  socket.on('disconnect', (reason) => {
    console.log(`❌ Client disconnected: ${socket.displayName} (${userId || 'unauthenticated'}), Reason: ${reason}`);
  });

  // Send current state to newly connected authenticated client
  if (userId) {
    database.getCounters(userId).then(data => {
      socket.emit('counterUpdate', data);
    }).catch(error => {
      console.error('❌ Error fetching counters for new connection:', error);
    });
  }

  // Handle client commands (only for authenticated users)
  socket.on('incrementDeaths', async () => {
    if (!userId) return socket.emit('error', { message: 'Authentication required' });
    try {
      const data = await database.incrementDeaths(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error incrementing deaths:', error);
      socket.emit('error', { message: 'Failed to increment deaths' });
    }
  });

  socket.on('decrementDeaths', async () => {
    if (!userId) return socket.emit('error', { message: 'Authentication required' });
    try {
      const data = await database.decrementDeaths(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error decrementing deaths:', error);
    }
  });

  socket.on('incrementSwears', async () => {
    if (!userId) return socket.emit('error', { message: 'Authentication required' });
    try {
      const data = await database.incrementSwears(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error incrementing swears:', error);
    }
  });

  socket.on('decrementSwears', async () => {
    if (!userId) return socket.emit('error', { message: 'Authentication required' });
    try {
      const data = await database.decrementSwears(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error decrementing swears:', error);
    }
  });

  socket.on('resetCounters', async () => {
    if (!userId) return socket.emit('error', { message: 'Authentication required' });
    try {
      const data = await database.resetCounters(userId);
      io.to(`user:${userId}`).emit('counterUpdate', data);
    } catch (error) {
      console.error('Error resetting counters:', error);
    }
  });

  // Connect user's Twitch bot when they connect
  socket.on('connectTwitch', async () => {
    if (!userId) return socket.emit('error', { message: 'Authentication required' });
    try {
      const success = await twitchService.connectUser(userId);
      socket.emit('twitchConnected', { success });
    } catch (error) {
      console.error('Error connecting Twitch:', error);
      socket.emit('twitchConnected', { success: false, error: error.message });
    }
  });

  // Allow overlay pages to join user room without authentication
  socket.on('joinRoom', (targetUserId) => {
    console.log(`🎨 Overlay joining room for user: ${targetUserId}`);
    socket.join(`user:${targetUserId}`);

    // Send current counter state to overlay
    database.getCounters(targetUserId).then(data => {
      socket.emit('counterUpdate', data);
      console.log(`📊 Sent initial counters to overlay for user ${targetUserId}:`, data);
    }).catch(error => {
      console.error('❌ Error fetching counters for overlay:', error);
    });
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

// Handle stream management commands
twitchService.on('startStream', async ({ userId, username }) => {
  try {
    const data = await database.startStream(userId);
    io.to(`user:${userId}`).emit('streamStarted', data);
    console.log(`🎬 Stream started by ${username}`);
  } catch (error) {
    console.error('Error handling stream start:', error);
  }
});

twitchService.on('endStream', async ({ userId, username }) => {
  try {
    const data = await database.endStream(userId);
    io.to(`user:${userId}`).emit('streamEnded', data);
    console.log(`🎬 Stream ended by ${username}`);
  } catch (error) {
    console.error('Error handling stream end:', error);
  }
});

twitchService.on('resetBits', async ({ userId, username }) => {
  try {
    const counters = await database.getCounters(userId);
    const updated = await database.saveCounters(userId, {
      ...counters,
      bits: 0
    });

    io.to(`user:${userId}`).emit('counterUpdate', {
      ...updated,
      change: { deaths: 0, swears: 0, bits: -counters.bits }
    });
    console.log(`💎 Bits counter reset by ${username}`);
  } catch (error) {
    console.error('Error handling bits reset:', error);
  }
});

// Handle bits events
twitchService.on('bitsReceived', async ({ userId, username, channel, amount, message, timestamp, thresholds }) => {
  try {
    console.log(`💎 Bits received: ${amount} from ${username} in ${channel}`);

    // Get updated counters including new bits
    const counters = await database.getCounters(userId);

    // Get custom alert configuration
    const alertConfig = await database.getAlertForEventType(userId, 'bits');

    // Broadcast to overlay and connected clients
    io.to(`user:${userId}`).emit('bitsReceived', {
      userId,
      username,
      amount,
      message,
      timestamp,
      thresholds,
      totalBits: counters.bits,
      alertConfig
    });

    // Trigger custom alert if enabled
    if (alertConfig) {
      io.to(`user:${userId}`).emit('customAlert', {
        type: 'bits',
        userId,
        username,
        data: { amount, message, totalBits: counters.bits },
        alertConfig,
        timestamp
      });
    }

    // Broadcast counter update with new bits total
    io.to(`user:${userId}`).emit('counterUpdate', {
      userId,
      counters: counters,
      change: { deaths: 0, swears: 0, bits: amount }
    });

    // Log to database if analytics feature is enabled
    const hasAnalytics = await database.hasFeature(userId, 'analytics');
    if (hasAnalytics) {
      console.log(`📊 Analytics: ${username} donated ${amount} bits (total: ${counters.bits})`);
    }

  } catch (error) {
    console.error('Error handling bits event:', error);
  }
});

// Handle subscriber events
twitchService.on('newSubscriber', async ({ userId, username, channel, tier, timestamp }) => {
  try {
    console.log(`🎉 New subscriber: ${username} (tier ${tier}) in ${channel}`);

    // Get custom alert configuration
    const alertConfig = await database.getAlertForEventType(userId, 'subscription');

    // Broadcast to overlay and connected clients
    io.to(`user:${userId}`).emit('newSubscriber', {
      userId,
      username,
      tier,
      timestamp,
      alertConfig
    });

    // Trigger custom alert if enabled
    if (alertConfig) {
      io.to(`user:${userId}`).emit('customAlert', {
        type: 'subscription',
        userId,
        username,
        data: { tier },
        alertConfig,
        timestamp
      });
    }

  } catch (error) {
    console.error('Error handling subscriber event:', error);
  }
});

// Handle resub events
twitchService.on('resub', async ({ userId, username, channel, months, message, tier, timestamp }) => {
  try {
    console.log(`🎉 Resub: ${username} (${months} months, tier ${tier}) in ${channel}`);

    // Get custom alert configuration
    const alertConfig = await database.getAlertForEventType(userId, 'resub');

    // Broadcast to overlay and connected clients
    io.to(`user:${userId}`).emit('resub', {
      userId,
      username,
      months,
      message,
      tier,
      timestamp,
      alertConfig
    });

    // Trigger custom alert if enabled
    if (alertConfig) {
      io.to(`user:${userId}`).emit('customAlert', {
        type: 'resub',
        userId,
        username,
        data: { months, message, tier },
        alertConfig,
        timestamp
      });
    }

  } catch (error) {
    console.error('Error handling resub event:', error);
  }
});

// Handle gift sub events
twitchService.on('giftSub', async ({ userId, gifter, recipient, channel, tier, timestamp }) => {
  try {
    console.log(`🎁 Gift sub: ${gifter} -> ${recipient} (tier ${tier}) in ${channel}`);

    // Get custom alert configuration
    const alertConfig = await database.getAlertForEventType(userId, 'giftsub');

    // Broadcast to overlay and connected clients
    io.to(`user:${userId}`).emit('giftSub', {
      userId,
      gifter,
      recipient,
      tier,
      timestamp,
      alertConfig
    });

    // Trigger custom alert if enabled
    if (alertConfig) {
      io.to(`user:${userId}`).emit('customAlert', {
        type: 'giftsub',
        userId,
        username: gifter,
        data: { recipient, tier },
        alertConfig,
        timestamp
      });
    }

  } catch (error) {
    console.error('Error handling gift sub event:', error);
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
    } else if (command === '!bits') {
      message = `💎 Current stream bits: ${counters.bits || 0}`;
    } else if (command === '!stats') {
      const total = counters.deaths + counters.swears;
      message = `📊 Stats - Deaths: ${counters.deaths} | Swears: ${counters.swears} | Total: ${total}`;
    } else if (command === '!streamstats') {
      // Get stream session info
      const streamSession = await database.getCurrentStreamSession(userId);
      if (streamSession) {
        const duration = Math.floor((Date.now() - new Date(streamSession.startTime)) / 1000 / 60); // minutes
        message = `🎮 Stream Stats - Duration: ${duration}min | Bits: ${counters.bits || 0} | Deaths: ${counters.deaths} | Swears: ${counters.swears}`;
      } else {
        message = `🎮 No active stream session. Use !startstream to begin!`;
      }
    }

    if (message) {
      await twitchService.sendMessage(userId, message);
    }
  } catch (error) {
    console.error('Error handling public command:', error);
  }
});

// Auto-start Twitch bots for users with chatCommands feature
async function autoStartTwitchBots() {
  try {
    console.log('🤖 Starting Twitch bots for users with chatCommands feature...');

    // Get all users from database
    const users = await database.getAllUsers();
    let botsStarted = 0;
    let botsSkipped = 0;

    for (const user of users) {
      try {
        // Parse features (could be string or object)
        const features = typeof user.features === 'string'
          ? JSON.parse(user.features)
          : user.features || {};

        // Check if user has chatCommands feature enabled
        if (features.chatCommands) {
          // Check if user has required auth tokens
          if (user.accessToken && user.refreshToken) {
            console.log(`🤖 Starting Twitch bot for ${user.username}...`);
            const success = await twitchService.connectUser(user.twitchUserId);

            if (success) {
              botsStarted++;
              console.log(`✅ Twitch bot started for ${user.username}`);
            } else {
              console.log(`❌ Failed to start Twitch bot for ${user.username}`);
            }
          } else {
            console.log(`⚠️  Skipping ${user.username} - missing auth tokens`);
            botsSkipped++;
          }
        } else {
          // Skip users without chatCommands feature
          botsSkipped++;
        }
      } catch (userError) {
        console.error(`❌ Error processing user ${user.username}:`, userError);
        botsSkipped++;
      }
    }

    console.log(`🤖 Twitch bots startup complete: ${botsStarted} started, ${botsSkipped} skipped`);

  } catch (error) {
    console.error('❌ Error auto-starting Twitch bots:', error);
  }
}

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

    // Auto-connect Twitch bots for users with chatCommands feature enabled
    await autoStartTwitchBots();

    // Initialize Stream Monitor
    const streamMonitorInitialized = await streamMonitor.initialize();
    if (streamMonitorInitialized) {
      // Subscribe to all active users for stream monitoring
      await streamMonitor.subscribeToAllUsers();

      // Handle stream events
      streamMonitor.on('streamOnline', async (data) => {
        console.log(`🔴 ${data.username} went LIVE! "${data.streamTitle}"`);
        io.to(`user:${data.userId}`).emit('streamOnline', data);
      });

      streamMonitor.on('streamOffline', async (data) => {
        console.log(`⚫ ${data.username} went OFFLINE`);

        try {
          // Automatically deactivate user when stream goes offline
          await database.updateUserStatus(data.userId, false);
          console.log(`🔄 Auto-deactivated ${data.username} after stream ended`);

          // Notify admin dashboard of user status change
          io.emit('userStatusChanged', {
            userId: data.userId,
            username: data.username,
            isActive: false,
            reason: 'Stream ended'
          });
        } catch (error) {
          console.error(`❌ Failed to auto-deactivate ${data.username}:`, error);
        }

        io.to(`user:${data.userId}`).emit('streamOffline', data);
      });

      // Handle channel point reward redemptions
      streamMonitor.on('rewardRedeemed', async (data) => {
        console.log(`🎯 ${data.rewardTitle} redeemed by ${data.redeemedBy} for ${data.username}`);
        io.to(`user:${data.userId}`).emit('rewardRedeemed', data);
      });

      // Handle counter updates from channel points
      streamMonitor.on('counterUpdate', async (data) => {
        if (data.source === 'channel_points') {
          console.log(`📊 Counter updated via channel points for user ${data.userId}`);
          io.to(`user:${data.userId}`).emit('counterUpdate', {
            counters: data.counters,
            change: data.counters.change || { deaths: 0, swears: 0, bits: 0 },
            source: 'channel_points',
            redeemedBy: data.redeemedBy
          });
        }
      });

      // Handle follow events
      streamMonitor.on('newFollower', async ({ userId, username, follower, timestamp }) => {
        try {
          console.log(`👥 New follower: ${follower} followed ${username}`);

          // Get custom alert configuration
          const alertConfig = await database.getAlertForEventType(userId, 'follow');

          // Broadcast to overlay and connected clients
          io.to(`user:${userId}`).emit('newFollower', {
            userId,
            username,
            follower,
            timestamp,
            alertConfig
          });

          // Trigger custom alert if enabled
          if (alertConfig) {
            io.to(`user:${userId}`).emit('customAlert', {
              type: 'follow',
              userId,
              username: follower,
              data: { follower },
              alertConfig,
              timestamp
            });
          }

        } catch (error) {
          console.error('Error handling follow event:', error);
        }
      });

      // Handle raid events
      streamMonitor.on('raidReceived', async ({ userId, username, raider, viewers, timestamp }) => {
        try {
          console.log(`🚨 Raid: ${raider} raided ${username} with ${viewers} viewers`);

          // Get custom alert configuration
          const alertConfig = await database.getAlertForEventType(userId, 'raid');

          // Broadcast to overlay and connected clients
          io.to(`user:${userId}`).emit('raidReceived', {
            userId,
            username,
            raider,
            viewers,
            timestamp,
            alertConfig
          });

          // Trigger custom alert if enabled
          if (alertConfig) {
            io.to(`user:${userId}`).emit('customAlert', {
              type: 'raid',
              userId,
              username: raider,
              data: { raider, viewers },
              alertConfig,
              timestamp
            });
          }

        } catch (error) {
          console.error('Error handling raid event:', error);
        }
      });
    }

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
      console.log(`║  Overlay:       http://localhost:${PORT}/overlay/{userId}`.padEnd(56) + '║');
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

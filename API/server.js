const express = require('express');
const http = require('http');
const socketIo = require('socket.io');
const cors = require('cors');
const cookieParser = require('cookie-parser');
require('dotenv').config();

const logger = require('./simpleLogger');
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
const debugRoutes = require('./debugRoutes');
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
  path: '/socket.io/',
  // Keepalive settings for stable connections
  pingTimeout: 60000,        // 60 seconds before considering connection dead
  pingInterval: 6000,        // 6 seconds between ping packets
  upgradeTimeout: 30000,     // 30 seconds for transport upgrade
  maxHttpBufferSize: 1e6,    // 1MB buffer size
  // Add security headers to Socket.IO responses
  allowRequest: (req, callback) => {
    // Socket.IO middleware - headers already set by Express middleware
    callback(null, true);
  }
});

// Configure Socket.IO engine to set proper content-type
io.engine.on('headers', (headers, req) => {
  headers['x-content-type-options'] = 'nosniff';
  headers['content-security-policy'] = "frame-ancestors 'none'";
  headers['referrer-policy'] = 'strict-origin-when-cross-origin';
  // Ensure charset is set for text responses
  if (headers['content-type'] && headers['content-type'].includes('text/')) {
    headers['content-type'] = headers['content-type'].replace(/; charset=.*$/, '') + '; charset=utf-8';
  }
});

// Add connection debugging
io.engine.on('connection_error', (err) => {
  console.log('❌ Socket.io connection error:', err.req);
  console.log('❌ Socket.io error details:', err.code, err.message, err.context);
});

// Make io, twitchService, and streamMonitor available to routes
app.set('io', io);
app.set('twitchService', twitchService);
app.set('streamMonitor', streamMonitor);

// Security headers middleware
app.use((req, res, next) => {
  // Remove X-Powered-By header (security best practice)
  res.removeHeader('X-Powered-By');

  // Add essential security headers
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin');

  // Use CSP with frame-ancestors instead of X-Frame-Options (modern approach)
  res.setHeader('Content-Security-Policy', "frame-ancestors 'none'");

  next();
});

// Disable X-Powered-By header globally
app.disable('x-powered-by');

// Middleware
app.use(cors({
  origin: process.env.CORS_ORIGIN || '*',
  credentials: true
}));
app.use(express.json({ type: 'application/json' }));
app.use(cookieParser());

// Add simple API request logging
app.use('/api', (req, res, next) => {
  const startTime = Date.now();

  // Log the request
  logger.apiRequest(req.method, req.path, req.user?.userId, {
    ip: req.ip,
    userAgent: req.get('User-Agent')
  });

  // Capture response details
  const originalSend = res.send;
  res.send = function(data) {
    const duration = Date.now() - startTime;
    logger.apiResponse(req.method, req.path, res.statusCode, duration, {
      userId: req.user?.userId
    });
    return originalSend.call(this, data);
  };

  next();
});

// Add cache-control headers to all API responses
app.use('/api', (req, res, next) => {
  // API responses should not be cached
  res.setHeader('Cache-Control', 'no-cache, private');
  // Set UTF-8 charset for JSON responses
  res.setHeader('Content-Type', 'application/json; charset=utf-8');
  next();
});

// Serve static frontend files with cache control
const path = require('path');
const frontendPath = process.env.NODE_ENV === 'production'
  ? path.join(__dirname, 'frontend')
  : path.join(__dirname, '..', 'modern-frontend', 'dist');

// Configure static file serving with cache control
app.use(express.static(frontendPath, {
  maxAge: process.env.NODE_ENV === 'production' ? '1h' : '0',
  etag: true,
  lastModified: true,
  setHeaders: (res, filePath) => {
    // Set cache control based on file type
    if (filePath.endsWith('.html')) {
      // HTML files - no cache (always fresh)
      res.setHeader('Cache-Control', 'no-cache');
    } else if (filePath.match(/\.(js|css|woff2?|ttf|eot|svg|png|jpg|jpeg|gif|ico)$/)) {
      // Static assets - cache for 1 year in production
      if (process.env.NODE_ENV === 'production') {
        res.setHeader('Cache-Control', 'public, max-age=31536000, immutable');
      } else {
        res.setHeader('Cache-Control', 'public, max-age=0');
      }
    } else {
      // Other files - cache for 1 hour in production
      res.setHeader('Cache-Control', process.env.NODE_ENV === 'production'
        ? 'public, max-age=3600'
        : 'no-cache');
    }
  }
}));

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

// Template management routes (requires authentication)
const templateRoutes = require('./templateRoutes');
app.use('/api/templates', templateRoutes);

// Custom counter management routes (requires authentication)
const customCounterRoutes = require('./customCounterRoutes');
app.use('/api/custom-counters', customCounterRoutes);

// Chat command management routes (requires authentication)
const chatCommandRoutes = require('./chatCommandRoutes');
app.use('/api/chat-commands', chatCommandRoutes);

// Debug routes (requires authentication)
app.use('/api/debug', debugRoutes);

// Logs routes (requires authentication)
const logsRoutes = require('./logsRoutes');
app.use('/api/logs', logsRoutes);

// Twitch status endpoint
app.get('/api/twitch/status', (req, res) => {
  const chatStatus = {
    initialized: true,
    connectedUsers: twitchService.getConnectedUsers().length
  };

  const eventSubStatus = streamMonitor ? streamMonitor.getConnectionStatus() : {
    totalConnections: 0,
    users: []
  };

  res.json({
    chat: chatStatus,
    eventsub: eventSubStatus,
    timestamp: new Date().toISOString()
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
    // Send counter state
    database.getCounters(userId).then(data => {
      socket.emit('counterUpdate', data);
    }).catch(error => {
      console.error('❌ Error fetching counters for new connection:', error);
    });

    // Send current stream status and features
    database.getUser(userId).then(user => {
      if (user) {
        const dbStreamStatus = user.streamStatus || 'offline';
        const features = typeof user.features === 'string' ? JSON.parse(user.features) : user.features || {};

        // Cross-check stream status with EventSub connection
        const streamMonitor = app.get('streamMonitor');
        const eventSubConnected = streamMonitor ? streamMonitor.isUserConnected(userId) : false;

        // Only trust "live" status if EventSub is actively monitoring
        let actualStreamStatus = dbStreamStatus;
        if (dbStreamStatus === 'live' && !eventSubConnected) {
          actualStreamStatus = 'offline';
          console.log(`⚠️ Stream status in DB is 'live' but EventSub not connected for ${user.username} - reporting as offline`);

          // Update DB to reflect actual status
          database.updateStreamStatus(userId, 'offline').catch(error => {
            console.error('❌ Failed to update stream status to offline:', error);
          });
        }

        socket.emit('streamStatusUpdate', {
          streamStatus: actualStreamStatus,
          isActive: user.isActive || false
        });

        socket.emit('userFeaturesUpdate', features);

        // Send EventSub connection status
        socket.emit('eventSubStatusChanged', {
          connected: eventSubConnected,
          monitoring: eventSubConnected,
          lastConnected: eventSubConnected ? new Date().toISOString() : null,
          subscriptionsEnabled: true
        });

        // If user is in streaming mode (prep or live), send active stream status
        if (actualStreamStatus === 'prepping') {
          socket.emit('prepModeActive', {
            userId: userId,
            username: user.username,
            displayName: user.displayName,
            streamStatus: 'prepping',
            eventListenersActive: eventSubConnected
          });

          console.log(`🎬 Client connected in prep mode: ${user.displayName}, EventSub monitoring: ${eventSubConnected}`);
        } else if (actualStreamStatus === 'live' && eventSubConnected) {
          socket.emit('streamModeActive', {
            userId: userId,
            username: user.username,
            displayName: user.displayName,
            streamStatus: 'live',
            eventListenersActive: true
          });

          console.log(`🔴 Client connected in live mode: ${user.displayName}, EventSub monitoring active`);
        }
      }
    }).catch(error => {
      console.error('❌ Error fetching user data for new connection:', error);
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
      socket.emit('twitchConnected', { success: false, error: error?.message });
    }
  });

  // Allow overlay pages to join user room without authentication
  socket.on('joinRoom', (targetUserId) => {
    console.log(`🎨 Overlay joining room for user: ${targetUserId}`);
    socket.join(`user:${targetUserId}`);

    // Log room membership for debugging
    const roomName = `user:${targetUserId}`;
    const roomSize = io.sockets.adapter.rooms.get(roomName)?.size || 0;
    console.log(`👥 Room '${roomName}' now has ${roomSize} clients`);

    // Send current counter state to overlay
    database.getCounters(targetUserId).then(data => {
      socket.emit('counterUpdate', data);
      console.log(`📊 Sent initial counters to overlay for user ${targetUserId}:`, data);
    }).catch(error => {
      console.error('❌ Error fetching counters for overlay:', error);
    });
  });

  // Handle stream status requests from overlay
  socket.on('getStreamStatus', async (targetUserId) => {
    try {
      console.log(`📡 Getting stream status for user: ${targetUserId}`);
      const user = await database.getUser(targetUserId);
      const streamStatus = user ? user.streamStatus || 'offline' : 'offline';

      socket.emit('streamStatus', {
        userId: targetUserId,
        streamStatus: streamStatus
      });

      console.log(`📊 Sent stream status to overlay: ${streamStatus}`);
    } catch (error) {
      console.error('❌ Error fetching stream status for overlay:', error);
      socket.emit('streamStatus', {
        userId: targetUserId,
        streamStatus: 'offline'
      });
    }
  });

  // Handle heartbeat/ping for any connections
  socket.on('ping', (callback) => {
    if (typeof callback === 'function') {
      callback('pong');
    }
  });

  socket.on('streamModeHeartbeat', async () => {
    if (!userId) return;

    try {
      const user = await database.getUser(userId);
      if (user && (user.streamStatus === 'prepping' || user.streamStatus === 'live')) {
        socket.emit('streamModeStatus', {
          active: true,
          streamStatus: user.streamStatus,
          eventListenersConnected: streamMonitor && streamMonitor.isUserSubscribed ? streamMonitor.isUserSubscribed(userId) : false,
          timestamp: new Date().toISOString()
        });
      }
    } catch (error) {
      console.error('❌ Error handling stream mode heartbeat:', error);
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

// ==================== SERIES SAVE STATE EVENTS ====================

twitchService.on('saveSeries', async ({ userId, username, seriesName }) => {
  try {
    const saveData = await database.saveSeries(userId, seriesName);

    // Send confirmation message to chat
    await twitchService.sendMessage(
      userId,
      `💾 Series saved: "${seriesName}" (Deaths: ${saveData.deaths}, Swears: ${saveData.swears})`
    );

    console.log(`💾 Series saved by ${username}: "${seriesName}"`);
  } catch (error) {
    console.error('Error handling series save:', error);
    await twitchService.sendMessage(
      userId,
      '❌ Failed to save series. Please try again.'
    );
  }
});

twitchService.on('loadSeries', async ({ userId, username, seriesId }) => {
  try {
    const loadedData = await database.loadSeries(userId, seriesId);

    // Broadcast counter update to all connected devices
    io.to(`user:${userId}`).emit('counterUpdate', {
      deaths: loadedData.deaths,
      swears: loadedData.swears,
      bits: loadedData.bits,
      lastUpdated: loadedData.lastUpdated,
      streamStarted: loadedData.streamStarted,
      change: { deaths: 0, swears: 0, bits: 0 }
    });

    // Send confirmation message to chat
    await twitchService.sendMessage(
      userId,
      `📂 Series loaded: "${loadedData.seriesName}" (Deaths: ${loadedData.deaths}, Swears: ${loadedData.swears})`
    );

    console.log(`📂 Series loaded by ${username}: "${loadedData.seriesName}"`);
  } catch (error) {
    console.error('Error handling series load:', error);
    if (error?.message === 'Series save not found') {
      await twitchService.sendMessage(
        userId,
        '❌ Series save not found. Use !listseries to see available saves.'
      );
    } else {
      await twitchService.sendMessage(
        userId,
        '❌ Failed to load series. Please try again.'
      );
    }
  }
});

twitchService.on('listSeries', async ({ userId, username, channel }) => {
  try {
    const saves = await database.listSeriesSaves(userId);

    if (saves.length === 0) {
      await twitchService.sendMessage(
        userId,
        'No series saves found. Use !saveseries [name] to create one.'
      );
      return;
    }

    // Show the 3 most recent saves
    const recentSaves = saves.slice(0, 3);
    const savesList = recentSaves.map((save, index) => {
      const date = new Date(save.savedAt).toLocaleDateString();
      return `${index + 1}. "${save.seriesName}" (${date}) - ID: ${save.seriesId}`;
    }).join(' | ');

    await twitchService.sendMessage(
      userId,
      `📋 Recent saves: ${savesList} | Total: ${saves.length}`
    );

    console.log(`📋 Series list requested by ${username}`);
  } catch (error) {
    console.error('Error handling series list:', error);
    await twitchService.sendMessage(
      userId,
      '❌ Failed to list series saves.'
    );
  }
});

twitchService.on('deleteSeries', async ({ userId, username, seriesId }) => {
  try {
    await database.deleteSeries(userId, seriesId);

    // Send confirmation message to chat
    await twitchService.sendMessage(
      userId,
      `🗑️  Series save deleted: ${seriesId}`
    );

    console.log(`🗑️  Series deleted by ${username}: ${seriesId}`);
  } catch (error) {
    console.error('Error handling series delete:', error);
    if (error?.message === 'Series save not found') {
      await twitchService.sendMessage(
        userId,
        '❌ Series save not found.'
      );
    } else {
      await twitchService.sendMessage(
        userId,
        '❌ Failed to delete series save.'
      );
    }
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

    // Initialize Stream Monitor (but don't auto-subscribe to users)
    const streamMonitorInitialized = await streamMonitor.initialize();
    if (streamMonitorInitialized) {
      // Pass Socket.io instance for real-time notification status updates
      streamMonitor.setSocketIo(io);
      console.log('✅ Stream Monitor ready - users can start monitoring manually');

      // Handle stream events
      streamMonitor.on('streamOnline', async (data) => {
        console.log(`🔴 ${data.username} went LIVE! "${data.streamTitle}"`);
        io.to(`user:${data.userId}`).emit('streamOnline', {
          ...data,
          timestamp: new Date().toISOString()
        });

        // Broadcast EventSub status change
        io.to(`user:${data.userId}`).emit('eventSubStatusChanged', {
          connected: true,
          monitoring: true,
          lastConnected: new Date().toISOString(),
          subscriptionsEnabled: true,
          lastStreamStart: new Date().toISOString(),
          streamStatus: 'live'  // Current stream status
        });
      });

      streamMonitor.on('streamOffline', async (data) => {
        console.log(`⚫ ${data.username} went OFFLINE`);

        // Keep monitoring active - DO NOT auto-deactivate user
        // Only update the stream status in real-time UI
        console.log(`� User ${data.username} remains active - monitoring continues`);

        // Broadcast offline status but keep monitoring active
        io.to(`user:${data.userId}`).emit('streamOffline', {
          ...data,
          timestamp: new Date().toISOString(),
          monitoringActive: true  // Monitoring stays active
        });

        // Broadcast EventSub status change (monitoring still active)
        io.to(`user:${data.userId}`).emit('eventSubStatusChanged', {
          connected: true,
          monitoring: true,  // Keep monitoring active
          lastConnected: new Date().toISOString(),
          subscriptionsEnabled: true,
          lastStreamEnd: new Date().toISOString(),
          streamStatus: 'offline'  // Current stream status
        });
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

      // Handle EventSub connection events
      streamMonitor.on('userConnected', (data) => {
        console.log(`🔗 EventSub connected for ${data.username}`);
        io.to(`user:${data.userId}`).emit('eventSubConnected', {
          status: 'connected',
          timestamp: new Date().toISOString()
        });
      });

      streamMonitor.on('userDisconnected', (data) => {
        console.log(`🔌 EventSub disconnected for ${data.username}:`, data.reason || 'Unknown reason');
        io.to(`user:${data.userId}`).emit('eventSubDisconnected', {
          status: 'disconnected',
          manual: data.manual,
          reason: data.reason,
          timestamp: new Date().toISOString()
        });
      });

      streamMonitor.on('authRevoked', (data) => {
        console.log(`🔐 EventSub auth revoked for ${data.username} - user needs to re-authenticate`);
        io.to(`user:${data.userId}`).emit('authRevoked', {
          message: 'Your Twitch authorization has been revoked. Please re-authenticate to continue receiving stream notifications.',
          timestamp: new Date().toISOString()
        });
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

      // Handle new subscription events
      streamMonitor.on('newSubscription', async ({ userId, username, subscriber, tier, isGift, timestamp, alert }) => {
        try {
          console.log(`⭐ New subscription: ${subscriber} subscribed to ${username} (Tier ${tier})`);

          // Broadcast to overlay and connected clients
          io.to(`user:${userId}`).emit('newSubscription', {
            userId,
            username,
            subscriber,
            tier,
            isGift,
            timestamp,
            alertConfig: alert
          });

          // Trigger custom alert if enabled
          if (alert) {
            io.to(`user:${userId}`).emit('customAlert', {
              type: 'subscription',
              userId,
              username: subscriber,
              data: { subscriber, tier, isGift },
              alertConfig: alert,
              timestamp
            });
          }

        } catch (error) {
          console.error('Error handling subscription event:', error);
        }
      });

      // Handle gift sub events
      streamMonitor.on('newGiftSub', async ({ userId, username, gifter, amount, tier, timestamp, alert }) => {
        try {
          console.log(`🎁 Gift subs: ${gifter} gifted ${amount} subs to ${username} (Tier ${tier})`);

          // Broadcast to overlay and connected clients
          io.to(`user:${userId}`).emit('newGiftSub', {
            userId,
            username,
            gifter,
            amount,
            tier,
            timestamp,
            alertConfig: alert
          });

          // Trigger custom alert if enabled
          if (alert) {
            io.to(`user:${userId}`).emit('customAlert', {
              type: 'giftsub',
              userId,
              username: gifter,
              data: { gifter, amount, tier },
              alertConfig: alert,
              timestamp
            });
          }

        } catch (error) {
          console.error('Error handling gift sub event:', error);
        }
      });

      // Handle resub events
      streamMonitor.on('newResub', async ({ userId, username, subscriber, tier, months, streakMonths, message, timestamp, alert }) => {
        try {
          console.log(`🔄 Resub: ${subscriber} resubscribed to ${username} (${months} months)`);

          // Broadcast to overlay and connected clients
          io.to(`user:${userId}`).emit('newResub', {
            userId,
            username,
            subscriber,
            tier,
            months,
            streakMonths,
            message,
            timestamp,
            alertConfig: alert
          });

          // Trigger custom alert if enabled
          if (alert) {
            io.to(`user:${userId}`).emit('customAlert', {
              type: 'resub',
              userId,
              username: subscriber,
              data: { subscriber, tier, months, streakMonths, message },
              alertConfig: alert,
              timestamp
            });
          }

        } catch (error) {
          console.error('Error handling resub event:', error);
        }
      });

      // Handle cheer/bits events
      streamMonitor.on('newCheer', async ({ userId, username, cheerer, bits, message, isAnonymous, timestamp, alert }) => {
        try {
          console.log(`💎 Cheer: ${cheerer} cheered ${bits} bits to ${username}`);

          // Broadcast to overlay and connected clients
          io.to(`user:${userId}`).emit('newCheer', {
            userId,
            username,
            cheerer,
            bits,
            message,
            isAnonymous,
            timestamp,
            alertConfig: alert
          });

          // Trigger custom alert if enabled
          if (alert) {
            io.to(`user:${userId}`).emit('customAlert', {
              type: 'bits',
              userId,
              username: cheerer,
              data: { cheerer, bits, message, isAnonymous },
              alertConfig: alert,
              timestamp
            });
          }

        } catch (error) {
          console.error('Error handling cheer event:', error);
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

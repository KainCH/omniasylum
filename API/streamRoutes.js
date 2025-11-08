const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

const router = express.Router();

/**
 * Update stream status (Phase 1)
 * POST /api/stream/status
 * Actions: prep, go-live, end-stream, cancel-prep
 */
router.post('/status', requireAuth, async (req, res) => {
  try {
    const { action } = req.body;
    const userId = req.user.userId; // Changed from twitchUserId to userId

    console.log(`🎬 Stream status update request: ${action} for user ${req.user.username}`);

    // Get current user data
    const user = await database.getUser(userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    const currentStatus = user.streamStatus || 'offline';
    let newStatus = currentStatus;

    // State machine for stream status transitions
    switch (action) {
      case 'prep':
        if (currentStatus === 'offline') {
          newStatus = 'prepping';
        }
        break;
      case 'go-live':
        if (currentStatus === 'prepping') {
          newStatus = 'live';
          // Start stream tracking
          await database.startStream(userId);
        }
        break;
      case 'end-stream':
        if (currentStatus === 'live' || currentStatus === 'ending') {
          newStatus = 'offline';
          // End stream tracking
          await database.endStream(userId);
        }
        break;
      case 'cancel-prep':
        if (currentStatus === 'prepping') {
          newStatus = 'offline';
        }
        break;
      default:
        return res.status(400).json({ error: 'Invalid action' });
    }

    // Update user's stream status
    await database.updateStreamStatus(userId, newStatus);

    console.log(`✅ Stream status updated: ${currentStatus} → ${newStatus}`);

    // Broadcast to connected clients
    const io = req.app.get('io');
    io.to(`user:${userId}`).emit('streamStatusUpdate', {
      userId,
      streamStatus: newStatus
    });

    res.json({
      message: `Stream status updated to ${newStatus}`,
      streamStatus: newStatus,
      previousStatus: currentStatus
    });
  } catch (error) {
    console.error('❌ Error updating stream status:', error);
    res.status(500).json({ error: 'Failed to update stream status' });
  }
});

/**
 * Get current stream session info
 * GET /api/stream/session
 */
router.get('/session', requireAuth, async (req, res) => {
  try {
    const counters = await database.getCounters(req.user.twitchUserId);
    const settings = await database.getStreamSettings(req.user.twitchUserId);

    res.json({
      isLive: !!counters?.streamStarted,
      streamStarted: counters?.streamStarted,
      counters: {
        deaths: counters?.deaths || 0,
        swears: counters?.swears || 0,
        bits: counters?.bits || 0
      },
      settings: settings
    });
  } catch (error) {
    console.error('Error getting stream session:', error);
    res.status(500).json({ error: 'Failed to get stream session' });
  }
});

/**
 * Start stream session
 * POST /api/stream/start
 */
router.post('/start', requireAuth, async (req, res) => {
  try {
    const counters = await database.startStream(req.user.twitchUserId);

    // Broadcast to connected clients
    const io = req.app.get('io');
    io.to(`user:${req.user.twitchUserId}`).emit('streamStarted', {
      userId: req.user.twitchUserId,
      streamStarted: counters?.streamStarted,
      counters: counters
    });

    res.json({
      message: 'Stream started successfully',
      streamStarted: counters?.streamStarted,
      counters: counters
    });
  } catch (error) {
    console.error('Error starting stream:', error);
    res.status(500).json({ error: 'Failed to start stream' });
  }
});

/**
 * End stream session
 * POST /api/stream/end
 */
router.post('/end', requireAuth, async (req, res) => {
  try {
    const counters = await database.endStream(req.user.twitchUserId);

    // Broadcast to connected clients
    const io = req.app.get('io');
    io.to(`user:${req.user.twitchUserId}`).emit('streamEnded', {
      userId: req.user.twitchUserId,
      counters: counters
    });

    res.json({
      message: 'Stream ended successfully',
      counters: counters
    });
  } catch (error) {
    console.error('Error ending stream:', error);
    res.status(500).json({ error: 'Failed to end stream' });
  }
});

/**
 * Get stream settings
 * GET /api/stream/settings
 */
router.get('/settings', requireAuth, async (req, res) => {
  try {
    const settings = await database.getStreamSettings(req.user.twitchUserId);
    res.json(settings);
  } catch (error) {
    console.error('Error getting stream settings:', error);
    res.status(500).json({ error: 'Failed to get stream settings' });
  }
});

/**
 * Update stream settings
 * PUT /api/stream/settings
 */
router.put('/settings', requireAuth, async (req, res) => {
  try {
    const { bitThresholds, autoStartStream, resetOnStreamStart, autoIncrementCounters } = req.body;

    // Validate bit thresholds
    if (bitThresholds) {
      if (typeof bitThresholds.death !== 'number' || bitThresholds.death < 1) {
        return res.status(400).json({ error: 'Death threshold must be a positive number' });
      }
      if (typeof bitThresholds.swear !== 'number' || bitThresholds.swear < 1) {
        return res.status(400).json({ error: 'Swear threshold must be a positive number' });
      }
      if (typeof bitThresholds.celebration !== 'number' || bitThresholds.celebration < 1) {
        return res.status(400).json({ error: 'Celebration threshold must be a positive number' });
      }
    }

    const settings = {
      bitThresholds: bitThresholds || { death: 100, swear: 50, celebration: 10 },
      autoStartStream: !!autoStartStream,
      resetOnStreamStart: resetOnStreamStart !== false, // default true
      autoIncrementCounters: !!autoIncrementCounters
    };

    await database.updateStreamSettings(req.user.twitchUserId, settings);

    res.json({
      message: 'Stream settings updated successfully',
      settings: settings
    });
  } catch (error) {
    console.error('Error updating stream settings:', error);
    res.status(500).json({ error: 'Failed to update stream settings' });
  }
});

/**
 * Reset bits counter manually
 * POST /api/stream/reset-bits
 */
router.post('/reset-bits', requireAuth, async (req, res) => {
  try {
    const counters = await database.getCounters(req.user.twitchUserId);
    const updated = await database.saveCounters(req.user.twitchUserId, {
      ...counters,
      bits: 0
    });

    // Broadcast to connected clients
    const io = req.app.get('io');
    io.to(`user:${req.user.twitchUserId}`).emit('counterUpdate', {
      ...updated,
      change: { deaths: 0, swears: 0, bits: -(counters?.bits || 0) }
    });

    res.json({
      message: 'Bits counter reset successfully',
      counters: updated
    });
  } catch (error) {
    console.error('Error resetting bits:', error);
    res.status(500).json({ error: 'Failed to reset bits counter' });
  }
});

/**
 * Get stream monitoring status
 * GET /api/stream/monitor/status
 */
router.get('/monitor/status', requireAuth, async (req, res) => {
  try {
    const streamMonitor = require('./streamMonitor');
    const status = streamMonitor.getStatus();

    // Check if current user is being monitored
    const userMonitored = status?.monitoredUsers?.some(u => u?.userId === req.user.twitchUserId) || false;

    res.json({
      ...status,
      currentUserMonitored: userMonitored
    });
  } catch (error) {
    console.error('Error getting stream monitor status:', error);
    res.status(500).json({ error: 'Failed to get stream monitor status' });
  }
});

/**
 * Subscribe to stream monitoring for current user
 * POST /api/stream/monitor/subscribe
 */
router.post('/monitor/subscribe', requireAuth, async (req, res) => {
  try {
    const streamMonitor = require('./streamMonitor');
    const success = await streamMonitor.subscribeToUser(req.user.twitchUserId);

    if (success) {
      res.json({
        message: 'Successfully subscribed to stream monitoring',
        userId: req.user.twitchUserId
      });
    } else {
      res.status(400).json({ error: 'Failed to subscribe to stream monitoring' });
    }
  } catch (error) {
    console.error('Error subscribing to stream monitoring:', error);
    res.status(500).json({ error: 'Failed to subscribe to stream monitoring' });
  }
});

/**
 * Unsubscribe from stream monitoring for current user
 * POST /api/stream/monitor/unsubscribe
 */
router.post('/monitor/unsubscribe', requireAuth, async (req, res) => {
  try {
    const streamMonitor = require('./streamMonitor');
    await streamMonitor.unsubscribeFromUser(req.user.twitchUserId);

    res.json({
      message: 'Successfully unsubscribed from stream monitoring',
      userId: req.user.twitchUserId
    });
  } catch (error) {
    console.error('Error unsubscribing from stream monitoring:', error);
    res.status(500).json({ error: 'Failed to unsubscribe from stream monitoring' });
  }
});

// ==================== PHASE 1: Enhanced Stream Status Management ====================

/**
 * Get current stream status
 * GET /api/stream/status
 */
router.get('/status', requireAuth, async (req, res) => {
  try {
    const user = await database.getUser(req.user.twitchUserId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Get current counters for context
    const counters = await database.getCounters(req.user.twitchUserId);

    res.json({
      userId: req.user.twitchUserId,
      username: user.username,
      displayName: user.displayName,
      streamStatus: user.streamStatus || 'offline',
      isActive: user.isActive !== false, // Backward compatibility
      counters: counters,
      lastUpdated: new Date().toISOString()
    });
  } catch (error) {
    console.error('❌ Error getting stream status:', error);
    res.status(500).json({ error: 'Failed to get stream status' });
  }
});

/**
 * Start prepping for stream
 * POST /api/stream/prep
 */
router.post('/prep', requireAuth, async (req, res) => {
  try {
    const user = await database.getUser(req.user.twitchUserId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Update status to prepping
    const updatedUser = await database.updateStreamStatus(req.user.twitchUserId, 'prepping');

    // Start Twitch bot if chat commands are enabled
    const features = typeof user.features === 'string' ? JSON.parse(user.features) : user.features || {};
    if (features.chatCommands) {
      const twitchService = req.app.get('twitchService');
      if (twitchService) {
        try {
          await twitchService.connectUser(req.user.twitchUserId);
          console.log(`🤖 Started Twitch bot for ${user.displayName} (prepping mode)`);
        } catch (error) {
          console.error(`❌ Failed to start Twitch bot for ${user.displayName}:`, error);
        }
      }
    }

    // Start EventSub stream monitoring when entering prep mode
    const streamMonitor = req.app.get('streamMonitor');
    if (streamMonitor) {
      try {
        const subscribed = await streamMonitor.subscribeToUser(req.user.twitchUserId);
        if (subscribed) {
          console.log(`🎬 Started EventSub monitoring for ${user.displayName} (prepping mode)`);
        } else {
          console.warn(`⚠️ Failed to start EventSub monitoring for ${user.displayName}`);
        }
      } catch (error) {
        console.error(`❌ Failed to start EventSub monitoring for ${user.displayName}:`, error);
      }
    }

    // Broadcast status change to connected clients
    const io = req.app.get('io');
    io.to(`user:${req.user.twitchUserId}`).emit('streamStatusChanged', {
      userId: req.user.twitchUserId,
      username: user.username,
      streamStatus: 'prepping',
      isActive: true,
      reason: 'User started prepping'
    });

    console.log(`🎬 ${user.displayName} started prepping for stream`);

    res.json({
      message: 'Stream prep started successfully',
      streamStatus: 'prepping',
      isActive: true,
      user: {
        userId: updatedUser?.twitchUserId,
        username: updatedUser?.username,
        displayName: updatedUser?.displayName
      }
    });
  } catch (error) {
    console.error('❌ Error starting stream prep:', error);
    res.status(500).json({ error: error?.message || 'Failed to start stream prep' });
  }
});

/**
 * Go live (from prepping to live)
 * POST /api/stream/go-live
 */
router.post('/go-live', requireAuth, async (req, res) => {
  try {
    const user = await database.getUser(req.user.twitchUserId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Validate current status
    const currentStatus = user.streamStatus || 'offline';
    if (currentStatus !== 'prepping') {
      return res.status(400).json({
        error: `Cannot go live from ${currentStatus} status. Must be prepping first.`,
        currentStatus: currentStatus
      });
    }

    // Update status to live
    const updatedUser = await database.updateStreamStatus(req.user.twitchUserId, 'live');

    // Update stream start time in counters
    await database.startStream(req.user.twitchUserId);

    // Broadcast status change to connected clients
    const io = req.app.get('io');
    io.to(`user:${req.user.twitchUserId}`).emit('streamStatusChanged', {
      userId: req.user.twitchUserId,
      username: user.username,
      streamStatus: 'live',
      isActive: true,
      reason: 'User went live'
    });

    // Also broadcast old streamStarted event for backward compatibility
    const counters = await database.getCounters(req.user.twitchUserId);
    io.to(`user:${req.user.twitchUserId}`).emit('streamStarted', {
      userId: req.user.twitchUserId,
      streamStarted: counters.streamStarted,
      counters: counters
    });

    console.log(`🔴 ${user.displayName} went LIVE!`);

    res.json({
      message: 'Stream went live successfully',
      streamStatus: 'live',
      isActive: true,
      streamStarted: counters?.streamStarted,
      user: {
        userId: updatedUser?.twitchUserId,
        username: updatedUser?.username,
        displayName: updatedUser?.displayName
      }
    });
  } catch (error) {
    console.error('❌ Error going live:', error);
    res.status(500).json({ error: error?.message || 'Failed to go live' });
  }
});

/**
 * End stream
 * POST /api/stream/end-stream
 */
router.post('/end-stream', requireAuth, async (req, res) => {
  try {
    const user = await database.getUser(req.user.twitchUserId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Validate current status
    const currentStatus = user.streamStatus || 'offline';
    if (!['prepping', 'live'].includes(currentStatus)) {
      return res.status(400).json({
        error: `Cannot end stream from ${currentStatus} status.`,
        currentStatus: currentStatus
      });
    }

    // Update status to offline
    const updatedUser = await database.updateStreamStatus(req.user.twitchUserId, 'offline');

    // End stream session if it was live
    if (currentStatus === 'live') {
      await database.endStream(req.user.twitchUserId);
    }

    // Stop Twitch bot
    const twitchService = req.app.get('twitchService');
    if (twitchService) {
      try {
        await twitchService.disconnectUser(req.user.twitchUserId);
        console.log(`🤖 Stopped Twitch bot for ${user.displayName}`);
      } catch (error) {
        console.error(`❌ Failed to stop Twitch bot for ${user.displayName}:`, error);
      }
    }

    // Stop EventSub stream monitoring when ending stream
    const streamMonitor = req.app.get('streamMonitor');
    if (streamMonitor) {
      try {
        await streamMonitor.unsubscribeFromUser(req.user.twitchUserId);
        console.log(`🎬 Stopped EventSub monitoring for ${user.displayName} (ended stream)`);
      } catch (error) {
        console.error(`❌ Failed to stop EventSub monitoring for ${user.displayName}:`, error);
      }
    }

    // Broadcast status change to connected clients
    const io = req.app.get('io');
    io.to(`user:${req.user.twitchUserId}`).emit('streamStatusChanged', {
      userId: req.user.twitchUserId,
      username: user.username,
      streamStatus: 'offline',
      isActive: false,
      reason: 'User ended stream'
    });

    // Also broadcast old streamEnded event for backward compatibility
    if (currentStatus === 'live') {
      const counters = await database.getCounters(req.user.twitchUserId);
      io.to(`user:${req.user.twitchUserId}`).emit('streamEnded', {
        userId: req.user.twitchUserId,
        streamEnded: new Date().toISOString(),
        counters: counters
      });
    }

    console.log(`⏹️ ${user.displayName} ended stream (was ${currentStatus})`);

    res.json({
      message: 'Stream ended successfully',
      streamStatus: 'offline',
      isActive: false,
      user: {
        userId: updatedUser.twitchUserId,
        username: updatedUser.username,
        displayName: updatedUser.displayName
      }
    });
  } catch (error) {
    console.error('❌ Error ending stream:', error);
    res.status(500).json({ error: error?.message || 'Failed to end stream' });
  }
});

/**
 * Cancel prepping (go back offline)
 * POST /api/stream/cancel-prep
 */
router.post('/cancel-prep', requireAuth, async (req, res) => {
  try {
    const user = await database.getUser(req.user.twitchUserId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Validate current status
    const currentStatus = user.streamStatus || 'offline';
    if (currentStatus !== 'prepping') {
      return res.status(400).json({
        error: `Cannot cancel prep from ${currentStatus} status. Must be prepping.`,
        currentStatus: currentStatus
      });
    }

    // Update status to offline
    const updatedUser = await database.updateStreamStatus(req.user.twitchUserId, 'offline');

    // Stop Twitch bot
    const twitchService = req.app.get('twitchService');
    if (twitchService) {
      try {
        await twitchService.disconnectUser(req.user.twitchUserId);
        console.log(`🤖 Stopped Twitch bot for ${user.displayName} (cancelled prep)`);
      } catch (error) {
        console.error(`❌ Failed to stop Twitch bot for ${user.displayName}:`, error);
      }
    }

    // Stop EventSub stream monitoring when cancelling prep
    const streamMonitor = req.app.get('streamMonitor');
    if (streamMonitor) {
      try {
        await streamMonitor.unsubscribeFromUser(req.user.twitchUserId);
        console.log(`🎬 Stopped EventSub monitoring for ${user.displayName} (cancelled prep)`);
      } catch (error) {
        console.error(`❌ Failed to stop EventSub monitoring for ${user.displayName}:`, error);
      }
    }

    // Broadcast status change to connected clients
    const io = req.app.get('io');
    io.to(`user:${req.user.twitchUserId}`).emit('streamStatusChanged', {
      userId: req.user.twitchUserId,
      username: user.username,
      streamStatus: 'offline',
      isActive: false,
      reason: 'User cancelled prep'
    });

    console.log(`❌ ${user.displayName} cancelled stream prep`);

    res.json({
      message: 'Stream prep cancelled successfully',
      streamStatus: 'offline',
      isActive: false,
      user: {
        userId: updatedUser.twitchUserId,
        username: updatedUser.username,
        displayName: updatedUser.displayName
      }
    });
  } catch (error) {
    console.error('❌ Error cancelling stream prep:', error);
    res.status(500).json({ error: error?.message || 'Failed to cancel stream prep' });
  }
});

module.exports = router;

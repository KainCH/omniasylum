const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

const router = express.Router();

/**
 * Get current stream session info
 * GET /api/stream/session
 */
router.get('/session', requireAuth, async (req, res) => {
  try {
    const counters = await database.getCounters(req.user.twitchUserId);
    const settings = await database.getStreamSettings(req.user.twitchUserId);

    res.json({
      isLive: !!counters.streamStarted,
      streamStarted: counters.streamStarted,
      counters: {
        deaths: counters.deaths,
        swears: counters.swears,
        bits: counters.bits
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
      streamStarted: counters.streamStarted,
      counters: counters
    });

    res.json({
      message: 'Stream started successfully',
      streamStarted: counters.streamStarted,
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
      change: { deaths: 0, swears: 0, bits: -counters.bits }
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
    const userMonitored = status.monitoredUsers.some(u => u.userId === req.user.twitchUserId);

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

module.exports = router;

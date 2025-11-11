const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');
const { sendDiscordNotification } = require('./userRoutes');

const router = express.Router();

/**
 * Check for milestones and send notifications (Discord and/or Channel)
 */
async function checkMilestones(userId, counterType, previousValue, newValue, req) {
  try {
    // Get user data for notification settings
    const user = await database.getUser(userId);
    if (!user) {
      return; // No user found
    }

    // Parse notification settings to get milestone thresholds and preferences
    let notificationSettings = {
      milestoneThresholds: {
        deaths: [10, 25, 50, 100, 250, 500, 1000],
        swears: [25, 50, 100, 200, 500, 1000],
        screams: [10, 25, 50, 100, 200, 500, 1000]
      },
      discordNotifications: {
        death_milestone: true,
        swear_milestone: true,
        scream_milestone: true
      },
      channelNotifications: {
        death_milestone: true,
        swear_milestone: true,
        scream_milestone: true
      }
    };

    if (user.discordSettings) {
      try {
        const parsed = JSON.parse(user.discordSettings);
        notificationSettings = { ...notificationSettings, ...parsed };
      } catch (error) {
        console.log('⚠️ Failed to parse notification settings for milestone check');
      }
    }

    // Determine event type
    const eventType = counterType === 'deaths' ? 'death_milestone' :
                     counterType === 'swears' ? 'swear_milestone' :
                     counterType === 'screams' ? 'scream_milestone' : null;

    // Check if any notification type is enabled
    const discordEnabled = user.discordWebhookUrl && notificationSettings.discordNotifications && notificationSettings.discordNotifications[eventType];
    const channelEnabled = notificationSettings.channelNotifications && notificationSettings.channelNotifications[eventType];

    if (!discordEnabled && !channelEnabled) {
      console.log(`📵 All ${eventType} notifications disabled for user ${user.username}`);
      return;
    }

    // Get the relevant thresholds
    const thresholds = counterType === 'deaths'
      ? notificationSettings.milestoneThresholds.deaths
      : counterType === 'swears'
      ? notificationSettings.milestoneThresholds.swears
      : counterType === 'screams'
      ? notificationSettings.milestoneThresholds.screams
      : null;

    if (!thresholds || !Array.isArray(thresholds)) {
      return;
    }

    // Check if we crossed any milestone thresholds
    const crossedMilestones = thresholds.filter(threshold =>
      previousValue < threshold && newValue >= threshold
    );

    // Send notifications for each crossed milestone
    for (const milestone of crossedMilestones) {
      // Find previous milestone for progress display
      const previousMilestone = thresholds
        .filter(t => t < milestone)
        .sort((a, b) => b - a)[0] || 0;

      console.log(`🎯 Milestone reached: ${counterType} ${milestone} for user ${user.username}`);

      // Send Discord notification if enabled
      if (discordEnabled) {
        await sendDiscordNotification(user, eventType, {
          count: milestone,
          actualCount: newValue,
          previousMilestone: previousMilestone,
          fields: [
            {
              name: '🎯 Milestone',
              value: `${milestone}`,
              inline: true
            },
            {
              name: '📊 Current Count',
              value: `${newValue}`,
              inline: true
            },
            {
              name: '📈 Progress',
              value: `${previousMilestone} → ${milestone}`,
              inline: true
            }
          ]
        });
      }

      // Send channel (Twitch chat) notification if enabled
      if (channelEnabled) {
        const twitchService = req.app.get('twitchService');
        if (twitchService) {
        const emoji = counterType === 'deaths' ? '💀' : counterType === 'swears' ? '🤬' : '😱';
        const counterName = counterType === 'deaths' ? 'deaths' : counterType === 'swears' ? 'swears' : 'screams';
          const chatMessage = `${emoji} MILESTONE REACHED! ${milestone} ${counterName}! Current count: ${newValue} ${emoji}`;

          await twitchService.sendMessage(userId, chatMessage);
          console.log(`💬 Sent milestone message to ${user.username}'s chat: ${chatMessage}`);
        }
      }

      // Emit milestone reached event to overlay for audio notifications
      const io = req.app.get('io');
      if (io) {
        io.to(`user:${userId}`).emit('milestoneReached', {
          userId,
          counterType,
          milestone,
          newValue,
          previousMilestone,
          timestamp: new Date().toISOString()
        });
      }
    }
  } catch (error) {
    console.error('❌ Error checking milestones:', error);
  }
}

/**
 * Get current counter state for authenticated user
 * GET /api/counters
 */
router.get('/', requireAuth, async (req, res) => {
  try {
    const data = await database.getCounters(req.user.userId);
    res.json(data);
  } catch (error) {
    console.error('Error fetching counters:', error);
    res.status(500).json({ error: 'Failed to fetch counters' });
  }
});

/**
 * Increment death counter
 * POST /api/counters/deaths/increment
 */
router.post('/deaths/increment', requireAuth, async (req, res) => {
  try {
    // Get current count before incrementing
    const currentData = await database.getCounters(req.user.userId);
    const previousDeaths = currentData.deaths || 0;

    const data = await database.incrementDeaths(req.user.userId);
    const newDeaths = data.deaths;

    // Check for milestones and send notifications
    await checkMilestones(req.user.userId, 'deaths', previousDeaths, newDeaths, req);

    // Emit WebSocket event to user's room
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);

    res.json(data);
  } catch (error) {
    console.error('Error incrementing deaths:', error);
    res.status(500).json({ error: 'Failed to increment deaths' });
  }
});

/**
 * Decrement death counter
 * POST /api/counters/deaths/decrement
 */
router.post('/deaths/decrement', requireAuth, async (req, res) => {
  try {
    const data = await database.decrementDeaths(req.user.userId);
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error decrementing deaths:', error);
    res.status(500).json({ error: 'Failed to decrement deaths' });
  }
});

/**
 * Increment swear counter
 * POST /api/counters/swears/increment
 */
router.post('/swears/increment', requireAuth, async (req, res) => {
  try {
    // Get current count before incrementing
    const currentData = await database.getCounters(req.user.userId);
    const previousSwears = currentData.swears || 0;

    const data = await database.incrementSwears(req.user.userId);
    const newSwears = data.swears;

    // Check for milestones and send notifications
    await checkMilestones(req.user.userId, 'swears', previousSwears, newSwears, req);

    // Emit WebSocket event to user's room
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);

    res.json(data);
  } catch (error) {
    console.error('Error incrementing swears:', error);
    res.status(500).json({ error: 'Failed to increment swears' });
  }
});

/**
 * Decrement swear counter
 * POST /api/counters/swears/decrement
 */
router.post('/swears/decrement', requireAuth, async (req, res) => {
  try {
    const data = await database.decrementSwears(req.user.userId);
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error decrementing swears:', error);
    res.status(500).json({ error: 'Failed to decrement swears' });
  }
});

/**
 * Increment scream counter
 * POST /api/counters/screams/increment
 */
router.post('/screams/increment', requireAuth, async (req, res) => {
  try {
    // Get current count before incrementing
    const currentData = await database.getCounters(req.user.userId);
    const previousScreams = currentData.screams || 0;

    const data = await database.incrementScreams(req.user.userId);
    const newScreams = data.screams;

    // Check for milestones and send notifications
    await checkMilestones(req.user.userId, 'screams', previousScreams, newScreams, req);

    // Emit WebSocket event to user's room
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);

    res.json(data);
  } catch (error) {
    console.error('Error incrementing screams:', error);
    res.status(500).json({ error: 'Failed to increment screams' });
  }
});

/**
 * Decrement scream counter
 * POST /api/counters/screams/decrement
 */
router.post('/screams/decrement', requireAuth, async (req, res) => {
  try {
    const data = await database.decrementScreams(req.user.userId);
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error decrementing screams:', error);
    res.status(500).json({ error: 'Failed to decrement screams' });
  }
});

/**
 * Reset all counters
 * POST /api/counters/reset
 */
router.post('/reset', requireAuth, async (req, res) => {
  try {
    const data = await database.resetCounters(req.user.userId);
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error resetting counters:', error);
    res.status(500).json({ error: 'Failed to reset counters' });
  }
});

/**
 * Export counter data
 * GET /api/counters/export
 */
router.get('/export', requireAuth, async (req, res) => {
  try {
    const data = await database.getCounters(req.user.userId);
    const exportData = {
      ...data,
      username: req.user.username,
      exportedAt: new Date().toISOString()
    };
    res.json(exportData);
  } catch (error) {
    console.error('Error exporting data:', error);
    res.status(500).json({ error: 'Failed to export data' });
  }
});

// ==================== SERIES SAVE STATE ROUTES ====================

/**
 * Save current counter state as a series save point
 * POST /api/counters/series/save
 * Body: { seriesName: string, description?: string }
 */
router.post('/series/save', requireAuth, async (req, res) => {
  try {
    const { seriesName, description } = req.body;

    if (!seriesName || seriesName.trim().length === 0) {
      return res.status(400).json({ error: 'Series name is required' });
    }

    if (seriesName.length > 100) {
      return res.status(400).json({ error: 'Series name must be 100 characters or less' });
    }

    const saveData = await database.saveSeries(
      req.user.userId,
      seriesName.trim(),
      description?.trim() || ''
    );

    console.log(`💾 User ${req.user.username} saved series: "${seriesName}"`);

    res.json({
      message: 'Series saved successfully',
      save: {
        seriesId: saveData.rowKey,
        seriesName: saveData.seriesName,
        description: saveData.description,
        deaths: saveData.deaths,
        swears: saveData.swears,
        bits: saveData.bits,
        savedAt: saveData.savedAt
      }
    });
  } catch (error) {
    console.error('Error saving series:', error);
    res.status(500).json({ error: 'Failed to save series' });
  }
});

/**
 * Load a series save state
 * POST /api/counters/series/load
 * Body: { seriesId: string }
 */
router.post('/series/load', requireAuth, async (req, res) => {
  try {
    const { seriesId } = req.body;

    if (!seriesId) {
      return res.status(400).json({ error: 'Series ID is required' });
    }

    const loadedData = await database.loadSeries(req.user.userId, seriesId);

    // Emit WebSocket event to update all connected devices
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', {
      deaths: loadedData.deaths,
      swears: loadedData.swears,
      bits: loadedData.bits,
      lastUpdated: loadedData.lastUpdated,
      streamStarted: loadedData.streamStarted,
      change: { deaths: 0, swears: 0, bits: 0 }
    });

    console.log(`📂 User ${req.user.username} loaded series: "${loadedData.seriesName}"`);

    res.json({
      message: 'Series loaded successfully',
      counters: {
        deaths: loadedData.deaths,
        swears: loadedData.swears,
        bits: loadedData.bits,
        lastUpdated: loadedData.lastUpdated
      },
      seriesInfo: {
        seriesName: loadedData.seriesName,
        description: loadedData.description,
        savedAt: loadedData.savedAt
      }
    });
  } catch (error) {
    if (error.message === 'Series save not found') {
      return res.status(404).json({ error: 'Series save not found' });
    }
    console.error('Error loading series:', error);
    res.status(500).json({ error: 'Failed to load series' });
  }
});

/**
 * List all series saves for the authenticated user
 * GET /api/counters/series/list
 */
router.get('/series/list', requireAuth, async (req, res) => {
  try {
    const saves = await database.listSeriesSaves(req.user.userId);

    res.json({
      count: saves.length,
      saves: saves
    });
  } catch (error) {
    console.error('Error listing series saves:', error);
    res.status(500).json({ error: 'Failed to list series saves' });
  }
});

/**
 * Delete a series save
 * DELETE /api/counters/series/:seriesId
 */
router.delete('/series/:seriesId', requireAuth, async (req, res) => {
  try {
    const { seriesId } = req.params;

    await database.deleteSeries(req.user.userId, seriesId);

    console.log(`🗑️  User ${req.user.username} deleted series: ${seriesId}`);

    res.json({
      message: 'Series save deleted successfully'
    });
  } catch (error) {
    if (error.message === 'Series save not found') {
      return res.status(404).json({ error: 'Series save not found' });
    }
    console.error('Error deleting series:', error);
    res.status(500).json({ error: 'Failed to delete series' });
  }
});

/**
 * Get overlay settings for authenticated user
 * GET /api/counters/overlay/settings
 */
router.get('/overlay/settings', requireAuth, async (req, res) => {
  try {
    // Check if user has streamOverlay feature enabled
    const hasFeature = await database.hasFeature(req.user.userId, 'streamOverlay');
    if (!hasFeature) {
      return res.status(403).json({
        error: 'Stream overlay feature is not enabled for your account'
      });
    }

    const settings = await database.getUserOverlaySettings(req.user.userId);
    res.json(settings);
  } catch (error) {
    console.error('❌ Error fetching overlay settings:', error);
    res.status(500).json({ error: 'Failed to fetch overlay settings' });
  }
});

/**
 * Update overlay settings for authenticated user
 * PUT /api/counters/overlay/settings
 */
router.put('/overlay/settings', requireAuth, async (req, res) => {
  try {
    // Check if user has streamOverlay feature enabled
    const hasFeature = await database.hasFeature(req.user.userId, 'streamOverlay');
    if (!hasFeature) {
      return res.status(403).json({
        error: 'Stream overlay feature is not enabled for your account'
      });
    }

    const { enabled, position, counters, theme, animations } = req.body;

    // Validate the settings structure
    const validPositions = ['top-left', 'top-right', 'bottom-left', 'bottom-right'];
    if (position && !validPositions.includes(position)) {
      return res.status(400).json({
        error: 'Invalid position. Must be one of: ' + validPositions.join(', ')
      });
    }

    // Build the settings object with validation
    const overlaySettings = {
      enabled: enabled === true,
      position: position || 'top-right',
      counters: {
        deaths: counters?.deaths === true,
        swears: counters?.swears === true,
        bits: counters?.bits === true
      },
      theme: {
        backgroundColor: theme?.backgroundColor || 'rgba(0, 0, 0, 0.7)',
        borderColor: theme?.borderColor || '#d4af37',
        textColor: theme?.textColor || 'white'
      },
      animations: {
        enabled: animations?.enabled !== false, // Default true
        showAlerts: animations?.showAlerts !== false, // Default true
        celebrationEffects: animations?.celebrationEffects !== false // Default true
      }
    };

    const updatedUser = await database.updateUserOverlaySettings(req.user.userId, overlaySettings);

    console.log(`✅ Updated overlay settings for user ${req.user.username}`);
    res.json({
      message: 'Overlay settings updated successfully',
      settings: overlaySettings
    });
  } catch (error) {
    console.error('❌ Error updating overlay settings:', error);
    res.status(500).json({ error: 'Failed to update overlay settings' });
  }
});

module.exports = router;

const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

const router = express.Router();

/**
 * Get user settings (stream status + overlay settings)
 * GET /api/user/settings
 */
router.get('/settings', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId; // Changed from twitchUserId to userId

    // Get user data including stream status and overlay settings
    const user = await database.getUser(userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Parse overlay settings if stored as string
    let overlaySettings = user.overlaySettings;
    if (typeof overlaySettings === 'string') {
      try {
        overlaySettings = JSON.parse(overlaySettings);
      } catch (e) {
        console.error('Failed to parse overlay settings:', e);
        overlaySettings = null;
      }
    }

    // Default overlay settings if none exist
    if (!overlaySettings) {
      overlaySettings = {
        enabled: true,
        position: 'top-right',
        size: 'medium',
        counters: {
          deaths: true,
          swears: true,
          bits: false,
          channelPoints: false
        },
        animations: {
          enabled: true,
          showAlerts: true,
          celebrationEffects: false,
          bounceOnUpdate: true,
          fadeTransitions: true
        },
        theme: {
          borderColor: '#9146ff',
          textColor: '#ffffff',
          backgroundColor: 'rgba(0, 0, 0, 0.8)'
        }
      };
    }

    res.json({
      streamStatus: user.streamStatus || 'offline',
      overlaySettings: overlaySettings,
      features: user.features || {}
    });
  } catch (error) {
    console.error('❌ Error fetching user settings:', error);
    res.status(500).json({ error: 'Failed to fetch user settings' });
  }
});

/**
 * Update overlay settings
 * PUT /api/overlay-settings
 */
router.put('/overlay-settings', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId; // Changed from twitchUserId to userId
    const newSettings = req.body;

    console.log(`🎨 Updating overlay settings for user ${req.user.username}`);

    // Validate settings structure
    if (!newSettings || typeof newSettings !== 'object') {
      return res.status(400).json({ error: 'Invalid settings format' });
    }

    // Get current user to preserve other data
    const user = await database.getUser(userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Update user with new overlay settings using the proper database function
    await database.updateUserOverlaySettings(userId, newSettings);

    console.log(`✅ Overlay settings updated for ${req.user.username}`);

    // Broadcast to connected clients (in case overlay is open)
    const io = req.app.get('io');
    io.to(`user:${userId}`).emit('overlaySettingsUpdate', {
      userId,
      overlaySettings: newSettings
    });

    res.json({
      message: 'Overlay settings updated successfully',
      overlaySettings: newSettings
    });
  } catch (error) {
    console.error('❌ Error updating overlay settings:', error);
    res.status(500).json({ error: 'Failed to update overlay settings' });
  }
});

module.exports = router;

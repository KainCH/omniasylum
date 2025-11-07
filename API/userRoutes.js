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

/**
 * Get Discord webhook configuration
 * GET /api/user/discord-webhook
 */
router.get('/discord-webhook', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    const user = await database.getUser(userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Check if feature is enabled
    const hasFeature = await database.hasFeature(userId, 'discordNotifications');

    res.json({
      webhookUrl: user.discordWebhookUrl || '',
      enabled: hasFeature
    });
  } catch (error) {
    console.error('❌ Error fetching Discord webhook:', error);
    res.status(500).json({ error: 'Failed to fetch Discord webhook configuration' });
  }
});

/**
 * Update Discord webhook URL
 * PUT /api/user/discord-webhook
 */
router.put('/discord-webhook', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    const { webhookUrl } = req.body;

    console.log(`🔔 Updating Discord webhook for user ${req.user.username}`);

    // Validate webhook URL format (basic validation)
    if (webhookUrl && !webhookUrl.startsWith('https://discord.com/api/webhooks/')) {
      return res.status(400).json({ error: 'Invalid Discord webhook URL format' });
    }

    // Get current user
    const user = await database.getUser(userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Update user with new Discord webhook URL
    const updatedUser = {
      ...user,
      discordWebhookUrl: webhookUrl || ''
    };
    await database.saveUser(updatedUser);

    console.log(`✅ Discord webhook updated for ${req.user.username}`);

    res.json({
      message: 'Discord webhook updated successfully',
      webhookUrl: webhookUrl || ''
    });
  } catch (error) {
    console.error('❌ Error updating Discord webhook:', error);
    res.status(500).json({ error: 'Failed to update Discord webhook' });
  }
});

/**
 * Test Discord webhook
 * POST /api/user/discord-webhook/test
 */
router.post('/discord-webhook/test', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    const user = await database.getUser(userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    if (!user.discordWebhookUrl) {
      return res.status(400).json({ error: 'No Discord webhook URL configured' });
    }

    console.log(`🧪 Testing Discord webhook for ${req.user.username}`);

    // Send test notification
    const response = await fetch(user.discordWebhookUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        content: `🧪 **Test Notification** from OmniForge Stream Tools`,
        embeds: [{
          title: 'Discord Integration Test',
          description: `This is a test notification for **${user.displayName}**.\n\nIf you see this, your Discord webhook is configured correctly! 🎉`,
          color: 0x00FF00, // Green
          thumbnail: {
            url: user.profileImageUrl
          },
          footer: {
            text: 'OmniForge Stream Tools'
          },
          timestamp: new Date().toISOString()
        }]
      })
    });

    if (response.ok) {
      console.log(`✅ Discord test notification sent for ${req.user.username}`);
      res.json({ message: 'Test notification sent successfully!' });
    } else {
      const errorText = await response.text();
      console.error(`❌ Discord test failed for ${req.user.username}:`, errorText);
      res.status(500).json({ error: 'Failed to send test notification', details: errorText });
    }
  } catch (error) {
    console.error('❌ Error testing Discord webhook:', error);
    res.status(500).json({ error: 'Failed to test Discord webhook' });
  }
});

module.exports = router;

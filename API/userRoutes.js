const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

/**
 * Create a consistent Discord embed format
 */
function createDiscordEmbed(title, description, user, options = {}) {
  return {
    username: 'OmniForge',
    avatar_url: options?.botAvatar || user?.profileImageUrl,
    embeds: [{
      title: title,
      description: description,
      color: options.color || 0x5865F2, // Discord blurple
      thumbnail: {
        url: user?.profileImageUrl
      },
      footer: {
        text: `OmniForge Stream Tools • Today at ${new Date().toLocaleTimeString('en-US', {
          hour: 'numeric',
          minute: '2-digit',
          hour12: true
        })}`
      },
      timestamp: new Date().toISOString(),
      ...(options?.fields && { fields: options.fields }),
      ...(options?.image && { image: { url: options.image } })
    }]
  };
}

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
    let overlaySettings = user?.overlaySettings;
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
      streamStatus: user?.streamStatus || 'offline',
      overlaySettings: overlaySettings,
      features: user?.features || {}
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
      webhookUrl: user?.discordWebhookUrl || '',
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

    // Use the dedicated updateUserDiscordWebhook method (same as admin routes)
    await database.updateUserDiscordWebhook(userId, webhookUrl || '');

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

    if (!user?.discordWebhookUrl) {
      return res.status(400).json({ error: 'No Discord webhook URL configured' });
    }

    console.log(`🧪 Testing Discord webhook for ${req.user.username}`);

    // Send test notification with rich embed format
    const discordPayload = createDiscordEmbed(
      'Discord Integration Test',
      `This is a test notification for **${user.displayName}**.\n\nIf you see this, your Discord webhook is configured correctly! 🎉`,
      user,
      { color: 0x00FF00 } // Green for success
    );

    const response = await fetch(user?.discordWebhookUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(discordPayload)
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

/**
 * Send Discord notification for stream events
 */
async function sendDiscordNotification(user, eventType, data) {
  if (!user?.discordWebhookUrl) return;

  // Parse Discord settings (if available)
  let discordSettings = {
    templateStyle: 'asylum_themed',
    enabledNotifications: {
      death_milestone: true,
      swear_milestone: true,
      stream_start: true,
      stream_end: true,
      follower_goal: false,
      subscriber_milestone: false,
      channel_point_redemption: false
    }
  };

  if (user?.discordSettings) {
    try {
      discordSettings = JSON.parse(user?.discordSettings);
    } catch (error) {
      console.log('⚠️ Failed to parse Discord settings, using defaults');
    }
  }

  // Check if this notification type is enabled
  if (!discordSettings.enabledNotifications[eventType]) {
    console.log(`🔇 Discord notification disabled for ${eventType} by user ${user?.username}`);
    return;
  }

  let title, description, color;

  // Generate content based on template style
  const templateStyle = discordSettings.templateStyle;

  switch (eventType) {
    case 'death_milestone':
      if (templateStyle === 'asylum_themed') {
        title = `💀 Death Milestone Reached!`;
        description = `**${user.displayName}** just reached **${data.count} deaths**!\n\nThe asylum claims another victim... 🔥`;
      } else if (templateStyle === 'minimal') {
        title = `💀 Death Counter`;
        description = `**${user.displayName}** • ${data.count} deaths`;
      } else {
        title = `💀 Death Milestone: ${data.count}`;
        description = `**${user.displayName}** has reached ${data.count} deaths!\n\n📊 **Progress:** ${data.previousMilestone || 0} → ${data.count}`;
      }
      color = 0xFF4444; // Red
      break;

    case 'swear_milestone':
      if (templateStyle === 'asylum_themed') {
        title = `🤬 Profanity Counter Update`;
        description = `**${user.displayName}** has sworn **${data.count} times**!\n\nThe madness is spreading... 😈`;
      } else if (templateStyle === 'minimal') {
        title = `🤬 Swear Counter`;
        description = `**${user.displayName}** • ${data.count} swears`;
      } else {
        title = `🤬 Swear Milestone: ${data.count}`;
        description = `**${user.displayName}** has reached ${data.count} swears!\n\n📊 **Progress:** ${data.previousMilestone || 0} → ${data.count}`;
      }
      color = 0xFF8800; // Orange
      break;

    case 'stream_start':
      if (templateStyle === 'asylum_themed') {
        title = `🔴 The Asylum Opens`;
        description = `**${user.displayName}** is now live!\n\nThe asylum doors are open... Enter if you dare! 👻`;
      } else if (templateStyle === 'minimal') {
        title = `🔴 Live Now`;
        description = `**${user.displayName}** is streaming`;
      } else {
        title = `🔴 Stream Started`;
        description = `**${user?.displayName}** is now live on Twitch!\n\n🎮 **Game:** ${data?.game || 'Unknown'}\n📺 **Watch:** https://twitch.tv/${user?.username}`;
      }
      color = 0x00FF00; // Green
      break;

    case 'stream_end':
      if (templateStyle === 'asylum_themed') {
        title = `⚫ Escaped the Asylum`;
        description = `**${user.displayName}** has escaped the asylum... for now.\n\nThanks for watching! 🙏`;
      } else if (templateStyle === 'minimal') {
        title = `⚫ Stream Ended`;
        description = `**${user.displayName}** is offline`;
      } else {
        title = `⚫ Stream Ended`;
        description = `**${user.displayName}** has ended the stream.\n\n⏱️ **Duration:** ${data.duration || 'Unknown'}\n💙 **Thanks for watching!**`;
      }
      color = 0x808080; // Gray
      break;

    default:
      title = `📢 OmniForge Notification`;
      description = `Event: ${eventType}`;
      color = 0x5865F2; // Discord blurple
  }

  const discordPayload = createDiscordEmbed(title, description, user, {
    color,
    fields: data?.fields || []
  });

  try {
    await fetch(user?.discordWebhookUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(discordPayload)
    });
    console.log(`✅ Discord notification sent: ${eventType} (${templateStyle}) for ${user?.username}`);
  } catch (error) {
    console.error(`❌ Discord notification failed: ${eventType} for ${user?.username}:`, error);
  }
}

/**
 * Get user Discord notification settings
 * GET /api/user/discord-settings
 */
router.get('/discord-settings', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    console.log(`📋 Getting Discord notification settings for user ${req.user.username}`);

    const user = await database.getUser(userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Parse Discord settings from user object, with defaults
    const discordSettings = user?.discordSettings ? JSON.parse(user.discordSettings) : {
      enableDiscordNotifications: false,
      enableChannelNotifications: false,
      deathMilestoneEnabled: false,
      swearMilestoneEnabled: false,
      deathThresholds: '10,25,50,100,250,500,1000',
      swearThresholds: '25,50,100,250,500,1000,2500'
    };

    console.log(`✅ Discord settings retrieved for ${req.user.username}`);
    res.json(discordSettings);
  } catch (error) {
    console.error('❌ Error getting Discord settings:', error);
    res.status(500).json({ error: 'Failed to get Discord notification settings' });
  }
});

/**
 * Update user Discord notification settings
 * PUT /api/user/discord-settings
 */
router.put('/discord-settings', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    const settings = req.body;

    console.log(`🔔 Updating Discord notification settings for user ${req.user.username}:`, settings);

    // Update user with new Discord settings
    const updatedUser = await database.updateUser(userId, {
      discordSettings: JSON.stringify(settings)
    });

    console.log(`✅ Discord notification settings updated for ${req.user.username}`);
    res.json({
      message: 'Discord notification settings updated successfully',
      settings: settings
    });
  } catch (error) {
    console.error('❌ Error updating Discord notification settings:', error);
    res.status(500).json({ error: 'Failed to update Discord notification settings' });
  }
});

// Export the router as default for compatibility, and additional functions
module.exports = router;
module.exports.sendDiscordNotification = sendDiscordNotification;
module.exports.createDiscordEmbed = createDiscordEmbed;

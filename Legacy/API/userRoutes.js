const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

/**
 * Create a consistent Discord embed format
 */
function createDiscordEmbed(title, description, user, options = {}) {
  const payload = {
    username: 'OmniForge',
    avatar_url: options?.botAvatar || user?.profileImageUrl,
    embeds: [{
      title: title,
      description: description,
      color: options.color || 0x5865F2, // Discord blurple
      ...(options?.url && { url: options.url }),
      thumbnail: {
        url: user?.profileImageUrl
      },
      footer: {
        text: 'OmniForge Stream Tools'
      },
      timestamp: new Date().toISOString(),
      ...(options?.fields && { fields: options.fields }),
      ...(options?.image && { image: { url: options.image } })
    }]
  };

  // Add button components if provided - wrap in Action Row
  if (options?.buttons) {
    payload.components = [{
      type: 1, // Action Row
      components: options.buttons.map(button => ({
        type: 2, // Button component type
        style: button.style || 5, // Default to link style
        label: button.label,
        url: button.url // For link buttons
      }))
    }];
  }

  return payload;
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

    // Call getUserDiscordWebhook instead of getUser to use enhanced logging
    const webhookData = await database.getUserDiscordWebhook(userId);

    if (!webhookData) {
      // Fallback: try to get user directly
      const user = await database.getUser(userId);
      if (!user) {
        return res.status(404).json({ error: 'User not found' });
      } else {
        webhookData = { webhookUrl: '', enabled: false };
      }
    }

    // Use the enabled status from the database, don't recalculate it
    const webhookUrl = webhookData.webhookUrl || '';
    const enabled = webhookData.enabled !== undefined ? webhookData.enabled : false;

    const result = {
      webhookUrl: webhookUrl,
      enabled: enabled
    };

    res.json(result);
  } catch (error) {
    console.error('❌ Error fetching Discord webhook:', error);
    res.status(500).json({ error: 'Failed to fetch Discord webhook configuration' });
  }
});/**
 * Update Discord webhook URL
 * PUT /api/user/discord-webhook
 */
router.put('/discord-webhook', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    // Accept both 'webhookUrl' and 'discordWebhookUrl' for backward compatibility
    const { webhookUrl, discordWebhookUrl, enabled } = req.body;
    const actualWebhookUrl = webhookUrl || discordWebhookUrl;

    console.log(`🔔 WEBHOOK UPDATE START - User: ${req.user.username} (ID: ${userId})`);
    console.log(`🔔 Raw request body:`, JSON.stringify(req.body, null, 2));
    console.log(`🔔 Field extraction:`, {
      webhookUrl: webhookUrl,
      discordWebhookUrl: discordWebhookUrl,
      actualWebhookUrl: actualWebhookUrl,
      webhookUrlType: typeof webhookUrl,
      discordWebhookUrlType: typeof discordWebhookUrl,
      actualWebhookUrlType: typeof actualWebhookUrl
    });
    console.log(`🔔 Request data:`, {
      webhookUrl: actualWebhookUrl ? `${actualWebhookUrl.substring(0, 50)}...` : 'EMPTY',
      enabled: enabled,
      bodyKeys: Object.keys(req.body),
      receivedFields: {
        webhookUrl: !!webhookUrl,
        discordWebhookUrl: !!discordWebhookUrl
      }
    });

    // Validate webhook URL format (basic validation)
    if (actualWebhookUrl && !actualWebhookUrl.startsWith('https://discord.com/api/webhooks/')) {
      console.log(`❌ Invalid webhook URL format: ${actualWebhookUrl}`);
      return res.status(400).json({ error: 'Invalid Discord webhook URL format' });
    }

    // Update webhook URL with explicit error handling
    let updatedUser;
    try {
      updatedUser = await database.updateUserDiscordWebhook(userId, actualWebhookUrl || '');
    } catch (dbError) {
      console.error('❌ Database updateUserDiscordWebhook failed:', dbError);
      throw dbError; // Re-throw to be caught by outer catch
    }

    // Verify the webhook was actually saved by reading it back
    let verification;
    try {
      verification = await database.getUserDiscordWebhook(userId);
    } catch (verifyError) {
      console.error('❌ Verification read failed:', verifyError);
      verification = null;
    }

    // No feature flag management needed - streamMonitor will check webhook presence directly

    console.log(`🎉 WEBHOOK UPDATE SUCCESS - ${req.user.username}`);

    res.json({
      message: 'Discord webhook updated successfully',
      webhookUrl: actualWebhookUrl || '',
      verified: verification
    });
  } catch (error) {
    console.error('❌ WEBHOOK UPDATE FAILED:', error);
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
 * Get Discord invite link
 * GET /api/user/discord-invite
 */
router.get('/discord-invite', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    const inviteLink = await database.getUserDiscordInviteLink(userId);

    res.json({
      discordInviteLink: inviteLink || '',
      hasInvite: !!(inviteLink && inviteLink.trim())
    });
  } catch (error) {
    console.error('❌ Error fetching Discord invite link:', error);
    res.status(500).json({ error: 'Failed to fetch Discord invite link' });
  }
});

/**
 * Update Discord invite link
 * PUT /api/user/discord-invite
 */
router.put('/discord-invite', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    const { discordInviteLink } = req.body;

    // Update the Discord invite link
    await database.updateUserDiscordInviteLink(userId, discordInviteLink);

    console.log(`✅ Discord invite link updated for ${req.user.username}: ${discordInviteLink ? 'Set' : 'Removed'}`);

    res.json({
      message: 'Discord invite link updated successfully',
      discordInviteLink: discordInviteLink || ''
    });
  } catch (error) {
    console.error('❌ Error updating Discord invite link:', error);
    if (error.message.includes('Invalid Discord invite link format')) {
      res.status(400).json({ error: 'Invalid Discord invite link format. Please use a valid Discord invite URL.' });
    } else {
      res.status(500).json({ error: 'Failed to update Discord invite link' });
    }
  }
});

/**
 * Send Discord notification for stream events
 */
async function sendDiscordNotification(user, eventType, data) {
  if (!user?.discordWebhookUrl) return;

  // Parse Discord notification settings
  let discordSettings = {
    enabledNotifications: {
      death_milestone: true,
      swear_milestone: true,
      stream_start: true,
      stream_end: false, // Disabled by default - user preference
      follower_goal: false,
      subscriber_milestone: false,
      channel_point_redemption: false
    }
  };

  if (user?.discordSettings) {
    try {
      const parsedSettings = JSON.parse(user?.discordSettings);
      discordSettings = {
        ...discordSettings,
        ...parsedSettings
      };
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

  // Generate notification content
  switch (eventType) {
    case 'death_milestone':
      // Standard death milestone notification
      title = `💀 Death Milestone: ${data.count}`;
      description = `**${user.displayName}** has reached ${data.count} deaths!\n\n📊 **Progress:** ${data.previousMilestone || 0} → ${data.count}`;
      color = 0xFF4444; // Red
      break;

    case 'swear_milestone':
      // Standard swear milestone notification
      title = `🤬 Swear Milestone: ${data.count}`;
      description = `**${user.displayName}** has reached ${data.count} swears!\n\n📊 **Progress:** ${data.previousMilestone || 0} → ${data.count}`;
      color = 0xFF8800; // Orange
      break;

    case 'stream_start':
      // Clean format - no description, just title and fields with prominent button
      title = `🔴 ${user.displayName} is now live on Twitch!`;

      // No description - let the title speak for itself
      description = null;

      // Add fields for stream title and category
      const fields = [];

      // Add Stream Title field - show actual title or placeholder
      const streamTitle = data?.title && data.title !== '' ? data.title : 'Stream Title Not Set';
      fields.push({
        name: '📺 Title',
        value: streamTitle,
        inline: false
      });

      // Add Streaming Category field - always show even if unknown
      const gameValue = data?.game && data.game !== '' ? data.game : 'Unknown Category';
      fields.push({
        name: '� Streaming',
        value: gameValue,
        inline: true
      });

      // Store fields and button data for embed creation
      data.fields = fields;
      data.showWatchButton = true; // Flag to show the prominent watch button

      // Use live thumbnail from Twitch API if available, otherwise generate one with timestamp
      if (data?.thumbnailUrl) {
        // Use the actual thumbnail URL from the Twitch API (already includes timestamp)
        data.image = data.thumbnailUrl;
      } else {
        // Fallback: Generate thumbnail URL with cache-busting timestamp
        const timestamp = Date.now();
        data.image = `https://static-cdn.jtvnw.net/previews-ttv/live_user_${user.username.toLowerCase()}-640x360.jpg?t=${timestamp}`;
      }

      color = 0x00FF00; // Green
      break;

    case 'stream_end':
      // Standard stream end notification
      title = `🔴 Stream Ended`;
      description = `**${user.displayName}** has ended the stream.\n\n⏱️ **Duration:** ${data.duration || 'Unknown'}\n💙 **Thanks for watching!**`;
      color = 0xFF4444; // Red
      break;

    default:
      title = `📢 OmniForge Notification`;
      description = `Event: ${eventType}`;
      color = 0x5865F2; // Discord blurple
  }

  const discordPayload = createDiscordEmbed(title, description, user, {
    color,
    fields: data?.fields || [],
    image: data?.image,
    url: eventType === 'stream_start' ? `https://twitch.tv/${user?.username}` : undefined,
    ...(data?.showWatchButton && {
      buttons: [{
        style: 5, // Link button (external URL)
        label: '🎮 **READY TO WATCH? CLICK HERE!**',
        url: `https://twitch.tv/${user?.username}`
      }]
    })
  });

  try {
    // Add with_components=true for interactive components
    const webhookUrl = data?.showWatchButton && user?.discordWebhookUrl
      ? `${user.discordWebhookUrl}?with_components=true`
      : user?.discordWebhookUrl;

    console.log(`📤 Sending Discord webhook to: ${webhookUrl.substring(0, 50)}...`);
    console.log(`📦 Discord payload:`, JSON.stringify(discordPayload, null, 2));

    const response = await fetch(webhookUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(discordPayload)
    });

    console.log(`📡 Discord webhook response - Status: ${response.status} ${response.statusText}`);

    if (!response.ok) {
      const errorText = await response.text();
      console.error(`❌ Discord webhook failed with ${response.status}: ${errorText}`);
      throw new Error(`Discord webhook failed: ${response.status} ${response.statusText} - ${errorText}`);
    }

    const responseBody = await response.text();
    console.log(`✅ Discord notification sent successfully: ${eventType} for ${user?.username}`);
    console.log(`📝 Discord response body:`, responseBody);

  } catch (error) {
    console.error(`❌ Discord notification failed: ${eventType} for ${user?.username}:`, error);
    throw error; // Re-throw to ensure proper error handling upstream
  }
}

/**
 * Get user Discord notification settings
 * GET /api/user/discord-settings
 */
router.get('/discord-settings', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;

    const user = await database.getUser(userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Parse Discord settings from user object, with defaults matching admin structure
    const defaultSettings = {
      templateStyle: 'asylum_themed',
      enabledNotifications: {
        death_milestone: true,
        swear_milestone: true,
        stream_start: true,
        stream_end: false,
        follower_goal: false,
        subscriber_milestone: false,
        channel_point_redemption: false
      },
      milestoneThresholds: {
        deaths: [10, 25, 50, 100, 250, 500],
        swears: [25, 50, 100, 200, 500]
      },
      // Legacy flat structure for backward compatibility
      enableChannelNotifications: false,
      deathMilestoneEnabled: true,
      swearMilestoneEnabled: true,
      deathThresholds: '10,25,50,100,250,500',
      swearThresholds: '25,50,100,200,500'
    };

    const discordSettings = user?.discordSettings ? JSON.parse(user.discordSettings) : defaultSettings;

    // Include webhook data in the response
    const webhookUrl = user?.discordWebhookUrl || '';
    const webhookEnabled = !!(webhookUrl && webhookUrl.trim());

    // Map structured format to flat format for frontend compatibility
    const completeSettings = {
      // Webhook data
      webhookUrl: webhookUrl,
      enabled: webhookEnabled,
      templateStyle: user?.templateStyle || discordSettings.templateStyle || 'asylum_themed',

      // Map structured notifications to flat format
      enableChannelNotifications: discordSettings.enableChannelNotifications || false,
      deathMilestoneEnabled: discordSettings.enabledNotifications?.death_milestone ?? discordSettings.deathMilestoneEnabled ?? true,
      swearMilestoneEnabled: discordSettings.enabledNotifications?.swear_milestone ?? discordSettings.swearMilestoneEnabled ?? true,

      // Map structured thresholds to comma-separated strings
      deathThresholds: discordSettings.milestoneThresholds?.deaths?.join(',') || discordSettings.deathThresholds || '10,25,50,100,250,500',
      swearThresholds: discordSettings.milestoneThresholds?.swears?.join(',') || discordSettings.swearThresholds || '25,50,100,200,500',

      // Include structured format for advanced features
      enabledNotifications: discordSettings.enabledNotifications || defaultSettings.enabledNotifications,
      milestoneThresholds: discordSettings.milestoneThresholds || defaultSettings.milestoneThresholds
    };

    res.json(completeSettings);
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
    const incomingSettings = req.body;

    console.log(`🔔 Updating Discord notification settings for user ${req.user.username}:`, incomingSettings);

    // Get current settings to merge with updates
    const user = await database.getUser(userId);
    const currentSettings = user?.discordSettings ? JSON.parse(user.discordSettings) : {};

    // Convert flat format to structured format for storage
    const structuredSettings = {
      templateStyle: incomingSettings.templateStyle || currentSettings.templateStyle || 'asylum_themed',
      enabledNotifications: {
        death_milestone: incomingSettings.deathMilestoneEnabled ?? incomingSettings.enabledNotifications?.death_milestone ?? currentSettings.enabledNotifications?.death_milestone ?? true,
        swear_milestone: incomingSettings.swearMilestoneEnabled ?? incomingSettings.enabledNotifications?.swear_milestone ?? currentSettings.enabledNotifications?.swear_milestone ?? true,
        stream_start: incomingSettings.enabledNotifications?.stream_start ?? currentSettings.enabledNotifications?.stream_start ?? true,
        stream_end: incomingSettings.enabledNotifications?.stream_end ?? currentSettings.enabledNotifications?.stream_end ?? false,
        follower_goal: incomingSettings.enabledNotifications?.follower_goal ?? currentSettings.enabledNotifications?.follower_goal ?? false,
        subscriber_milestone: incomingSettings.enabledNotifications?.subscriber_milestone ?? currentSettings.enabledNotifications?.subscriber_milestone ?? false,
        channel_point_redemption: incomingSettings.enabledNotifications?.channel_point_redemption ?? currentSettings.enabledNotifications?.channel_point_redemption ?? false
      },
      milestoneThresholds: {
        deaths: incomingSettings.deathThresholds ?
          incomingSettings.deathThresholds.split(',').map(n => parseInt(n.trim())).filter(n => !isNaN(n)) :
          (incomingSettings.milestoneThresholds?.deaths || currentSettings.milestoneThresholds?.deaths || [10, 25, 50, 100, 250, 500]),
        swears: incomingSettings.swearThresholds ?
          incomingSettings.swearThresholds.split(',').map(n => parseInt(n.trim())).filter(n => !isNaN(n)) :
          (incomingSettings.milestoneThresholds?.swears || currentSettings.milestoneThresholds?.swears || [25, 50, 100, 200, 500])
      },
      // Keep legacy flat structure for backward compatibility
      enableChannelNotifications: incomingSettings.enableChannelNotifications ?? currentSettings.enableChannelNotifications ?? false,
      deathMilestoneEnabled: incomingSettings.deathMilestoneEnabled ?? currentSettings.deathMilestoneEnabled ?? true,
      swearMilestoneEnabled: incomingSettings.swearMilestoneEnabled ?? currentSettings.swearMilestoneEnabled ?? true,
      deathThresholds: incomingSettings.deathThresholds || currentSettings.deathThresholds || '10,25,50,100,250,500',
      swearThresholds: incomingSettings.swearThresholds || currentSettings.swearThresholds || '25,50,100,200,500'
    };

    console.log(`📊 Converted to structured format:`, structuredSettings);

    // Update user with new Discord settings using the specific method
    const updatedUser = await database.updateUserDiscordSettings(userId, structuredSettings);

    console.log(`✅ Discord notification settings updated for ${req.user.username}`);
    res.json({
      message: 'Discord notification settings updated successfully',
      settings: structuredSettings
    });
  } catch (error) {
    console.error('❌ Error updating Discord notification settings:', error);
    res.status(500).json({ error: 'Failed to update Discord notification settings' });
  }
});

/**
 * Update user Discord template style preference
 * PUT /api/user/template-style
 */
router.put('/template-style', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    const { templateStyle } = req.body;

    console.log(`🎨 Updating template style for user ${req.user.username}:`, templateStyle);

    // Validate template style
    const validTemplates = ['asylum_themed', 'detailed', 'minimal'];
    if (!validTemplates.includes(templateStyle)) {
      return res.status(400).json({ error: 'Invalid template style' });
    }

    // Update user's template preference
    const updatedUser = await database.updateUserTemplateStyle(userId, templateStyle);

    console.log(`✅ Template style updated to ${templateStyle} for ${req.user.username}`);
    res.json({
      message: 'Template style updated successfully',
      templateStyle: templateStyle
    });
  } catch (error) {
    console.error('❌ Error updating template style:', error);
    res.status(500).json({ error: 'Failed to update template style' });
  }
});

// Export the router as default for compatibility, and additional functions
module.exports = router;
module.exports.sendDiscordNotification = sendDiscordNotification;
module.exports.createDiscordEmbed = createDiscordEmbed;

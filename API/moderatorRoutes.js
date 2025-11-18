const express = require('express');
const database = require('./database');
const { requireAuth, requireModAccess } = require('./authMiddleware');

const router = express.Router();

// ==================== STREAMER MODERATOR MANAGEMENT ====================
// These routes allow streamers to manage who can moderate their settings

/**
 * Get all moderators for the current streamer
 * GET /api/moderator/my-moderators
 */
router.get('/my-moderators', requireAuth, async (req, res) => {
  try {
    const streamerId = req.user.userId;

    // Get all users with mod role who have this streamer in their managedStreamers
    const allUsers = await database.getAllUsers();
    const myModerators = allUsers.filter(user => {
      return user.role === 'mod' &&
             user.managedStreamers &&
             user.managedStreamers.includes(streamerId);
    });

    const moderators = myModerators.map(mod => ({
      userId: mod.twitchUserId,
      username: mod.username,
      displayName: mod.displayName,
      profileImageUrl: mod.profileImageUrl,
      lastLogin: mod.lastLogin,
      isActive: mod.isActive,
      grantedAt: mod.createdAt // Could add specific grant timestamp later
    }));

    console.log(`📋 Streamer ${req.user.username} listed ${moderators.length} moderators`);

    res.json({
      moderators,
      total: moderators.length,
      streamerId
    });
  } catch (error) {
    console.error('❌ Error fetching moderators:', error);
    res.status(500).json({ error: 'Failed to fetch moderators' });
  }
});

/**
 * Grant moderator access to another user for this streamer's settings
 * POST /api/moderator/grant-access
 */
router.post('/grant-access', requireAuth, async (req, res) => {
  try {
    const { moderatorUserId } = req.body;
    const streamerId = req.user.userId;

    if (!moderatorUserId) {
      return res.status(400).json({ error: 'Moderator user ID is required' });
    }

    // Prevent self-granting
    if (moderatorUserId === streamerId) {
      return res.status(400).json({ error: 'Cannot grant moderator access to yourself' });
    }

    // Check if moderator user exists
    const moderatorUser = await database.getUser(moderatorUserId);
    if (!moderatorUser) {
      return res.status(404).json({ error: 'Moderator user not found' });
    }

    // Only allow granting to users with 'mod' role or promote them
    if (moderatorUser.role !== 'mod') {
      // Auto-promote to mod role if they're a streamer
      if (moderatorUser.role === 'streamer') {
        await database.updateUserRole(moderatorUserId, 'mod');
        console.log(`⬆️ Promoted user ${moderatorUser.username} from streamer to mod`);
      } else if (moderatorUser.role === 'admin') {
        return res.status(400).json({ error: 'Cannot grant moderator access to admin users' });
      }
    }

    // Grant moderator permissions
    const updatedMod = await database.grantModPermissions(moderatorUserId, streamerId);

    console.log(`✅ Streamer ${req.user.username} granted moderator access to ${moderatorUser.username}`);

    res.json({
      message: 'Moderator access granted successfully',
      moderator: {
        userId: updatedMod.twitchUserId,
        username: updatedMod.username,
        displayName: updatedMod.displayName,
        role: updatedMod.role
      },
      streamer: {
        userId: streamerId,
        username: req.user.username
      }
    });
  } catch (error) {
    console.error('❌ Error granting moderator access:', error);
    if (error.message.includes('already has permissions')) {
      res.status(409).json({ error: 'User already has moderator access to your settings' });
    } else {
      res.status(500).json({ error: 'Failed to grant moderator access' });
    }
  }
});

/**
 * Revoke moderator access from a user for this streamer's settings
 * DELETE /api/moderator/revoke-access/:moderatorUserId
 */
router.delete('/revoke-access/:moderatorUserId', requireAuth, async (req, res) => {
  try {
    const { moderatorUserId } = req.params;
    const streamerId = req.user.userId;

    if (!moderatorUserId) {
      return res.status(400).json({ error: 'Moderator user ID is required' });
    }

    // Check if moderator user exists
    const moderatorUser = await database.getUser(moderatorUserId);
    if (!moderatorUser) {
      return res.status(404).json({ error: 'Moderator user not found' });
    }

    // Revoke moderator permissions
    const updatedMod = await database.revokeModPermissions(moderatorUserId, streamerId);

    console.log(`❌ Streamer ${req.user.username} revoked moderator access from ${moderatorUser.username}`);

    res.json({
      message: 'Moderator access revoked successfully',
      moderator: {
        userId: updatedMod.twitchUserId,
        username: updatedMod.username,
        displayName: updatedMod.displayName,
        role: updatedMod.role
      },
      streamer: {
        userId: streamerId,
        username: req.user.username
      }
    });
  } catch (error) {
    console.error('❌ Error revoking moderator access:', error);
    if (error.message.includes('does not have permissions')) {
      res.status(404).json({ error: 'User does not have moderator access to your settings' });
    } else {
      res.status(500).json({ error: 'Failed to revoke moderator access' });
    }
  }
});

/**
 * Search for users to grant moderator access to
 * GET /api/moderator/search-users?q=username
 */
router.get('/search-users', requireAuth, async (req, res) => {
  try {
    const { q } = req.query;
    const streamerId = req.user.userId;

    if (!q || q.length < 2) {
      return res.status(400).json({ error: 'Search query must be at least 2 characters' });
    }

    const allUsers = await database.getAllUsers();

    // Filter users that match the search and aren't already moderators
    const searchResults = allUsers
      .filter(user => {
        // Exclude the streamer themselves
        if (user.twitchUserId === streamerId) return false;

        // Exclude admin users
        if (user.role === 'admin') return false;

        // Must be active
        if (user.isActive === false) return false;

        // Check if username or displayName contains search query
        const searchLower = q.toLowerCase();
        const usernameMatch = user.username && user.username.toLowerCase().includes(searchLower);
        const displayNameMatch = user.displayName && user.displayName.toLowerCase().includes(searchLower);

        return usernameMatch || displayNameMatch;
      })
      .slice(0, 10) // Limit to 10 results
      .map(user => ({
        userId: user.twitchUserId,
        username: user.username,
        displayName: user.displayName,
        profileImageUrl: user.profileImageUrl,
        role: user.role,
        isAlreadyModerator: user.managedStreamers && user.managedStreamers.includes(streamerId)
      }));

    res.json({
      results: searchResults,
      query: q,
      total: searchResults.length
    });
  } catch (error) {
    console.error('❌ Error searching users:', error);
    res.status(500).json({ error: 'Failed to search users' });
  }
});

// ==================== MODERATOR MANAGEMENT ROUTES ====================
// These routes allow moderators to manage settings for streamers they have permissions for

/**
 * Get all streamers that the current moderator can manage
 * GET /api/moderator/managed-streamers
 */
router.get('/managed-streamers', requireAuth, requireModAccess, async (req, res) => {
  try {
    const moderatorId = req.user.userId;
    const moderator = await database.getUser(moderatorId);

    if (!moderator || !moderator.managedStreamers) {
      return res.json({
        streamers: [],
        total: 0,
        moderatorId
      });
    }

    // Get details for all managed streamers
    const streamers = [];
    for (const streamerId of moderator.managedStreamers) {
      try {
        const streamer = await database.getUser(streamerId);
        if (streamer) {
          const counters = await database.getCounters(streamerId);
          streamers.push({
            userId: streamer.twitchUserId,
            username: streamer.username,
            displayName: streamer.displayName,
            profileImageUrl: streamer.profileImageUrl,
            isActive: streamer.isActive,
            lastLogin: streamer.lastLogin,
            streamStatus: streamer.streamStatus || 'offline',
            counters: {
              deaths: counters.deaths || 0,
              swears: counters.swears || 0,
              screams: counters.screams || 0,
              bits: counters.bits || 0
            }
          });
        }
      } catch (error) {
        console.error(`❌ Error loading streamer ${streamerId}:`, error);
        // Continue with other streamers
      }
    }

    console.log(`📋 Moderator ${req.user.username} listed ${streamers.length} managed streamers`);

    res.json({
      streamers,
      total: streamers.length,
      moderatorId
    });
  } catch (error) {
    console.error('❌ Error fetching managed streamers:', error);
    res.status(500).json({ error: 'Failed to fetch managed streamers' });
  }
});

/**
 * Get specific streamer details (if moderator has access)
 * GET /api/moderator/streamers/:streamerId
 */
router.get('/streamers/:streamerId', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    const counters = await database.getCounters(streamerId);

    console.log(`👀 Moderator ${req.user.username} viewing details for streamer ${streamer.username}`);

    res.json({
      streamer: {
        userId: streamer.twitchUserId,
        username: streamer.username,
        displayName: streamer.displayName,
        email: streamer.email,
        profileImageUrl: streamer.profileImageUrl,
        role: streamer.role,
        features: typeof streamer.features === 'string' ? JSON.parse(streamer.features) : streamer.features,
        isActive: streamer.isActive,
        createdAt: streamer.createdAt,
        lastLogin: streamer.lastLogin,
        streamStatus: streamer.streamStatus || 'offline'
      },
      counters
    });
  } catch (error) {
    console.error('❌ Error fetching streamer details:', error);
    res.status(500).json({ error: 'Failed to fetch streamer details' });
  }
});

/**
 * Update streamer features (if moderator has access)
 * PUT /api/moderator/streamers/:streamerId/features
 */
router.put('/streamers/:streamerId/features', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const features = req.body.features || req.body;

    // Validate features object
    if (!features || typeof features !== 'object' || Array.isArray(features)) {
      return res.status(400).json({ error: 'Invalid features object' });
    }

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    const currentFeatures = typeof streamer.features === 'string' ? JSON.parse(streamer.features) : streamer.features || {};

    // Handle streamOverlay feature auto-enabling
    const wasOverlayEnabled = currentFeatures.streamOverlay;
    const isOverlayBeingEnabled = features.streamOverlay && !wasOverlayEnabled;

    const updatedUser = await database.updateUserFeatures(streamerId, features);

    // Auto-enable overlay settings if streamOverlay feature was just enabled
    if (isOverlayBeingEnabled) {
      console.log(`✅ StreamOverlay feature enabled for ${updatedUser.username} by moderator, auto-enabling overlay settings`);

      let overlaySettings = null;
      try {
        const currentSettings = await database.getUserOverlaySettings(streamerId);
        overlaySettings = currentSettings ?
          (typeof currentSettings.overlaySettings === 'string' ?
            JSON.parse(currentSettings.overlaySettings) :
            currentSettings.overlaySettings) : null;
      } catch (e) {
        overlaySettings = null;
      }

      if (!overlaySettings) {
        overlaySettings = {
          enabled: true,
          position: 'top-right',
          counters: { deaths: true, swears: true, screams: true, bits: false, channelPoints: false },
          theme: {
            backgroundColor: 'rgba(0,0,0,0.8)',
            borderColor: '#9146ff',
            textColor: '#ffffff',
            accentColor: '#f0f0f0'
          },
          animations: {
            enabled: true,
            showAlerts: true,
            celebrationEffects: true,
            bounceOnUpdate: true,
            fadeTransitions: true
          },
          display: {
            showLabels: true,
            showIcons: true,
            compactMode: false,
            hideWhenZero: false
          }
        };
      } else {
        overlaySettings.enabled = true;
      }

      await database.updateUserOverlaySettings(streamerId, overlaySettings);
    }

    // Handle chatCommands feature changes
    const wasChatEnabled = currentFeatures.chatCommands;
    const isChatBeingEnabled = features.chatCommands && !wasChatEnabled;
    const isChatBeingDisabled = !features.chatCommands && wasChatEnabled;

    if (isChatBeingEnabled) {
      console.log(`🤖 ChatCommands feature enabled for ${updatedUser.username} by moderator, starting Twitch bot...`);
      const twitchService = require('./multiTenantTwitchService');
      const success = await twitchService.connectUser(streamerId);
      if (success) {
        console.log(`✅ Twitch bot started for ${updatedUser.username}`);
      } else {
        console.log(`❌ Failed to start Twitch bot for ${updatedUser.username} - check auth tokens`);
      }
    } else if (isChatBeingDisabled) {
      console.log(`🤖 ChatCommands feature disabled for ${updatedUser.username} by moderator, stopping Twitch bot...`);
      const twitchService = require('./multiTenantTwitchService');
      await twitchService.disconnectUser(streamerId);
      console.log(`✅ Twitch bot stopped for ${updatedUser.username}`);
    }

    console.log(`✅ Moderator ${req.user.username} updated features for streamer ${updatedUser.username}`);

    res.json({
      message: 'Features updated successfully',
      streamer: {
        userId: updatedUser.twitchUserId,
        username: updatedUser.username,
        features: typeof updatedUser.features === 'string' ? JSON.parse(updatedUser.features) : updatedUser.features
      },
      moderator: req.user.username
    });
  } catch (error) {
    console.error('❌ Error updating streamer features:', error);
    res.status(500).json({ error: 'Failed to update features' });
  }
});

/**
 * Update streamer overlay settings (if moderator has access)
 * PUT /api/moderator/streamers/:streamerId/overlay
 */
router.put('/streamers/:streamerId/overlay', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const { overlaySettings } = req.body;

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    await database.updateUserOverlaySettings(streamerId, overlaySettings);

    // Broadcast the update to the streamer's connected clients
    req.io.to(`user:${streamerId}`).emit('overlaySettingsUpdate', {
      overlaySettings: overlaySettings
    });

    console.log(`✅ Moderator ${req.user.username} updated overlay settings for streamer ${streamer.username}`);

    res.json({
      message: 'Overlay settings updated successfully',
      overlaySettings,
      streamer: {
        userId: streamerId,
        username: streamer.username
      },
      moderator: req.user.username
    });
  } catch (error) {
    console.error('❌ Error updating streamer overlay settings:', error);
    res.status(500).json({ error: 'Failed to update overlay settings' });
  }
});

/**
 * Get streamer overlay settings (if moderator has access)
 * GET /api/moderator/streamers/:streamerId/overlay
 */
router.get('/streamers/:streamerId/overlay', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    let overlaySettings = streamer.overlaySettings;
    if (typeof overlaySettings === 'string') {
      try {
        overlaySettings = JSON.parse(overlaySettings);
      } catch (e) {
        overlaySettings = null;
      }
    }

    console.log(`👀 Moderator ${req.user.username} viewed overlay settings for streamer ${streamer.username}`);

    res.json({
      overlaySettings,
      streamer: {
        userId: streamerId,
        username: streamer.username
      }
    });
  } catch (error) {
    console.error('❌ Error fetching streamer overlay settings:', error);
    res.status(500).json({ error: 'Failed to fetch overlay settings' });
  }
});

/**
 * Update streamer Discord webhook settings (if moderator has access)
 * PUT /api/moderator/streamers/:streamerId/discord-webhook
 */
router.put('/streamers/:streamerId/discord-webhook', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const { webhookUrl, enabled } = req.body;

    // Validate webhook URL if provided
    if (webhookUrl && !webhookUrl.startsWith('https://discord.com/api/webhooks/')) {
      return res.status(400).json({ error: 'Invalid Discord webhook URL format' });
    }

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    // Update webhook URL
    await database.updateUserDiscordWebhook(streamerId, webhookUrl || '');

    // Update Discord notifications feature flag if enabled field is provided
    if (typeof enabled === 'boolean') {
      const currentFeatures = typeof streamer.features === 'string' ? JSON.parse(streamer.features) : streamer.features || {};
      currentFeatures.discordNotifications = enabled;
      await database.updateUserFeatures(streamerId, currentFeatures);
      console.log(`✅ Updated discordNotifications feature to ${enabled} for streamer ${streamer.username}`);
    }

    console.log(`✅ Moderator ${req.user.username} updated Discord webhook for streamer ${streamer.username}`);

    res.json({
      message: 'Discord webhook updated successfully',
      webhookUrl: webhookUrl || '',
      enabled: typeof enabled === 'boolean' ? enabled : await database.hasFeature(streamerId, 'discordNotifications'),
      streamer: {
        userId: streamerId,
        username: streamer.username
      },
      moderator: req.user.username
    });
  } catch (error) {
    console.error('❌ Error updating streamer Discord webhook:', error);
    res.status(500).json({ error: 'Failed to update Discord webhook' });
  }
});

/**
 * Get streamer Discord webhook settings (if moderator has access)
 * GET /api/moderator/streamers/:streamerId/discord-webhook
 */
router.get('/streamers/:streamerId/discord-webhook', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    const hasDiscordFeature = await database.hasFeature(streamerId, 'discordNotifications');

    console.log(`👀 Moderator ${req.user.username} viewed Discord webhook for streamer ${streamer.username}`);

    res.json({
      webhookUrl: streamer.discordWebhookUrl || '',
      enabled: hasDiscordFeature,
      streamer: {
        userId: streamerId,
        username: streamer.username
      }
    });
  } catch (error) {
    console.error('❌ Error fetching streamer Discord webhook:', error);
    res.status(500).json({ error: 'Failed to fetch Discord webhook' });
  }
});

/**
 * Get streamer's series saves (if moderator has access)
 * GET /api/moderator/streamers/:streamerId/series-saves
 */
router.get('/streamers/:streamerId/series-saves', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    const seriesSaves = await database.listSeriesSaves(streamerId);

    console.log(`📋 Moderator ${req.user.username} listed ${seriesSaves.length} series saves for streamer ${streamer.username}`);

    res.json({
      seriesSaves,
      streamer: {
        userId: streamerId,
        username: streamer.username
      },
      moderator: req.user.username
    });
  } catch (error) {
    console.error('❌ Error fetching streamer series saves:', error);
    res.status(500).json({ error: 'Failed to fetch series saves' });
  }
});

/**
 * Create a series save for streamer (if moderator has access)
 * POST /api/moderator/streamers/:streamerId/series-saves
 */
router.post('/streamers/:streamerId/series-saves', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const { seriesName, description } = req.body;

    if (!seriesName || !seriesName.trim()) {
      return res.status(400).json({ error: 'Series name is required' });
    }

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    // Get current counters
    const currentCounters = await database.getCounters(streamerId);

    // Create series save data
    const seriesData = {
      seriesName: seriesName.trim(),
      description: description || '',
      counters: {
        deaths: currentCounters.deaths || 0,
        swears: currentCounters.swears || 0,
        screams: currentCounters.screams || 0,
        bits: currentCounters.bits || 0
      },
      createdBy: moderatorId,
      createdByUsername: req.user.username,
      createdAt: new Date().toISOString()
    };

    const savedSeries = await database.saveSeries(streamerId, seriesData);

    console.log(`✅ Moderator ${req.user.username} created series save "${seriesName}" for streamer ${streamer.username}`);

    res.json({
      message: 'Series save created successfully',
      seriesSave: savedSeries,
      streamer: {
        userId: streamerId,
        username: streamer.username
      },
      moderator: req.user.username
    });
  } catch (error) {
    console.error('❌ Error creating series save:', error);
    res.status(500).json({ error: 'Failed to create series save' });
  }
});

/**
 * Load a series save for streamer (if moderator has access)
 * POST /api/moderator/streamers/:streamerId/series-saves/:seriesId/load
 */
router.post('/streamers/:streamerId/series-saves/:seriesId/load', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId, seriesId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    // Load the series
    const loadedCounters = await database.loadSeries(streamerId, seriesId);

    // Broadcast counter update to connected clients
    req.io.to(`user:${streamerId}`).emit('counterUpdate', loadedCounters);

    console.log(`🔄 Moderator ${req.user.username} loaded series save ${seriesId} for streamer ${streamer.username}`);

    res.json({
      message: 'Series save loaded successfully',
      counters: loadedCounters,
      seriesId,
      streamer: {
        userId: streamerId,
        username: streamer.username
      },
      moderator: req.user.username
    });
  } catch (error) {
    console.error('❌ Error loading series save:', error);
    if (error.message.includes('not found')) {
      res.status(404).json({ error: 'Series save not found' });
    } else {
      res.status(500).json({ error: 'Failed to load series save' });
    }
  }
});

/**
 * Delete a series save for streamer (if moderator has access)
 * DELETE /api/moderator/streamers/:streamerId/series-saves/:seriesId
 */
router.delete('/streamers/:streamerId/series-saves/:seriesId', requireAuth, requireModAccess, async (req, res) => {
  try {
    const { streamerId, seriesId } = req.params;
    const moderatorId = req.user.userId;

    // Check if moderator has access to this streamer
    const canManage = await database.canModeratorManageUser(moderatorId, streamerId);
    if (!canManage) {
      return res.status(403).json({ error: 'You do not have permission to manage this streamer' });
    }

    const streamer = await database.getUser(streamerId);
    if (!streamer) {
      return res.status(404).json({ error: 'Streamer not found' });
    }

    await database.deleteSeries(streamerId, seriesId);

    console.log(`🗑️ Moderator ${req.user.username} deleted series save ${seriesId} for streamer ${streamer.username}`);

    res.json({
      message: 'Series save deleted successfully',
      seriesId,
      streamer: {
        userId: streamerId,
        username: streamer.username
      },
      moderator: req.user.username
    });
  } catch (error) {
    console.error('❌ Error deleting series save:', error);
    if (error.message.includes('not found')) {
      res.status(404).json({ error: 'Series save not found' });
    } else {
      res.status(500).json({ error: 'Failed to delete series save' });
    }
  }
});

module.exports = router;

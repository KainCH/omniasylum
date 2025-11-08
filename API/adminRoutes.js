const express = require('express');
const database = require('./database');
const keyVault = require('./keyVault');
const { requireAuth, requireAdmin } = require('./authMiddleware');

const router = express.Router();

// Role hierarchy for permission checking
const roleHierarchy = {
  'streamer': 0,
  'moderator': 1,
  'manager': 2,
  'admin': 3
};

// Permission mapping for each role
const rolePermissions = {
  'streamer': ['view_own_counters', 'modify_own_counters', 'export_own_data'],
  'moderator': ['view_own_counters', 'modify_own_counters', 'export_own_data', 'manage_stream_sessions', 'view_overlay_settings'],
  'manager': ['view_own_counters', 'modify_own_counters', 'export_own_data', 'manage_stream_sessions', 'view_overlay_settings', 'manage_user_features', 'manage_overlay_settings', 'view_analytics'],
  'admin': ['*'] // All permissions
};

/**
 * Middleware to require specific role or higher
 */
function requireRole(requiredRole) {
  return (req, res, next) => {
    const userRole = req.user?.role || 'streamer';
    const userRoleLevel = roleHierarchy[userRole] || 0;
    const requiredRoleLevel = roleHierarchy[requiredRole] || 0;

    if (userRoleLevel >= requiredRoleLevel) {
      next();
    } else {
      res.status(403).json({
        error: 'Insufficient permissions',
        required: requiredRole,
        current: userRole
      });
    }
  };
}

/**
 * Middleware to require specific permission
 */
function requirePermission(permission) {
  return (req, res, next) => {
    const userRole = req.user?.role || 'streamer';
    const userPermissions = rolePermissions[userRole] || [];

    if (userPermissions.includes('*') || userPermissions.includes(permission)) {
      next();
    } else {
      res.status(403).json({
        error: 'Permission denied',
        required: permission,
        role: userRole
      });
    }
  };
}

/**
 * Get all users
 * GET /api/admin/users
 */
router.get('/users', requireAuth, requireRole('manager'), async (req, res) => {
  try {
    const users = await database.getAllUsers();

    // Don't send sensitive data and classify user status
    const sanitizedUsers = users.map(user => {
      const hasValidUserId = user.twitchUserId && user.twitchUserId !== 'undefined' && user.twitchUserId !== null;
      const hasMissingUsername = !user.username || user.username === 'undefined' || user.username === null;
      const hasMissingDisplayName = !user.displayName || user.displayName === 'undefined' || user.displayName === null;

      let userStatus = 'complete';
      if (!hasValidUserId) {
        userStatus = 'broken'; // No valid ID - truly unknown
      } else if (hasMissingUsername || hasMissingDisplayName) {
        userStatus = 'incomplete'; // Has ID but missing profile data
      }

      return {
        userId: user.twitchUserId,
        twitchUserId: user.twitchUserId, // Add this for frontend compatibility
        username: user.username,
        displayName: user.displayName,
        email: user.email,
        profileImageUrl: user.profileImageUrl,
        role: user.role,
        features: typeof user.features === 'string' ? JSON.parse(user.features) : user.features,
        isActive: user.isActive !== undefined ? user.isActive : true,
        createdAt: user.createdAt,
        lastLogin: user.lastLogin,
        discordWebhookUrl: user.discordWebhookUrl || '',
        userStatus: userStatus, // Add classification
        partitionKey: user.partitionKey || user.twitchUserId,
        rowKey: user.rowKey || user.twitchUserId
      };
    });

    res.json({
      users: sanitizedUsers,
      total: sanitizedUsers.length
    });
  } catch (error) {
    console.error('Error fetching users:', error);
    res.status(500).json({ error: 'Failed to fetch users' });
  }
});

/**
 * Get specific user details
 * GET /api/admin/users/:userId
 */
router.get('/users/:userId', requireAuth, requireRole('manager'), async (req, res) => {
  try {
    const user = await database.getUser(req.params.userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    const counters = await database.getCounters(req.params.userId);

    res.json({
      user: {
        userId: user.twitchUserId,
        username: user.username,
        displayName: user.displayName,
        email: user.email,
        profileImageUrl: user.profileImageUrl,
        role: user.role,
        features: typeof user.features === 'string' ? JSON.parse(user.features) : user.features,
        isActive: user.isActive !== undefined ? user.isActive : true,
        createdAt: user.createdAt,
        lastLogin: user.lastLogin
      },
      counters: counters
    });
  } catch (error) {
    console.error('Error fetching user:', error);
    res.status(500).json({ error: 'Failed to fetch user' });
  }
});

/**
 * Update user features
 * PUT /api/admin/users/:userId/features
 */
router.put('/users/:userId/features', requireAuth, requirePermission('manage_user_features'), async (req, res) => {
  try {
    const { features } = req.body;

    if (!features || typeof features !== 'object') {
      return res.status(400).json({ error: 'Invalid features object' });
    }

    // Check if streamOverlay feature is being enabled
    const user = await database.getUser(req.params.userId);
    const currentFeatures = typeof user.features === 'string' ? JSON.parse(user.features) : user.features || {};
    const wasOverlayEnabled = currentFeatures.streamOverlay;
    const isOverlayBeingEnabled = features.streamOverlay && !wasOverlayEnabled;

    const updatedUser = await database.updateUserFeatures(req.params.userId, features);

    // If streamOverlay feature was just enabled, automatically enable overlay settings
    if (isOverlayBeingEnabled) {
      console.log(`✅ StreamOverlay feature enabled for ${updatedUser.username}, auto-enabling overlay settings`);

      // Get current overlay settings or create default ones
      let overlaySettings;
      try {
        const currentSettings = await database.getUserOverlaySettings(req.params.userId);
        overlaySettings = currentSettings ?
          (typeof currentSettings.overlaySettings === 'string' ?
            JSON.parse(currentSettings.overlaySettings) :
            currentSettings.overlaySettings) : null;
      } catch (e) {
        overlaySettings = null;
      }

      // Create default overlay settings if none exist
      if (!overlaySettings) {
        overlaySettings = {
          enabled: true,
          position: 'top-right',
          counters: { deaths: true, swears: true, bits: false, channelPoints: false },
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
        // Just enable the existing settings
        overlaySettings.enabled = true;
      }

      // Update the overlay settings
      await database.updateUserOverlaySettings(req.params.userId, overlaySettings);
      console.log(`✅ Overlay settings auto-enabled for ${updatedUser.username}`);
    }

    // Handle chatCommands feature changes - start/stop Twitch bot
    const wasChatEnabled = currentFeatures.chatCommands;
    const isChatBeingEnabled = features.chatCommands && !wasChatEnabled;
    const isChatBeingDisabled = !features.chatCommands && wasChatEnabled;

    if (isChatBeingEnabled) {
      console.log(`🤖 ChatCommands feature enabled for ${updatedUser.username}, starting Twitch bot...`);

      // Import the Twitch service (need to require it here to avoid circular dependency)
      const twitchService = require('./multiTenantTwitchService');

      // Start the Twitch bot for this user
      const success = await twitchService.connectUser(req.params.userId);

      if (success) {
        console.log(`✅ Twitch bot started for ${updatedUser.username}`);
      } else {
        console.log(`❌ Failed to start Twitch bot for ${updatedUser.username} - check auth tokens`);
      }
    } else if (isChatBeingDisabled) {
      console.log(`🤖 ChatCommands feature disabled for ${updatedUser.username}, stopping Twitch bot...`);

      // Import the Twitch service
      const twitchService = require('./multiTenantTwitchService');

      // Stop the Twitch bot for this user
      await twitchService.disconnectUser(req.params.userId);
      console.log(`✅ Twitch bot stopped for ${updatedUser.username}`);
    }

    res.json({
      message: 'Features updated successfully',
      user: {
        userId: updatedUser.twitchUserId,
        username: updatedUser.username,
        features: typeof updatedUser.features === 'string' ? JSON.parse(updatedUser.features) : updatedUser.features
      }
    });
  } catch (error) {
    console.error('Error updating features:', error);
    res.status(500).json({ error: error.message || 'Failed to update features' });
  }
});

/**
 * Get user overlay settings
 * GET /api/admin/users/:userId/overlay-settings
 */
router.get('/users/:userId/overlay-settings', requireAuth, requirePermission('view_overlay_settings'), async (req, res) => {
  try {
    const settings = await database.getUserOverlaySettings(req.params.userId);

    if (!settings) {
      return res.status(404).json({ error: 'User not found' });
    }

    res.json({
      userId: req.params.userId,
      overlaySettings: settings
    });
  } catch (error) {
    console.error('❌ Error fetching user overlay settings:', error);
    res.status(500).json({ error: error.message || 'Failed to fetch overlay settings' });
  }
});

/**
 * Update user overlay settings
 * PUT /api/admin/users/:userId/overlay-settings
 */
router.put('/users/:userId/overlay-settings', requireAuth, requirePermission('manage_user_features'), async (req, res) => {
  try {
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

    const updatedUser = await database.updateUserOverlaySettings(req.params.userId, overlaySettings);

    console.log(`✅ Admin ${req.user.username} updated overlay settings for user ${req.params.userId}`);
    res.json({
      message: 'Overlay settings updated successfully',
      userId: req.params.userId,
      settings: overlaySettings
    });
  } catch (error) {
    console.error('❌ Error updating overlay settings:', error);
    res.status(500).json({ error: error.message || 'Failed to update overlay settings' });
  }
});

/**
 * Update user role
 * PUT /api/admin/users/:userId/role
 */
router.put('/users/:userId/role', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { role } = req.body;
    const targetUserId = req.params.userId;

    // Validate role
    if (!role || !roleHierarchy.hasOwnProperty(role)) {
      return res.status(400).json({
        error: 'Invalid role',
        validRoles: Object.keys(roleHierarchy)
      });
    }

    // Prevent non-admin from setting admin role
    if (role === 'admin' && req.user.role !== 'admin') {
      return res.status(403).json({ error: 'Only super admins can assign admin role' });
    }

    // Prevent user from demoting themselves if they're the only admin
    if (req.user.userId === targetUserId && req.user.role === 'admin') {
      const users = await database.getAllUsers();
      const adminCount = users.filter(u => u.role === 'admin').length;

      if (adminCount <= 1 && role !== 'admin') {
        return res.status(400).json({ error: 'Cannot demote the last admin user' });
      }
    }

    const updatedUser = await database.updateUserRole(targetUserId, role);

    res.json({
      message: 'Role updated successfully',
      user: {
        userId: updatedUser.twitchUserId,
        username: updatedUser.username,
        role: updatedUser.role
      }
    });
  } catch (error) {
    console.error('Error updating user role:', error);
    res.status(500).json({ error: error.message || 'Failed to update role' });
  }
});

/**
 * Enable/disable user account
 * PUT /api/admin/users/:userId/status
 */
router.put('/users/:userId/status', requireAuth, async (req, res) => {
  try {
    const { isActive } = req.body;
    const targetUserId = req.params.userId;
    const currentUser = req.user;

    if (typeof isActive !== 'boolean') {
      return res.status(400).json({ error: 'isActive must be a boolean' });
    }

    // Check permissions: Admin can modify any user, users can only activate themselves
    const isAdmin = currentUser.role === 'admin';
    const isSelfActivation = currentUser.userId === targetUserId && isActive === true;

    if (!isAdmin && !isSelfActivation) {
      return res.status(403).json({
        error: 'Insufficient permissions. You can only activate your own account.'
      });
    }

    // If not admin, prevent deactivation (only activation allowed for self)
    if (!isAdmin && !isActive) {
      return res.status(403).json({
        error: 'You cannot deactivate your own account. Contact an admin if needed.'
      });
    }

    const updatedUser = await database.updateUserStatus(targetUserId, isActive);

    // If enabling chat commands and user becomes active, start Twitch bot
    if (isActive) {
      const user = await database.getUser(targetUserId);
      if (user) {
        const features = typeof user.features === 'string' ? JSON.parse(user.features) : user.features || {};
        if (features.chatCommands) {
          const twitchService = req.app.get('twitchService');
          if (twitchService) {
            try {
              await twitchService.connectUser(targetUserId);
              console.log(`✅ Started Twitch bot for ${user.username} after activation`);
            } catch (error) {
              console.error(`❌ Failed to start Twitch bot for ${user.username}:`, error);
            }
          }
        }
      }
    }

    console.log(`✅ User status updated: ${updatedUser.username} is now ${isActive ? 'active' : 'inactive'} ${isSelfActivation ? '(self-activation)' : '(admin action)'}`);

    res.json({
      message: `User ${isActive ? 'activated' : 'deactivated'} successfully`,
      user: {
        userId: updatedUser.twitchUserId,
        username: updatedUser.username,
        isActive: updatedUser.isActive
      }
    });
  } catch (error) {
    console.error('Error updating status:', error);
    res.status(500).json({ error: error.message || 'Failed to update status' });
  }
});

/**
 * Get system statistics
 * GET /api/admin/stats
 */
router.get('/stats', requireAuth, requireRole('manager'), async (req, res) => {
  try {
    const users = await database.getAllUsers();

    const stats = {
      totalUsers: users.length,
      activeUsers: users.filter(u => u.isActive !== false).length,
      adminUsers: users.filter(u => u.role === 'admin').length,
      recentLogins: users
        .sort((a, b) => new Date(b.lastLogin) - new Date(a.lastLogin))
        .slice(0, 10)
        .map(u => ({
          username: u.username,
          displayName: u.displayName,
          lastLogin: u.lastLogin
        })),
      featureUsage: {}
    };

    // Calculate feature usage
    const featureNames = ['chatCommands', 'channelPoints', 'autoClip', 'customCommands', 'analytics', 'webhooks', 'bitsIntegration', 'streamOverlay', 'alertAnimations', 'streamAlerts'];
    featureNames.forEach(feature => {
      stats.featureUsage[feature] = users.filter(u => {
        try {
          const features = typeof u.features === 'string' ? JSON.parse(u.features) : u.features;
          return features && features[feature] === true;
        } catch {
          return false;
        }
      }).length;
    });

    res.json(stats);
  } catch (error) {
    console.error('Error fetching stats:', error);
    res.status(500).json({ error: 'Failed to fetch statistics' });
  }
});

/**
 * Delete user account (use with caution)
 * DELETE /api/admin/users/:userId
 */
router.delete('/users/:userId', requireAuth, requireAdmin, async (req, res) => {
  try {
    const userId = req.params.userId;

    // Try to get user first
    let user;
    try {
      user = await database.getUser(userId);
    } catch (error) {
      // User might not exist or be broken
      user = null;
    }

    // If user exists and is admin, prevent deletion
    if (user && user.role === 'admin') {
      return res.status(403).json({ error: 'Cannot delete admin accounts' });
    }

    // Try normal deletion first
    if (user && user.twitchUserId) {
      await database.deleteUser(userId);
      console.log(`✅ Admin ${req.user.username} deleted user: ${userId}`);
    } else {
      // If user doesn't exist or has no twitchUserId, try to delete by table keys
      // For broken records, userId might be the rowKey, partitionKey is usually 'user'
      await database.deleteUserByKeys('user', userId);
      console.log(`✅ Admin ${req.user.username} deleted broken user record: ${userId}`);
    }

    res.json({
      message: 'User deleted successfully',
      userId: userId
    });
  } catch (error) {
    console.error('❌ Error deleting user:', error);
    res.status(500).json({ error: 'Failed to delete user' });
  }
});

/**
 * Delete user by table keys (for broken records)
 * DELETE /api/admin/users/by-keys/:partitionKey/:rowKey
 */
router.delete('/users/by-keys/:partitionKey/:rowKey', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { partitionKey, rowKey } = req.params;

    await database.deleteUserByKeys(partitionKey, rowKey);

    console.log(`✅ Admin ${req.user.username} deleted user by keys: ${partitionKey}/${rowKey}`);
    res.json({
      message: 'User deleted successfully by table keys',
      partitionKey,
      rowKey
    });
  } catch (error) {
    console.error('❌ Error deleting user by keys:', error);
    res.status(500).json({ error: 'Failed to delete user by keys' });
  }
});

/**
 * Fetch Twitch user data including avatar
 */
async function fetchTwitchUserData(username) {
  try {
    const clientId = await keyVault.getSecret('TWITCH-CLIENT-ID');
    const clientSecret = await keyVault.getSecret('TWITCH-CLIENT-SECRET');

    // Get app access token
    const tokenResponse = await fetch('https://id.twitch.tv/oauth2/token', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded'
      },
      body: new URLSearchParams({
        client_id: clientId,
        client_secret: clientSecret,
        grant_type: 'client_credentials'
      })
    });

    const tokenData = await tokenResponse.json();
    if (!tokenResponse.ok) {
      console.error('Failed to get app token:', tokenData);
      return null;
    }

    // Fetch user data from Twitch
    const userResponse = await fetch(`https://api.twitch.tv/helix/users?login=${username}`, {
      headers: {
        'Authorization': `Bearer ${tokenData.access_token}`,
        'Client-Id': clientId
      }
    });

    const userData = await userResponse.json();
    if (userData.data && userData.data.length > 0) {
      return userData.data[0];
    }

    return null;
  } catch (error) {
    console.error('Error fetching Twitch user data:', error);
    return null;
  }
}

/**
 * Manually add a new user (admin only)
 * POST /api/admin/users
 */
router.post('/users', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { username, displayName, email, twitchUserId } = req.body;

    if (!username || !twitchUserId) {
      return res.status(400).json({ error: 'Username and Twitch User ID are required' });
    }

    // Check if user already exists
    const existingUser = await database.getUser(twitchUserId);
    if (existingUser) {
      return res.status(409).json({ error: 'User already exists' });
    }

    // Try to fetch real Twitch data for avatar and other info
    const twitchUserData = await fetchTwitchUserData(username);

    // Safely handle username
    const safeUsername = (username || '').toString().trim();
    if (!safeUsername) {
      return res.status(400).json({ error: 'Username cannot be empty' });
    }

    // Create user with fetched Twitch data or fallback to provided values
    const userData = {
      twitchUserId: twitchUserData?.id || twitchUserId,
      username: safeUsername.toLowerCase(),
      displayName: twitchUserData?.display_name || displayName || username,
      email: twitchUserData?.email || email || '',
      profileImageUrl: twitchUserData?.profile_image_url || '',
      accessToken: '', // Will need to be set via OAuth
      refreshToken: '', // Will need to be set via OAuth
      tokenExpiry: new Date().toISOString(),
      role: safeUsername.toLowerCase() === 'riress' ? 'admin' : 'streamer',
      isActive: true
    };

    const newUser = await database.saveUser(userData);

    // Provide informative message about avatar status
    const avatarStatus = twitchUserData?.profile_image_url ? 'with Twitch avatar' : 'with generated avatar';
    const message = `User created successfully ${avatarStatus}`;

    res.status(201).json({
      message,
      user: {
        userId: newUser.twitchUserId,
        twitchUserId: newUser.twitchUserId, // Add this for frontend compatibility
        username: newUser.username,
        displayName: newUser.displayName,
        email: newUser.email,
        role: newUser.role,
        features: typeof newUser.features === 'string' ? JSON.parse(newUser.features) : newUser.features,
        isActive: newUser.isActive,
        createdAt: newUser.createdAt
      }
    });
  } catch (error) {
    console.error('Error creating user:', error);
    res.status(500).json({ error: 'Failed to create user' });
  }
});

/**
 * Get stream sessions for all users
 * GET /api/admin/streams
 */
router.get('/streams', requireAuth, requireAdmin, async (req, res) => {
  try {
    const users = await database.getAllUsers();
    const streamSessions = [];

    for (const user of users) {
      try {
        const counters = await database.getCounters(user.twitchUserId);
        const currentSession = await database.getCurrentStreamSession(user.twitchUserId);

        streamSessions.push({
          userId: user.twitchUserId,
          username: user.username,
          displayName: user.displayName,
          isStreaming: !!currentSession,
          streamStartTime: currentSession?.startTime || null,
          sessionBits: counters.bits || 0,
          sessionDeaths: counters.deaths || 0,
          sessionSwears: counters.swears || 0,
          streamSettings: await database.getStreamSettings(user.twitchUserId)
        });
      } catch (error) {
        console.error(`Error getting stream data for ${user.username}:`, error);
        // Continue with other users
      }
    }

    res.json({
      sessions: streamSessions,
      totalActive: streamSessions.filter(s => s.isStreaming).length
    });
  } catch (error) {
    console.error('Error fetching stream sessions:', error);
    res.status(500).json({ error: 'Failed to fetch stream sessions' });
  }
});

/**
 * Reset stream state for a user (clear duplicate notification flag)
 * POST /api/admin/streams/:userId/reset
 */
router.post('/streams/:userId/reset', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { userId } = req.params;
    const streamMonitor = require('./streamMonitor');

    // Verify user exists
    const user = await database.getUser(userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Reset the stream state
    const success = await streamMonitor.resetStreamState(userId);

    if (success) {
      console.log(`🔧 Admin ${req.user.username} reset stream state for ${user.username}`);
      res.json({
        message: `Stream state reset successfully for ${user.displayName}`,
        userId: userId,
        resetBy: req.user.username,
        timestamp: new Date().toISOString()
      });
    } else {
      res.status(500).json({ error: 'Failed to reset stream state' });
    }
  } catch (error) {
    console.error('Error resetting stream state:', error);
    res.status(500).json({ error: error?.message || 'Failed to reset stream state' });
  }
});

/**
 * Available features configuration
 * GET /api/admin/features
 */
router.get('/features', requireAuth, requireRole('moderator'), async (req, res) => {
  res.json({
    features: [
      {
        id: 'chatCommands',
        name: 'Chat Commands',
        icon: '💬',
        description: 'Enable Twitch chat command integration (!deaths, !swears, etc.)',
        defaultEnabled: true,
        requiredRole: 'streamer'
      },
      {
        id: 'channelPoints',
        name: 'Channel Points',
        icon: '⭐',
        description: 'Allow viewers to use channel points to trigger counters',
        defaultEnabled: false,
        requiredRole: 'streamer'
      },
      {
        id: 'autoClip',
        name: 'Auto Clip Creation',
        icon: '🎬',
        description: 'Automatically create clips on milestone achievements',
        defaultEnabled: false,
        requiredRole: 'moderator'
      },
      {
        id: 'customCommands',
        name: 'Custom Commands',
        icon: '⚙️',
        description: 'Create custom chat commands beyond defaults',
        defaultEnabled: false,
        requiredRole: 'moderator'
      },
      {
        id: 'analytics',
        name: 'Analytics Dashboard',
        icon: '📊',
        description: 'Access to detailed analytics and historical data',
        defaultEnabled: false,
        requiredRole: 'manager'
      },
      {
        id: 'webhooks',
        name: 'Webhook Integration',
        icon: '🔗',
        description: 'Send counter updates to external services via webhooks',
        defaultEnabled: false,
        requiredRole: 'manager'
      },
      {
        id: 'bitsIntegration',
        name: 'Bits Celebrations',
        icon: '💎',
        description: 'Show celebration effects and thank you messages for bit donations (does not auto-increment counters)',
        defaultEnabled: false,
        requiredRole: 'streamer'
      },
      {
        id: 'streamOverlay',
        name: 'Stream Overlay',
        icon: '📺',
        description: 'Display counters as browser source overlay in OBS/streaming software',
        defaultEnabled: false,
        requiredRole: 'streamer'
      },
      {
        id: 'alertAnimations',
        name: 'Alert Animations',
        icon: '✨',
        description: 'Show animated pop-ups when counters change during stream',
        defaultEnabled: false,
        requiredRole: 'moderator'
      },
      {
        id: 'discordNotifications',
        name: 'Discord Notifications',
        icon: '🔔',
        description: 'Send counter updates and stream events to Discord channel via webhook',
        defaultEnabled: true,
        requiredRole: 'streamer'
      }
    ]
  });
});

/**
 * Get available roles and their permissions
 * GET /api/admin/roles
 */
router.get('/roles', requireAuth, requireRole('manager'), async (req, res) => {
  res.json({
    roles: [
      {
        id: 'streamer',
        name: 'Streamer',
        description: 'Basic counter access, can view own data only',
        permissions: rolePermissions.streamer,
        color: '#10b981',
        icon: '🎮',
        level: roleHierarchy.streamer
      },
      {
        id: 'moderator',
        name: 'Moderator',
        description: 'Can help manage stream counters and sessions during broadcasts',
        permissions: rolePermissions.moderator,
        color: '#f59e0b',
        icon: '🛡️',
        level: roleHierarchy.moderator
      },
      {
        id: 'manager',
        name: 'Manager',
        description: 'Can manage user features, analytics, and stream settings',
        permissions: rolePermissions.manager,
        color: '#8b5cf6',
        icon: '👔',
        level: roleHierarchy.manager
      },
      {
        id: 'admin',
        name: 'Super Admin',
        description: 'Full system access and user management',
        permissions: rolePermissions.admin,
        color: '#ef4444',
        icon: '👑',
        level: roleHierarchy.admin
      }
    ]
  });
});

/**
 * Get available permissions catalog
 * GET /api/admin/permissions
 */
router.get('/permissions', requireAuth, requireRole('manager'), async (req, res) => {
  res.json({
    permissions: [
      {
        id: 'view_own_counters',
        name: 'View Own Counters',
        description: 'View personal counter data and statistics',
        category: 'basic'
      },
      {
        id: 'modify_own_counters',
        name: 'Modify Own Counters',
        description: 'Increment, decrement, and reset personal counters',
        category: 'basic'
      },
      {
        id: 'export_own_data',
        name: 'Export Own Data',
        description: 'Export personal counter data and statistics',
        category: 'basic'
      },
      {
        id: 'manage_stream_sessions',
        name: 'Manage Stream Sessions',
        description: 'Start, stop, and configure stream sessions',
        category: 'streaming'
      },
      {
        id: 'view_overlay_settings',
        name: 'View Overlay Settings',
        description: 'Access stream overlay configuration',
        category: 'streaming'
      },
      {
        id: 'manage_user_features',
        name: 'Manage User Features',
        description: 'Enable/disable user features and integrations',
        category: 'management'
      },
      {
        id: 'view_analytics',
        name: 'View Analytics',
        description: 'Access detailed analytics and historical data',
        category: 'analytics'
      },
      {
        id: 'manage_all_users',
        name: 'Manage All Users',
        description: 'Create, edit, and delete user accounts',
        category: 'administration'
      },
      {
        id: 'system_configuration',
        name: 'System Configuration',
        description: 'Modify system-wide settings and configuration',
        category: 'administration'
      },
      {
        id: 'view_system_logs',
        name: 'View System Logs',
        description: 'Access application logs and audit trails',
        category: 'administration'
      }
    ]
  });
});

/**
 * Get user's overlay settings (admin)
 * GET /api/admin/users/:userId/overlay
 */
router.get('/users/:userId/overlay', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { userId } = req.params;
    const user = await database.getUser(userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    let overlaySettings = user.overlaySettings;
    if (typeof overlaySettings === 'string') {
      try {
        overlaySettings = JSON.parse(overlaySettings);
      } catch (e) {
        overlaySettings = null;
      }
    }

    res.json({ overlaySettings });
  } catch (error) {
    console.error('❌ Error fetching user overlay settings:', error);
    res.status(500).json({ error: 'Failed to fetch overlay settings' });
  }
});

/**
 * Update user's overlay settings (admin)
 * PUT /api/admin/users/:userId/overlay
 */
router.put('/users/:userId/overlay', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { userId } = req.params;
    const { overlaySettings } = req.body;

  await database.updateUserOverlaySettings(userId, overlaySettings);

    console.log(`✅ Admin ${req.user.username} updated overlay settings for user ${userId}`);
    res.json({
      message: 'Overlay settings updated successfully',
      overlaySettings
    });
  } catch (error) {
    console.error('❌ Error updating user overlay settings:', error);
    res.status(500).json({ error: 'Failed to update overlay settings' });
  }
});

/**
 * Get user's Discord webhook (admin)
 * GET /api/admin/users/:userId/discord-webhook
 */
router.get('/users/:userId/discord-webhook', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { userId } = req.params;
    const user = await database.getUser(userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    res.json({
      webhookUrl: user.discordWebhookUrl || '',
      enabled: user.features?.discordNotifications || false
    });
  } catch (error) {
    console.error('❌ Error fetching user Discord webhook:', error);
    res.status(500).json({ error: 'Failed to fetch Discord webhook' });
  }
});

/**
 * Update user's Discord webhook (admin)
 * PUT /api/admin/users/:userId/discord-webhook
 */
router.put('/users/:userId/discord-webhook', requireAuth, requireAdmin, async (req, res) => {
  const startTime = Date.now();
  let userId = 'unknown';

  try {
    userId = req.params.userId;
    const { webhookUrl } = req.body;

    // Validate webhook URL if provided
    if (webhookUrl && !webhookUrl.startsWith('https://discord.com/api/webhooks/')) {
      return res.status(400).json({ error: 'Invalid Discord webhook URL format' });
    }

    // Check if user exists first
    const user = await database.getUser(userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    await database.updateUserDiscordWebhook(userId, webhookUrl || '');

    console.log(`✅ Admin ${req.user.username} updated Discord webhook for user ${userId}`);

    res.json({
      message: 'Discord webhook updated successfully',
      webhookUrl,
      duration: `${duration}ms`
    });
  } catch (error) {
    const duration = Date.now() - startTime;
    console.error(`❌ [${startTime}] Error updating Discord webhook for user ${userId} (${duration}ms):`, error.message);
    console.error(`❌ [${startTime}] Full error:`, error);
    console.error(`❌ [${startTime}] Stack trace:`, error.stack);

    res.status(500).json({
      error: 'Failed to update Discord webhook',
      details: error.message,
      duration: `${duration}ms`
    });
  }
});

/**
 * Get user's Discord notification settings (admin)
 * GET /api/admin/users/:userId/discord-settings
 */
router.get('/users/:userId/discord-settings', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { userId } = req.params;
    const user = await database.getUser(userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Return Discord settings or defaults
    const defaultSettings = {
      templateStyle: 'asylum_themed',
      enabledNotifications: {
        death_milestone: true,
        swear_milestone: true,
        stream_start: true,
        stream_end: false, // Disabled by default - user preference
        follower_goal: false,
        subscriber_milestone: false,
        channel_point_redemption: false
      },
      milestoneThresholds: {
        deaths: [10, 25, 50, 100, 250, 500],
        swears: [25, 50, 100, 200, 500]
      }
    };

    const settings = user.discordSettings ? JSON.parse(user.discordSettings) : defaultSettings;

    res.json({ discordSettings: settings });
  } catch (error) {
    console.error('❌ Error getting Discord settings:', error);
    res.status(500).json({ error: 'Failed to get Discord settings' });
  }
});

/**
 * Update user's Discord notification settings (admin)
 * PUT /api/admin/users/:userId/discord-settings
 */
router.put('/users/:userId/discord-settings', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { userId } = req.params;
    const settings = req.body;

    console.log(`🔔 Admin ${req.user.username} updating Discord settings for user ${userId}`);

    // Validate settings structure
    if (!settings.templateStyle || !settings.enabledNotifications || !settings.milestoneThresholds) {
      return res.status(400).json({ error: 'Invalid Discord settings format' });
    }

    await database.updateUserDiscordSettings(userId, settings);

    console.log(`✅ Discord settings updated for user ${userId}`);
    res.json({ message: 'Discord settings updated successfully' });
  } catch (error) {
    console.error('❌ Error updating Discord settings:', error);
    res.status(500).json({ error: 'Failed to update Discord settings' });
  }
});

/**
 * Test endpoint for troubleshooting
 * PUT /api/admin/test-put
 */
router.put('/test-put', requireAuth, requireAdmin, async (req, res) => {
  console.log('🧪 Test PUT endpoint hit by:', req.user?.username);
  console.log('🧪 Request body:', req.body);
  res.json({
    message: 'PUT test successful',
    user: req.user?.username,
    timestamp: new Date().toISOString(),
    body: req.body
  });
});

/**
 * Test user's Discord webhook (admin)
 * POST /api/admin/users/:userId/discord-webhook/test
 */
router.post('/users/:userId/discord-webhook/test', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { userId } = req.params;
    const user = await database.getUser(userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    const webhookUrl = user.discordWebhookUrl;
    if (!webhookUrl) {
      return res.status(400).json({ error: 'No Discord webhook configured for this user' });
    }

    // Send test notification
    const embed = {
      title: `🧪 Test Notification for ${user.displayName}`,
      description: `This is a test notification sent by admin ${req.user.username}`,
      color: 0x9146FF,
      fields: [
        {
          name: 'User',
          value: `@${user.username}`,
          inline: true
        },
        {
          name: 'Status',
          value: 'Test successful ✅',
          inline: true
        }
      ],
      footer: {
        text: 'OmniAsylum Stream Counter - Admin Test',
        icon_url: user.profileImageUrl
      },
      timestamp: new Date().toISOString()
    };

    const response = await fetch(webhookUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ embeds: [embed] })
    });

    if (response.ok) {
      console.log(`✅ Admin ${req.user.username} sent test Discord notification for user ${userId}`);
      res.json({ message: 'Test notification sent successfully' });
    } else {
      const errorText = await response.text();
      console.error('Discord webhook error:', errorText);
      throw new Error('Discord webhook returned an error');
    }
  } catch (error) {
    console.error('❌ Error testing Discord webhook:', error);
    res.status(500).json({ error: 'Failed to send test notification' });
  }
});

/**
 * Find users with incomplete data
 * GET /api/admin/cleanup/unknown-users
 */
router.get('/cleanup/unknown-users', requireAuth, requireAdmin, async (req, res) => {
  try {
    const allUsers = await database.getAllUsers();

    // Find users with missing profile data but valid twitchUserId (these should be FIXED, not deleted)
    const incompleteUsers = allUsers.filter(user => {
      const hasValidUserId = user.twitchUserId && user.twitchUserId !== 'undefined' && user.twitchUserId !== null;
      const hasMissingUsername = !user.username || user.username === 'undefined' || user.username === null;
      const hasMissingDisplayName = !user.displayName || user.displayName === 'undefined' || user.displayName === null;

      // Include users with valid ID but missing username/displayName
      return hasValidUserId && (hasMissingUsername || hasMissingDisplayName);
    });

    // Find TRULY broken users (no valid twitchUserId) - these can be safely deleted
    const brokenUsers = allUsers.filter(user => {
      const hasInvalidUserId = !user.twitchUserId || user.twitchUserId === 'undefined' || user.twitchUserId === null;
      return hasInvalidUserId;
    });

    console.log(`🔍 Found ${incompleteUsers.length} users with incomplete data, ${brokenUsers.length} truly broken users`);

    res.json({
      count: incompleteUsers.length + brokenUsers.length,
      incomplete: incompleteUsers.length,
      broken: brokenUsers.length,
      users: [...incompleteUsers, ...brokenUsers].map(u => ({
        partitionKey: u.partitionKey || u.twitchUserId,
        rowKey: u.rowKey || u.twitchUserId,
        username: u.username,
        displayName: u.displayName,
        twitchUserId: u.twitchUserId,
        createdAt: u.createdAt,
        type: (u.twitchUserId && u.twitchUserId !== 'undefined' && u.twitchUserId !== null) ? 'incomplete' : 'broken'
      }))
    });
  } catch (error) {
    console.error('❌ Error finding users with issues:', error);
    res.status(500).json({ error: 'Failed to find users with issues' });
  }
});

/**
 * Delete unknown/invalid users
 * DELETE /api/admin/cleanup/unknown-users
 */
router.delete('/cleanup/unknown-users', requireAuth, requireAdmin, async (req, res) => {
  try {
    const allUsers = await database.getAllUsers();

    // Find users with TRULY missing or invalid data (must be explicitly 'undefined' string or null)
    // DO NOT delete users with empty strings or other falsy values
    const unknownUsers = allUsers.filter(user => {
      // Only consider truly broken records where the string is literally 'undefined' or null/missing entirely
      const hasInvalidUsername = user.username === 'undefined' || user.username === null || user.username === undefined;
      const hasInvalidUserId = user.twitchUserId === 'undefined' || user.twitchUserId === null || user.twitchUserId === undefined;
      const hasInvalidDisplayName = user.displayName === 'undefined' || user.displayName === null || user.displayName === undefined;

      // User must have at least username AND twitchUserId to be valid
      return hasInvalidUsername && hasInvalidUserId;
    });

    console.log(`🗑️ Deleting ${unknownUsers.length} unknown/invalid users (strict criteria)...`);

    let deleted = 0;
    for (const user of unknownUsers) {
      try {
        const userId = user.partitionKey || user.twitchUserId || user.rowKey;
        await database.deleteUser(userId);
        deleted++;
        console.log(`✅ Deleted unknown user: ${userId}`);
      } catch (error) {
        console.error(`❌ Failed to delete user:`, error);
      }
    }

    console.log(`✅ Admin ${req.user.username} deleted ${deleted} unknown users`);
    res.json({
      message: `Deleted ${deleted} unknown users`,
      deleted: deleted,
      total: unknownUsers.length
    });
  } catch (error) {
    console.error('❌ Error deleting unknown users:', error);
    res.status(500).json({ error: 'Failed to delete unknown users' });
  }
});

module.exports = router;

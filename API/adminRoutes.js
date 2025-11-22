const express = require('express');
const database = require('./database');
const keyVault = require('./keyVault');
const { requireAuth, requireAdmin, requireSuperAdmin, requireModAccess, requireRole } = require('./authMiddleware');

const router = express.Router();

// Role hierarchy for permission checking
const roleHierarchy = {
  'streamer': 0,
  'mod': 1,
  'admin': 2
};

// Permission mapping for each role
const rolePermissions = {
  'streamer': ['view_own_counters', 'modify_own_counters', 'export_own_data'],
  'mod': ['view_own_counters', 'modify_own_counters', 'export_own_data', 'manage_stream_sessions', 'view_overlay_settings', 'manage_user_features', 'manage_overlay_settings', 'view_analytics'],
  'admin': ['*'] // All permissions
};

/**
 * Get all users
 * GET /api/admin/users
 */
router.get('/users', requireAuth, requireRole('admin'), async (req, res) => {
  try {
    const users = await database.getAllUsers();

    // Debug: Log problematic users
    const brokenUsers = users.filter(user => !user.twitchUserId);
    if (brokenUsers.length > 0) {
      console.log('🔍 Found broken users without twitchUserId:', brokenUsers.map(u => ({
        username: u.username,
        partitionKey: u.partitionKey,
        rowKey: u.rowKey,
        keys: Object.keys(u)
      })));
    }

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

      let features = typeof user.features === 'string' ? JSON.parse(user.features) : user.features;
      
      // Apply defaults if features are missing
      if (!features) {
        features = {
          chatCommands: true,
          channelPoints: false,
          autoClip: false,
          customCommands: false,
          analytics: false,
          streamOverlay: false,
          alertAnimations: false,
          discordNotifications: true,
          discordWebhook: false,
          templateStyle: 'asylum_themed',
          streamAlerts: true
        };
      }

      return {
        userId: user.twitchUserId,
        twitchUserId: user.twitchUserId, // Add this for frontend compatibility
        username: user.username,
        displayName: user.displayName,
        email: user.email,
        profileImageUrl: user.profileImageUrl,
        role: user.role,
        features: features,
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
 * Get user diagnostics (for debugging broken users)
 * GET /api/admin/users/diagnostics
 */
router.get('/users/diagnostics', requireAuth, requireAdmin, async (req, res) => {
  try {
    const users = await database.getAllUsers();

    const diagnostics = {
      totalUsers: users.length,
      validUsers: [],
      brokenUsers: [],
      suspiciousUsers: []
    };

    for (const user of users) {
      const userInfo = {
        username: user.username || 'MISSING',
        displayName: user.displayName || 'MISSING',
        twitchUserId: user.twitchUserId || 'MISSING',
        partitionKey: user.partitionKey || 'MISSING',
        rowKey: user.rowKey || 'MISSING',
        email: user.email ? 'EXISTS' : 'MISSING',
        profileImageUrl: user.profileImageUrl ? 'EXISTS' : 'MISSING',
        role: user.role || 'MISSING',
        isActive: user.isActive,
        allFields: Object.keys(user)
      };

      if (!user.twitchUserId) {
        // Assign random 3-digit ID for broken users so they can be deleted
        const randomId = Math.floor(100 + Math.random() * 900).toString();
        userInfo.tempDeleteId = randomId;
        userInfo.canDelete = true;
        diagnostics.brokenUsers.push(userInfo);
      } else if (!user.username || !user.displayName) {
        diagnostics.suspiciousUsers.push(userInfo);
      } else {
        diagnostics.validUsers.push(userInfo);
      }
    }

    res.json(diagnostics);
  } catch (error) {
    console.error('Error running user diagnostics:', error);
    res.status(500).json({ error: 'Failed to run diagnostics' });
  }
});

/**
 * Get user overlay settings (admin)
 * GET /api/admin/users/:userId/overlay-settings
 * Alias: /api/admin/users/:userId/overlay
 */
const getOverlaySettings = async (req, res) => {
  try {
    const { userId } = req.params;
    const user = await database.getUser(userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Get overlay settings or defaults
    let overlaySettings = user.overlaySettings;

    // Parse if string
    if (typeof overlaySettings === 'string') {
      try {
        overlaySettings = JSON.parse(overlaySettings);
      } catch (e) {
        console.error('Error parsing overlay settings:', e);
        overlaySettings = null;
      }
    }

    if (!overlaySettings) {
      overlaySettings = {
        enabled: false,
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
      settings: overlaySettings,
      overlaySettings: overlaySettings // Support both formats
    });
  } catch (error) {
    console.error('Error fetching user overlay settings (admin):', error);
    res.status(500).json({ error: 'Failed to fetch overlay settings' });
  }
};

router.get('/users/:userId/overlay-settings', requireAuth, requireAdmin, getOverlaySettings);
router.get('/users/:userId/overlay', requireAuth, requireAdmin, getOverlaySettings);

/**
 * Update user overlay settings (admin)
 * PUT /api/admin/users/:userId/overlay-settings
 * Alias: /api/admin/users/:userId/overlay
 */
const updateOverlaySettings = async (req, res) => {
  try {
    const { userId } = req.params;
    let newSettings = req.body;

    // Handle wrapped settings object
    if (newSettings.overlaySettings) {
      newSettings = newSettings.overlaySettings;
    }

    // Validate settings structure
    if (!newSettings || typeof newSettings !== 'object') {
      return res.status(400).json({ error: 'Invalid settings format' });
    }

    const user = await database.getUser(userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Update user with new overlay settings
    await database.updateUserOverlaySettings(userId, newSettings);

    // Broadcast to connected clients
    const io = req.app.get('io');
    if (io) {
      io.to(`user:${userId}`).emit('overlaySettingsUpdate', newSettings);
    }

    res.json({
      message: 'Overlay settings updated successfully',
      settings: newSettings,
      overlaySettings: newSettings
    });
  } catch (error) {
    console.error('Error updating user overlay settings (admin):', error);
    res.status(500).json({ error: 'Failed to update overlay settings' });
  }
};

router.put('/users/:userId/overlay-settings', requireAuth, requireAdmin, updateOverlaySettings);
router.put('/users/:userId/overlay', requireAuth, requireAdmin, updateOverlaySettings);

/**
 * Get specific user details
 * GET /api/admin/users/:userId
 */
router.get('/users/:userId', requireAuth, requireAdmin, async (req, res) => {
  try {
    const user = await database.getUser(req.params.userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    const counters = await database.getCounters(req.params.userId);

    let features = typeof user.features === 'string' ? JSON.parse(user.features) : user.features;
    
    // Apply defaults if features are missing
    if (!features) {
      features = {
        chatCommands: true,
        channelPoints: false,
        autoClip: false,
        customCommands: false,
        analytics: false,
        streamOverlay: false,
        alertAnimations: false,
        discordNotifications: true,
        discordWebhook: false,
        templateStyle: 'asylum_themed',
        streamAlerts: true
      };
    }

    res.json({
      user: {
        userId: user.twitchUserId,
        username: user.username,
        displayName: user.displayName,
        email: user.email,
        profileImageUrl: user.profileImageUrl,
        role: user.role,
        features: features,
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
router.put('/users/:userId/features', requireAuth, requireModAccess, async (req, res) => {
  try {
    console.log('🔍 Feature update request for user:', req.params.userId);
    console.log('🔍 Raw request body:', JSON.stringify(req.body, null, 2));
    console.log('🔍 Request body keys:', Object.keys(req.body));

    // Handle both formats: { features: {...} } and direct features object
    let features;
    if (req.body.features) {
      // New format: { features: { ... } }
      features = req.body.features;
      console.log('🔍 Using nested features format');
    } else {
      // Legacy format: direct features in body
      features = req.body;
      console.log('🔍 Using direct features format');
    }

    console.log('🔍 Processed features:', JSON.stringify(features, null, 2));
    console.log('🔍 Features type:', typeof features);
    console.log('🔍 Features is object:', typeof features === 'object');
    console.log('🔍 Features is truthy:', !!features);
    console.log('🔍 Features is array:', Array.isArray(features));

    // Validate features object
    if (!features || typeof features !== 'object' || Array.isArray(features)) {
      console.log('❌ Feature validation failed - invalid object');
      console.log('❌ Full request details:', {
        userId: req.params.userId,
        bodyKeys: Object.keys(req.body),
        bodyType: typeof req.body,
        featuresType: typeof features,
        isArray: Array.isArray(features)
      });
      return res.status(400).json({
        error: 'Invalid features object',
        details: {
          received: features,
          expected: 'object',
          type: typeof features
        }
      });
    }

    // Additional validation: ensure it looks like a features object
    const validFeatureKeys = [
      'chatCommands', 'channelPoints', 'autoClip', 'customCommands', 'analytics',
      'webhooks', 'bitsIntegration', 'streamOverlay', 'alertAnimations',
      'discordNotifications', 'discordWebhook', 'templateStyle', 'streamAlerts'
    ];

    const receivedKeys = Object.keys(features);
    const hasValidKeys = receivedKeys.some(key => validFeatureKeys.includes(key));

    if (!hasValidKeys) {
      console.log('❌ No valid feature keys found in request');
      console.log('❌ Received keys:', receivedKeys);
      console.log('❌ Valid keys:', validFeatureKeys);
      return res.status(400).json({
        error: 'Invalid features object - no recognized feature keys',
        details: {
          receivedKeys,
          validKeys: validFeatureKeys
        }
      });
    }

    console.log('✅ Features validation passed');
    console.log('✅ Valid feature keys found:', receivedKeys.filter(key => validFeatureKeys.includes(key)));

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
router.get('/stats', requireAuth, requireRole('admin'), async (req, res) => {
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
 * Get all user access requests
 * GET /api/admin/user-requests
 */
router.get('/user-requests', requireAuth, requireAdmin, async (req, res) => {
  try {
    const requests = await database.getAllUserRequests();
    res.json(requests);
  } catch (error) {
    console.error('❌ Error getting user requests:', error);
    res.status(500).json({ error: 'Failed to get user requests' });
  }
});

/**
 * Approve user access request
 * POST /api/admin/user-requests/:requestId/approve
 */
router.post('/user-requests/:requestId/approve', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { requestId } = req.params;
    const { adminNotes } = req.body;

    const newUser = await database.approveUserRequest(requestId, req.user.userId);

    console.log(`✅ Admin ${req.user.username} approved user request: ${requestId}`);

    res.json({
      message: 'User request approved and account created',
      user: {
        twitchUserId: newUser.twitchUserId,
        username: newUser.username,
        displayName: newUser.displayName
      }
    });
  } catch (error) {
    console.error('❌ Error approving user request:', error);
    res.status(500).json({ error: error.message || 'Failed to approve user request' });
  }
});

/**
 * Reject user access request
 * POST /api/admin/user-requests/:requestId/reject
 */
router.post('/user-requests/:requestId/reject', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { requestId } = req.params;
    const { adminNotes } = req.body;

    await database.updateUserRequestStatus(requestId, 'rejected', req.user.userId, adminNotes || 'Request rejected');

    console.log(`❌ Admin ${req.user.username} rejected user request: ${requestId}`);

    res.json({ message: 'User request rejected' });
  } catch (error) {
    console.error('❌ Error rejecting user request:', error);
    res.status(500).json({ error: error.message || 'Failed to reject user request' });
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
        // Skip users without a valid twitchUserId
        if (!user.twitchUserId) {
          console.log(`⚠️ Skipping user ${user.username || 'unknown'} - missing twitchUserId`);
          continue;
        }

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
          sessionScreams: counters.screams || 0,
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
router.get('/roles', requireAuth, requireRole('admin'), async (req, res) => {
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
        id: 'mod',
        name: 'Mod',
        description: 'Technical mod linked to specific streamers - can configure settings for assigned streamers',
        permissions: rolePermissions.mod,
        color: '#8b5cf6',
        icon: '�',
        level: roleHierarchy.mod
      },
      {
        id: 'admin',
        name: 'Admin',
        description: 'System administrator - can see all user profiles and manage system settings',
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
router.get('/permissions', requireAuth, requireRole('admin'), async (req, res) => {
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

module.exports = router;

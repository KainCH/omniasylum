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
  'manager': ['view_own_counters', 'modify_own_counters', 'export_own_data', 'manage_stream_sessions', 'view_overlay_settings', 'manage_user_features', 'view_analytics'],
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

    // Don't send sensitive data
    const sanitizedUsers = users.map(user => ({
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
      lastLogin: user.lastLogin
    }));

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

    const updatedUser = await database.updateUserFeatures(req.params.userId, features);

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
    if (req.user.twitchUserId === targetUserId && req.user.role === 'admin') {
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
router.put('/users/:userId/status', requireAuth, requireAdmin, async (req, res) => {
  try {
    const { isActive } = req.body;

    if (typeof isActive !== 'boolean') {
      return res.status(400).json({ error: 'isActive must be a boolean' });
    }

    const updatedUser = await database.updateUserStatus(req.params.userId, isActive);

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
    const featureNames = ['chatCommands', 'channelPoints', 'autoClip', 'customCommands', 'analytics', 'webhooks', 'bitsIntegration', 'streamOverlay', 'alertAnimations'];
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
    // Prevent deleting admin accounts
    const user = await database.getUser(req.params.userId);

    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    if (user.role === 'admin') {
      return res.status(403).json({ error: 'Cannot delete admin accounts' });
    }

    await database.deleteUser(req.params.userId);

    res.json({
      message: 'User deleted successfully',
      userId: req.params.userId
    });
  } catch (error) {
    console.error('Error deleting user:', error);
    res.status(500).json({ error: 'Failed to delete user' });
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

    // Create user with fetched Twitch data or fallback to provided values
    const userData = {
      twitchUserId: twitchUserData?.id || twitchUserId,
      username: username.toLowerCase(),
      displayName: twitchUserData?.display_name || displayName || username,
      email: twitchUserData?.email || email || '',
      profileImageUrl: twitchUserData?.profile_image_url || '',
      accessToken: '', // Will need to be set via OAuth
      refreshToken: '', // Will need to be set via OAuth
      tokenExpiry: new Date().toISOString(),
      role: username.toLowerCase() === 'riress' ? 'admin' : 'streamer',
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
 * Available features configuration
 * GET /api/admin/features
 */
router.get('/features', requireAuth, requireRole('moderator'), async (req, res) => {
  res.json({
    features: [
      {
        id: 'chatCommands',
        name: 'Chat Commands',
        description: 'Enable Twitch chat command integration (!deaths, !swears, etc.)',
        defaultEnabled: true,
        requiredRole: 'streamer'
      },
      {
        id: 'channelPoints',
        name: 'Channel Points',
        description: 'Allow viewers to use channel points to trigger counters',
        defaultEnabled: false,
        requiredRole: 'streamer'
      },
      {
        id: 'autoClip',
        name: 'Auto Clip Creation',
        description: 'Automatically create clips on milestone achievements',
        defaultEnabled: false,
        requiredRole: 'moderator'
      },
      {
        id: 'customCommands',
        name: 'Custom Commands',
        description: 'Create custom chat commands beyond defaults',
        defaultEnabled: false,
        requiredRole: 'moderator'
      },
      {
        id: 'analytics',
        name: 'Analytics Dashboard',
        description: 'Access to detailed analytics and historical data',
        defaultEnabled: false,
        requiredRole: 'manager'
      },
      {
        id: 'webhooks',
        name: 'Webhook Integration',
        description: 'Send counter updates to external services via webhooks',
        defaultEnabled: false,
        requiredRole: 'manager'
      },
      {
        id: 'bitsIntegration',
        name: 'Bits Celebrations',
        description: 'Show celebration effects and thank you messages for bit donations (does not auto-increment counters)',
        defaultEnabled: false,
        requiredRole: 'streamer'
      },
      {
        id: 'streamOverlay',
        name: 'Stream Overlay',
        description: 'Display counters as browser source overlay in OBS/streaming software',
        defaultEnabled: false,
        requiredRole: 'streamer'
      },
      {
        id: 'alertAnimations',
        name: 'Alert Animations',
        description: 'Show animated pop-ups when counters change during stream',
        defaultEnabled: false,
        requiredRole: 'moderator'
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

module.exports = router;

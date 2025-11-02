const express = require('express');
const database = require('./database');
const { requireAdmin } = require('./authMiddleware');

const router = express.Router();

/**
 * Get all users
 * GET /api/admin/users
 */
router.get('/users', requireAdmin, async (req, res) => {
  try {
    const users = await database.getAllUsers();

    // Don't send sensitive data
    const sanitizedUsers = users.map(user => ({
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
router.get('/users/:userId', requireAdmin, async (req, res) => {
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
router.put('/users/:userId/features', requireAdmin, async (req, res) => {
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
 * Enable/disable user account
 * PUT /api/admin/users/:userId/status
 */
router.put('/users/:userId/status', requireAdmin, async (req, res) => {
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
router.get('/stats', requireAdmin, async (req, res) => {
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
    const featureNames = ['chatCommands', 'channelPoints', 'autoClip', 'customCommands', 'analytics', 'webhooks'];
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
router.delete('/users/:userId', requireAdmin, async (req, res) => {
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
 * Manually add a new user (admin only)
 * POST /api/admin/users
 */
router.post('/users', requireAdmin, async (req, res) => {
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

    // Create user with default values
    const userData = {
      twitchUserId,
      username: username.toLowerCase(),
      displayName: displayName || username,
      email: email || '',
      profileImageUrl: '',
      accessToken: '', // Will need to be set via OAuth
      refreshToken: '', // Will need to be set via OAuth
      tokenExpiry: new Date().toISOString(),
      role: username.toLowerCase() === 'riress' ? 'admin' : 'streamer',
      isActive: true
    };

    const newUser = await database.saveUser(userData);

    res.status(201).json({
      message: 'User created successfully',
      user: {
        userId: newUser.twitchUserId,
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
 * Available features configuration
 * GET /api/admin/features
 */
router.get('/features', requireAdmin, async (req, res) => {
  res.json({
    features: [
      {
        id: 'chatCommands',
        name: 'Chat Commands',
        description: 'Enable Twitch chat command integration (!deaths, !swears, etc.)',
        defaultEnabled: true
      },
      {
        id: 'channelPoints',
        name: 'Channel Points',
        description: 'Allow viewers to use channel points to trigger counters',
        defaultEnabled: false
      },
      {
        id: 'autoClip',
        name: 'Auto Clip Creation',
        description: 'Automatically create clips on milestone achievements',
        defaultEnabled: false
      },
      {
        id: 'customCommands',
        name: 'Custom Commands',
        description: 'Create custom chat commands beyond defaults',
        defaultEnabled: false
      },
      {
        id: 'analytics',
        name: 'Analytics Dashboard',
        description: 'Access to detailed analytics and historical data',
        defaultEnabled: false
      },
      {
        id: 'webhooks',
        name: 'Webhook Integration',
        description: 'Send counter updates to external services via webhooks',
        defaultEnabled: false
      },
      {
        id: 'bitsIntegration',
        name: 'Bits Celebrations',
        description: 'Show celebration effects and thank you messages for bit donations (does not auto-increment counters)',
        defaultEnabled: false
      },
      {
        id: 'streamOverlay',
        name: 'Stream Overlay',
        description: 'Display counters as browser source overlay in OBS/streaming software',
        defaultEnabled: false
      },
      {
        id: 'alertAnimations',
        name: 'Alert Animations',
        description: 'Show animated pop-ups when counters change during stream',
        defaultEnabled: false
      }
    ]
  });
});

module.exports = router;

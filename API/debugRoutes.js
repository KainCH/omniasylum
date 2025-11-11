const express = require('express');
const router = express.Router();
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

/**
 * Test webhook save functionality
 * POST /api/debug/test-webhook-save
 */
router.post('/test-webhook-save', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    const testWebhookUrl = 'https://discord.com/api/webhooks/1234567890/test-webhook-token-12345';

    console.log(`🧪 DEBUG: Testing webhook save for user ${req.user.username} (ID: ${userId})`);

    // Step 1: Get current user state
    console.log('1. Getting current user data...');
    const currentUser = await database.getUser(userId);
    console.log('Current user data:', {
      twitchUserId: currentUser?.twitchUserId,
      username: currentUser?.username,
      discordWebhookUrl: currentUser?.discordWebhookUrl,
      partitionKey: currentUser?.partitionKey,
      rowKey: currentUser?.rowKey
    });

    // Step 2: Test the save operation
    console.log('2. Attempting to save webhook...');
    const saveResult = await database.updateUserDiscordWebhook(userId, testWebhookUrl);
    console.log('Save result:', {
      twitchUserId: saveResult?.twitchUserId,
      username: saveResult?.username,
      discordWebhookUrl: saveResult?.discordWebhookUrl ? `${saveResult.discordWebhookUrl.substring(0, 50)}...` : 'EMPTY'
    });

    // Step 3: Verify by reading back
    console.log('3. Verifying save by reading back...');
    const verification = await database.getUserDiscordWebhook(userId);
    console.log('Verification result:', verification);

    // Step 4: Check Azure Table directly
    console.log('4. Checking Azure Table Storage directly...');
    const directUser = await database.getUser(userId);
    console.log('Direct table read:', {
      twitchUserId: directUser?.twitchUserId,
      username: directUser?.username,
      discordWebhookUrl: directUser?.discordWebhookUrl
    });

    res.json({
      success: true,
      message: 'Webhook save test completed',
      results: {
        currentUser: {
          twitchUserId: currentUser?.twitchUserId,
          username: currentUser?.username,
          discordWebhookUrl: currentUser?.discordWebhookUrl
        },
        saveResult: {
          twitchUserId: saveResult?.twitchUserId,
          discordWebhookUrl: saveResult?.discordWebhookUrl ? 'SAVED' : 'EMPTY'
        },
        verification,
        directTableRead: {
          twitchUserId: directUser?.twitchUserId,
          discordWebhookUrl: directUser?.discordWebhookUrl
        }
      }
    });

  } catch (error) {
    console.error('❌ Webhook save test failed:', error);
    res.status(500).json({
      success: false,
      error: error.message,
      details: {
        name: error.name,
        statusCode: error.statusCode,
        code: error.code
      }
    });
  }
});

/**
 * Test stream notification trigger (simulate stream going live)
 * POST /api/debug/test-stream-notification
 */
router.post('/test-stream-notification', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    console.log(`🧪 DEBUG: Testing stream notification for user ${req.user.username} (ID: ${userId})`);

    // Import streamMonitor
    const streamMonitor = require('./streamMonitor');

    // Check if user is being monitored
    const isMonitored = streamMonitor.connectedUsers && streamMonitor.connectedUsers.has(userId);
    if (!isMonitored) {
      return res.status(400).json({
        success: false,
        error: 'User not being monitored via EventSub',
        recommendation: 'Start monitoring first'
      });
    }

    // Get user data
    const user = await database.getUser(userId);
    const webhookData = await database.getUserDiscordWebhook(userId);

    if (!webhookData?.webhookUrl) {
      return res.status(400).json({
        success: false,
        error: 'No Discord webhook configured',
        recommendation: 'Configure Discord webhook first'
      });
    }

    // Create mock stream event
    const mockStreamEvent = {
      id: `test_stream_${Date.now()}`,
      broadcasterId: userId,
      broadcasterName: user.username,
      broadcasterLogin: user.username.toLowerCase(),
      startedAt: new Date()
    };

    console.log(`🚀 Triggering test stream notification for ${user.username}...`);

    // Trigger the stream online handler
    await streamMonitor.handleStreamOnline(mockStreamEvent, userId);

    res.json({
      success: true,
      message: `Test stream notification triggered for ${user.username}`,
      mockEvent: {
        streamId: mockStreamEvent.id,
        broadcaster: mockStreamEvent.broadcasterName,
        startedAt: mockStreamEvent.startedAt
      },
      webhookConfigured: true
    });

  } catch (error) {
    console.error('❌ Stream notification test failed:', error);
    res.status(500).json({
      success: false,
      error: error.message,
      details: {
        name: error.name,
        statusCode: error.statusCode,
        code: error.code
      }
    });
  }
});

/**
 * Test webhook read functionality
 * GET /api/debug/test-webhook-read
 */
router.get('/test-webhook-read', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;

    console.log(`🧪 DEBUG: Testing webhook read for user ${req.user.username} (ID: ${userId})`);

    // Test the getUserDiscordWebhook function
    const webhookData = await database.getUserDiscordWebhook(userId);
    console.log('Webhook read result:', webhookData);

    res.json({
      success: true,
      webhookData
    });

  } catch (error) {
    console.error('❌ Webhook read test failed:', error);
    res.status(500).json({
      success: false,
      error: error.message
    });
  }
});

/**
 * Clean up user data in Azure Table Storage
 * POST /api/debug/cleanup-user-data
 */
router.post('/cleanup-user-data', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;

    console.log(`🧹 DEBUG: Cleaning up user data for ${req.user.username} (ID: ${userId})`);

    // Step 1: Get current user state
    console.log('1. Getting current user data from Azure...');
    const currentUser = await database.getUser(userId);

    if (!currentUser) {
      return res.status(404).json({
        success: false,
        error: 'User not found in database'
      });
    }

    console.log('Current user data keys:', Object.keys(currentUser));

    // Step 2: Identify problematic fields
    const problematicFields = [];

    // Check for test webhook data
    if (currentUser.discordWebhookUrl && currentUser.discordWebhookUrl.includes('test-webhook-token')) {
      problematicFields.push('discordWebhookUrl (contains test data)');
    }

    // Check for duplicate/malformed field names
    const fieldVariations = [
      'DiscordWebhookUrl',
      'discord_webhook_url',
      'webhook_url',
      'webhookUrl'
    ];

    fieldVariations.forEach(field => {
      if (currentUser[field]) {
        problematicFields.push(`${field} (duplicate/wrong case)`);
      }
    });

    console.log('Problematic fields found:', problematicFields);

    // Step 3: Clean the user data if needed
    if (problematicFields.length > 0) {
      console.log('Cleaning up problematic fields...');

      // Reset webhook URL to empty (clean slate)
      const cleanResult = await database.updateUserDiscordWebhook(userId, '');
      console.log('Webhook reset result:', {
        success: cleanResult ? 'YES' : 'NO',
        newWebhookUrl: cleanResult?.discordWebhookUrl || 'EMPTY'
      });

      // Step 4: Verify cleanup
      console.log('Verifying cleanup...');
      const verifyUser = await database.getUser(userId);
      const verifyWebhook = await database.getUserDiscordWebhook(userId);

      console.log('Verification results:', {
        userKeys: Object.keys(verifyUser),
        webhookUrl: verifyWebhook?.webhookUrl || 'EMPTY',
        webhookEnabled: verifyWebhook?.enabled || false
      });

      res.json({
        success: true,
        message: 'User data cleaned successfully',
        results: {
          problematicFieldsFound: problematicFields,
          cleanupPerformed: true,
          currentWebhookUrl: verifyWebhook?.webhookUrl || '',
          userDataKeys: Object.keys(verifyUser)
        }
      });

    } else {
      console.log('No problematic fields found, user data is clean');

      res.json({
        success: true,
        message: 'User data is already clean',
        results: {
          problematicFieldsFound: [],
          cleanupPerformed: false,
          currentWebhookUrl: currentUser.discordWebhookUrl || '',
          userDataKeys: Object.keys(currentUser)
        }
      });
    }

  } catch (error) {
    console.error('❌ User data cleanup failed:', error);
    res.status(500).json({
      success: false,
      error: error.message,
      details: {
        name: error.name,
        statusCode: error.statusCode,
        code: error.code
      }
    });
  }
});

/**
 * Clean Discord webhook data issues
 * POST /api/debug/clean-discord-webhook
 */
router.post('/clean-discord-webhook', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    console.log(`🧹 DEBUG: Cleaning Discord webhook for user ${req.user.username} (ID: ${userId})`);

    // Import the cleanup class
    const DiscordWebhookCleanup = require('./cleanup-discord-webhook');
    const cleanup = new DiscordWebhookCleanup();

    if (!(await cleanup.initialize())) {
      throw new Error('Failed to initialize cleanup service');
    }

    // Perform the cleanup
    const result = await cleanup.cleanUserWebhookData(userId);

    console.log('🧹 Webhook cleanup result:', result);

    res.json({
      success: true,
      message: 'Discord webhook cleanup completed',
      cleanupResult: result,
      nextSteps: 'You can now try saving your Discord webhook again'
    });

  } catch (error) {
    console.error('❌ Discord webhook cleanup failed:', error);
    res.status(500).json({
      success: false,
      error: error.message,
      details: {
        name: error.name,
        statusCode: error.statusCode,
        code: error.code
      }
    });
  }
});

/**
 * Start EventSub monitoring for current user
 * POST /api/debug/start-monitoring
 */
router.post('/start-monitoring', requireAuth, async (req, res) => {
  try {
    const userId = req.user.userId;
    console.log(`🧪 DEBUG: Starting EventSub monitoring for user ${req.user.username} (ID: ${userId})`);

    // Import streamMonitor
    const streamMonitor = require('./streamMonitor');

    // Check if already monitoring
    const isAlreadyMonitored = streamMonitor.connectedUsers && streamMonitor.connectedUsers.has(userId);
    if (isAlreadyMonitored) {
      return res.json({
        success: true,
        message: `User ${req.user.username} is already being monitored`,
        alreadyMonitored: true,
        status: streamMonitor.getConnectionStatus()
      });
    }

    // Start monitoring
    const success = await streamMonitor.subscribeToUser(userId);

    if (success) {
      const status = streamMonitor.getConnectionStatus();
      res.json({
        success: true,
        message: `EventSub monitoring started for ${req.user.username}`,
        status: status,
        userConnection: status.users.find(u => u.userId === userId)
      });
    } else {
      res.status(500).json({
        success: false,
        error: 'Failed to start EventSub monitoring',
        recommendation: 'Check logs for details'
      });
    }

  } catch (error) {
    console.error('❌ Start monitoring test failed:', error);
    res.status(500).json({
      success: false,
      error: error.message,
      details: {
        name: error.name,
        statusCode: error.statusCode,
        code: error.code
      }
    });
  }
});

/**
 * Check EventSub subscription costs
 * GET /api/debug/subscription-costs
 */
router.get('/subscription-costs', requireAuth, async (req, res) => {
  try {
    const streamMonitor = require('./streamMonitor');

    console.log(`🔍 DEBUG: Checking EventSub subscription costs for ${req.user.username}`);

    const result = await streamMonitor.checkAndCleanupSubscriptions();

    res.json({
      success: true,
      message: 'Subscription cost check completed',
      checkResult: result,
      info: 'Check server logs for detailed subscription cost information'
    });

  } catch (error) {
    console.error('❌ Subscription cost check failed:', error);
    res.status(500).json({
      success: false,
      error: error.message
    });
  }
});

/**
 * Comprehensive Discord notification diagnostics
 * GET /api/debug/discord-diagnostics/:userId?
 */
router.get('/discord-diagnostics/:userId?', requireAuth, async (req, res) => {
  try {
    const targetUserId = req.params.userId || req.user.userId;
    const requestingUser = req.user;

    // Only allow users to check themselves or admins to check anyone
    if (requestingUser.userId !== targetUserId && requestingUser.role !== 'admin') {
      return res.status(403).json({ error: 'Insufficient permissions' });
    }

    console.log(`🔍 Running Discord diagnostics for user ${targetUserId}...`);

    const diagnostics = {
      timestamp: new Date().toISOString(),
      userId: targetUserId,
      checks: {},
      summary: {}
    };

    // Check 1: User exists and basic info
    console.log('📋 Step 1: Checking user existence...');
    try {
      const user = await database.getUser(targetUserId);
      diagnostics.checks.userExists = {
        status: user ? 'success' : 'error',
        data: user ? {
          username: user.username,
          displayName: user.displayName,
          role: user.role,
          isActive: user.isActive,
          lastLogin: user.lastLogin
        } : null,
        message: user ? 'User found' : 'User not found in database'
      };
    } catch (error) {
      diagnostics.checks.userExists = {
        status: 'error',
        data: null,
        message: `Error checking user: ${error.message}`
      };
    }

    // Check 2: Discord webhook configuration
    console.log('🔗 Step 2: Checking Discord webhook...');
    try {
      const webhookData = await database.getUserDiscordWebhook(targetUserId);
      const hasWebhook = !!(webhookData?.webhookUrl);
      const isEnabled = webhookData?.enabled || false;

      diagnostics.checks.discordWebhook = {
        status: hasWebhook && isEnabled ? 'success' : hasWebhook ? 'warning' : 'error',
        data: {
          configured: hasWebhook,
          enabled: isEnabled,
          urlPreview: hasWebhook ? `${webhookData.webhookUrl.substring(0, 50)}...` : 'None',
          fullData: webhookData
        },
        message: hasWebhook && isEnabled ? 'Discord webhook configured and enabled' :
                hasWebhook ? 'Discord webhook configured but disabled' :
                'No Discord webhook configured'
      };
    } catch (error) {
      diagnostics.checks.discordWebhook = {
        status: 'error',
        data: null,
        message: `Error checking webhook: ${error.message}`
      };
    }

    // Check 3: Discord notification settings
    console.log('📢 Step 3: Checking notification settings...');
    try {
      const user = await database.getUser(targetUserId);
      let settings = null;

      if (user?.discordSettings) {
        settings = JSON.parse(user.discordSettings);
      }

      const notificationsEnabled = settings?.enableDiscordNotifications || false;

      diagnostics.checks.discordSettings = {
        status: settings && notificationsEnabled ? 'success' : settings ? 'warning' : 'error',
        data: settings,
        message: settings && notificationsEnabled ? 'Discord notifications enabled' :
                settings ? 'Discord notifications disabled' :
                'No Discord settings found'
      };
    } catch (error) {
      diagnostics.checks.discordSettings = {
        status: 'error',
        data: null,
        message: `Error checking settings: ${error.message}`
      };
    }

    // Check 4: EventSub monitoring status
    console.log('🎯 Step 4: Checking EventSub status...');
    try {
      const streamMonitor = require('./streamMonitor');

      if (!streamMonitor) {
        diagnostics.checks.eventSub = {
          status: 'error',
          data: null,
          message: 'Stream monitor not initialized'
        };
      } else {
        const isMonitored = streamMonitor.connectedUsers?.has(targetUserId) || false;
        const userStatus = streamMonitor.getUserConnectionStatus ?
          streamMonitor.getUserConnectionStatus(targetUserId) : null;

        diagnostics.checks.eventSub = {
          status: isMonitored ? 'success' : 'warning',
          data: {
            monitored: isMonitored,
            userStatus: userStatus,
            websocketConnected: !!streamMonitor.listener,
            connectedUsersCount: streamMonitor.connectedUsers?.size || 0
          },
          message: isMonitored ? 'User is being monitored via EventSub' : 'User not being monitored'
        };
      }
    } catch (error) {
      diagnostics.checks.eventSub = {
        status: 'error',
        data: null,
        message: `Error checking EventSub: ${error.message}`
      };
    }

    // Check 5: Overall health calculation
    const statusCounts = Object.values(diagnostics.checks).reduce((acc, check) => {
      acc[check.status] = (acc[check.status] || 0) + 1;
      return acc;
    }, {});

    const totalChecks = Object.keys(diagnostics.checks).length;
    const successRate = Math.round(((statusCounts.success || 0) / totalChecks) * 100);

    diagnostics.summary = {
      overallStatus: successRate >= 75 ? 'healthy' : successRate >= 50 ? 'warning' : 'critical',
      successRate,
      statusCounts,
      recommendations: generateRecommendations(diagnostics.checks)
    };

    console.log(`✅ Discord diagnostics completed for user ${targetUserId}. Success rate: ${successRate}%`);

    res.json(diagnostics);
  } catch (error) {
    console.error('❌ Discord diagnostics error:', error);
    res.status(500).json({
      error: 'Failed to run Discord diagnostics',
      details: error.message
    });
  }
});

/**
 * Test Discord webhook with detailed response
 * POST /api/debug/test-discord-webhook/:userId?
 */
router.post('/test-discord-webhook/:userId?', requireAuth, async (req, res) => {
  try {
    const targetUserId = req.params.userId || req.user.userId;
    const requestingUser = req.user;

    // Only allow users to test their own webhook or admins to test anyone's
    if (requestingUser.userId !== targetUserId && requestingUser.role !== 'admin') {
      return res.status(403).json({ error: 'Insufficient permissions' });
    }

    console.log(`🧪 Testing Discord webhook for user ${targetUserId}...`);

    const user = await database.getUser(targetUserId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    const webhookData = await database.getUserDiscordWebhook(targetUserId);
    if (!webhookData?.webhookUrl) {
      return res.status(400).json({
        error: 'No Discord webhook configured',
        webhookData: webhookData
      });
    }

    // Create detailed test notification
    const testEmbed = {
      embeds: [{
        title: '🔧 Discord Webhook Test',
        description: `**Debug test for ${user.displayName} (@${user.username})**\n\nThis is a comprehensive test to verify your Discord webhook integration is working correctly.`,
        color: 0x00FF00,
        fields: [
          {
            name: '🧪 Test Type',
            value: 'Manual Debug Test',
            inline: true
          },
          {
            name: '⏰ Timestamp',
            value: new Date().toLocaleString(),
            inline: true
          },
          {
            name: '🔗 Webhook Status',
            value: webhookData.enabled ? '✅ Enabled' : '⚠️ Disabled',
            inline: true
          },
          {
            name: '👤 User Info',
            value: `${user.displayName} (@${user.username})`,
            inline: false
          }
        ],
        footer: {
          text: 'OmniAsylum Stream Counter - Debug Test',
          icon_url: user.profileImageUrl
        },
        timestamp: new Date().toISOString()
      }]
    };

    console.log(`🔗 Sending test to webhook: ${webhookData.webhookUrl.substring(0, 50)}...`);

    const startTime = Date.now();
    const response = await fetch(webhookData.webhookUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(testEmbed)
    });
    const responseTime = Date.now() - startTime;

    const responseText = await response.text();

    if (response.ok) {
      console.log(`✅ Discord test successful for ${user.username} in ${responseTime}ms`);
      res.json({
        success: true,
        message: 'Discord webhook test successful!',
        details: {
          responseTime: responseTime,
          statusCode: response.status,
          webhookUrl: `${webhookData.webhookUrl.substring(0, 50)}...`,
          enabled: webhookData.enabled,
          testType: 'Manual debug test'
        }
      });
    } else {
      console.error(`❌ Discord test failed for ${user.username}:`, response.status, responseText);
      res.status(500).json({
        success: false,
        error: 'Discord webhook test failed',
        details: {
          statusCode: response.status,
          responseText: responseText,
          responseTime: responseTime,
          webhookUrl: `${webhookData.webhookUrl.substring(0, 50)}...`
        }
      });
    }
  } catch (error) {
    console.error('❌ Discord webhook test error:', error);
    res.status(500).json({
      success: false,
      error: 'Failed to test Discord webhook',
      details: error.message
    });
  }
});

/**
 * Get system health overview
 * GET /api/debug/system-health
 */
router.get('/system-health', requireAuth, async (req, res) => {
  try {
    console.log(`🔍 Getting system health overview requested by ${req.user.username}...`);

    const health = {
      timestamp: new Date().toISOString(),
      uptime: process.uptime(),
      memory: process.memoryUsage(),
      nodeVersion: process.version,
      environment: process.env.NODE_ENV || 'development',
      checks: {}
    };

    // Database health
    try {
      await database.getAllUsers();
      health.checks.database = {
        status: 'success',
        message: 'Database connection successful',
        mode: process.env.NODE_ENV === 'production' ? 'azure' : 'local'
      };
    } catch (error) {
      health.checks.database = {
        status: 'error',
        message: `Database error: ${error.message}`
      };
    }

    // EventSub health (check multi-tenant per-user status)
    try {
      const streamMonitor = require('./streamMonitor');

      if (streamMonitor && streamMonitor.connectedUsers) {
        const totalUsers = streamMonitor.connectedUsers.size;
        const activeConnections = [];

        // Check each user's connection status
        for (const [userId, userData] of streamMonitor.connectedUsers) {
          const hasActiveListener = !!(userData.userListener && userData.onlineSubscription && userData.offlineSubscription);
          activeConnections.push({
            userId,
            username: userData.username,
            hasListener: hasActiveListener,
            subscriptions: userData.subscriptions || []
          });
        }

        const activeListeners = activeConnections.filter(conn => conn.hasListener).length;
        const hasActiveConnections = activeListeners > 0;

        health.checks.eventSub = {
          status: hasActiveConnections ? 'success' : (totalUsers > 0 ? 'warning' : 'info'),
          message: hasActiveConnections ?
            `EventSub multi-tenant mode: ${activeListeners}/${totalUsers} users with active listeners` :
            (totalUsers > 0 ?
              `EventSub users registered but no active listeners (${totalUsers} users)` :
              'EventSub multi-tenant mode: No users registered'),
          data: {
            globalConnected: hasActiveConnections, // For compatibility with frontend
            connectedUsers: totalUsers,
            activeListeners: activeListeners,
            uptime: 0, // Multi-tenant doesn't have single uptime
            listenerReady: hasActiveConnections, // For compatibility with frontend
            connections: activeConnections
          }
        };
      } else {
        health.checks.eventSub = {
          status: 'error',
          message: 'Stream monitor not initialized or connectedUsers map missing'
        };
      }
    } catch (error) {
      health.checks.eventSub = {
        status: 'error',
        message: `EventSub error: ${error.message}`
      };
    }

    // Discord integration health
    try {
      const users = await database.getAllUsers();
      const usersWithDiscord = users.filter(user => {
        if (user.discordWebhookUrl) return true;
        // Also check new webhook system
        try {
          const webhookData = database.getUserDiscordWebhook(user.twitchUserId);
          return webhookData?.webhookUrl;
        } catch {
          return false;
        }
      });

      health.checks.discord = {
        status: 'success',
        message: `${usersWithDiscord.length}/${users.length} users have Discord configured`,
        data: {
          totalUsers: users.length,
          usersWithDiscord: usersWithDiscord.length,
          percentage: users.length > 0 ? Math.round((usersWithDiscord.length / users.length) * 100) : 0
        }
      };
    } catch (error) {
      health.checks.discord = {
        status: 'error',
        message: `Discord check error: ${error.message}`
      };
    }

    res.json(health);
  } catch (error) {
    console.error('❌ System health error:', error);
    res.status(500).json({ error: 'Failed to get system health', details: error.message });
  }
});

// Helper function to generate recommendations based on diagnostic results
function generateRecommendations(checks) {
  const recommendations = [];

  if (checks.userExists?.status === 'error') {
    recommendations.push('User not found - ensure you are logged in correctly');
  }

  if (checks.discordWebhook?.status === 'error') {
    recommendations.push('Configure a Discord webhook URL in the Discord Integration settings');
  } else if (checks.discordWebhook?.status === 'warning') {
    recommendations.push('Enable your Discord webhook in the settings');
  }

  if (checks.discordSettings?.status === 'error') {
    recommendations.push('Configure Discord notification settings');
  } else if (checks.discordSettings?.status === 'warning') {
    recommendations.push('Enable Discord notifications in your settings');
  }

  if (checks.eventSub?.status === 'warning') {
    recommendations.push('EventSub monitoring may need to be restarted - contact admin');
  } else if (checks.eventSub?.status === 'error') {
    recommendations.push('EventSub monitoring is not working - contact admin');
  }

  if (recommendations.length === 0) {
    recommendations.push('All systems appear to be working correctly! Try going live to test notifications.');
  }

  return recommendations;
}

/**
 * Test stream status change events (manual trigger) - NO AUTH for testing
 * POST /api/debug/test-stream-status/:userId/:status
 */
router.post('/test-stream-status/:userId/:status', async (req, res) => {
  try {
    const { userId, status } = req.params;

    if (!['offline', 'live'].includes(status)) {
      return res.status(400).json({ error: 'Status must be "offline" or "live"' });
    }

    console.log(`🧪 DEBUG: Manual stream status test - Setting user ${userId} to ${status}`);    // Update database status
    await database.updateStreamStatus(userId, status);

    // Get io instance and emit event
    const io = req.app.get('io');
    if (io) {
      const roomName = `user:${userId}`;
      console.log(`🧪 DEBUG: Manual emit streamStatusChanged to room ${roomName} with status: ${status}`);

      // Check room membership
      const room = io.sockets.adapter.rooms.get(roomName);
      const clientCount = room ? room.size : 0;
      console.log(`🧪 DEBUG: Room ${roomName} has ${clientCount} clients`);

      // Emit the event
      io.to(roomName).emit('streamStatusChanged', {
        userId,
        status,
        timestamp: new Date().toISOString(),
        source: 'manual-debug'
      });

      console.log(`✅ DEBUG: Manual streamStatusChanged event emitted successfully`);

      res.json({
        success: true,
        message: `Stream status manually set to ${status}`,
        roomClients: clientCount,
        userId
      });
    } else {
      console.error('❌ DEBUG: Socket.io instance not found');
      res.status(500).json({ error: 'Socket.io not available' });
    }
  } catch (error) {
    console.error('❌ DEBUG: Manual stream status test failed:', error);
    res.status(500).json({ error: 'Test failed', details: error.message });
  }
});

/**
 * Test notification system (no auth required for debugging)
 * POST /api/debug/test-notification/:userId/:eventType
 */
router.post('/test-notification/:userId/:eventType', async (req, res) => {
  try {
    const { userId, eventType } = req.params;
    const io = req.app.get('io');

    if (!io) {
      return res.status(500).json({ error: 'WebSocket not available' });
    }

    console.log(`🧪 DEBUG: Testing ${eventType} notification for user ${userId}`);

    // Get user info for more realistic test data
    let username = 'TestUser';
    try {
      const user = await database.getUser(userId);
      if (user) {
        username = user.username || user.displayName || 'TestUser';
      }
    } catch (error) {
      console.warn('Could not get user info, using default test data');
    }

    const testData = {
      follow: {
        userId: userId,
        username: username,
        follower: 'DebugFollower',
        timestamp: new Date().toISOString()
      },
      subscription: {
        userId: userId,
        username: username,
        subscriber: 'DebugSubscriber',
        tier: 1,
        isGift: false,
        timestamp: new Date().toISOString()
      },
      resub: {
        userId: userId,
        username: username,
        subscriber: 'DebugResubscriber',
        tier: 1,
        months: 12,
        streakMonths: 6,
        message: 'Debug resub message!',
        timestamp: new Date().toISOString()
      },
      giftsub: {
        userId: userId,
        username: username,
        gifter: 'DebugGifter',
        amount: 5,
        tier: 1,
        timestamp: new Date().toISOString()
      },
      bits: {
        userId: userId,
        username: username,
        cheerer: 'DebugCheerer',
        bits: 500,
        message: 'Debug cheer! cheer500',
        isAnonymous: false,
        timestamp: new Date().toISOString()
      },
      milestone: {
        userId: userId,
        counterType: 'deaths',
        milestone: 100,
        newValue: 100,
        previousMilestone: 50,
        timestamp: new Date().toISOString()
      }
    };

    const data = testData[eventType];
    if (!data) {
      return res.status(400).json({
        error: 'Invalid event type',
        validTypes: Object.keys(testData)
      });
    }

    // Map event type to WebSocket event name
    const eventName = eventType === 'subscription' ? 'newSubscription' :
                     eventType === 'resub' ? 'newResub' :
                     eventType === 'giftsub' ? 'newGiftSub' :
                     eventType === 'bits' ? 'newCheer' :
                     eventType === 'milestone' ? 'milestoneReached' :
                     `new${eventType.charAt(0).toUpperCase() + eventType.slice(1)}`;

    // Check room membership
    const roomName = `user:${userId}`;
    const room = io.sockets.adapter.rooms.get(roomName);
    const clientCount = room ? room.size : 0;

    console.log(`🧪 DEBUG: Emitting ${eventName} to room ${roomName} (${clientCount} clients)`);
    console.log(`🧪 DEBUG: Event data:`, data);

    // Emit the test event
    io.to(roomName).emit(eventName, data);

    res.json({
      success: true,
      message: `Debug ${eventType} notification sent`,
      eventName: eventName,
      roomClients: clientCount,
      data: data
    });

  } catch (error) {
    console.error('❌ DEBUG: Test notification failed:', error);
    res.status(500).json({ error: 'Test notification failed', details: error.message });
  }
});

/**
 * Get current user info for debugging (requires auth)
 * GET /api/debug/user-info
 */
router.get('/user-info', requireAuth, async (req, res) => {
  try {
    const user = await database.getUser(req.user.userId);

    res.json({
      success: true,
      userInfo: {
        userId: req.user.userId,
        username: req.user.username,
        displayName: req.user.displayName,
        role: req.user.role,
        dbUser: {
          twitchUserId: user?.twitchUserId,
          username: user?.username,
          displayName: user?.displayName,
          isActive: user?.isActive,
          streamStatus: user?.streamStatus,
          features: user?.features ? JSON.parse(user.features) : null
        }
      }
    });
  } catch (error) {
    console.error('❌ DEBUG: Failed to get user info:', error);
    res.status(500).json({ error: 'Failed to get user info', details: error.message });
  }
});

/**
 * Serve debug notifications HTML page
 * GET /api/debug/notifications
 */
router.get('/notifications', (req, res) => {
  try {
    res.sendFile('debug-notifications.html', { root: './frontend' });
  } catch (error) {
    console.error('❌ DEBUG: Failed to serve debug page:', error);
    res.status(500).json({ error: 'Failed to load debug page' });
  }
});

/**
 * Test all notifications at once
 * POST /api/debug/test-all-notifications/:userId
 */
router.post('/test-all-notifications/:userId', async (req, res) => {
  try {
    const { userId } = req.params;
    const io = req.app.get('io');

    if (!io) {
      return res.status(500).json({ error: 'WebSocket not available' });
    }

    console.log(`🧪 DEBUG: Testing ALL notifications for user ${userId}`);

    const eventTypes = ['follow', 'subscription', 'resub', 'giftsub', 'bits', 'milestone'];
    const results = [];

    // Delay between notifications to prevent spam
    for (let i = 0; i < eventTypes.length; i++) {
      const eventType = eventTypes[i];

      // Wait 2 seconds between each notification
      if (i > 0) {
        await new Promise(resolve => setTimeout(resolve, 2000));
      }

      try {
        // Trigger the individual notification
        const response = await fetch(`http://localhost:${process.env.PORT || 3000}/api/debug/test-notification/${userId}/${eventType}`, {
          method: 'POST'
        });

        if (response.ok) {
          const result = await response.json();
          results.push({ eventType, success: true, result });
          console.log(`✅ DEBUG: ${eventType} notification sent`);
        } else {
          results.push({ eventType, success: false, error: 'HTTP error' });
        }
      } catch (error) {
        results.push({ eventType, success: false, error: error.message });
        console.error(`❌ DEBUG: ${eventType} notification failed:`, error);
      }
    }

    res.json({
      success: true,
      message: `All debug notifications sent to user ${userId}`,
      results: results
    });

  } catch (error) {
    console.error('❌ DEBUG: Test all notifications failed:', error);
    res.status(500).json({ error: 'Test all notifications failed', details: error.message });
  }
});

/**
 * Get Azure Log Analytics KQL queries for investigation
 * GET /api/debug/kql-queries
 */
router.get('/kql-queries', (req, res) => {
  try {
    const queries = {
      "API Request Analysis": {
        "description": "Monitor all HTTP requests with response codes and timing",
        "query": `AppTraces
| where TimeGenerated > ago(1h)
| where Message contains "HTTP" or Message contains "API"
| project TimeGenerated, Message, SeverityLevel
| order by TimeGenerated desc
| limit 100`
      },

      "Error Monitoring": {
        "description": "Track application errors and exceptions",
        "query": `AppTraces
| where TimeGenerated > ago(1h)
| where SeverityLevel >= 3  // Warning and above
| project TimeGenerated, Message, SeverityLevel, Properties
| order by TimeGenerated desc
| limit 50`
      },

      "Twitch Events": {
        "description": "Monitor EventSub webhooks and bot connections",
        "query": `AppTraces
| where TimeGenerated > ago(2h)
| where Message contains "EventSub" or Message contains "Twitch" or Message contains "🔗" or Message contains "🎯"
| project TimeGenerated, Message, SeverityLevel
| order by TimeGenerated desc
| limit 100`
      },

      "Authentication Flow": {
        "description": "Track user logins and token operations",
        "query": `AppTraces
| where TimeGenerated > ago(1h)
| where Message contains "auth" or Message contains "login" or Message contains "token" or Message contains "🔐"
| project TimeGenerated, Message, SeverityLevel
| order by TimeGenerated desc
| limit 50`
      },

      "Real-time Monitoring": {
        "description": "Live application activity (last 5 minutes)",
        "query": `AppTraces
| where TimeGenerated > ago(5m)
| project TimeGenerated, Message, SeverityLevel
| order by TimeGenerated desc`
      },

      "Structured Logs": {
        "description": "Query structured logs from the new logging system",
        "query": `AppTraces
| where TimeGenerated > ago(1h)
| where Message contains "[API]" or Message contains "[AUTH]" or Message contains "[TWITCH]" or Message contains "[DATABASE]"
| project TimeGenerated, Message, SeverityLevel, Properties
| order by TimeGenerated desc
| limit 100`
      },

      "HTTP Request Logs": {
        "description": "Track all HTTP requests with detailed timing",
        "query": `AppTraces
| where TimeGenerated > ago(1h)
| where Message contains "HTTP" and (Message contains "GET" or Message contains "POST" or Message contains "PUT" or Message contains "DELETE")
| project TimeGenerated, Message, SeverityLevel
| order by TimeGenerated desc
| limit 100`
      },

      "Slow Requests": {
        "description": "Find requests taking longer than 1 second",
        "query": `AppTraces
| where TimeGenerated > ago(1h)
| where Message contains "⏱️" and Message contains "Slow request"
| project TimeGenerated, Message, SeverityLevel
| order by TimeGenerated desc
| limit 50`
      },

      "Performance Analysis": {
        "description": "Database and service performance metrics",
        "query": `AppTraces
| where TimeGenerated > ago(1h)
| where Message contains "ms" or Message contains "performance" or Message contains "⏱️"
| project TimeGenerated, Message, SeverityLevel
| order by TimeGenerated desc
| limit 50`
      },

      "Discord Notifications": {
        "description": "Track Discord webhook notifications and events",
        "query": `AppTraces
| where TimeGenerated > ago(1h)
| where Message contains "Discord" or Message contains "webhook" or Message contains "🔔"
| project TimeGenerated, Message, SeverityLevel
| order by TimeGenerated desc
| limit 100`
      },

      "Counter Operations": {
        "description": "Monitor counter updates and milestone events",
        "query": `AppTraces
| where TimeGenerated > ago(1h)
| where Message contains "counter" or Message contains "death" or Message contains "swear" or Message contains "milestone"
| project TimeGenerated, Message, SeverityLevel
| order by TimeGenerated desc
| limit 100`
      }
    };

    res.json({
      success: true,
      message: "Azure Log Analytics KQL queries for investigation",
      workspace: "Use these queries in your Azure Log Analytics workspace",
      usage: "Copy queries into Log Analytics workspace query editor",
      queries: queries,
      tips: [
        "Adjust time ranges by changing 'ago(1h)' to 'ago(2h)', 'ago(30m)', etc.",
        "Use 'limit 200' for more results or 'limit 20' for fewer",
        "Add '| where Message contains \"your-search-term\"' for specific filtering",
        "SeverityLevel: 0=Verbose, 1=Information, 2=Warning, 3=Error, 4=Critical"
      ]
    });

  } catch (error) {
    console.error('❌ DEBUG: Failed to get KQL queries:', error);
    res.status(500).json({ error: 'Failed to get KQL queries', details: error.message });
  }
});

module.exports = router;

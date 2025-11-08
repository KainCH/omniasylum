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

module.exports = router;

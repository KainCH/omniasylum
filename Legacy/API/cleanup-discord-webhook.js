/**
 * Discord Webhook Cleanup Script
 * This script addresses the issue where Discord webhook values are not being saved properly.
 * It removes any conflicting or corrupted webhook data and ensures a clean state.
 */

const { TableClient } = require('@azure/data-tables');
const { ManagedIdentityCredential } = require('@azure/identity');

class DiscordWebhookCleanup {
  constructor() {
    this.usersClient = null;
  }

  async initialize() {
    try {
      const accountName = process.env.AZURE_STORAGE_ACCOUNT || 'bearsstream';
      const credential = new ManagedIdentityCredential('b72c3d28-61d4-4c35-bac8-5e4928de2c7e');
      const serviceUrl = `https://${accountName}.table.core.windows.net`;

      this.usersClient = new TableClient(serviceUrl, 'users', credential);
      console.log('✅ Connected to Azure Table Storage for cleanup');
      return true;
    } catch (error) {
      console.error('❌ Failed to initialize Azure Tables for cleanup:', error);
      return false;
    }
  }

  /**
   * Clean Discord webhook data for a specific user
   * This removes any corrupted webhook fields and resets them to a clean state
   */
  async cleanUserWebhookData(twitchUserId) {
    try {
      console.log(`🧹 Starting Discord webhook cleanup for user: ${twitchUserId}`);

      // Step 1: Get current user entity
      const entity = await this.usersClient.getEntity('user', twitchUserId);
      console.log('📋 Current entity keys:', Object.keys(entity));

      // Show all discord-related fields
      const discordFields = Object.keys(entity).filter(key =>
        key.toLowerCase().includes('discord') || key.toLowerCase().includes('webhook')
      );
      console.log('🔍 Discord-related fields found:', discordFields);

      // Step 2: Create a clean update entity that explicitly removes/resets webhook data
      const cleanEntity = {
        partitionKey: entity.partitionKey,
        rowKey: entity.rowKey,
        discordWebhookUrl: '', // Reset to empty string
        // Remove any case variations that might exist
        DiscordWebhookUrl: undefined,
        discordWebhookURL: undefined,
        DiscordWebhookURL: undefined,
        webhookUrl: undefined,
        WebhookUrl: undefined
      };

      console.log('🔄 Applying cleanup update...');
      await this.usersClient.updateEntity(cleanEntity, 'Merge');

      // Step 3: Verify the cleanup
      const verificationEntity = await this.usersClient.getEntity('user', twitchUserId);
      console.log('✅ Cleanup completed. Verification:');
      console.log('   - discordWebhookUrl:', verificationEntity.discordWebhookUrl);
      console.log('   - Type:', typeof verificationEntity.discordWebhookUrl);
      console.log('   - Value is empty:', !verificationEntity.discordWebhookUrl);

      return {
        success: true,
        message: 'Discord webhook data cleaned successfully',
        beforeCleanup: discordFields,
        afterCleanup: verificationEntity.discordWebhookUrl
      };

    } catch (error) {
      console.error(`❌ Error cleaning Discord webhook data for user ${twitchUserId}:`, error);
      return {
        success: false,
        message: error.message,
        error: error
      };
    }
  }

  /**
   * List all users with Discord webhook data issues
   */
  async findProblematicWebhookEntries() {
    try {
      console.log('🔍 Scanning for problematic Discord webhook entries...');
      const problematicUsers = [];

      // Query all users
      const entitiesIter = this.usersClient.listEntities();

      for await (const entity of entitiesIter) {
        // Check for Discord-related fields
        const discordFields = Object.keys(entity).filter(key =>
          key.toLowerCase().includes('discord') || key.toLowerCase().includes('webhook')
        );

        if (discordFields.length > 1) {
          // Multiple discord fields - potential issue
          problematicUsers.push({
            twitchUserId: entity.rowKey,
            username: entity.username,
            discordFields: discordFields,
            issue: 'Multiple discord/webhook fields'
          });
        }

        // Check for undefined or null values
        if (entity.discordWebhookUrl === null || entity.discordWebhookUrl === undefined) {
          problematicUsers.push({
            twitchUserId: entity.rowKey,
            username: entity.username,
            discordFields: discordFields,
            issue: 'Null or undefined webhook URL'
          });
        }
      }

      console.log(`📊 Found ${problematicUsers.length} users with potential webhook issues:`);
      problematicUsers.forEach(user => {
        console.log(`   - ${user.username} (${user.twitchUserId}): ${user.issue}`);
        console.log(`     Fields: ${user.discordFields.join(', ')}`);
      });

      return problematicUsers;

    } catch (error) {
      console.error('❌ Error scanning for problematic webhook entries:', error);
      return [];
    }
  }

  /**
   * Reset webhook for the riress user specifically
   */
  async resetRiressWebhook() {
    const riressUserId = '125828897'; // riress's Twitch user ID
    console.log(`🎯 Specifically cleaning webhook data for riress (${riressUserId})`);

    return await this.cleanUserWebhookData(riressUserId);
  }
}

module.exports = DiscordWebhookCleanup;

// If run directly, clean up riress's webhook
if (require.main === module) {
  (async () => {
    const cleanup = new DiscordWebhookCleanup();

    if (await cleanup.initialize()) {
      // First scan for issues
      console.log('🔍 Scanning all users for webhook issues...');
      await cleanup.findProblematicWebhookEntries();

      console.log('\n' + '='.repeat(50) + '\n');

      // Clean up riress specifically
      console.log('🎯 Cleaning riress webhook data...');
      const result = await cleanup.resetRiressWebhook();
      console.log('✅ Cleanup result:', result);
    }
  })();
}

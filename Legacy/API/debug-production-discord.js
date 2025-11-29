/**
 * Debug Discord Notifications in Production
 * Checks the production Azure Tables for Discord configuration
 */

const { TableClient } = require('@azure/data-tables');
const { ManagedIdentityCredential } = require('@azure/identity');

async function debugProductionDiscord() {
  console.log('🔍 Checking Production Discord Configuration for riress...\n');

  try {
    // Use managed identity for Azure access
    const credential = new ManagedIdentityCredential();

    // Connect to users table
    const usersClient = new TableClient(
      `https://omni46jismtjodyuc.table.core.windows.net`,
      'users',
      credential
    );

    const riressUserId = '125828897'; // riress's Twitch user ID

    console.log('👤 Step 1: Check if riress user exists in production...');
    try {
      const user = await usersClient.getEntity('user', riressUserId);
      console.log('✅ User found:', {
        username: user.username,
        displayName: user.displayName,
        role: user.role,
        isActive: user.isActive
      });
    } catch (error) {
      console.error('❌ User not found in production:', error.message);
      return;
    }

    console.log('\n🔗 Step 2: Check Discord webhook configuration...');
    try {
      const user = await usersClient.getEntity('user', riressUserId);

      // Check if discordWebhookUrl exists (legacy field)
      if (user.discordWebhookUrl) {
        console.log('✅ Legacy Discord webhook found');
        console.log(`   URL: ${user.discordWebhookUrl.substring(0, 50)}...`);
      } else {
        console.log('❌ No legacy Discord webhook URL found');
      }

      // Check discordSettings field
      if (user.discordSettings) {
        const settings = JSON.parse(user.discordSettings);
        console.log('📢 Discord settings found:', {
          enableDiscordNotifications: settings.enableDiscordNotifications,
          enableChannelNotifications: settings.enableChannelNotifications,
          deathMilestoneEnabled: settings.deathMilestoneEnabled,
          swearMilestoneEnabled: settings.swearMilestoneEnabled
        });
      } else {
        console.log('❌ No Discord settings found');
      }

    } catch (error) {
      console.error('❌ Error checking webhook config:', error.message);
    }

    console.log('\n🎯 Step 3: Check EventSub monitoring status...');
    // Note: This would require connecting to the stream monitor, which is complex
    console.log('⚠️  EventSub status check requires application runtime context');

    console.log('\n📋 Summary:');
    console.log('1. Check if user is logged in and has Discord webhook configured');
    console.log('2. Verify Discord notifications are enabled in settings');
    console.log('3. Ensure EventSub is monitoring the stream');
    console.log('4. Test with a manual notification to verify webhook works');

  } catch (error) {
    console.error('❌ Error connecting to production:', error.message);
    console.log('💡 Note: This script must run from Azure Container App with managed identity');
  }
}

// Run the diagnostic
debugProductionDiscord().catch(console.error);

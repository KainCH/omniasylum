/**
 * Discord Notification Diagnostic Script
 *
 * This script helps diagnose why Discord notifications aren't working
 * by checking user settings and EventSub subscription status
 */

const database = require('./database');

async function diagnoseDiscordNotifications(userId = '125828897') { // riress's user ID
  console.log('🔍 Discord Notification Diagnostic\n');

  try {
    // Step 1: Check if user exists
    console.log('👤 Step 1: Check User');
    const user = await database.getUser(userId);
    if (!user) {
      console.error('❌ User not found!');
      return;
    }
    console.log(`✅ User found: ${user.displayName} (@${user.username})`);
    console.log(`   Role: ${user.role || 'streamer'}`);
    console.log('');

    // Step 2: Check Discord webhook configuration
    console.log('🔗 Step 2: Check Discord Webhook');
    try {
      const webhookData = await database.getUserDiscordWebhook(userId);
      console.log('Webhook data:', webhookData);

      if (!webhookData) {
        console.log('❌ No Discord webhook configured');
      } else if (!webhookData.webhookUrl) {
        console.log('❌ Discord webhook URL missing');
      } else if (!webhookData.enabled) {
        console.log('⚠️  Discord webhook disabled');
      } else {
        console.log('✅ Discord webhook configured and enabled');
        console.log(`   URL: ${webhookData.webhookUrl.substring(0, 50)}...`);
      }
    } catch (error) {
      console.error('❌ Error checking Discord webhook:', error.message);
    }
    console.log('');

    // Step 3: Check notification settings
    console.log('📢 Step 3: Check Notification Settings');
    try {
      const notificationSettings = await database.getUserNotificationSettings(userId);
      console.log('Notification settings:', notificationSettings);

      if (!notificationSettings) {
        console.log('❌ No notification settings found');
      } else {
        console.log(`   Discord notifications: ${notificationSettings.enableDiscordNotifications ? '✅' : '❌'}`);
        console.log(`   Channel notifications: ${notificationSettings.enableChannelNotifications ? '✅' : '❌'}`);
        console.log(`   Death milestones: ${notificationSettings.deathMilestoneEnabled ? '✅' : '❌'}`);
        console.log(`   Swear milestones: ${notificationSettings.swearMilestoneEnabled ? '✅' : '❌'}`);
      }
    } catch (error) {
      console.error('❌ Error checking notification settings:', error.message);
    }
    console.log('');

    // Step 4: Check if user data has the settings stored differently
    console.log('💾 Step 4: Check User Data Structure');
    console.log('User features:', user.features);

    if (typeof user.features === 'string') {
      try {
        const features = JSON.parse(user.features);
        console.log('Parsed features:', features);
      } catch (error) {
        console.log('❌ Failed to parse user features JSON');
      }
    }
    console.log('');

    // Step 5: Show recommendations
    console.log('💡 Step 5: Recommendations');
    const webhookData = await database.getUserDiscordWebhook(userId).catch(() => null);
    const notificationSettings = await database.getUserNotificationSettings(userId).catch(() => null);

    const hasWebhook = !!(webhookData?.webhookUrl && webhookData?.enabled);
    const hasNotifications = !!(notificationSettings?.enableDiscordNotifications);

    if (!hasWebhook) {
      console.log('🔧 Configure Discord webhook in user settings');
    }
    if (!hasNotifications) {
      console.log('🔧 Enable Discord notifications in user settings');
    }
    if (hasWebhook && hasNotifications) {
      console.log('✅ Configuration looks correct - check EventSub subscription logs');
    }

  } catch (error) {
    console.error('❌ Diagnostic failed:', error);
  }
}

// Run the diagnostic
if (require.main === module) {
  diagnoseDiscordNotifications().then(() => {
    console.log('\n🏁 Diagnostic complete');
    process.exit(0);
  }).catch((error) => {
    console.error('Diagnostic error:', error);
    process.exit(1);
  });
}

module.exports = { diagnoseDiscordNotifications };

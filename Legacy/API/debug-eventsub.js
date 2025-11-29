// Debug script to manually trigger EventSub subscription for riress user
process.env.NODE_ENV = 'production';
process.env.DB_MODE = 'azure';

const database = require('./database');

async function debugEventSubSubscription() {
  try {
    const userId = '125828897'; // riress user ID

    console.log('🔍 Step 1: Check user data...');
    const user = await database.getUser(userId);
    if (!user) {
      console.error('❌ User not found');
      return;
    }

    console.log(`✅ User found: ${user.username}`);
    console.log(`   - isActive: ${user.isActive}`);
    console.log(`   - hasAccessToken: ${!!user.accessToken}`);

    console.log('\n🔍 Step 2: Check Discord webhook...');
    const webhookData = await database.getUserDiscordWebhook(userId);
    console.log(`   - Webhook URL: ${webhookData?.webhookUrl ? 'CONFIGURED' : 'MISSING'}`);
    console.log(`   - Webhook enabled: ${webhookData?.enabled}`);

    console.log('\n🔍 Step 3: Check notification settings...');
    const notificationSettings = await database.getUserNotificationSettings(userId);
    console.log('   - Notification settings:', notificationSettings);

    console.log('\n🔍 Step 4: Check feature flags...');
    const discordNotificationsEnabled = await database.hasFeature(userId, 'discordNotifications');
    console.log(`   - discordNotifications feature: ${discordNotificationsEnabled}`);

    console.log('\n🔍 Step 5: Manual subscription decision logic...');
    const discordWebhookEnabled = !!(webhookData?.webhookUrl && webhookData?.enabled);
    console.log(`   - Discord webhook enabled: ${discordWebhookEnabled}`);

    const shouldSubscribeToAlerts = (
      (notificationSettings?.enableDiscordNotifications && discordWebhookEnabled) ||
      notificationSettings?.enableChannelNotifications
    );
    console.log(`   - Should subscribe to alerts: ${shouldSubscribeToAlerts}`);

    console.log('\n📊 Summary:');
    console.log(`   - User active: ${user.isActive ? '✅' : '❌'}`);
    console.log(`   - Has access token: ${user.accessToken ? '✅' : '❌'}`);
    console.log(`   - Discord webhook configured: ${discordWebhookEnabled ? '✅' : '❌'}`);
    console.log(`   - Discord notifications enabled: ${notificationSettings?.enableDiscordNotifications ? '✅' : '❌'}`);
    console.log(`   - Should create EventSub subscriptions: ${shouldSubscribeToAlerts ? '✅' : '❌'}`);

    if (!shouldSubscribeToAlerts) {
      console.log('\n⚠️  EventSub subscriptions will NOT be created because:');
      if (!discordWebhookEnabled) {
        console.log('   - Discord webhook is not properly configured or enabled');
      }
      if (!notificationSettings?.enableDiscordNotifications) {
        console.log('   - enableDiscordNotifications is false');
      }
      if (!notificationSettings?.enableChannelNotifications) {
        console.log('   - enableChannelNotifications is false');
      }

      console.log('\n🔧 To fix this:');
      console.log('   1. Ensure Discord webhook URL is saved');
      console.log('   2. Enable Discord notifications in user settings');
      console.log('   3. Make sure notification settings have enableDiscordNotifications: true');
    }

  } catch (error) {
    console.error('❌ Debug failed:', error);
  }
}

debugEventSubSubscription();

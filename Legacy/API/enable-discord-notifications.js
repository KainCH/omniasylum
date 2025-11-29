const { TableClient } = require('@azure/data-tables');
const { DefaultAzureCredential } = require('@azure/identity');

async function enableDiscordNotifications() {
  try {
    console.log('🔧 Manually enabling Discord notifications for riress user...');

    const credential = new DefaultAzureCredential();
    const usersClient = new TableClient(
      `https://omni46jismtjodyuc.table.core.windows.net`,
      'users',
      credential
    );

    const riressUserId = '125828897';

    console.log('🔍 Step 1: Get current user data...');
    const currentUser = await usersClient.getEntity('user', riressUserId);
    console.log('📋 Current discordSettings:', currentUser.discordSettings);

    console.log('🔄 Step 2: Update Discord notification settings...');

    // Create or update Discord settings with notifications enabled
    const discordSettings = {
      enableDiscordNotifications: true,  // ← This is the key setting!
      enableChannelNotifications: false,
      deathMilestoneEnabled: true,
      swearMilestoneEnabled: true,
      deathThresholds: '10,25,50,100,250,500,1000',
      swearThresholds: '25,50,100,250,500,1000,2500'
    };

    const updateEntity = {
      partitionKey: 'user',
      rowKey: riressUserId,
      discordSettings: JSON.stringify(discordSettings)
    };

    await usersClient.updateEntity(updateEntity, 'Merge');
    console.log('✅ Discord notification settings updated successfully!');

    console.log('🔍 Step 3: Verify the update...');
    const updatedUser = await usersClient.getEntity('user', riressUserId);
    const parsedSettings = JSON.parse(updatedUser.discordSettings);
    console.log('📋 New Discord settings:', parsedSettings);

    console.log('🎯 Discord notifications status:');
    console.log(`   - enableDiscordNotifications: ${parsedSettings.enableDiscordNotifications ? '✅ ENABLED' : '❌ DISABLED'}`);
    console.log(`   - enableChannelNotifications: ${parsedSettings.enableChannelNotifications ? '✅ ENABLED' : '❌ DISABLED'}`);
    console.log(`   - deathMilestoneEnabled: ${parsedSettings.deathMilestoneEnabled ? '✅ ENABLED' : '❌ DISABLED'}`);
    console.log(`   - swearMilestoneEnabled: ${parsedSettings.swearMilestoneEnabled ? '✅ ENABLED' : '❌ DISABLED'}`);

    console.log('\n🚀 Next steps:');
    console.log('1. The StreamMonitor should now create EventSub subscriptions for your user');
    console.log('2. Test by triggering a follow, subscription, or other Twitch event');
    console.log('3. Check logs for EventSub subscription creation');

  } catch (error) {
    console.error('❌ Failed to enable Discord notifications:', error);
  }
}

enableDiscordNotifications();

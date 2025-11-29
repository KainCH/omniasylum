const { TableClient } = require('@azure/data-tables');
const { DefaultAzureCredential } = require('@azure/identity');

async function testWebhookUpdate() {
  try {
    console.log('🧪 Testing webhook update for riress user...');

    const credential = new DefaultAzureCredential();
    const usersClient = new TableClient(
      `https://omni46jismtjodyuc.table.core.windows.net`,
      'users',
      credential
    );

    const riressUserId = '125828897'; // From the debug output
    const testWebhookUrl = 'https://discord.com/api/webhooks/TEST123456/test-webhook-url-for-riress-debug';

    console.log(`🔍 Step 1: Get current riress user data...`);
    try {
      const currentUser = await usersClient.getEntity('user', riressUserId);
      console.log(`📋 Current riress data:`, {
        partitionKey: currentUser.partitionKey,
        rowKey: currentUser.rowKey,
        username: currentUser.username,
        discordWebhookUrl: currentUser.discordWebhookUrl,
        DiscordWebhookUrl: currentUser.DiscordWebhookUrl
      });
    } catch (error) {
      console.error(`❌ Failed to get current user:`, error.message);
      return;
    }

    console.log(`🔄 Step 2: Attempt to update webhook URL...`);
    const updateEntity = {
      partitionKey: 'user',
      rowKey: riressUserId,
      discordWebhookUrl: testWebhookUrl
    };

    try {
      await usersClient.updateEntity(updateEntity, 'Merge');
      console.log(`✅ Update succeeded!`);
    } catch (error) {
      console.error(`❌ Update failed:`, error.message);
      console.error(`❌ Status code:`, error.statusCode);
      return;
    }

    console.log(`🔍 Step 3: Verify the update...`);
    try {
      const updatedUser = await usersClient.getEntity('user', riressUserId);
      console.log(`📋 Updated riress data:`, {
        partitionKey: updatedUser.partitionKey,
        rowKey: updatedUser.rowKey,
        username: updatedUser.username,
        discordWebhookUrl: updatedUser.discordWebhookUrl,
        DiscordWebhookUrl: updatedUser.DiscordWebhookUrl,
        webhookMatches: updatedUser.discordWebhookUrl === testWebhookUrl
      });

      if (updatedUser.discordWebhookUrl === testWebhookUrl) {
        console.log(`🎯 SUCCESS: Webhook URL was saved correctly!`);
      } else {
        console.log(`❌ FAILED: Webhook URL did not save correctly`);
        console.log(`   Expected: ${testWebhookUrl}`);
        console.log(`   Got: ${updatedUser.discordWebhookUrl}`);
      }
    } catch (error) {
      console.error(`❌ Failed to verify update:`, error.message);
    }

  } catch (error) {
    console.error('❌ Test failed:', error);
  }
}

testWebhookUpdate();

// Test webhook save for riress user
const { Database } = require('./database');

async function testWebhookSave() {
  console.log('🧪 Testing webhook save for riress...');

  const database = new Database();
  const testUserId = '125828897'; // riress
  const testWebhookUrl = 'https://discord.com/api/webhooks/1234567890/test-webhook-token-12345';

  try {
    // First, get the user to see current state
    console.log('1. Getting current user data...');
    const user = await database.getUser(testUserId);
    console.log('Current user:', {
      twitchUserId: user?.twitchUserId,
      username: user?.username,
      discordWebhookUrl: user?.discordWebhookUrl,
      partitionKey: user?.partitionKey,
      rowKey: user?.rowKey
    });

    // Test the webhook save
    console.log('2. Attempting to save webhook...');
    const result = await database.updateUserDiscordWebhook(testUserId, testWebhookUrl);
    console.log('✅ Save completed, result:', {
      twitchUserId: result?.twitchUserId,
      username: result?.username,
      discordWebhookUrl: result?.discordWebhookUrl ? `${result.discordWebhookUrl.substring(0, 50)}...` : 'EMPTY'
    });

    // Verify by reading it back
    console.log('3. Verifying save by reading back...');
    const verification = await database.getUserDiscordWebhook(testUserId);
    console.log('Verification read:', verification);

  } catch (error) {
    console.error('❌ Test failed:', error);
    console.error('Error details:', {
      name: error.name,
      message: error.message,
      statusCode: error.statusCode,
      code: error.code
    });
  }
}

testWebhookSave().then(() => {
  console.log('🏁 Test completed');
  process.exit(0);
}).catch(error => {
  console.error('💥 Test crashed:', error);
  process.exit(1);
});

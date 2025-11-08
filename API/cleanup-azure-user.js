const Database = require('./database');

async function examineAndCleanupAzureUser() {
  // Force Azure mode regardless of local environment
  process.env.DB_MODE = 'azure';

  const db = new Database();
  const userId = '125828897'; // riress

  console.log('🔍 Examining AZURE user data structure...');
  console.log('🔧 Mode:', process.env.DB_MODE);

  try {
    // Initialize the database connection to Azure
    await db.initialize();

    // Get the raw user data from Azure Table Storage
    const user = await db.getUser(userId);

    if (user) {
      console.log('📊 Current Azure user data structure:');
      console.log('Keys found:', Object.keys(user));
      console.log('');

      // Show field details
      for (const [key, value] of Object.entries(user)) {
        const valueStr = String(value).substring(0, 100);
        console.log(`- ${key}: ${typeof value} = '${valueStr}${valueStr.length >= 100 ? '...' : ''}'`);
      }

      console.log('');
      console.log('🔧 Checking for problematic fields...');

      // Look for fields that might be causing issues
      const problematicFields = [];

      if (user.discordWebhookUrl && user.discordWebhookUrl.includes('test-webhook-token')) {
        problematicFields.push('discordWebhookUrl (test data)');
      }

      // Check for duplicate or malformed fields
      const fieldVariations = [
        'discordWebhookUrl',
        'DiscordWebhookUrl',
        'discord_webhook_url',
        'webhook_url',
        'webhookUrl'
      ];

      fieldVariations.forEach(field => {
        if (user[field] && field !== 'discordWebhookUrl') {
          problematicFields.push(`${field} (duplicate/wrong case)`);
        }
      });

      if (problematicFields.length > 0) {
        console.log('❌ Found problematic fields:');
        problematicFields.forEach(field => console.log(`  - ${field}`));

        console.log('');
        console.log('🧹 Cleaning up problematic fields in AZURE...');

        // Create clean user object
        const cleanUser = { ...user };

        // Remove test webhook data
        if (cleanUser.discordWebhookUrl && cleanUser.discordWebhookUrl.includes('test-webhook-token')) {
          console.log('  Removing test webhook data from Azure...');
          cleanUser.discordWebhookUrl = '';
        }

        // Remove duplicate/malformed fields
        fieldVariations.forEach(field => {
          if (field !== 'discordWebhookUrl' && cleanUser[field]) {
            console.log(`  Removing duplicate field from Azure: ${field}`);
            delete cleanUser[field];
          }
        });

        // Update the user in Azure Table Storage
        console.log('💾 Updating user in Azure Table Storage...');

        const result = await db.updateUserDiscordWebhook(userId, cleanUser.discordWebhookUrl);

        console.log('✅ Azure user data cleaned successfully');
        console.log('New webhook URL:', cleanUser.discordWebhookUrl || '(empty)');

        // Verify the cleanup
        console.log('');
        console.log('🔍 Verifying cleanup...');
        const verifyUser = await db.getUser(userId);
        console.log('Verification - Keys found:', Object.keys(verifyUser));
        console.log('Verification - discordWebhookUrl:', verifyUser.discordWebhookUrl || '(empty)');

        return { success: true, cleanUser };

      } else {
        console.log('✅ No problematic fields found in Azure');
        return { success: true, user };
      }

    } else {
      console.log('❌ User not found in Azure Table Storage');
      return { success: false, error: 'User not found' };
    }

  } catch (error) {
    console.error('❌ Error examining/cleaning Azure user:', error);
    return { success: false, error: error.message };
  }
}

examineAndCleanupAzureUser().then(result => {
  if (result) {
    console.log('');
    console.log('📝 Azure cleanup complete.');
    if (result.success) {
      console.log('✅ Azure user data is now clean and ready for webhook configuration');
    } else {
      console.log('❌ Azure cleanup failed:', result.error);
    }
  }
  process.exit(0);
});

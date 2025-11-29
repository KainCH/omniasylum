const Database = require('./database');

async function examineAndCleanupUser() {
  const db = new Database();
  const userId = '125828897'; // riress

  console.log('🔍 Examining user data structure...');

  try {
    // Get the raw user data
    const user = await db.getUser(userId);

    if (user) {
      console.log('📊 Current user data structure:');
      console.log('Keys found:', Object.keys(user));
      console.log('');

      // Show field details
      for (const [key, value] of Object.entries(user)) {
        const valueStr = String(value).substring(0, 100);
        console.log(`- ${key}: ${typeof value} = '${valueStr}...'`);
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
        console.log('🧹 Cleaning up problematic fields...');

        // Create clean user object
        const cleanUser = { ...user };

        // Remove test webhook data
        if (cleanUser.discordWebhookUrl && cleanUser.discordWebhookUrl.includes('test-webhook-token')) {
          console.log('  Removing test webhook data...');
          cleanUser.discordWebhookUrl = '';
        }

        // Remove duplicate/malformed fields
        fieldVariations.forEach(field => {
          if (field !== 'discordWebhookUrl' && cleanUser[field]) {
            console.log(`  Removing duplicate field: ${field}`);
            delete cleanUser[field];
          }
        });

        // Update the user in the database
        console.log('💾 Updating user in database...');

        // Use the database's internal update method
        const result = await db.updateUser(userId, cleanUser);

        console.log('✅ User data cleaned successfully');
        console.log('New webhook URL:', cleanUser.discordWebhookUrl || '(empty)');

        return { success: true, cleanUser };

      } else {
        console.log('✅ No problematic fields found');
        return { success: true, user };
      }

    } else {
      console.log('❌ User not found');
      return { success: false, error: 'User not found' };
    }

  } catch (error) {
    console.error('❌ Error examining/cleaning user:', error);
    return { success: false, error: error.message };
  }
}

examineAndCleanupUser().then(result => {
  if (result) {
    console.log('');
    console.log('📝 Cleanup complete.');
    if (result.success) {
      console.log('✅ User data is now clean and ready for webhook configuration');
    } else {
      console.log('❌ Cleanup failed:', result.error);
    }
  }
  process.exit(0);
});

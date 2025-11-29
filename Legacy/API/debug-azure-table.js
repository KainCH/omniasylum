const { TableClient } = require('@azure/data-tables');
const { DefaultAzureCredential } = require('@azure/identity');

async function debugAzureTableStorage() {
  try {
    console.log('🔍 Testing Azure Table Storage field structure...');

    // Create credentials and client directly
    const credential = new DefaultAzureCredential();
    const usersClient = new TableClient(
      `https://omni46jismtjodyuc.table.core.windows.net`,
      'users',
      credential
    );

    // Query a specific user (replace with a known user ID)
    const entities = usersClient.listEntities({
      queryOptions: {
        select: ['partitionKey', 'rowKey', 'discordWebhookUrl', 'DiscordWebhookUrl', 'username', 'displayName']
      }
    });

    console.log('📋 Raw Azure Table Storage entities:');
    let count = 0;
    for await (const entity of entities) {
      console.log(`\n🎯 Entity ${++count}:`);
      console.log('  - Keys:', Object.keys(entity));
      console.log('  - PartitionKey:', entity.partitionKey);
      console.log('  - RowKey:', entity.rowKey);
      console.log('  - Username:', entity.username);
      console.log('  - DisplayName:', entity.displayName);
      console.log('  - discordWebhookUrl:', entity.discordWebhookUrl);
      console.log('  - DiscordWebhookUrl:', entity.DiscordWebhookUrl);
      console.log('  - Type of discordWebhookUrl:', typeof entity.discordWebhookUrl);

      // Check for any field containing 'discord' or 'webhook'
      const relatedFields = Object.keys(entity).filter(key =>
        key.toLowerCase().includes('discord') || key.toLowerCase().includes('webhook')
      );
      console.log('  - Discord/webhook related fields:', relatedFields);

      if (count >= 3) break; // Limit to first 3 entities
    }

  } catch (error) {
    console.error('❌ Error debugging Azure Table Storage:', error);
  }
}

debugAzureTableStorage();

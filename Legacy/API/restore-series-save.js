const { TableClient } = require('@azure/data-tables');
const { DefaultAzureCredential } = require('@azure/identity');

// ==========================================
// CONFIGURATION - EDIT THESE VALUES
// ==========================================
const CONFIG = {
  // Target System: 'NODE' (Legacy) or 'DOTNET' (New)
  targetSystem: 'DOTNET',

  // Azure Storage Account Name
  storageAccountName: 'omni46jismtjodyuc', // Default from debug script, change if needed

  // The Twitch User ID to restore the save for
  twitchUserId: 'YOUR_TWITCH_USER_ID_HERE',

  // Series Details
  seriesName: 'Restored Series',
  description: 'Restored via script',

  // Counter Values to Restore
  counters: {
    deaths: 0,
    swears: 0,
    bits: 0
  },

  // Optional: Set a specific save time (default is now)
  savedAt: new Date().toISOString()
};

async function restoreSeriesSave() {
  try {
    console.log(`🔄 Starting Series Save Restoration for [${CONFIG.targetSystem}]...`);

    // 1. Construct the Service URL
    const serviceUrl = `https://${CONFIG.storageAccountName}.table.core.windows.net`;
    console.log(`📡 Connecting to: ${serviceUrl}`);

    // 2. Create Credential (uses Azure CLI login locally)
    const credential = new DefaultAzureCredential();

    // 3. Determine Table Name and Schema based on Target System
    let tableName;
    let entity;

    // Generate Series ID (RowKey) - Format: <timestamp>_<sanitized_series_name>
    const timestamp = Date.now();
    const sanitizedName = CONFIG.seriesName.replace(/[^a-zA-Z0-9]/g, '_');
    const seriesId = `${timestamp}_${sanitizedName}`;

    if (CONFIG.targetSystem === 'DOTNET') {
      // .NET Schema
      tableName = 'series';
      entity = {
        partitionKey: CONFIG.twitchUserId,
        rowKey: seriesId,
        name: CONFIG.seriesName,           // .NET uses 'name'
        description: CONFIG.description,
        snapshot: JSON.stringify(CONFIG.counters), // .NET stores counters in JSON 'snapshot'
        createdAt: CONFIG.savedAt,
        lastUpdated: CONFIG.savedAt,
        isActive: true
      };
    } else {
      // Node.js Legacy Schema
      tableName = 'seriessaves';
      entity = {
        partitionKey: CONFIG.twitchUserId,
        rowKey: seriesId,
        seriesName: CONFIG.seriesName,     // Node uses 'seriesName'
        description: CONFIG.description,
        deaths: CONFIG.counters.deaths,    // Node uses top-level columns
        swears: CONFIG.counters.swears,
        bits: CONFIG.counters.bits,
        savedAt: CONFIG.savedAt
      };
    }

    // 4. Create Table Client
    const client = new TableClient(serviceUrl, tableName, credential);

    console.log(`📂 Target Table: ${tableName}`);
    console.log('📝 Preparing to insert entity:', entity);

    // 5. Insert Entity
    await client.upsertEntity(entity, 'Replace');

    console.log('✅ Successfully restored series save!');
    console.log(`🔑 Series ID: ${seriesId}`);
    console.log(`👤 User ID: ${CONFIG.twitchUserId}`);

  } catch (error) {
    console.error('❌ Error restoring series save:', error);
    console.log('\n💡 Troubleshooting:');
    console.log('1. Ensure you are logged in via Azure CLI: "az login"');
    console.log('2. Check if the storage account name is correct.');
    console.log('3. Verify you have permissions to write to this table.');
  }
}

// Run the function
restoreSeriesSave();

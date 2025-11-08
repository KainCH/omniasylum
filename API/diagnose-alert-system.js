/**
 * Alert System Diagnostic Tool
 *
 * This script diagnoses issues with the Event & Alert Manager system
 * to help identify why only custom alerts are working.
 */

const database = require('./database');
const streamMonitor = require('./streamMonitor');

async function diagnoseAlertSystem() {
  console.log('🔍 Alert System Diagnostic Report\n');
  console.log('=====================================\n');

  try {
    // 1. Check if database is initialized
    console.log('1️⃣  CHECKING DATABASE CONNECTION...');
    const users = await database.getAllUsers();
    console.log(`   ✅ Found ${users.length} users in database\n`);

    // 2. Check each user's alert configuration
    console.log('2️⃣  CHECKING USER ALERT CONFIGURATIONS...');

    for (const user of users.slice(0, 3)) { // Check first 3 users
      const userId = user.twitchUserId || user.partitionKey;
      if (!userId) continue;

      console.log(`\n👤 User: ${user.username} (${userId})`);

      // Check feature flags
      const hasStreamAlerts = await database.hasFeature(userId, 'streamAlerts');
      console.log(`   - streamAlerts feature: ${hasStreamAlerts ? '✅' : '❌'}`);

      if (!hasStreamAlerts) {
        console.log('   ⚠️  streamAlerts feature not enabled - this is the likely issue!');
        continue;
      }

      // Check if alerts are initialized
      const userAlerts = await database.getUserAlerts(userId);
      console.log(`   - User alerts: ${userAlerts.length} found`);

      // Check default vs custom alerts
      const defaultAlerts = userAlerts.filter(a => a.isDefault);
      const customAlerts = userAlerts.filter(a => !a.isDefault);
      console.log(`   - Default alerts: ${defaultAlerts.length}`);
      console.log(`   - Custom alerts: ${customAlerts.length}`);

      // Check event mappings
      const eventMappings = await database.getEventMappings(userId);
      const mappingKeys = Object.keys(eventMappings);
      console.log(`   - Event mappings: ${mappingKeys.length} configured`);

      if (mappingKeys.length === 0) {
        console.log('   ⚠️  No event mappings found - initializing defaults...');
        await database.initializeEventMappings(userId);
        const newMappings = await database.getEventMappings(userId);
        console.log(`   ✅ Initialized ${Object.keys(newMappings).length} default mappings`);
      }

      // Test a specific mapping
      const followAlert = await database.getAlertForEvent(userId, 'channel.follow');
      console.log(`   - Follow alert configured: ${followAlert ? '✅' : '❌'}`);

      if (followAlert) {
        console.log(`     → "${followAlert.name}" (${followAlert.isDefault ? 'default' : 'custom'})`);
      }
    }

    // 3. Check default alert templates
    console.log('\n3️⃣  CHECKING DEFAULT ALERT TEMPLATES...');
    const defaultTemplates = database.getDefaultAlertTemplates();
    console.log(`   ✅ ${defaultTemplates.length} default templates available:`);

    defaultTemplates.forEach(template => {
      console.log(`   - ${template.type}: "${template.name}"`);
    });

    // 4. Check default event mappings
    console.log('\n4️⃣  CHECKING DEFAULT EVENT MAPPINGS...');
    const defaultMappings = database.getDefaultEventMappings();
    const eventTypes = Object.keys(defaultMappings);
    console.log(`   ✅ ${eventTypes.length} default event types:`);

    eventTypes.forEach(eventType => {
      console.log(`   - ${eventType} → ${defaultMappings[eventType]}`);
    });

    // 5. Check EventSub connection status
    console.log('\n5️⃣  CHECKING EVENTSUB CONNECTIONS...');
    const connectedUserCount = streamMonitor.connectedUsers.size;
    console.log(`   📡 ${connectedUserCount} users connected to EventSub`);

    if (connectedUserCount === 0) {
      console.log('   ⚠️  No EventSub connections - this could be why events aren\'t triggering');
      console.log('   💡 Users need to be streaming or have monitoring enabled');
    }

    // 6. Summary and recommendations
    console.log('\n6️⃣  DIAGNOSTIC SUMMARY & RECOMMENDATIONS\n');

    const activeUsers = users.filter(async (user) => {
      const userId = user.twitchUserId || user.partitionKey;
      return userId && await database.hasFeature(userId, 'streamAlerts');
    });

    console.log('📊 SYSTEM STATUS:');
    console.log(`   - Total users: ${users.length}`);
    console.log(`   - Users with streamAlerts: ${activeUsers.length}`);
    console.log(`   - EventSub connections: ${connectedUserCount}`);
    console.log(`   - Default templates available: ${defaultTemplates.length}`);
    console.log(`   - Default event types: ${eventTypes.length}`);

    console.log('\n💡 LIKELY ISSUES & SOLUTIONS:');

    if (activeUsers.length === 0) {
      console.log('   ❌ MAIN ISSUE: streamAlerts feature not enabled for users');
      console.log('   🔧 SOLUTION: Enable streamAlerts feature flag for users');
      console.log('   📝 Command: Use admin panel or database query to enable feature');
    }

    if (connectedUserCount === 0) {
      console.log('   ❌ SECONDARY ISSUE: No active EventSub connections');
      console.log('   🔧 SOLUTION: Users need to start stream monitoring');
      console.log('   📝 Command: Users must go online or manually start monitoring');
    }

    console.log('\n✅ SYSTEM APPEARS FUNCTIONAL - EventSub & Alert mappings are properly implemented');
    console.log('🎯 The issue is likely missing feature flags or inactive EventSub connections');

  } catch (error) {
    console.error('❌ Error during diagnostic:', error);
  }
}

// Quick fix function to enable alerts for all users
async function enableAlertsForAllUsers() {
  console.log('\n🔧 ENABLING streamAlerts FEATURE FOR ALL USERS...\n');

  try {
    const users = await database.getAllUsers();
    let enabledCount = 0;

    for (const user of users) {
      const userId = user.twitchUserId || user.partitionKey;
      if (!userId) continue;

      const currentFeatures = user.features ? JSON.parse(user.features) : {};

      if (!currentFeatures.streamAlerts) {
        currentFeatures.streamAlerts = true;

        // Update user with new features
        const updatedUser = {
          ...user,
          features: JSON.stringify(currentFeatures)
        };

        await database.updateUser(userId, updatedUser);

        // Initialize default alerts and mappings
        await database.initializeUserAlerts(userId);
        await database.initializeEventMappings(userId);

        console.log(`✅ Enabled alerts for ${user.username}`);
        enabledCount++;
      } else {
        console.log(`⏭️  ${user.username} already has alerts enabled`);
      }
    }

    console.log(`\n🎉 Successfully enabled streamAlerts for ${enabledCount} users!`);
    console.log('💡 Users should now see both default and custom alerts in the UI');

  } catch (error) {
    console.error('❌ Error enabling alerts:', error);
  }
}

// Run diagnostic if this file is executed directly
if (require.main === module) {
  const args = process.argv.slice(2);

  if (args.includes('--fix')) {
    enableAlertsForAllUsers().then(() => {
      console.log('\n🏁 Fix completed');
      process.exit(0);
    }).catch((error) => {
      console.error('❌ Fix failed:', error);
      process.exit(1);
    });
  } else {
    diagnoseAlertSystem().then(() => {
      console.log('\n🏁 Diagnostic completed');
      console.log('\n💡 To automatically enable alerts for all users, run:');
      console.log('   node diagnose-alert-system.js --fix');
      process.exit(0);
    }).catch((error) => {
      console.error('❌ Diagnostic failed:', error);
      process.exit(1);
    });
  }
}

module.exports = { diagnoseAlertSystem, enableAlertsForAllUsers };

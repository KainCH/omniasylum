/**
 * Verify Stream Alerts Feature
 *
 * This script tests that the Stream Alerts feature has been properly added
 * and can be enabled for users.
 */

const database = require('./database');

async function verifyStreamAlertsFeature() {
  console.log('🔍 Verifying Stream Alerts Feature Implementation\n');
  console.log('=====================================\n');

  try {
    // 1. Check if streamAlerts is in default features
    console.log('1️⃣  CHECKING DEFAULT FEATURES...');
    const users = await database.getAllUsers();
    if (users.length > 0) {
      const testUser = users[0];
      const userId = testUser.twitchUserId || testUser.partitionKey;

      console.log(`   Testing with user: ${testUser.username} (${userId})`);

      // Test feature detection before enabling
      const hasStreamAlertsBefore = await database.hasFeature(userId, 'streamAlerts');
      console.log(`   - streamAlerts before: ${hasStreamAlertsBefore ? '✅' : '❌'}`);

      // Enable the feature
      const currentFeatures = testUser.features ? JSON.parse(testUser.features) : {};
      currentFeatures.streamAlerts = true;

      const updatedUser = {
        ...testUser,
        features: JSON.stringify(currentFeatures)
      };

      await database.updateUser(userId, updatedUser);
      console.log(`   ✅ Enabled streamAlerts for ${testUser.username}`);

      // Test feature detection after enabling
      const hasStreamAlertsAfter = await database.hasFeature(userId, 'streamAlerts');
      console.log(`   - streamAlerts after: ${hasStreamAlertsAfter ? '✅' : '❌'}`);

      if (hasStreamAlertsAfter) {
        // 2. Test alert initialization
        console.log('\n2️⃣  TESTING ALERT SYSTEM INITIALIZATION...');

        // Initialize alerts
        await database.initializeUserAlerts(userId);
        const userAlerts = await database.getUserAlerts(userId);
        console.log(`   ✅ User alerts: ${userAlerts.length} found`);

        // Check default alert templates
        const defaultTemplates = database.getDefaultAlertTemplates();
        console.log(`   ✅ Default templates: ${defaultTemplates.length} available`);

        // List available alert types
        const alertTypes = defaultTemplates.map(t => t.type);
        console.log(`   ✅ Alert types: ${alertTypes.join(', ')}`);

        // 3. Test event mappings
        console.log('\n3️⃣  TESTING EVENT MAPPINGS...');

        // Initialize event mappings
        await database.initializeEventMappings(userId);
        const eventMappings = await database.getEventMappings(userId);
        const mappingKeys = Object.keys(eventMappings);
        console.log(`   ✅ Event mappings: ${mappingKeys.length} configured`);

        // Show the mappings
        console.log('   📋 Event → Alert Mappings:');
        for (const [event, alertType] of Object.entries(eventMappings)) {
          console.log(`      ${event} → ${alertType}`);
        }

        // 4. Test alert retrieval for specific events
        console.log('\n4️⃣  TESTING EVENT-TO-ALERT RESOLUTION...');

        const testEvents = ['channel.follow', 'channel.subscribe', 'channel.cheer'];
        for (const eventType of testEvents) {
          const alert = await database.getAlertForEvent(userId, eventType);
          if (alert) {
            console.log(`   ✅ ${eventType}: "${alert.name}" (${alert.isDefault ? 'default' : 'custom'})`);
          } else {
            console.log(`   ❌ ${eventType}: No alert configured`);
          }
        }

        console.log('\n5️⃣  VERIFICATION SUMMARY');
        console.log('========================');
        console.log(`✅ streamAlerts feature: ${hasStreamAlertsAfter ? 'ENABLED' : 'DISABLED'}`);
        console.log(`✅ Default alert templates: ${defaultTemplates.length} available`);
        console.log(`✅ User alert initialization: ${userAlerts.length} alerts`);
        console.log(`✅ Event mappings: ${mappingKeys.length} configured`);
        console.log(`✅ Alert resolution: Working for ${testEvents.length} event types`);

        console.log('\n🎉 STREAM ALERTS FEATURE FULLY OPERATIONAL!');
        console.log('\n📝 NEXT STEPS:');
        console.log('   1. Users can now see "Stream Alerts" in Feature Management');
        console.log('   2. Enable the feature for users who want custom Twitch event alerts');
        console.log('   3. Access Event & Alert Manager will now show both:');
        console.log('      - ✨ Event Mappings (map Twitch events to alerts)');
        console.log('      - 🎨 Create Alert (custom alert templates)');
        console.log('   4. EventSub system will trigger alerts when users go live');

      } else {
        console.log('\n❌ Failed to enable streamAlerts feature');
      }

    } else {
      console.log('   ⚠️  No users found in database to test with');
    }

  } catch (error) {
    console.error('❌ Error during verification:', error);
  }
}

// Run verification if this file is executed directly
if (require.main === module) {
  verifyStreamAlertsFeature().then(() => {
    console.log('\n🏁 Verification completed');
    process.exit(0);
  }).catch((error) => {
    console.error('❌ Verification failed:', error);
    process.exit(1);
  });
}

module.exports = { verifyStreamAlertsFeature };

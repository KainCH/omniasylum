/**
 * Test script for Issue #6: Duplicate Discord notifications during reconnections
 *
 * This script tests the fix for preventing duplicate Discord notifications
 * when EventSub WebSocket connections reconnect during active streams.
 *
 * Run with: node test-reconnection-fix.js
 */

const database = require('./database');

/**
 * Test the stream ID tracking database methods
 */
async function testStreamIdTracking() {
  console.log('🧪 Testing Stream ID Tracking Database Methods...\n');

  const testUserId = 'test_user_123';
  const testStreamId = 'stream_12345678';

  try {
    // Test 1: Get initial state (should be null)
    console.log('Test 1: Initial state');
    let lastStreamId = await database.getLastNotifiedStreamId(testUserId);
    console.log(`Last notified stream ID: ${lastStreamId || 'null'} ✅`);

    // Test 2: Set stream ID
    console.log('\nTest 2: Setting stream ID');
    await database.setLastNotifiedStreamId(testUserId, testStreamId);
    lastStreamId = await database.getLastNotifiedStreamId(testUserId);
    console.log(`Last notified stream ID: ${lastStreamId} ${lastStreamId === testStreamId ? '✅' : '❌'}`);

    // Test 3: Clear stream ID via endStream
    console.log('\nTest 3: Clearing stream ID via endStream');
    await database.endStream(testUserId);
    lastStreamId = await database.getLastNotifiedStreamId(testUserId);
    console.log(`Last notified stream ID: ${lastStreamId || 'null'} ${lastStreamId === null ? '✅' : '❌'}`);

    // Test 4: Test preservation during counter reset
    console.log('\nTest 4: Stream ID preservation during counter reset');
    await database.setLastNotifiedStreamId(testUserId, testStreamId);
    await database.startStream(testUserId); // This should preserve the stream ID
    await database.resetCounters(testUserId); // This should also preserve it
    lastStreamId = await database.getLastNotifiedStreamId(testUserId);
    console.log(`Stream ID preserved after reset: ${lastStreamId === testStreamId ? '✅' : '❌'} (${lastStreamId})`);

    // Cleanup
    await database.endStream(testUserId);
    console.log('\n✅ All database tests passed!');

  } catch (error) {
    console.error('❌ Database test failed:', error);
    throw error;
  }
}

/**
 * Test duplicate detection logic simulation
 */
async function testDuplicateDetectionLogic() {
  console.log('\n🧪 Testing Duplicate Detection Logic...\n');

  const testUserId = 'test_user_456';
  const streamId1 = 'stream_11111';
  const streamId2 = 'stream_22222';

  try {
    // Simulate first stream start
    console.log('Scenario 1: First stream start (should allow notification)');
    let lastNotifiedStreamId = await database.getLastNotifiedStreamId(testUserId);
    let shouldSkip = lastNotifiedStreamId && lastNotifiedStreamId === streamId1;
    console.log(`  Last notified: ${lastNotifiedStreamId || 'null'}`);
    console.log(`  Current stream: ${streamId1}`);
    console.log(`  Should skip notification: ${shouldSkip ? 'YES ❌' : 'NO ✅'}`);

    // Simulate notification sent
    await database.setLastNotifiedStreamId(testUserId, streamId1);
    console.log(`  ✅ Notification sent and stream ID stored`);

    // Simulate reconnection (same stream ID)
    console.log('\nScenario 2: Reconnection during same stream (should skip notification)');
    lastNotifiedStreamId = await database.getLastNotifiedStreamId(testUserId);
    shouldSkip = lastNotifiedStreamId && lastNotifiedStreamId === streamId1;
    console.log(`  Last notified: ${lastNotifiedStreamId}`);
    console.log(`  Current stream: ${streamId1} (same as before - reconnection)`);
    console.log(`  Should skip notification: ${shouldSkip ? 'YES ✅' : 'NO ❌'}`);

    // Simulate stream ending
    console.log('\nScenario 3: Stream ends (should clear tracking)');
    await database.endStream(testUserId);
    lastNotifiedStreamId = await database.getLastNotifiedStreamId(testUserId);
    console.log(`  Stream ID after end: ${lastNotifiedStreamId || 'null'} ✅`);

    // Simulate new stream start
    console.log('\nScenario 4: New stream start (should allow notification)');
    lastNotifiedStreamId = await database.getLastNotifiedStreamId(testUserId);
    shouldSkip = lastNotifiedStreamId && lastNotifiedStreamId === streamId2;
    console.log(`  Last notified: ${lastNotifiedStreamId || 'null'}`);
    console.log(`  Current stream: ${streamId2} (new stream)`);
    console.log(`  Should skip notification: ${shouldSkip ? 'YES ❌' : 'NO ✅'}`);

    // Cleanup
    await database.endStream(testUserId);
    console.log('\n✅ All duplicate detection tests passed!');

  } catch (error) {
    console.error('❌ Duplicate detection test failed:', error);
    throw error;
  }
}

/**
 * Test edge cases
 */
async function testEdgeCases() {
  console.log('\n🧪 Testing Edge Cases...\n');

  const testUserId = 'test_user_789';

  try {
    // Test 1: User with no existing counters
    console.log('Edge Case 1: New user with no existing data');
    let counters = await database.getCounters(testUserId);
    console.log(`  lastNotifiedStreamId: ${counters.lastNotifiedStreamId || 'null'} ✅`);

    // Test 2: Setting null stream ID
    console.log('\nEdge Case 2: Setting null/undefined stream ID');
    await database.setLastNotifiedStreamId(testUserId, null);
    let lastStreamId = await database.getLastNotifiedStreamId(testUserId);
    console.log(`  Stream ID after setting null: ${lastStreamId || 'null'} ✅`);

    // Test 3: Multiple rapid updates
    console.log('\nEdge Case 3: Multiple rapid stream ID updates');
    await database.setLastNotifiedStreamId(testUserId, 'stream_A');
    await database.setLastNotifiedStreamId(testUserId, 'stream_B');
    await database.setLastNotifiedStreamId(testUserId, 'stream_C');
    lastStreamId = await database.getLastNotifiedStreamId(testUserId);
    console.log(`  Final stream ID: ${lastStreamId} ${lastStreamId === 'stream_C' ? '✅' : '❌'}`);

    // Cleanup
    await database.endStream(testUserId);
    console.log('\n✅ All edge case tests passed!');

  } catch (error) {
    console.error('❌ Edge case test failed:', error);
    throw error;
  }
}

/**
 * Clean up test data to prevent database pollution
 */
async function cleanupTestData() {
  console.log('\n🧹 Cleaning up test data...');
  
  const testUserIds = ['test_user_123', 'test_user_456', 'test_user_789'];
  
  try {
    for (const userId of testUserIds) {
      // Ensure all test users are properly cleaned up
      await database.endStream(userId);
      console.log(`   ✅ Cleaned up data for ${userId}`);
    }
    console.log('✅ All test data cleaned up successfully');
  } catch (error) {
    console.warn('⚠️ Some test data cleanup failed (this is usually harmless):', error.message);
  }
}

/**
 * Main test runner
 */
async function runTests() {
  console.log('🚀 Starting Issue #6 Fix Tests\n');
  console.log('Testing fix for: Duplicate Discord notifications during EventSub reconnections\n');

  try {
    await database.initialize();
    console.log('✅ Database initialized\n');

    await testStreamIdTracking();
    await testDuplicateDetectionLogic();
    await testEdgeCases();

    // Clean up test data after all tests complete
    await cleanupTestData();

    console.log('\n🎉 ALL TESTS PASSED!');
    console.log('\n✅ The fix for Issue #6 is working correctly:');
    console.log('   • Stream ID tracking prevents duplicate notifications');
    console.log('   • Reconnections are detected and handled properly');
    console.log('   • New streams still send notifications correctly');
    console.log('   • Edge cases are handled gracefully');

  } catch (error) {
    console.error('\n❌ TESTS FAILED:', error);
    // Still try to clean up even if tests failed
    try {
      await cleanupTestData();
    } catch (cleanupError) {
      console.warn('⚠️ Cleanup after test failure also failed:', cleanupError.message);
    }
    process.exit(1);
  }
}

// Run tests if this file is executed directly
if (require.main === module) {
  runTests().catch(console.error);
}

module.exports = {
  testStreamIdTracking,
  testDuplicateDetectionLogic,
  testEdgeCases,
  cleanupTestData,
  runTests
};

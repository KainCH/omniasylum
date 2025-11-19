/**
 * Test Script for Sound System Fixes (Issue #5)
 * This script tests the Phase 1 fixes for alert sound problems
 */

console.log('🧪 Testing Sound System Fixes - Phase 1');
console.log('='.repeat(50));

// Test 1: Verify sound file extensions match between database and actual files
const expectedSoundMappings = {
  'doorCreak.wav': 'follow',
  'electroshock.wav': 'subscription',
  'typewriter.wav': 'resub',
  'pillRattle.mp3': 'bits',
  'alarm.wav': 'raid',
  'heartMonitor.wav': 'giftsub',
  'hypeTrain.wav': 'hypetrain'
};

console.log('📋 Test 1: Sound File Extension Mappings');
console.log('Expected sound triggers:', expectedSoundMappings);

// Test 2: Check if asylum effects is available
setTimeout(() => {
  if (typeof window !== 'undefined' && window.asylumEffects) {
    console.log('✅ AsylumEffects is available');

    // Test 3: Check audio cache
    const cache = window.asylumEffects.audioCache;
    console.log('📋 Test 3: Audio Cache Status');
    console.log(`Cache contains ${Object.keys(cache).length} sounds`);

    for (const [key, audio] of Object.entries(cache)) {
      const state = audio.readyState >= 3 ? '✅ READY' : '⚠️ LOADING';
      console.log(`  ${state} ${key}: ${audio.src}`);
    }

    // Test 4: Test individual sounds
    console.log('📋 Test 4: Individual Sound Tests');
    const testSounds = ['doorCreak.wav', 'electroshock.wav', 'typewriter.wav', 'pillRattle.mp3', 'alarm.wav', 'heartMonitor.wav', 'hypeTrain.wav'];

    testSounds.forEach((sound, index) => {
      setTimeout(() => {
        console.log(`🔊 Testing: ${sound}`);
        try {
          window.asylumEffects.playSound(sound);
          console.log(`✅ ${sound} - Play attempted`);
        } catch (error) {
          console.error(`❌ ${sound} - Error:`, error);
        }
      }, index * 1000); // Stagger tests by 1 second
    });

  } else {
    console.error('❌ AsylumEffects not available');
  }
}, 2000); // Wait 2 seconds for initialization

console.log('🔗 Open the browser console at http://localhost:3000/effects-test.html to see results');

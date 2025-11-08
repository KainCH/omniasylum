/**
 * Production Discord Settings Checker
 *
 * This script checks the current Discord notification settings for user riress
 * by querying the production API endpoints to see what's configured
 */

const https = require('https');

const BASE_URL = 'https://omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io';

// You'll need to get this from your browser's developer tools
// Look for the Authorization header in network requests
const AUTH_TOKEN = 'YOUR_JWT_TOKEN_HERE'; // Replace with actual token from browser

async function checkProductionSettings() {
  console.log('🔍 Checking Discord notification settings in production...\n');

  // Helper function to make authenticated requests
  function makeRequest(path) {
    return new Promise((resolve, reject) => {
      const options = {
        hostname: 'omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io',
        path: path,
        method: 'GET',
        headers: {
          'Authorization': `Bearer ${AUTH_TOKEN}`,
          'Content-Type': 'application/json'
        }
      };

      const req = https.request(options, (res) => {
        let data = '';
        res.on('data', (chunk) => data += chunk);
        res.on('end', () => {
          if (res.statusCode === 200) {
            try {
              resolve(JSON.parse(data));
            } catch (error) {
              resolve(data);
            }
          } else {
            reject(new Error(`HTTP ${res.statusCode}: ${data}`));
          }
        });
      });

      req.on('error', reject);
      req.end();
    });
  }

  try {
    console.log('👤 Step 1: Check User Profile');
    try {
      const profile = await makeRequest('/api/user/profile');
      console.log(`✅ User: ${profile.displayName} (@${profile.username})`);
      console.log(`   Role: ${profile.role || 'streamer'}`);
      console.log(`   User ID: ${profile.twitchUserId}`);
    } catch (error) {
      console.error('❌ Failed to get user profile:', error.message);
      if (error.message.includes('401')) {
        console.log('💡 You need to login and get your JWT token from browser developer tools');
        console.log('   1. Open browser developer tools (F12)');
        console.log('   2. Go to Network tab');
        console.log('   3. Make any API request from the app');
        console.log('   4. Look for Authorization header: Bearer <token>');
        console.log('   5. Copy the token and update AUTH_TOKEN in this script');
        return;
      }
    }
    console.log('');

    console.log('🔗 Step 2: Check Discord Webhook');
    try {
      const webhook = await makeRequest('/api/user/discord-webhook');
      console.log('Discord webhook configuration:', webhook);
    } catch (error) {
      console.error('❌ Failed to get Discord webhook:', error.message);
    }
    console.log('');

    console.log('📢 Step 3: Check Notification Settings');
    try {
      const settings = await makeRequest('/api/user/discord-settings');
      console.log('Discord notification settings:', settings);
    } catch (error) {
      console.error('❌ Failed to get Discord settings:', error.message);
    }
    console.log('');

    console.log('🎛️ Step 4: Check Feature Flags');
    try {
      const features = await makeRequest('/api/user/features');
      console.log('User features:', features);
    } catch (error) {
      console.error('❌ Failed to get user features:', error.message);
    }
    console.log('');

    console.log('🔌 Step 5: Check EventSub Status');
    try {
      const eventSubStatus = await makeRequest('/api/stream/eventsub-status');
      console.log('EventSub status:', eventSubStatus);
    } catch (error) {
      console.error('❌ Failed to get EventSub status:', error.message);
    }

  } catch (error) {
    console.error('❌ Production settings check failed:', error);
  }
}

console.log('📝 Instructions:');
console.log('1. Login to your stream counter app in browser');
console.log('2. Open Developer Tools (F12) > Network tab');
console.log('3. Make any API request (like clicking a button)');
console.log('4. Find a request and copy the Authorization header token');
console.log('5. Replace AUTH_TOKEN in this script with your token');
console.log('6. Run this script again');
console.log('');

if (AUTH_TOKEN === 'YOUR_JWT_TOKEN_HERE') {
  console.log('⚠️  Please update AUTH_TOKEN with your actual JWT token first!');
} else {
  checkProductionSettings().then(() => {
    console.log('🏁 Production settings check complete');
  }).catch((error) => {
    console.error('Script error:', error);
  });
}

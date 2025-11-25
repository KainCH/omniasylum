/**
 * Manual Webhook Save Test
 *
 * This script manually tests the webhook save functionality
 * by making a direct API call to save the webhook
 */

const https = require('https');

// You'll need your JWT token from browser developer tools
const AUTH_TOKEN = 'YOUR_JWT_TOKEN_HERE';
const WEBHOOK_URL = 'https://discord.com/api/webhooks/1442675628372394025/g3KOlf2i5W141v5DZxjKUB9V4O6wQ8tw9oPy-AIrvzqxUncIT9CmnfnY-PsIPvpZU3Aw';

async function testWebhookSave() {
  console.log('🧪 Manual Webhook Save Test\n');

  if (AUTH_TOKEN === 'YOUR_JWT_TOKEN_HERE') {
    console.log('⚠️  Please update AUTH_TOKEN with your actual JWT token first!');
    console.log('   1. Open browser developer tools (F12)');
    console.log('   2. Go to Network tab');
    console.log('   3. Make any API request from the app');
    console.log('   4. Look for Authorization header: Bearer <token>');
    console.log('   5. Copy the token and update AUTH_TOKEN in this script');
    return;
  }

  // Test data
  const testData = {
    webhookUrl: WEBHOOK_URL,
    enabled: true
  };

  console.log('📤 Testing webhook save with data:', {
    webhookUrl: `${testData.webhookUrl.substring(0, 50)}...`,
    enabled: testData.enabled
  });

  // Make PUT request
  const postData = JSON.stringify(testData);

  const options = {
    hostname: 'omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io',
    path: '/api/user/discord-webhook',
    method: 'PUT',
    headers: {
      'Authorization': `Bearer ${AUTH_TOKEN}`,
      'Content-Type': 'application/json',
      'Content-Length': Buffer.byteLength(postData)
    }
  };

  return new Promise((resolve, reject) => {
    const req = https.request(options, (res) => {
      let data = '';
      res.on('data', (chunk) => data += chunk);
      res.on('end', () => {
        console.log(`📬 Response Status: ${res.statusCode}`);
        console.log(`📬 Response Headers:`, res.headers);

        if (res.statusCode >= 200 && res.statusCode < 300) {
          try {
            const response = JSON.parse(data);
            console.log('✅ Webhook save successful:', response);

            // Now test reading it back
            setTimeout(() => testWebhookRead(), 2000);
          } catch (error) {
            console.log('✅ Webhook save successful (non-JSON response):', data);
          }
        } else {
          console.error('❌ Webhook save failed:', data);
        }
        resolve();
      });
    });

    req.on('error', (error) => {
      console.error('❌ Request error:', error);
      reject(error);
    });

    req.write(postData);
    req.end();
  });
}

async function testWebhookRead() {
  console.log('\n🔍 Testing webhook read back...');

  const options = {
    hostname: 'omniforgestream-api-prod.proudplant-8dc6fe7a.southcentralus.azurecontainerapps.io',
    path: '/api/user/discord-webhook',
    method: 'GET',
    headers: {
      'Authorization': `Bearer ${AUTH_TOKEN}`,
      'Content-Type': 'application/json'
    }
  };

  return new Promise((resolve, reject) => {
    const req = https.request(options, (res) => {
      let data = '';
      res.on('data', (chunk) => data += chunk);
      res.on('end', () => {
        console.log(`📬 Read Response Status: ${res.statusCode}`);

        if (res.statusCode >= 200 && res.statusCode < 300) {
          try {
            const response = JSON.parse(data);
            console.log('📋 Current webhook config:', {
              webhookUrl: response.webhookUrl ? `${response.webhookUrl.substring(0, 50)}...` : 'EMPTY',
              enabled: response.enabled
            });

            if (response.webhookUrl) {
              console.log('✅ Webhook was successfully saved and retrieved!');
            } else {
              console.log('❌ Webhook was not saved - URL is empty after save attempt');
            }
          } catch (error) {
            console.log('❌ Failed to parse read response:', data);
          }
        } else {
          console.error('❌ Webhook read failed:', data);
        }
        resolve();
      });
    });

    req.on('error', reject);
    req.end();
  });
}

// Run the test
testWebhookSave().then(() => {
  console.log('\n🏁 Manual webhook test complete');
}).catch((error) => {
  console.error('Test error:', error);
});

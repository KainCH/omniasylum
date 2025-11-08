/**
 * Comprehensive Discord Webhook Save Debug Script
 * This script tests the webhook save functionality at multiple levels
 */

console.log('🔍 Starting Discord Webhook Save Debug Script');

// Test 1: Check if we're authenticated
async function testAuthentication() {
    console.log('\n=== TEST 1: Authentication ===');

    try {
        const response = await fetch('/api/user/profile', {
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${localStorage.getItem('authToken')}`,
                'Content-Type': 'application/json'
            }
        });

        if (response.ok) {
            const data = await response.json();
            console.log('✅ Authentication working:', data.displayName);
            return true;
        } else {
            console.error('❌ Authentication failed:', response.status, response.statusText);
            return false;
        }
    } catch (error) {
        console.error('❌ Authentication error:', error);
        return false;
    }
}

// Test 2: Check current webhook value
async function getCurrentWebhook() {
    console.log('\n=== TEST 2: Current Webhook Value ===');

    try {
        const response = await fetch('/api/user/discord-webhook', {
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${localStorage.getItem('authToken')}`,
                'Content-Type': 'application/json'
            }
        });

        if (response.ok) {
            const data = await response.json();
            console.log('✅ Current webhook data:', data);
            return data.discordWebhookUrl || '';
        } else {
            console.error('❌ Failed to get webhook:', response.status, response.statusText);
            return null;
        }
    } catch (error) {
        console.error('❌ Get webhook error:', error);
        return null;
    }
}

// Test 3: Test PUT request with detailed logging
async function testWebhookSave(webhookUrl) {
    console.log('\n=== TEST 3: Webhook Save Test ===');
    console.log('Attempting to save webhook:', webhookUrl);

    const requestData = {
        discordWebhookUrl: webhookUrl
    };

    console.log('Request payload:', requestData);
    console.log('Auth token:', localStorage.getItem('authToken') ? 'Present' : 'Missing');

    try {
        const response = await fetch('/api/user/discord-webhook', {
            method: 'PUT',
            headers: {
                'Authorization': `Bearer ${localStorage.getItem('authToken')}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestData)
        });

        console.log('Response status:', response.status);
        console.log('Response headers:', Object.fromEntries(response.headers.entries()));

        const responseText = await response.text();
        console.log('Raw response:', responseText);

        if (response.ok) {
            try {
                const data = JSON.parse(responseText);
                console.log('✅ Webhook save successful:', data);
                return true;
            } catch (parseError) {
                console.log('✅ Webhook save successful (non-JSON response):', responseText);
                return true;
            }
        } else {
            console.error('❌ Webhook save failed:', response.status, response.statusText);
            console.error('❌ Error response:', responseText);
            return false;
        }
    } catch (error) {
        console.error('❌ Network error during save:', error);
        return false;
    }
}

// Test 4: Verify the save worked
async function verifyWebhookSave(expectedUrl) {
    console.log('\n=== TEST 4: Verify Save Worked ===');

    // Wait a moment for the save to complete
    await new Promise(resolve => setTimeout(resolve, 1000));

    const currentWebhook = await getCurrentWebhook();

    if (currentWebhook === expectedUrl) {
        console.log('✅ Webhook verification successful!');
        return true;
    } else {
        console.log('❌ Webhook verification failed!');
        console.log('Expected:', expectedUrl);
        console.log('Actual:', currentWebhook);
        return false;
    }
}

// Main test runner
async function runAllTests() {
    console.log('🚀 Starting comprehensive webhook save tests...');

    // Step 1: Test authentication
    const authOk = await testAuthentication();
    if (!authOk) {
        console.log('❌ Cannot proceed - authentication failed');
        return;
    }

    // Step 2: Get current webhook
    const currentWebhook = await getCurrentWebhook();
    console.log('Current webhook state:', currentWebhook);

    // Step 3: Test saving a webhook
    const testWebhookUrl = 'https://discord.com/api/webhooks/1234567890/test-webhook-' + Date.now();
    const saveOk = await testWebhookSave(testWebhookUrl);

    if (!saveOk) {
        console.log('❌ Save test failed - stopping here');
        return;
    }

    // Step 4: Verify the save worked
    const verifyOk = await verifyWebhookSave(testWebhookUrl);

    if (verifyOk) {
        console.log('\n🎉 ALL TESTS PASSED! Webhook save is working correctly.');
    } else {
        console.log('\n❌ Save succeeded but verification failed - possible timing issue');
    }

    // Step 5: Clean up - restore original webhook
    if (currentWebhook !== null) {
        console.log('\n=== CLEANUP: Restoring Original Webhook ===');
        await testWebhookSave(currentWebhook);
    }
}

// Export for manual testing
window.debugWebhookSave = {
    testAuthentication,
    getCurrentWebhook,
    testWebhookSave,
    verifyWebhookSave,
    runAllTests
};

console.log('🔧 Debug functions loaded! Run: debugWebhookSave.runAllTests()');

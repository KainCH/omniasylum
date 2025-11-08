const express = require('express');
const { requireAuth } = require('./authMiddleware');
const router = express.Router();

// Apply authentication to all routes
router.use(requireAuth);

// Get current EventSub subscriptions for a user
router.get('/subscriptions/:userId', async (req, res) => {
  try {
    const { userId } = req.params;

    // Verify user has access to this data (admin or self)
    if (req.user.twitchUserId !== userId && req.user.role !== 'admin') {
      return res.status(403).json({ error: 'Access denied' });
    }

    // Get user's current EventSub subscriptions from database
    const subscriptions = await req.database.getEventSubSubscriptions(userId);

    res.json({
      success: true,
      subscriptions: subscriptions || {}
    });
  } catch (error) {
    console.error('❌ Error fetching EventSub subscriptions:', error);
    res.status(500).json({ error: 'Failed to fetch subscriptions' });
  }
});

// Update EventSub subscription for a user
router.put('/subscriptions/:userId', async (req, res) => {
  try {
    const { userId } = req.params;
    const { eventType, enabled } = req.body;

    // Verify user has access to modify this data (admin or self)
    if (req.user.twitchUserId !== userId && req.user.role !== 'admin') {
      return res.status(403).json({ error: 'Access denied' });
    }

    if (!eventType || typeof enabled !== 'boolean') {
      return res.status(400).json({ error: 'Missing eventType or enabled parameter' });
    }

    // Validate eventType
    const validEventTypes = [
      'channel.follow',
      'channel.subscribe',
      'channel.subscription.gift',
      'channel.subscription.message',
      'channel.cheer',
      'channel.raid',
      'channel.channel_points_custom_reward_redemption.add',
      'channel.chat.message',
      'stream.online',
      'stream.offline'
    ];

    if (!validEventTypes.includes(eventType)) {
      return res.status(400).json({ error: 'Invalid event type' });
    }

    // Get user data for Twitch operations
    const user = await req.database.getUser(userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Get current subscriptions
    let subscriptions = await req.database.getEventSubSubscriptions(userId) || {};

    if (enabled) {
      // Subscribe to event
      try {
        const subscriptionId = await req.twitchService.subscribeToEvent(user, eventType);
        subscriptions[eventType] = {
          enabled: true,
          subscriptionId: subscriptionId,
          subscribedAt: new Date().toISOString()
        };

        console.log(`✅ Subscribed ${userId} to ${eventType} (ID: ${subscriptionId})`);
      } catch (error) {
        console.error(`❌ Failed to subscribe ${userId} to ${eventType}:`, error);
        return res.status(500).json({ error: `Failed to subscribe to ${eventType}` });
      }
    } else {
      // Unsubscribe from event
      if (subscriptions[eventType]?.subscriptionId) {
        try {
          await req.twitchService.unsubscribeFromEvent(subscriptions[eventType].subscriptionId);
          delete subscriptions[eventType];

          console.log(`✅ Unsubscribed ${userId} from ${eventType}`);
        } catch (error) {
          console.error(`❌ Failed to unsubscribe ${userId} from ${eventType}:`, error);
          // Still remove from our records even if Twitch API fails
          delete subscriptions[eventType];
        }
      } else {
        // Just remove from our records
        delete subscriptions[eventType];
      }
    }

    // Save updated subscriptions
    await req.database.saveEventSubSubscriptions(userId, subscriptions);

    res.json({
      success: true,
      eventType,
      enabled,
      subscriptions
    });
  } catch (error) {
    console.error('❌ Error updating EventSub subscription:', error);
    res.status(500).json({ error: 'Failed to update subscription' });
  }
});

// Unsubscribe from all events for a user
router.post('/subscriptions/:userId/unsubscribe-all', async (req, res) => {
  try {
    const { userId } = req.params;

    // Verify user has access to modify this data (admin or self)
    if (req.user.twitchUserId !== userId && req.user.role !== 'admin') {
      return res.status(403).json({ error: 'Access denied' });
    }

    // Get current subscriptions
    const subscriptions = await req.database.getEventSubSubscriptions(userId) || {};

    // Unsubscribe from each event
    const unsubscribePromises = Object.entries(subscriptions).map(async ([eventType, data]) => {
      if (data.subscriptionId) {
        try {
          await req.twitchService.unsubscribeFromEvent(data.subscriptionId);
          console.log(`✅ Unsubscribed ${userId} from ${eventType}`);
        } catch (error) {
          console.error(`❌ Failed to unsubscribe ${userId} from ${eventType}:`, error);
        }
      }
    });

    await Promise.all(unsubscribePromises);

    // Clear all subscriptions
    await req.database.saveEventSubSubscriptions(userId, {});

    console.log(`✅ Unsubscribed ${userId} from all EventSub events`);

    res.json({
      success: true,
      message: 'Unsubscribed from all events'
    });
  } catch (error) {
    console.error('❌ Error unsubscribing from all events:', error);
    res.status(500).json({ error: 'Failed to unsubscribe from all events' });
  }
});

// Get EventSub webhook status and statistics
router.get('/status', async (req, res) => {
  try {
    // Only allow admins or authenticated users to see status
    if (!req.user) {
      return res.status(401).json({ error: 'Authentication required' });
    }

    const status = await req.twitchService.getEventSubStatus();

    res.json({
      success: true,
      status
    });
  } catch (error) {
    console.error('❌ Error getting EventSub status:', error);
    res.status(500).json({ error: 'Failed to get EventSub status' });
  }
});

// Get detailed EventSub subscription status for current user
router.get('/subscription-status', async (req, res) => {
  try {
    const userId = req.user.twitchUserId;

    // Get user's current subscriptions
    const userSubscriptions = await req.database.getEventSubSubscriptions(userId);

    // Get overall EventSub system status
    const systemStatus = await req.twitchService.getEventSubStatus();

    // Event type definitions for display
    const eventTypeInfo = {
      'channel.follow': { name: 'Follow Events', icon: '👥', category: 'Engagement' },
      'channel.subscribe': { name: 'Subscription Events', icon: '⭐', category: 'Revenue' },
      'channel.subscription.gift': { name: 'Gift Sub Events', icon: '🎁', category: 'Revenue' },
      'channel.subscription.message': { name: 'Resub Events', icon: '🔄', category: 'Engagement' },
      'channel.cheer': { name: 'Cheer/Bits Events', icon: '💎', category: 'Revenue' },
      'channel.raid': { name: 'Raid Events', icon: '🚨', category: 'Engagement' },
      'channel.channel_points_custom_reward_redemption.add': { name: 'Channel Points', icon: '🏆', category: 'Engagement' },
      'channel.chat.message': { name: 'Chat Messages', icon: '💬', category: 'Chat' },
      'stream.online': { name: 'Stream Online', icon: '🔴', category: 'Stream Status' },
      'stream.offline': { name: 'Stream Offline', icon: '⚫', category: 'Stream Status' }
    };

    // Build subscription summary
    const activeSubscriptions = Object.keys(userSubscriptions).filter(key => userSubscriptions[key]?.enabled);
    const subscriptionsByCategory = {};

    activeSubscriptions.forEach(eventType => {
      const info = eventTypeInfo[eventType];
      if (info) {
        if (!subscriptionsByCategory[info.category]) {
          subscriptionsByCategory[info.category] = [];
        }
        subscriptionsByCategory[info.category].push({
          eventType,
          name: info.name,
          icon: info.icon,
          subscriptionId: userSubscriptions[eventType].subscriptionId,
          subscribedAt: userSubscriptions[eventType].subscribedAt
        });
      }
    });

    res.json({
      success: true,
      userId: userId,
      username: req.user.username,
      subscriptionStatus: {
        totalActive: activeSubscriptions.length,
        totalPossible: Object.keys(eventTypeInfo).length,
        activeSubscriptions,
        subscriptionsByCategory,
        systemConnected: systemStatus.connected,
        totalSystemSubscriptions: systemStatus.subscriptions || 0
      },
      lastUpdated: new Date().toISOString()
    });

  } catch (error) {
    console.error('❌ Error getting EventSub subscription status:', error);
    res.status(500).json({ error: 'Failed to get subscription status' });
  }
});

// Get available event types and their descriptions
router.get('/event-types', (req, res) => {
  const eventTypes = {
    'channel.follow': {
      name: 'Follow Events',
      description: 'User follows the channel',
      category: 'Engagement',
      cost: 'Low',
      requiresAuth: true
    },
    'channel.subscribe': {
      name: 'Subscription Events',
      description: 'New subscriptions (including gift subs)',
      category: 'Revenue',
      cost: 'Medium',
      requiresAuth: true
    },
    'channel.subscription.gift': {
      name: 'Gift Sub Events',
      description: 'Community gift subscriptions',
      category: 'Revenue',
      cost: 'Medium',
      requiresAuth: true
    },
    'channel.subscription.message': {
      name: 'Resub Events',
      description: 'Resubs with messages',
      category: 'Engagement',
      cost: 'Medium',
      requiresAuth: true
    },
    'channel.cheer': {
      name: 'Cheer/Bits Events',
      description: 'Bits/cheers from viewers',
      category: 'Revenue',
      cost: 'Medium',
      requiresAuth: true
    },
    'channel.raid': {
      name: 'Raid Events',
      description: 'Channel receives raid',
      category: 'Engagement',
      cost: 'Low',
      requiresAuth: true
    },
    'channel.channel_points_custom_reward_redemption.add': {
      name: 'Channel Points',
      description: 'Channel point redemptions',
      category: 'Engagement',
      cost: 'High',
      requiresAuth: true
    },
    'stream.online': {
      name: 'Stream Online',
      description: 'Stream goes live',
      category: 'Stream Status',
      cost: 'Low',
      requiresAuth: false
    },
    'stream.offline': {
      name: 'Stream Offline',
      description: 'Stream goes offline',
      category: 'Stream Status',
      cost: 'Low',
      requiresAuth: false
    }
  };

  res.json({
    success: true,
    eventTypes
  });
});

module.exports = router;

const express = require('express');
const database = require('./database');
const streamMonitor = require('./streamMonitor');
const { requireAuth, requireAdmin } = require('./authMiddleware');

const router = express.Router();

/**
 * Get all channel point rewards for current user
 * GET /api/rewards
 */
router.get('/', requireAuth, async (req, res) => {
  try {
    // Check if user has channel points feature enabled
    const hasChannelPoints = await database.hasFeature(req.user.twitchUserId, 'channelPoints');
    if (!hasChannelPoints) {
      return res.status(403).json({ error: 'Channel points feature not enabled' });
    }

    const rewards = await database.getUserChannelPointRewards(req.user.twitchUserId);
    res.json({
      rewards: rewards,
      count: rewards.length
    });
  } catch (error) {
    console.error('Error getting channel point rewards:', error);
    res.status(500).json({ error: 'Failed to get channel point rewards' });
  }
});

/**
 * Create a new channel point reward
 * POST /api/rewards
 */
router.post('/', requireAuth, async (req, res) => {
  try {
    // Check if user has channel points feature enabled
    const hasChannelPoints = await database.hasFeature(req.user.twitchUserId, 'channelPoints');
    if (!hasChannelPoints) {
      return res.status(403).json({ error: 'Channel points feature not enabled' });
    }

    const {
      title,
      cost,
      action,
      prompt,
      backgroundColor,
      maxRedemptionsPerStream,
      maxRedemptionsPerUserPerStream,
      globalCooldown
    } = req.body;

    // Validate required fields
    if (!title || !cost || !action) {
      return res.status(400).json({ error: 'Title, cost, and action are required' });
    }

    // Validate action type
    const validActions = ['increment_deaths', 'increment_swears', 'decrement_deaths', 'decrement_swears'];
    if (!validActions.includes(action)) {
      return res.status(400).json({ error: 'Invalid action type' });
    }

    // Validate cost
    if (typeof cost !== 'number' || cost < 1 || cost > 1000000) {
      return res.status(400).json({ error: 'Cost must be between 1 and 1,000,000' });
    }

    const rewardData = {
      title,
      cost,
      action,
      prompt,
      backgroundColor,
      maxRedemptionsPerStream,
      maxRedemptionsPerUserPerStream,
      globalCooldown
    };

    const result = await streamMonitor.createCustomReward(req.user.twitchUserId, rewardData);

    if (result.success) {
      res.json({
        message: 'Channel point reward created successfully',
        reward: result.reward
      });
    } else {
      res.status(400).json({ error: result.error });
    }
  } catch (error) {
    console.error('Error creating channel point reward:', error);
    res.status(500).json({ error: 'Failed to create channel point reward' });
  }
});

/**
 * Delete a channel point reward
 * DELETE /api/rewards/:rewardId
 */
router.delete('/:rewardId', requireAuth, async (req, res) => {
  try {
    // Check if user has channel points feature enabled
    const hasChannelPoints = await database.hasFeature(req.user.twitchUserId, 'channelPoints');
    if (!hasChannelPoints) {
      return res.status(403).json({ error: 'Channel points feature not enabled' });
    }

    const { rewardId } = req.params;

    // Verify the reward belongs to the current user
    const reward = await database.getChannelPointReward(req.user.twitchUserId, rewardId);
    if (!reward) {
      return res.status(404).json({ error: 'Reward not found' });
    }

    const result = await streamMonitor.deleteCustomReward(req.user.twitchUserId, rewardId);

    if (result.success) {
      res.json({
        message: 'Channel point reward deleted successfully'
      });
    } else {
      res.status(400).json({ error: result.error });
    }
  } catch (error) {
    console.error('Error deleting channel point reward:', error);
    res.status(500).json({ error: 'Failed to delete channel point reward' });
  }
});

/**
 * Get reward redemption history (if analytics enabled)
 * GET /api/rewards/history
 */
router.get('/history', requireAuth, async (req, res) => {
  try {
    // Check if user has both channel points and analytics features enabled
    const hasChannelPoints = await database.hasFeature(req.user.twitchUserId, 'channelPoints');
    const hasAnalytics = await database.hasFeature(req.user.twitchUserId, 'analytics');

    if (!hasChannelPoints) {
      return res.status(403).json({ error: 'Channel points feature not enabled' });
    }

    if (!hasAnalytics) {
      return res.status(403).json({ error: 'Analytics feature required for redemption history' });
    }

    // TODO: Implement redemption history tracking
    // For now, return empty array
    res.json({
      redemptions: [],
      message: 'Redemption history tracking not yet implemented'
    });
  } catch (error) {
    console.error('Error getting redemption history:', error);
    res.status(500).json({ error: 'Failed to get redemption history' });
  }
});

/**
 * Admin endpoint: Get all rewards across all users
 * GET /api/rewards/admin/all
 */
router.get('/admin/all', requireAuth, requireAdmin, async (req, res) => {
  try {
    const users = await database.getAllUsers();
    const allRewards = [];

    for (const user of users) {
      // Skip users without valid twitchUserId
      if (!user.twitchUserId && !user.partitionKey) {
        continue;
      }

      const userId = user.twitchUserId || user.partitionKey;
      const hasChannelPoints = await database.hasFeature(userId, 'channelPoints');
      if (hasChannelPoints) {
        const rewards = await database.getUserChannelPointRewards(userId);
        allRewards.push({
          userId: userId,
          username: user.username,
          displayName: user.displayName,
          rewards: rewards
        });
      }
    }

    res.json({
      users: allRewards,
      totalUsers: allRewards.length,
      totalRewards: allRewards.reduce((sum, user) => sum + user.rewards.length, 0)
    });
  } catch (error) {
    console.error('Error getting all rewards (admin):', error);
    res.status(500).json({ error: 'Failed to get all rewards' });
  }
});

module.exports = router;

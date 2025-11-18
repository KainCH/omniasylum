const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

const router = express.Router();

/**
 * Get user's custom counters configuration
 * GET /api/custom-counters
 */
router.get('/', requireAuth, async (req, res) => {
  try {
    const customCounters = await database.getUserCustomCounters(req.user.userId);
    res.json(customCounters);
  } catch (error) {
    console.error('❌ Error getting custom counters:', error);
    res.status(500).json({ error: 'Failed to get custom counters' });
  }
});

/**
 * Save user's custom counters configuration
 * PUT /api/custom-counters
 */
router.put('/', requireAuth, async (req, res) => {
  try {
    const { counters } = req.body;

    if (!counters || typeof counters !== 'object') {
      return res.status(400).json({ error: 'Counters configuration is required' });
    }

    // Validate counter structure
    for (const [counterId, counter] of Object.entries(counters)) {
      if (!counter.name || !counter.icon) {
        return res.status(400).json({
          error: `Counter ${counterId} missing required fields (name, icon)`
        });
      }
    }

    await database.saveUserCustomCounters(req.user.userId, { counters });

    // Emit update to connected clients
    req.app.get('io').to(`user:${req.user.userId}`).emit('customCountersUpdated', {
      counters
    });

    res.json({
      success: true,
      counters
    });
  } catch (error) {
    console.error('❌ Error saving custom counters:', error);
    res.status(500).json({ error: 'Failed to save custom counters' });
  }
});

/**
 * Increment a custom counter
 * POST /api/custom-counters/:counterId/increment
 */
router.post('/:counterId/increment', requireAuth, async (req, res) => {
  try {
    const { counterId } = req.params;

    // Get current counter data
    const counters = await database.getCounters(req.user.userId);
    const customCounters = await database.getUserCustomCounters(req.user.userId);

    // Validate counter exists
    if (!customCounters.counters || !customCounters.counters[counterId]) {
      return res.status(404).json({ error: 'Custom counter not found' });
    }

    const counterConfig = customCounters.counters[counterId];

    // Initialize counter if it doesn't exist
    if (!counters[counterId]) {
      counters[counterId] = 0;
    }

    // Get previous value for milestone checking
    const previousValue = counters[counterId];

    // Increment counter
    counters[counterId] += counterConfig.incrementBy || 1;

    // Save updated counters
    await database.saveCounters(req.user.userId, counters);

    // Check for milestones if configured
    if (counterConfig.milestones && Array.isArray(counterConfig.milestones)) {
      const newValue = counters[counterId];
      const crossedMilestones = counterConfig.milestones.filter(threshold =>
        previousValue < threshold && newValue >= threshold
      );

      for (const milestone of crossedMilestones) {
        console.log(`🎯 Custom counter milestone: ${counterId} reached ${milestone}`);

        // Emit milestone event
        req.app.get('io').to(`user:${req.user.userId}`).emit('customMilestoneReached', {
          counterId,
          counterName: counterConfig.name,
          milestone,
          newValue,
          icon: counterConfig.icon
        });
      }
    }

    // Emit counter update
    req.app.get('io').to(`user:${req.user.userId}`).emit('customCounterUpdate', {
      counterId,
      value: counters[counterId],
      change: counterConfig.incrementBy || 1
    });

    res.json({
      counterId,
      value: counters[counterId],
      change: counterConfig.incrementBy || 1
    });
  } catch (error) {
    console.error(`❌ Error incrementing custom counter ${req.params.counterId}:`, error);
    res.status(500).json({ error: 'Failed to increment counter' });
  }
});

/**
 * Decrement a custom counter
 * POST /api/custom-counters/:counterId/decrement
 */
router.post('/:counterId/decrement', requireAuth, async (req, res) => {
  try {
    const { counterId } = req.params;

    // Get current counter data
    const counters = await database.getCounters(req.user.userId);
    const customCounters = await database.getUserCustomCounters(req.user.userId);

    // Validate counter exists
    if (!customCounters.counters || !customCounters.counters[counterId]) {
      return res.status(404).json({ error: 'Custom counter not found' });
    }

    const counterConfig = customCounters.counters[counterId];

    // Initialize counter if it doesn't exist
    if (!counters[counterId]) {
      counters[counterId] = 0;
    }

    // Decrement counter (don't allow negative values)
    const decrementBy = counterConfig.decrementBy || counterConfig.incrementBy || 1;
    const change = counters[counterId] > 0 ? -decrementBy : 0;
    counters[counterId] = Math.max(0, counters[counterId] - decrementBy);

    // Save updated counters
    await database.saveCounters(req.user.userId, counters);

    // Emit counter update
    req.app.get('io').to(`user:${req.user.userId}`).emit('customCounterUpdate', {
      counterId,
      value: counters[counterId],
      change
    });

    res.json({
      counterId,
      value: counters[counterId],
      change
    });
  } catch (error) {
    console.error(`❌ Error decrementing custom counter ${req.params.counterId}:`, error);
    res.status(500).json({ error: 'Failed to decrement counter' });
  }
});

/**
 * Reset a custom counter
 * POST /api/custom-counters/:counterId/reset
 */
router.post('/:counterId/reset', requireAuth, async (req, res) => {
  try {
    const { counterId } = req.params;

    // Get current counter data
    const counters = await database.getCounters(req.user.userId);
    const customCounters = await database.getUserCustomCounters(req.user.userId);

    // Validate counter exists
    if (!customCounters.counters || !customCounters.counters[counterId]) {
      return res.status(404).json({ error: 'Custom counter not found' });
    }

    const previousValue = counters[counterId] || 0;
    counters[counterId] = 0;

    // Save updated counters
    await database.saveCounters(req.user.userId, counters);

    // Emit counter update
    req.app.get('io').to(`user:${req.user.userId}`).emit('customCounterUpdate', {
      counterId,
      value: 0,
      change: -previousValue
    });

    res.json({
      counterId,
      value: 0,
      change: -previousValue
    });
  } catch (error) {
    console.error(`❌ Error resetting custom counter ${req.params.counterId}:`, error);
    res.status(500).json({ error: 'Failed to reset counter' });
  }
});

module.exports = router;

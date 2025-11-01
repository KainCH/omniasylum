const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

const router = express.Router();

/**
 * Get current counter state for authenticated user
 * GET /api/counters
 */
router.get('/', requireAuth, async (req, res) => {
  try {
    const data = await database.getCounters(req.user.userId);
    res.json(data);
  } catch (error) {
    console.error('Error fetching counters:', error);
    res.status(500).json({ error: 'Failed to fetch counters' });
  }
});

/**
 * Increment death counter
 * POST /api/counters/deaths/increment
 */
router.post('/deaths/increment', requireAuth, async (req, res) => {
  try {
    const data = await database.incrementDeaths(req.user.userId);
    
    // Emit WebSocket event to user's room
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);
    
    res.json(data);
  } catch (error) {
    console.error('Error incrementing deaths:', error);
    res.status(500).json({ error: 'Failed to increment deaths' });
  }
});

/**
 * Decrement death counter
 * POST /api/counters/deaths/decrement
 */
router.post('/deaths/decrement', requireAuth, async (req, res) => {
  try {
    const data = await database.decrementDeaths(req.user.userId);
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error decrementing deaths:', error);
    res.status(500).json({ error: 'Failed to decrement deaths' });
  }
});

/**
 * Increment swear counter
 * POST /api/counters/swears/increment
 */
router.post('/swears/increment', requireAuth, async (req, res) => {
  try {
    const data = await database.incrementSwears(req.user.userId);
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error incrementing swears:', error);
    res.status(500).json({ error: 'Failed to increment swears' });
  }
});

/**
 * Decrement swear counter
 * POST /api/counters/swears/decrement
 */
router.post('/swears/decrement', requireAuth, async (req, res) => {
  try {
    const data = await database.decrementSwears(req.user.userId);
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error decrementing swears:', error);
    res.status(500).json({ error: 'Failed to decrement swears' });
  }
});

/**
 * Reset all counters
 * POST /api/counters/reset
 */
router.post('/reset', requireAuth, async (req, res) => {
  try {
    const data = await database.resetCounters(req.user.userId);
    req.app.get('io').to(`user:${req.user.userId}`).emit('counterUpdate', data);
    res.json(data);
  } catch (error) {
    console.error('Error resetting counters:', error);
    res.status(500).json({ error: 'Failed to reset counters' });
  }
});

/**
 * Export counter data
 * GET /api/counters/export
 */
router.get('/export', requireAuth, async (req, res) => {
  try {
    const data = await database.getCounters(req.user.userId);
    const exportData = {
      ...data,
      username: req.user.username,
      exportedAt: new Date().toISOString()
    };
    res.json(exportData);
  } catch (error) {
    console.error('Error exporting data:', error);
    res.status(500).json({ error: 'Failed to export data' });
  }
});

module.exports = router;

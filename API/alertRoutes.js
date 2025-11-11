const express = require('express');
const database = require('./database');
const { requireAuth, requireAdmin } = require('./authMiddleware');

const router = express.Router();

// Debug route to verify alertRoutes are loaded
router.get('/debug/status', (req, res) => {
  res.json({ message: 'alertRoutes loaded successfully', timestamp: new Date().toISOString() });
});

// Test PUT route to debug routing issue
router.put('/debug/test-put', requireAuth, (req, res) => {
  res.json({ message: 'PUT route works', userId: req.user.userId, timestamp: new Date().toISOString() });
});

console.log('🔧 AlertRoutes: Loading alert routes...');
console.log('🔧 AlertRoutes: PUT /event-mappings route being registered');
console.log('🔧 AlertRoutes: Test PUT /debug/test-put route being registered');

/**
 * Get all alerts for current user (includes defaults + custom)
 * GET /api/alerts
 */
router.get('/', requireAuth, async (req, res) => {
  try {
    // Check if user has alerts feature enabled
    const hasAlerts = await database.hasFeature(req.user.userId, 'streamAlerts');
    if (!hasAlerts) {
      return res.status(403).json({ error: 'Stream alerts feature not enabled' });
    }

    // Initialize default alerts if user has none
    await database.initializeUserAlerts(req.user.userId);

    // Get user's custom alerts
    const customAlerts = await database.getUserAlerts(req.user.userId);

    // Get default alert templates
    const defaultTemplates = database.getDefaultAlertTemplates();

    // Combine custom alerts and default templates
    const allAlerts = [
      ...defaultTemplates.map(template => ({
        ...template,
        isDefault: true,
        enabled: true, // Default templates are always enabled
        userId: req.user.userId
      })),
      ...customAlerts.filter(alert => !alert.isDefault) // Only include custom alerts
    ];

    res.json({
      alerts: allAlerts,
      customAlerts: customAlerts,
      defaultTemplates: defaultTemplates,
      count: allAlerts.length
    });
  } catch (error) {
    console.error('Error getting user alerts:', error);
    res.status(500).json({ error: 'Failed to get alerts' });
  }
});

/**
 * Create a new custom alert
 * POST /api/alerts
 */
router.post('/', requireAuth, async (req, res) => {
  try {
    // Check if user has alerts feature enabled
    const hasAlerts = await database.hasFeature(req.user.userId, 'streamAlerts');
    if (!hasAlerts) {
      return res.status(403).json({ error: 'Stream alerts feature not enabled' });
    }

    const {
      type,
      name,
      visualCue,
      sound,
      soundDescription,
      textPrompt,
      duration,
      backgroundColor,
      textColor,
      borderColor
    } = req.body;

    // Validate required fields
    if (!type || !name || !textPrompt) {
      return res.status(400).json({ error: 'Type, name, and text prompt are required' });
    }

    // Validate alert type
    const validTypes = ['follow', 'subscription', 'resub', 'bits', 'raid', 'giftsub', 'hypetrain', 'custom'];
    if (!validTypes.includes(type)) {
      return res.status(400).json({ error: 'Invalid alert type' });
    }

    // Validate duration
    if (duration && (typeof duration !== 'number' || duration < 1000 || duration > 30000)) {
      return res.status(400).json({ error: 'Duration must be between 1000ms and 30000ms' });
    }

    const alertConfig = {
      userId: req.user.userId,
      type,
      name: name.trim(),
      visualCue: visualCue?.trim() || '',
      sound: sound?.trim() || '',
      soundDescription: soundDescription?.trim() || '',
      textPrompt: textPrompt.trim(),
      duration: duration || 4000,
      backgroundColor: backgroundColor || '#1a0d0d',
      textColor: textColor || '#ffffff',
      borderColor: borderColor || '#666666',
      isEnabled: true,
      isDefault: false
    };

    const alertId = await database.saveAlert(alertConfig);

    res.json({
      message: 'Alert created successfully',
      alertId: alertId,
      alert: { ...alertConfig, id: alertId }
    });
  } catch (error) {
    console.error('Error creating alert:', error);
    res.status(500).json({ error: 'Failed to create alert' });
  }
});

/**
 * Update event-to-alert mappings for current user
 * PUT /api/alerts/event-mappings
 * NOTE: This MUST come before /:alertId route to avoid conflicts!
 */
router.put('/event-mappings', requireAuth, async (req, res) => {
  try {
    // Check if user has alerts feature enabled
    const hasAlerts = await database.hasFeature(req.user.userId, 'streamAlerts');
    if (!hasAlerts) {
      return res.status(403).json({ error: 'Stream alerts feature not enabled' });
    }

    console.log('🔍 Event mappings update request for user:', req.user.userId);

    // Handle both formats: { mappings: {...} } and direct mappings object
    let mappings;
    if (req.body.mappings) {
      mappings = req.body.mappings;
    } else {
      mappings = req.body;
    }

    if (!mappings || typeof mappings !== 'object' || Array.isArray(mappings)) {
      return res.status(400).json({
        error: 'Invalid mappings format',
        received: mappings,
        type: typeof mappings
      });
    }

    // Validate that all event types are valid
    const validEvents = database.getAllAvailableEvents();

    for (const eventType of Object.keys(mappings)) {
      if (!validEvents.includes(eventType)) {
        console.log(`❌ Invalid event type found: ${eventType}`);
        console.log(`❌ Valid events are: ${validEvents.join(', ')}`);
        return res.status(400).json({ error: `Invalid event type: ${eventType}` });
      }
    }

    // Validate that all alert types exist for this user (including defaults and "none")
    const userAlerts = await database.getUserAlerts(req.user.userId);
    const defaultTemplates = database.getDefaultAlertTemplates();

    const validAlertTypes = [
      'none',
      ...defaultTemplates.map(template => template.type),
      ...userAlerts.filter(alert => !alert.isDefault).map(alert => alert.type)
    ];

    for (const alertType of Object.values(mappings)) {
      if (alertType && !validAlertTypes.includes(alertType)) {
        return res.status(400).json({
          error: `No alert found with type: ${alertType}. Use "none" to disable alerts for an event.`,
          availableTypes: validAlertTypes,
          disableOption: 'none'
        });
      }
    }

    await database.saveEventMappings(req.user.userId, mappings);

    res.json({
      message: 'Event mappings updated successfully',
      mappings: mappings
    });
  } catch (error) {
    console.error('Error updating event mappings:', error);
    res.status(500).json({ error: 'Failed to update event mappings' });
  }
});

/**
 * Update an existing alert
 * PUT /api/alerts/:alertId
 */
router.put('/:alertId', requireAuth, async (req, res) => {
  try {
    const { alertId } = req.params;
    const {
      name,
      visualCue,
      sound,
      soundDescription,
      textPrompt,
      duration,
      backgroundColor,
      textColor,
      borderColor,
      isEnabled
    } = req.body;

    // Check if user has alerts feature enabled
    const hasAlerts = await database.hasFeature(req.user.userId, 'streamAlerts');
    if (!hasAlerts) {
      return res.status(403).json({ error: 'Stream alerts feature not enabled' });
    }

    // Get existing alert to verify ownership
    const existingAlerts = await database.getUserAlerts(req.user.userId);
    const existingAlert = existingAlerts.find(alert => alert.id === alertId);

    if (!existingAlert) {
      return res.status(404).json({ error: 'Alert not found' });
    }

    // Prevent editing default alerts type and core properties
    if (existingAlert.isDefault) {
      return res.status(400).json({ error: 'Cannot modify default alert templates. Create a custom alert instead.' });
    }

    // Validate duration if provided
    if (duration && (typeof duration !== 'number' || duration < 1000 || duration > 30000)) {
      return res.status(400).json({ error: 'Duration must be between 1000ms and 30000ms' });
    }

    const updatedAlert = {
      ...existingAlert,
      name: name?.trim() || existingAlert.name,
      visualCue: visualCue?.trim() || existingAlert.visualCue,
      sound: sound?.trim() || existingAlert.sound,
      soundDescription: soundDescription?.trim() || existingAlert.soundDescription,
      textPrompt: textPrompt?.trim() || existingAlert.textPrompt,
      duration: duration || existingAlert.duration,
      backgroundColor: backgroundColor || existingAlert.backgroundColor,
      textColor: textColor || existingAlert.textColor,
      borderColor: borderColor || existingAlert.borderColor,
      isEnabled: isEnabled !== undefined ? isEnabled : existingAlert.isEnabled
    };

    await database.saveAlert(updatedAlert);

    res.json({
      message: 'Alert updated successfully',
      alert: updatedAlert
    });
  } catch (error) {
    console.error('Error updating alert:', error);
    res.status(500).json({ error: 'Failed to update alert' });
  }
});

/**
 * Delete a custom alert
 * DELETE /api/alerts/:alertId
 */
router.delete('/:alertId', requireAuth, async (req, res) => {
  try {
    const { alertId } = req.params;

    // Check if user has alerts feature enabled
    const hasAlerts = await database.hasFeature(req.user.userId, 'streamAlerts');
    if (!hasAlerts) {
      return res.status(403).json({ error: 'Stream alerts feature not enabled' });
    }

    // Get existing alert to verify ownership
    const existingAlerts = await database.getUserAlerts(req.user.userId);
    const existingAlert = existingAlerts.find(alert => alert.id === alertId);

    if (!existingAlert) {
      return res.status(404).json({ error: 'Alert not found' });
    }

    // Prevent deleting default alerts
    if (existingAlert.isDefault) {
      return res.status(400).json({ error: 'Cannot delete default alert templates. Disable them instead.' });
    }

    await database.deleteAlert(req.user.userId, alertId);

    res.json({
      message: 'Alert deleted successfully',
      alertId: alertId
    });
  } catch (error) {
    console.error('Error deleting alert:', error);
    res.status(500).json({ error: 'Failed to delete alert' });
  }
});

/**
 * Reset user alerts to defaults
 * POST /api/alerts/reset
 */
router.post('/reset', requireAuth, async (req, res) => {
  try {
    // Check if user has alerts feature enabled
    const hasAlerts = await database.hasFeature(req.user.userId, 'streamAlerts');
    if (!hasAlerts) {
      return res.status(403).json({ error: 'Stream alerts feature not enabled' });
    }

    // Get all user alerts and delete non-default ones
    const existingAlerts = await database.getUserAlerts(req.user.userId);

    for (const alert of existingAlerts) {
      if (!alert.isDefault) {
        await database.deleteAlert(req.user.userId, alert.id);
      }
    }

    // Re-initialize default alerts
    await database.initializeUserAlerts(req.user.userId);

    const alerts = await database.getUserAlerts(req.user.userId);

    res.json({
      message: 'Alerts reset to defaults successfully',
      alerts: alerts,
      count: alerts.length
    });
  } catch (error) {
    console.error('Error resetting alerts:', error);
    res.status(500).json({ error: 'Failed to reset alerts' });
  }
});

/**
 * Admin endpoint: Get all alerts across all users
 * GET /api/alerts/admin/all
 */
router.get('/admin/all', requireAuth, requireAdmin, async (req, res) => {
  try {
    const users = await database.getAllUsers();
    const allAlerts = [];

    for (const user of users) {
      // Skip users without valid twitchUserId
      if (!user.twitchUserId && !user.partitionKey) {
        continue;
      }

      const userId = user.twitchUserId || user.partitionKey;
      const hasAlerts = await database.hasFeature(userId, 'streamAlerts');
      if (hasAlerts) {
        const alerts = await database.getUserAlerts(userId);
        allAlerts.push({
          userId: userId,
          username: user.username,
          displayName: user.displayName,
          alerts: alerts
        });
      }
    }

    res.json({
      users: allAlerts,
      totalUsers: allAlerts.length,
      totalAlerts: allAlerts.reduce((sum, user) => sum + user.alerts.length, 0)
    });
  } catch (error) {
    console.error('Error getting all alerts (admin):', error);
    res.status(500).json({ error: 'Failed to get alerts data' });
  }
});

/**
 * Get alerts for a specific user (admin endpoint)
 * GET /api/alerts/user/:userId
 */
router.get('/user/:userId', requireAuth, async (req, res) => {
  try {
    const targetUserId = req.params.userId;

    // Admin can access any user's alerts, regular users only their own
    if (req.user.role !== 'admin' && req.user.userId !== targetUserId) {
      return res.status(403).json({ error: 'Access denied' });
    }

    // Check if target user exists
    const targetUser = await database.getUser(targetUserId);
    if (!targetUser) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Check if user has alerts feature enabled
    const hasAlerts = await database.hasFeature(targetUserId, 'streamAlerts');
    if (!hasAlerts) {
      return res.status(403).json({ error: 'Stream alerts feature not enabled for this user' });
    }

    // Initialize default alerts if user has none
    await database.initializeUserAlerts(targetUserId);

    // Get user's custom alerts
    const customAlerts = await database.getUserAlerts(targetUserId);

    // Get default alert templates
    const defaultTemplates = database.getDefaultAlertTemplates();

    // Combine custom alerts and default templates
    const allAlerts = [
      ...defaultTemplates.map(template => ({
        ...template,
        isDefault: true,
        enabled: true, // Default templates are always enabled
        userId: targetUserId
      })),
      ...customAlerts.filter(alert => !alert.isDefault) // Only include custom alerts
    ];

    res.json({
      alerts: allAlerts,
      customAlerts: customAlerts,
      defaultTemplates: defaultTemplates,
      count: allAlerts.length
    });
  } catch (error) {
    console.error('Error getting user alerts:', error);
    res.status(500).json({ error: 'Failed to get alerts' });
  }
});

/**
 * Get alert templates (public endpoint for creating new alerts)
 * GET /api/alerts/templates
 */
router.get('/templates', requireAuth, async (req, res) => {
  try {
    const templates = database.getDefaultAlertTemplates();

    res.json({
      templates: templates,
      count: templates.length
    });
  } catch (error) {
    console.error('Error getting alert templates:', error);
    res.status(500).json({ error: 'Failed to get alert templates' });
  }
});

// ==================== EVENT-TO-ALERT MAPPING ENDPOINTS ====================

/**
 * Get event-to-alert mappings for current user
 * GET /api/alerts/event-mappings
 */
router.get('/event-mappings', requireAuth, async (req, res) => {
  try {
    // Check if user has alerts feature enabled
    const hasAlerts = await database.hasFeature(req.user.userId, 'streamAlerts');
    if (!hasAlerts) {
      return res.status(403).json({ error: 'Stream alerts feature not enabled' });
    }

    const mappings = await database.getEventMappings(req.user.userId);
    const defaultMappings = database.getDefaultEventMappings();
    const availableEvents = database.getAllAvailableEvents();
    const availableAlertTypes = database.getAvailableAlertTypes();

    res.json({
      mappings: mappings,
      defaultMappings: defaultMappings,
      availableEvents: availableEvents,
      availableAlertTypes: availableAlertTypes,
      disableOption: 'none' // Indicates which value disables alerts
    });
  } catch (error) {
    console.error('Error getting event mappings:', error);
    res.status(500).json({ error: 'Failed to get event mappings' });
  }
});




/**
 * Reset event mappings to defaults
 * POST /api/alerts/event-mappings/reset
 */
router.post('/event-mappings/reset', requireAuth, async (req, res) => {
  try {
    // Check if user has alerts feature enabled
    const hasAlerts = await database.hasFeature(req.user.userId, 'streamAlerts');
    if (!hasAlerts) {
      return res.status(403).json({ error: 'Stream alerts feature not enabled' });
    }

    const defaultMappings = database.getDefaultEventMappings();
    await database.saveEventMappings(req.user.userId, defaultMappings);

    res.json({
      message: 'Event mappings reset to defaults successfully',
      mappings: defaultMappings
    });
  } catch (error) {
    console.error('Error resetting event mappings:', error);
    res.status(500).json({ error: 'Failed to reset event mappings' });
  }
});

/**
 * Debug endpoint - Get validation info for troubleshooting
 * GET /api/alerts/debug/validation
 */
router.get('/debug/validation', requireAuth, async (req, res) => {
  try {
    const userAlerts = await database.getUserAlerts(req.user.userId);
    const defaultTemplates = database.getDefaultAlertTemplates();
    const defaultMappings = database.getDefaultEventMappings();
    const userMappings = await database.getEventMappings(req.user.userId);

    // Get all valid alert types (including "none" for disabling)
    const validAlertTypes = [
      'none',  // Special value to disable alerts
      ...defaultTemplates.map(template => template.type),
      ...userAlerts.filter(alert => !alert.isDefault).map(alert => alert.type)
    ];

    res.json({
      debug: {
        userId: req.user.userId,
        defaultAlertTypes: defaultTemplates.map(t => t.type),
        userCustomAlertTypes: userAlerts.filter(a => !a.isDefault).map(a => a.type),
        validAlertTypes: validAlertTypes,
        defaultEventTypes: Object.keys(defaultMappings),
        defaultMappings: defaultMappings,
        userMappings: userMappings,
        mappingValidation: Object.entries(userMappings || defaultMappings).map(([event, alertType]) => ({
          event,
          alertType,
          isValid: validAlertTypes.includes(alertType)
        }))
      }
    });
  } catch (error) {
    console.error('Error in debug endpoint:', error);
    res.status(500).json({ error: 'Debug endpoint failed', details: error.message });
  }
});

module.exports = router;

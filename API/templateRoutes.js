const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

const router = express.Router();

/**
 * Get available template options
 * GET /api/templates/available
 */
router.get('/available', requireAuth, async (req, res) => {
  try {
    const templates = database.getAvailableTemplates();
    res.json({
      templates: Object.entries(templates).map(([id, template]) => ({
        id,
        ...template
      }))
    });
  } catch (error) {
    console.error('❌ Error getting available templates:', error);
    res.status(500).json({ error: 'Failed to get templates' });
  }
});

/**
 * Get user's current template
 * GET /api/templates/current
 */
router.get('/current', requireAuth, async (req, res) => {
  try {
    const template = await database.getUserTemplate(req.user.userId);
    res.json(template);
  } catch (error) {
    console.error('❌ Error getting user template:', error);
    res.status(500).json({ error: 'Failed to get user template' });
  }
});

/**
 * Update user's template selection
 * PUT /api/templates/select
 */
router.put('/select', requireAuth, async (req, res) => {
  try {
    const { templateStyle } = req.body;

    if (!templateStyle) {
      return res.status(400).json({ error: 'Template style is required' });
    }

    // Validate template exists
    const availableTemplates = database.getAvailableTemplates();
    if (templateStyle !== 'custom' && !availableTemplates[templateStyle]) {
      return res.status(400).json({ error: 'Invalid template style' });
    }

    await database.updateUserTemplateStyle(req.user.userId, templateStyle);

    // Get the updated template
    const updatedTemplate = await database.getUserTemplate(req.user.userId);

    // Emit template change to user's connected clients
    req.app.get('io').to(`user:${req.user.userId}`).emit('templateChanged', {
      templateStyle,
      template: updatedTemplate
    });

    res.json({
      success: true,
      templateStyle,
      template: updatedTemplate
    });
  } catch (error) {
    console.error('❌ Error updating template:', error);
    res.status(500).json({ error: 'Failed to update template' });
  }
});

/**
 * Get user's custom template
 * GET /api/templates/custom
 */
router.get('/custom', requireAuth, async (req, res) => {
  try {
    const customTemplate = await database.getUserCustomTemplate(req.user.userId);
    res.json(customTemplate);
  } catch (error) {
    console.error('❌ Error getting custom template:', error);
    res.status(500).json({ error: 'Failed to get custom template' });
  }
});

/**
 * Save user's custom template
 * PUT /api/templates/custom
 */
router.put('/custom', requireAuth, async (req, res) => {
  try {
    const { name, config } = req.body;

    if (!name || !config) {
      return res.status(400).json({ error: 'Template name and config are required' });
    }

    // Validate config structure
    if (!config.colors || !config.fonts || !config.animations) {
      return res.status(400).json({ error: 'Invalid template config structure' });
    }

    const templateData = {
      name,
      config,
      type: 'custom',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString()
    };

    await database.saveUserCustomTemplate(req.user.userId, templateData);

    // Also update user's template style to 'custom' if requested
    if (req.body.makeActive) {
      await database.updateUserTemplateStyle(req.user.userId, 'custom');
    }

    res.json({
      success: true,
      template: templateData
    });
  } catch (error) {
    console.error('❌ Error saving custom template:', error);
    res.status(500).json({ error: 'Failed to save custom template' });
  }
});

module.exports = router;

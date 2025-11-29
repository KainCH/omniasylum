const express = require('express');
const database = require('./database');
const { requireAuth } = require('./authMiddleware');

const router = express.Router();

/**
 * Get user's chat commands
 * GET /api/chat-commands
 */
router.get('/', requireAuth, async (req, res) => {
  try {
    const commands = await database.getUserChatCommands(req.user.userId);
    res.json(commands);
  } catch (error) {
    console.error('❌ Error getting chat commands:', error);
    res.status(500).json({ error: 'Failed to get chat commands' });
  }
});

/**
 * Get default chat commands template
 * GET /api/chat-commands/defaults
 */
router.get('/defaults', requireAuth, async (req, res) => {
  try {
    const defaultCommands = database.getDefaultChatCommands();
    res.json(defaultCommands);
  } catch (error) {
    console.error('❌ Error getting default chat commands:', error);
    res.status(500).json({ error: 'Failed to get default commands' });
  }
});

/**
 * Save user's chat commands
 * PUT /api/chat-commands
 */
router.put('/', requireAuth, async (req, res) => {
  try {
    const { commands } = req.body;

    if (!commands || typeof commands !== 'object') {
      return res.status(400).json({ error: 'Commands configuration is required' });
    }

    // Validate command structure
    for (const [command, config] of Object.entries(commands)) {
      if (!command.startsWith('!')) {
        return res.status(400).json({
          error: `Command ${command} must start with !`
        });
      }

      if (!config.permission || !['everyone', 'subscriber', 'moderator', 'broadcaster'].includes(config.permission)) {
        return res.status(400).json({
          error: `Command ${command} has invalid permission level`
        });
      }

      if (config.cooldown && (typeof config.cooldown !== 'number' || config.cooldown < 0)) {
        return res.status(400).json({
          error: `Command ${command} has invalid cooldown`
        });
      }
    }

    await database.saveUserChatCommands(req.user.userId, commands);

    // Emit update to Twitch bot to reload commands
    req.app.get('io').to(`user:${req.user.userId}`).emit('chatCommandsUpdated', {
      commands
    });

    res.json({
      success: true,
      commands
    });
  } catch (error) {
    console.error('❌ Error saving chat commands:', error);
    res.status(500).json({ error: 'Failed to save chat commands' });
  }
});

/**
 * Add a new custom chat command
 * POST /api/chat-commands
 */
router.post('/', requireAuth, async (req, res) => {
  try {
    const { command, config } = req.body;

    if (!command || !command.startsWith('!')) {
      return res.status(400).json({ error: 'Command must start with !' });
    }

    if (!config || !config.response) {
      return res.status(400).json({ error: 'Command config with response is required' });
    }

    // Get current commands
    const currentCommands = await database.getUserChatCommands(req.user.userId);

    // Check if command already exists
    if (currentCommands[command]) {
      return res.status(400).json({ error: 'Command already exists' });
    }

    // Add new command with defaults
    const newCommand = {
      response: config.response,
      permission: config.permission || 'everyone',
      cooldown: config.cooldown || 5,
      enabled: config.enabled !== false,
      custom: true,
      createdAt: new Date().toISOString()
    };

    currentCommands[command] = newCommand;

    await database.saveUserChatCommands(req.user.userId, currentCommands);

    // Emit update to Twitch bot
    req.app.get('io').to(`user:${req.user.userId}`).emit('chatCommandsUpdated', {
      commands: currentCommands
    });

    res.json({
      success: true,
      command,
      config: newCommand
    });
  } catch (error) {
    console.error('❌ Error adding chat command:', error);
    res.status(500).json({ error: 'Failed to add chat command' });
  }
});

/**
 * Update a specific chat command
 * PUT /api/chat-commands/:command
 */
router.put('/:command', requireAuth, async (req, res) => {
  try {
    const { command } = req.params;
    const { config } = req.body;

    if (!command.startsWith('!')) {
      return res.status(400).json({ error: 'Command must start with !' });
    }

    // Get current commands
    const currentCommands = await database.getUserChatCommands(req.user.userId);

    if (!currentCommands[command]) {
      return res.status(404).json({ error: 'Command not found' });
    }

    // Update command config
    currentCommands[command] = {
      ...currentCommands[command],
      ...config,
      updatedAt: new Date().toISOString()
    };

    await database.saveUserChatCommands(req.user.userId, currentCommands);

    // Emit update to Twitch bot
    req.app.get('io').to(`user:${req.user.userId}`).emit('chatCommandsUpdated', {
      commands: currentCommands
    });

    res.json({
      success: true,
      command,
      config: currentCommands[command]
    });
  } catch (error) {
    console.error('❌ Error updating chat command:', error);
    res.status(500).json({ error: 'Failed to update chat command' });
  }
});

/**
 * Delete a custom chat command
 * DELETE /api/chat-commands/:command
 */
router.delete('/:command', requireAuth, async (req, res) => {
  try {
    const { command } = req.params;

    if (!command.startsWith('!')) {
      return res.status(400).json({ error: 'Command must start with !' });
    }

    // Get current commands
    const currentCommands = await database.getUserChatCommands(req.user.userId);

    if (!currentCommands[command]) {
      return res.status(404).json({ error: 'Command not found' });
    }

    // Don't allow deletion of core commands
    if (!currentCommands[command].custom) {
      return res.status(400).json({ error: 'Cannot delete core commands' });
    }

    delete currentCommands[command];

    await database.saveUserChatCommands(req.user.userId, currentCommands);

    // Emit update to Twitch bot
    req.app.get('io').to(`user:${req.user.userId}`).emit('chatCommandsUpdated', {
      commands: currentCommands
    });

    res.json({
      success: true,
      command,
      deleted: true
    });
  } catch (error) {
    console.error('❌ Error deleting chat command:', error);
    res.status(500).json({ error: 'Failed to delete chat command' });
  }
});

/**
 * Test a chat command (simulate execution)
 * POST /api/chat-commands/:command/test
 */
router.post('/:command/test', requireAuth, async (req, res) => {
  try {
    const { command } = req.params;

    // Get current commands
    const currentCommands = await database.getUserChatCommands(req.user.userId);

    if (!currentCommands[command]) {
      return res.status(404).json({ error: 'Command not found' });
    }

    const commandConfig = currentCommands[command];

    if (!commandConfig.enabled) {
      return res.status(400).json({ error: 'Command is disabled' });
    }

    // Get current counters for template replacement
    const counters = await database.getCounters(req.user.userId);

    let response = commandConfig.response || 'Command executed';

    // Replace template variables
    response = response.replace(/\{\{(\w+)\}\}/g, (match, key) => {
      return counters[key] !== undefined ? counters[key] : match;
    });

    res.json({
      success: true,
      command,
      response,
      config: commandConfig,
      testMode: true
    });
  } catch (error) {
    console.error('❌ Error testing chat command:', error);
    res.status(500).json({ error: 'Failed to test chat command' });
  }
});

module.exports = router;

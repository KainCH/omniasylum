const { ApiClient } = require('@twurple/api');
const { RefreshingAuthProvider } = require('@twurple/auth');
const { ChatClient } = require('@twurple/chat');
const { EventEmitter } = require('events');
const database = require('./database');
const keyVault = require('./keyVault');

/**
 * Multi-tenant Twitch service
 * Manages multiple Twitch chat bots, one per authenticated streamer
 */
class MultiTenantTwitchService extends EventEmitter {
  constructor() {
    super();
    this.clients = new Map(); // userId -> { apiClient, chatClient }
    this.clientId = null;
    this.clientSecret = null;
  }

  /**
   * Initialize service with Twitch app credentials
   */
  async initialize() {
    try {
      this.clientId = await keyVault.getSecret('TWITCH-CLIENT-ID');
      this.clientSecret = await keyVault.getSecret('TWITCH-CLIENT-SECRET');

      if (!this.clientId || !this.clientSecret) {
        console.log('⚠️  Twitch app credentials not configured');
        return false;
      }

      console.log('✅ Twitch service initialized');
      return true;
    } catch (error) {
      console.error('❌ Failed to initialize Twitch service:', error);
      return false;
    }
  }

  /**
   * Connect a user's Twitch bot
   * Called after user authenticates via OAuth
   */
  async connectUser(userId) {
    try {
      // Get user from database
      const user = await database.getUser(userId);
      if (!user) {
        console.error(`User ${userId} not found`);
        return false;
      }

      // Check if already connected
      if (this.clients.has(userId)) {
        console.log(`User ${userId} already connected`);
        return true;
      }

      // Create refreshing auth provider
      const authProvider = new RefreshingAuthProvider(
        {
          clientId: this.clientId,
          clientSecret: this.clientSecret
        },
        {
          accessToken: user.accessToken,
          refreshToken: user.refreshToken,
          expiresIn: 0,
          obtainmentTimestamp: 0
        }
      );

      // Handle token refresh
      authProvider.onRefresh(async (userId, newTokenData) => {
        await database.saveUser({
          ...user,
          accessToken: newTokenData.accessToken,
          refreshToken: newTokenData.refreshToken,
          tokenExpiry: new Date(newTokenData.expiresIn * 1000 + Date.now()).toISOString()
        });
        console.log(`🔄 Token refreshed for user ${userId}`);
      });

      // Create API client
      const apiClient = new ApiClient({ authProvider });

      // Create chat client
      const chatClient = new ChatClient({
        authProvider,
        channels: [user.username]
      });

      // Handle chat messages
      chatClient.onMessage((channel, username, message, msg) => {
        this.handleChatMessage(userId, channel, username, message, msg);
      });

      // Connect to chat
      await chatClient.connect();

      // Store clients
      this.clients.set(userId, {
        apiClient,
        chatClient,
        username: user.username,
        displayName: user.displayName
      });

      console.log(`✅ Connected Twitch bot for ${user.displayName} (#${user.username})`);
      return true;

    } catch (error) {
      console.error(`❌ Failed to connect user ${userId}:`, error);
      return false;
    }
  }

  /**
   * Disconnect a user's Twitch bot
   */
  async disconnectUser(userId) {
    const client = this.clients.get(userId);
    if (client && client.chatClient) {
      await client.chatClient.quit();
      this.clients.delete(userId);
      console.log(`👋 Disconnected Twitch bot for user ${userId}`);
    }
  }

  /**
   * Check if user has permission (broadcaster or moderator)
   */
  hasPermission(msg) {
    return msg.userInfo.isBroadcaster || msg.userInfo.isMod;
  }

  /**
   * Handle incoming chat messages for a specific user's channel
   */
  handleChatMessage(userId, channel, username, message, msg) {
    const text = message.toLowerCase().trim();

    // Public commands (anyone can use)
    if (text === '!deaths' || text === '!swears' || text === '!stats') {
      this.emit('publicCommand', { userId, channel, username, command: text });
      return;
    }

    // Mod-only commands
    if (!this.hasPermission(msg)) {
      return; // Silently ignore unauthorized commands
    }

    // Increment/decrement commands
    if (text === '!death+' || text === '!d+') {
      this.emit('incrementDeaths', { userId, username });
    } else if (text === '!death-' || text === '!d-') {
      this.emit('decrementDeaths', { userId, username });
    } else if (text === '!swear+' || text === '!s+') {
      this.emit('incrementSwears', { userId, username });
    } else if (text === '!swear-' || text === '!s-') {
      this.emit('decrementSwears', { userId, username });
    } else if (text === '!resetcounters') {
      this.emit('resetCounters', { userId, username });
    }
  }

  /**
   * Send a message to a user's chat
   */
  async sendMessage(userId, message) {
    const client = this.clients.get(userId);
    if (client && client.chatClient) {
      try {
        await client.chatClient.say(client.username, message);
        return true;
      } catch (error) {
        console.error(`Error sending message for user ${userId}:`, error);
        return false;
      }
    }
    return false;
  }

  /**
   * Get stream info for a user
   */
  async getStreamInfo(userId) {
    const client = this.clients.get(userId);
    if (!client) return null;

    try {
      const user = await client.apiClient.users.getUserByName(client.username);
      if (!user) return null;

      const stream = await client.apiClient.streams.getStreamByUserId(user.id);
      
      return stream ? {
        isLive: true,
        title: stream.title,
        game: stream.gameName,
        viewers: stream.viewers,
        startedAt: stream.startDate
      } : { isLive: false };

    } catch (error) {
      console.error(`Error getting stream info for ${userId}:`, error);
      return null;
    }
  }

  /**
   * Create a clip for a user's stream
   */
  async createClip(userId) {
    const client = this.clients.get(userId);
    if (!client) return null;

    try {
      const user = await client.apiClient.users.getUserByName(client.username);
      if (!user) return null;

      const clipId = await client.apiClient.clips.createClip({
        channelId: user.id
      });

      console.log(`✅ Clip created for ${client.username}: ${clipId}`);
      return clipId;

    } catch (error) {
      console.error(`Error creating clip for ${userId}:`, error);
      return null;
    }
  }

  /**
   * Check if a user is connected
   */
  isUserConnected(userId) {
    return this.clients.has(userId);
  }

  /**
   * Get all connected users
   */
  getConnectedUsers() {
    return Array.from(this.clients.keys());
  }

  /**
   * Disconnect all users
   */
  async disconnectAll() {
    for (const userId of this.clients.keys()) {
      await this.disconnectUser(userId);
    }
  }
}

// Export singleton instance
module.exports = new MultiTenantTwitchService();

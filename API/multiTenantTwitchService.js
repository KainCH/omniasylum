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

      // Handle bits (cheers) - requires channel:read:subscriptions scope
      chatClient.onCheer((channel, username, message, msg) => {
        this.handleBitsEvent(userId, channel, username, message, msg);
      });

      // Handle subscriber events
      chatClient.onSub((channel, username, subInfo, msg) => {
        this.handleSubscriberEvent(userId, channel, username, subInfo, msg);
      });

      // Handle resub events
      chatClient.onResub((channel, username, months, message, subInfo, msg) => {
        this.handleResubEvent(userId, channel, username, months, message, subInfo, msg);
      });

      // Handle gifted subs
      chatClient.onSubGift((channel, username, recipient, subInfo, msg) => {
        this.handleGiftSubEvent(userId, channel, username, recipient, subInfo, msg);
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
    if (text === '!deaths' || text === '!swears' || text === '!stats' || text === '!bits' || text === '!streamstats') {
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
    } else if (text === '!startstream') {
      this.emit('startStream', { userId, username });
    } else if (text === '!endstream') {
      this.emit('endStream', { userId, username });
    } else if (text === '!resetbits') {
      this.emit('resetBits', { userId, username });
    }
  }

  /**
   * Handle bits (cheers) events
   */
  async handleBitsEvent(userId, channel, username, message, msg) {
    try {
      // Check if user has bits integration enabled
      const hasBitsFeature = await database.hasFeature(userId, 'bitsIntegration');
      if (!hasBitsFeature) {
        return;
      }

      const bitsAmount = msg.bits;
      if (!bitsAmount || bitsAmount <= 0) {
        return;
      }

      console.log(`💎 ${username} cheered ${bitsAmount} bits in ${channel}`);

      // Get stream settings for thresholds
      const streamSettings = await database.getStreamSettings(userId);
      const thresholds = streamSettings.bitThresholds;

      // Add to bits counter
      await database.addBits(userId, bitsAmount);

      // Emit bits event for overlay and tracking
      this.emit('bitsReceived', {
        userId,
        username,
        channel,
        amount: bitsAmount,
        message: message,
        timestamp: new Date(),
        thresholds: thresholds
      });

      // Check if bits meet counter thresholds (optional feature)
      const hasAutoIncrement = streamSettings.autoIncrementCounters || false;
      if (hasAutoIncrement) {
        if (bitsAmount >= thresholds.death) {
          this.emit('incrementDeaths', {
            userId,
            username: `${username} (${bitsAmount} bits)`,
            source: 'bits'
          });
        } else if (bitsAmount >= thresholds.swear) {
          this.emit('incrementSwears', {
            userId,
            username: `${username} (${bitsAmount} bits)`,
            source: 'bits'
          });
        }
      }

      // Send thank you message in chat (if enabled)
      const shouldRespond = bitsAmount >= thresholds.celebration;
      if (shouldRespond) {
        const responses = [
          `Thanks for the ${bitsAmount} bits, ${username}! 💎`,
          `${username} just dropped ${bitsAmount} bits! Much appreciated! ✨`,
          `Woah! ${bitsAmount} bits from ${username}! You're awesome! 🎉`
        ];

        const response = responses[Math.floor(Math.random() * responses.length)];
        await this.sendMessage(userId, response);
      }

    } catch (error) {
      console.error(`❌ Error handling bits event for user ${userId}:`, error);
    }
  }

  /**
   * Handle new subscriber events
   */
  async handleSubscriberEvent(userId, channel, username, subInfo, msg) {
    try {
      console.log(`🎉 New subscriber: ${username} in ${channel}`);

      // Emit subscriber event for celebration effects
      this.emit('newSubscriber', {
        userId,
        username,
        channel,
        tier: subInfo.plan,
        timestamp: new Date()
      });

      // Send congratulations message
      const responses = [
        `Welcome to the squad, ${username}! Thanks for subscribing! 🎉`,
        `${username} just joined the family! Welcome aboard! 🚀`,
        `New subscriber ${username}! You're awesome! 💜`
      ];

      const response = responses[Math.floor(Math.random() * responses.length)];
      await this.sendMessage(userId, response);

    } catch (error) {
      console.error(`❌ Error handling subscriber event for user ${userId}:`, error);
    }
  }

  /**
   * Handle resub events
   */
  async handleResubEvent(userId, channel, username, months, message, subInfo, msg) {
    try {
      console.log(`🎉 Resub: ${username} (${months} months) in ${channel}`);

      // Emit resub event
      this.emit('resub', {
        userId,
        username,
        channel,
        months: months,
        message: message,
        tier: subInfo.plan,
        timestamp: new Date()
      });

      // Send congratulations message
      const response = `${username} has been subscribed for ${months} months! Thanks for the continued support! 💜`;
      await this.sendMessage(userId, response);

    } catch (error) {
      console.error(`❌ Error handling resub event for user ${userId}:`, error);
    }
  }

  /**
   * Handle gifted sub events
   */
  async handleGiftSubEvent(userId, channel, username, recipient, subInfo, msg) {
    try {
      console.log(`🎁 Gift sub: ${username} gifted to ${recipient} in ${channel}`);

      // Emit gift sub event
      this.emit('giftSub', {
        userId,
        gifter: username,
        recipient: recipient,
        channel,
        tier: subInfo.plan,
        timestamp: new Date()
      });

      // Send thank you message
      const response = `${username} just gifted a sub to ${recipient}! What a legend! 🎁💜`;
      await this.sendMessage(userId, response);

    } catch (error) {
      console.error(`❌ Error handling gift sub event for user ${userId}:`, error);
    }
  }  /**
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

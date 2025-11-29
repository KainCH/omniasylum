const { ApiClient } = require('@twurple/api');
const { StaticAuthProvider } = require('@twurple/auth');
const { ChatClient } = require('@twurple/chat');
const { EventEmitter } = require('events');

class TwitchService extends EventEmitter {
  constructor() {
    super();
    this.apiClient = null;
    this.chatClient = null;
    this.connected = false;
    this.channelName = null;
  }

  /**
   * Initialize Twitch API and Chat clients
   */
  async initialize() {
    try {
      const clientId = process.env.TWITCH_CLIENT_ID;
      const clientSecret = process.env.TWITCH_CLIENT_SECRET;
      const channelName = process.env.TWITCH_CHANNEL_NAME;
      const oauthToken = process.env.TWITCH_OAUTH_TOKEN;

      if (!clientId || !clientSecret) {
        console.log('⚠️  Twitch credentials not configured');
        return false;
      }

      // Initialize API client
      const authProvider = new StaticAuthProvider(clientId, oauthToken);
      this.apiClient = new ApiClient({ authProvider });
      this.channelName = channelName;

      // Initialize Chat client if OAuth token is provided
      if (oauthToken && channelName) {
        await this.initializeChatBot(channelName, oauthToken);
      }

      this.connected = true;
      console.log('✅ Twitch API initialized successfully');
      return true;

    } catch (error) {
      console.error('❌ Failed to initialize Twitch service:', error);
      this.connected = false;
      return false;
    }
  }

  /**
   * Initialize Twitch chat bot
   */
  async initializeChatBot(channelName, oauthToken) {
    try {
      this.chatClient = new ChatClient({
        channels: [channelName]
      });

      // Handle chat messages
      this.chatClient.onMessage((channel, user, message, msg) => {
        this.handleChatMessage(channel, user, message, msg);
      });

      await this.chatClient.connect();
      console.log(`✅ Chat bot connected to #${channelName}`);

    } catch (error) {
      console.error('❌ Failed to initialize chat bot:', error);
    }
  }

  /**
   * Check if user has permission (broadcaster or moderator)
   */
  hasPermission(msg) {
    return msg.userInfo.isBroadcaster || msg.userInfo.isMod;
  }

  /**
   * Handle incoming chat messages for commands
   */
  handleChatMessage(channel, user, message, msg) {
    const text = message.toLowerCase().trim();

    // Example chat commands (can be expanded)
    if (text === '!deaths') {
      // Send current death count to chat
      // This would need dataStore access - implement in server.js
      console.log(`Chat command: !deaths from ${user}`);
    } else if (text === '!swears') {
      // Send current swear count to chat
      console.log(`Chat command: !swears from ${user}`);
    } else if (text === '!stats') {
      // Send overall stats to chat
      console.log(`Chat command: !stats from ${user}`);
    }
    // Mod-only commands for controlling counters
    else if (text.startsWith('!death+') || text === '!d+') {
      if (!this.hasPermission(msg)) {
        return; // Silently ignore unauthorized commands
      }
      console.log(`🔧 Mod command: increment deaths from ${user}`);
      // Emit event to increment deaths - handle in server.js
      this.emit('incrementDeaths', user);
    } else if (text.startsWith('!death-') || text === '!d-') {
      if (!this.hasPermission(msg)) {
        return;
      }
      console.log(`🔧 Mod command: decrement deaths from ${user}`);
      this.emit('decrementDeaths', user);
    } else if (text.startsWith('!swear+') || text === '!s+') {
      if (!this.hasPermission(msg)) {
        return;
      }
      console.log(`🔧 Mod command: increment swears from ${user}`);
      this.emit('incrementSwears', user);
    } else if (text.startsWith('!swear-') || text === '!s-') {
      if (!this.hasPermission(msg)) {
        return;
      }
      console.log(`🔧 Mod command: decrement swears from ${user}`);
      this.emit('decrementSwears', user);
    } else if (text === '!resetcounters') {
      if (!this.hasPermission(msg)) {
        return;
      }
      console.log(`🔧 Mod command: reset counters from ${user}`);
      this.emit('resetCounters', user);
    }
  }

  /**
   * Send a message to chat
   */
  async sendChatMessage(message) {
    if (this.chatClient && this.channelName) {
      try {
        await this.chatClient.say(this.channelName, message);
        return true;
      } catch (error) {
        console.error('Error sending chat message:', error);
        return false;
      }
    }
    return false;
  }

  /**
   * Get stream information
   */
  async getStreamInfo() {
    if (!this.apiClient || !this.channelName) {
      return null;
    }

    try {
      const user = await this.apiClient.users.getUserByName(this.channelName);
      if (!user) return null;

      const stream = await this.apiClient.streams.getStreamByUserId(user.id);
      return stream ? {
        isLive: true,
        title: stream.title,
        game: stream.gameName,
        viewers: stream.viewers,
        startedAt: stream.startDate
      } : { isLive: false };

    } catch (error) {
      console.error('Error fetching stream info:', error);
      return null;
    }
  }

  /**
   * Create a clip (requires appropriate OAuth scopes)
   */
  async createClip() {
    if (!this.apiClient || !this.channelName) {
      return null;
    }

    try {
      const user = await this.apiClient.users.getUserByName(this.channelName);
      if (!user) return null;

      const clip = await this.apiClient.clips.createClip({
        channelId: user.id
      });

      console.log(`✅ Clip created: ${clip}`);
      return clip;

    } catch (error) {
      console.error('Error creating clip:', error);
      return null;
    }
  }

  /**
   * Check if service is connected
   */
  isConnected() {
    return this.connected;
  }

  /**
   * Disconnect and cleanup
   */
  async disconnect() {
    if (this.chatClient) {
      await this.chatClient.quit();
      console.log('👋 Chat bot disconnected');
    }
    this.connected = false;
  }
}

// Export singleton instance
module.exports = new TwitchService();

const { EventSubWsListener } = require('@twurple/eventsub-ws');
const { ApiClient } = require('@twurple/api');
const { RefreshingAuthProvider } = require('@twurple/auth');
const { EventEmitter } = require('events');
const database = require('./database');
const keyVault = require('./keyVault');

/**
 * Stream Monitor Service
 * Monitors Twitch streams using EventSub WebSocket for real-time notifications
 * Automatically detects when users go live/offline and updates counters
 */
class StreamMonitor extends EventEmitter {
  constructor() {
    super();
    this.listener = null;
    this.connectedUsers = new Map(); // userId -> subscription objects
    this.clientId = null;
    this.clientSecret = null;
    this.appApiClient = null;
    this.appAuthProvider = null;
  }

  /**
   * Initialize the stream monitor with app credentials
   */
  async initialize() {
    try {
      this.clientId = await keyVault.getSecret('TWITCH-CLIENT-ID');
      this.clientSecret = await keyVault.getSecret('TWITCH-CLIENT-SECRET');

      if (!this.clientId || !this.clientSecret ||
          this.clientId === 'your_client_id_here' ||
          this.clientSecret === 'your_client_secret_here') {
        console.log('⚠️  Twitch app credentials not configured for stream monitoring');
        return false;
      }

      // Create app auth provider for EventSub
      this.appAuthProvider = new RefreshingAuthProvider(
        {
          clientId: this.clientId,
          clientSecret: this.clientSecret
        }
      );

      // Get app access token
      await this.appAuthProvider.getAppAccessToken();
      this.appApiClient = new ApiClient({ authProvider: this.appAuthProvider });

      // Create EventSub WebSocket listener
      this.listener = new EventSubWsListener({
        apiClient: this.appApiClient
      });

      // Set up event handlers
      this.setupEventHandlers();

      // Start listening
      await this.listener.start();

      console.log('✅ Stream Monitor initialized with EventSub WebSocket');
      return true;
    } catch (error) {
      console.error('❌ Failed to initialize Stream Monitor:', error);
      return false;
    }
  }

  /**
   * Set up EventSub event handlers
   */
  setupEventHandlers() {
    // Handle stream online events
    this.listener.onStreamOnline((event) => {
      this.handleStreamOnline(event);
    });

    // Handle stream offline events
    this.listener.onStreamOffline((event) => {
      this.handleStreamOffline(event);
    });

    // Handle channel point redemptions
    this.listener.onChannelRedemptionAdd((event) => {
      this.handleRewardRedemption(event);
    });

    // Handle connection events
    this.listener.onConnect(() => {
      console.log('✅ EventSub WebSocket connected');
    });

    this.listener.onDisconnect(() => {
      console.log('⚠️  EventSub WebSocket disconnected');
    });

    this.listener.onRevoke((subscription) => {
      console.log('⚠️  EventSub subscription revoked:', subscription.id);
    });
  }

  /**
   * Subscribe to stream events for a user
   */
  async subscribeToUser(userId) {
    try {
      if (!this.listener) {
        console.error('Stream monitor not initialized');
        return false;
      }

      // Get user data
      const user = await database.getUser(userId);
      if (!user) {
        console.error(`User ${userId} not found`);
        return false;
      }

      // Check if already subscribed
      if (this.connectedUsers.has(userId)) {
        console.log(`Already monitoring streams for ${user.username}`);
        return true;
      }

      // Get Twitch user info to get their ID
      const twitchUser = await this.appApiClient.users.getUserByName(user.username);
      if (!twitchUser) {
        console.error(`Twitch user ${user.username} not found`);
        return false;
      }

      // Subscribe to stream online/offline events
      const onlineSubscription = this.listener.onStreamOnline(twitchUser.id, (event) => {
        this.handleStreamOnline(event, userId);
      });

      const offlineSubscription = this.listener.onStreamOffline(twitchUser.id, (event) => {
        this.handleStreamOffline(event, userId);
      });

      // Subscribe to channel point redemptions (if user has feature enabled)
      let redemptionSubscription = null;
      const hasChannelPoints = await database.hasFeature(userId, 'channelPoints');
      if (hasChannelPoints) {
        redemptionSubscription = this.listener.onChannelRedemptionAdd(twitchUser.id, (event) => {
          this.handleRewardRedemption(event, userId);
        });
      }

      // Subscribe to follow events (if user has alerts enabled)
      let followSubscription = null;
      const hasAlerts = await database.hasFeature(userId, 'streamAlerts');
      if (hasAlerts) {
        followSubscription = this.listener.onChannelFollow(twitchUser.id, (event) => {
          this.handleFollowEvent(event, userId);
        });
      }

      // Subscribe to raid events (if user has alerts enabled)
      let raidSubscription = null;
      if (hasAlerts) {
        raidSubscription = this.listener.onChannelRaidTo(twitchUser.id, (event) => {
          this.handleRaidEvent(event, userId);
        });
      }

      // Store subscriptions
      this.connectedUsers.set(userId, {
        twitchUserId: twitchUser.id,
        username: user.username,
        onlineSubscription,
        offlineSubscription,
        redemptionSubscription,
        followSubscription,
        raidSubscription
      });

      console.log(`🎬 Now monitoring streams for ${user.username} (${userId})`);

      // Check current stream status
      await this.checkCurrentStreamStatus(userId, twitchUser.id);

      return true;
    } catch (error) {
      console.error(`Failed to subscribe to stream events for user ${userId}:`, error);
      return false;
    }
  }

  /**
   * Unsubscribe from stream events for a user
   */
  async unsubscribeFromUser(userId) {
    try {
      const subscription = this.connectedUsers.get(userId);
      if (!subscription) return;

      // Remove EventSub subscriptions
      if (subscription.onlineSubscription) {
        subscription.onlineSubscription.stop();
      }
      if (subscription.offlineSubscription) {
        subscription.offlineSubscription.stop();
      }
      if (subscription.redemptionSubscription) {
        subscription.redemptionSubscription.stop();
      }
      if (subscription.followSubscription) {
        subscription.followSubscription.stop();
      }
      if (subscription.raidSubscription) {
        subscription.raidSubscription.stop();
      }

      this.connectedUsers.delete(userId);
      console.log(`🎬 Stopped monitoring streams for user ${userId}`);
    } catch (error) {
      console.error(`Failed to unsubscribe from stream events for user ${userId}:`, error);
    }
  }

  /**
   * Handle stream going online
   */
  async handleStreamOnline(event, userId) {
    try {
      // Find user ID if not provided
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Stream online for unknown user: ${event.broadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      console.log(`🔴 ${user.username} went LIVE! Title: "${event.streamTitle}"`);

      // Auto-start stream session if not already started
      const counters = await database.getCounters(userId);
      if (!counters.streamStarted) {
        await database.startStream(userId);
        console.log(`🎬 Auto-started stream session for ${user.username}`);
      }

      // Emit event for real-time updates
      this.emit('streamOnline', {
        userId,
        username: user.username,
        displayName: user.displayName,
        streamTitle: event.streamTitle,
        gameName: event.categoryName,
        startedAt: event.startDate
      });

    } catch (error) {
      console.error('Error handling stream online event:', error);
    }
  }

  /**
   * Handle stream going offline
   */
  async handleStreamOffline(event, userId) {
    try {
      // Find user ID if not provided
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Stream offline for unknown user: ${event.broadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      console.log(`⚫ ${user.username} went OFFLINE`);

      // Emit event for real-time updates
      this.emit('streamOffline', {
        userId,
        username: user.username,
        displayName: user.displayName
      });

    } catch (error) {
      console.error('Error handling stream offline event:', error);
    }
  }

  /**
   * Handle channel point reward redemption
   */
  async handleRewardRedemption(event, userId) {
    try {
      // Find user ID if not provided
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Reward redemption for unknown user: ${event.broadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      // Check if user has channel points feature enabled
      const hasChannelPoints = await database.hasFeature(userId, 'channelPoints');
      if (!hasChannelPoints) {
        console.log(`Channel points disabled for ${user.username}`);
        return;
      }

      console.log(`🎯 Channel point redemption: ${event.rewardTitle} by ${event.userName} for ${user.username}`);

      // Get the reward configuration
      const rewardConfig = await database.getChannelPointReward(userId, event.rewardId);

      if (!rewardConfig || !rewardConfig.isEnabled) {
        console.log(`Unknown or disabled reward: ${event.rewardTitle}`);
        return;
      }

      // Process the reward action
      let counterUpdate = null;
      switch (rewardConfig.action) {
        case 'increment_deaths':
          counterUpdate = await database.incrementDeaths(userId);
          console.log(`💀 Deaths incremented by ${event.userName} via channel points`);
          break;
        case 'increment_swears':
          counterUpdate = await database.incrementSwears(userId);
          console.log(`🤬 Swears incremented by ${event.userName} via channel points`);
          break;
        case 'decrement_deaths':
          counterUpdate = await database.decrementDeaths(userId);
          console.log(`💀 Deaths decremented by ${event.userName} via channel points`);
          break;
        case 'decrement_swears':
          counterUpdate = await database.decrementSwears(userId);
          console.log(`🤬 Swears decremented by ${event.userName} via channel points`);
          break;
        default:
          console.log(`Unknown reward action: ${rewardConfig.action}`);
          return;
      }

      // Emit events for real-time updates
      this.emit('rewardRedeemed', {
        userId,
        username: user.username,
        redeemedBy: event.userName,
        rewardTitle: event.rewardTitle,
        rewardId: event.rewardId,
        action: rewardConfig.action,
        cost: rewardConfig.cost,
        timestamp: new Date().toISOString()
      });

      // Emit counter update if applicable
      if (counterUpdate) {
        this.emit('counterUpdate', {
          userId,
          counters: counterUpdate,
          source: 'channel_points',
          redeemedBy: event.userName
        });
      }

    } catch (error) {
      console.error('Error handling reward redemption event:', error);
    }
  }

  /**
   * Check current stream status for a user
   */
  async checkCurrentStreamStatus(userId, twitchUserId) {
    try {
      const stream = await this.appApiClient.streams.getStreamByUserId(twitchUserId);

      if (stream) {
        // User is currently live
        await this.handleStreamOnline({
          broadcasterId: twitchUserId,
          broadcasterName: stream.userName,
          streamTitle: stream.title,
          categoryName: stream.gameName,
          startDate: stream.startDate
        }, userId);
      }
    } catch (error) {
      console.error(`Error checking current stream status for user ${userId}:`, error);
    }
  }

  /**
   * Subscribe to all active users
   */
  async subscribeToAllUsers() {
    try {
      const users = await database.getAllUsers();
      const activeUsers = users.filter(user => user.isActive);

      console.log(`🎬 Subscribing to stream events for ${activeUsers.length} active users...`);

      for (const user of activeUsers) {
        await this.subscribeToUser(user.twitchUserId);
        // Add small delay to avoid rate limits
        await new Promise(resolve => setTimeout(resolve, 100));
      }

      console.log(`✅ Stream monitoring active for ${this.connectedUsers.size} users`);
    } catch (error) {
      console.error('Error subscribing to all users:', error);
    }
  }

  /**
   * Get monitoring status
   */
  getStatus() {
    return {
      connected: !!this.listener,
      monitoredUsers: Array.from(this.connectedUsers.entries()).map(([userId, data]) => ({
        userId,
        username: data.username,
        twitchUserId: data.twitchUserId
      }))
    };
  }

  /**
   * Create custom channel point reward
   */
  async createCustomReward(userId, rewardData) {
    try {
      const user = await database.getUser(userId);
      if (!user) {
        throw new Error('User not found');
      }

      // Get user's API client with proper scopes
      const userAuth = new RefreshingAuthProvider(
        {
          clientId: this.clientId,
          clientSecret: this.clientSecret
        },
        {
          accessToken: user.accessToken,
          refreshToken: user.refreshToken,
          expiryTimestamp: new Date(user.tokenExpiry).getTime()
        }
      );

      const userApiClient = new ApiClient({ authProvider: userAuth });

      // Create reward on Twitch
      const twitchReward = await userApiClient.channelPoints.createCustomReward(user.twitchUserId, {
        title: rewardData.title,
        cost: rewardData.cost,
        prompt: rewardData.prompt || `Trigger ${rewardData.action.replace('_', ' ')} counter`,
        isEnabled: true,
        backgroundColor: rewardData.backgroundColor || '#9147FF',
        userInputRequired: false,
        maxRedemptionsPerStream: rewardData.maxRedemptionsPerStream || null,
        maxRedemptionsPerUserPerStream: rewardData.maxRedemptionsPerUserPerStream || null,
        globalCooldown: rewardData.globalCooldown || null
      });

      // Save reward configuration to database
      const rewardConfig = {
        userId: userId,
        rewardId: twitchReward.id,
        rewardTitle: twitchReward.title,
        cost: twitchReward.cost,
        action: rewardData.action,
        isEnabled: true,
        createdAt: new Date().toISOString()
      };

      await database.saveChannelPointReward(rewardConfig);

      console.log(`🎯 Created custom reward: ${twitchReward.title} (${twitchReward.cost} points) for ${user.username}`);

      return {
        success: true,
        reward: {
          id: twitchReward.id,
          title: twitchReward.title,
          cost: twitchReward.cost,
          action: rewardData.action
        }
      };
    } catch (error) {
      console.error('Error creating custom reward:', error);
      return {
        success: false,
        error: error.message
      };
    }
  }

  /**
   * Delete custom channel point reward
   */
  async deleteCustomReward(userId, rewardId) {
    try {
      const user = await database.getUser(userId);
      if (!user) {
        throw new Error('User not found');
      }

      // Get user's API client
      const userAuth = new RefreshingAuthProvider(
        {
          clientId: this.clientId,
          clientSecret: this.clientSecret
        },
        {
          accessToken: user.accessToken,
          refreshToken: user.refreshToken,
          expiryTimestamp: new Date(user.tokenExpiry).getTime()
        }
      );

      const userApiClient = new ApiClient({ authProvider: userAuth });

      // Delete reward from Twitch
      await userApiClient.channelPoints.deleteCustomReward(user.twitchUserId, rewardId);

      // Remove from database
      await database.deleteChannelPointReward(userId, rewardId);

      console.log(`🗑️  Deleted custom reward ${rewardId} for ${user.username}`);

      return { success: true };
    } catch (error) {
      console.error('Error deleting custom reward:', error);
      return {
        success: false,
        error: error.message
      };
    }
  }

  /**
   * Handle follow events
   */
  async handleFollowEvent(event, userId) {
    try {
      // Find user ID if not provided
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Follow event for unknown user: ${event.broadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      // Check if user has alerts feature enabled
      const hasAlerts = await database.hasFeature(userId, 'streamAlerts');
      if (!hasAlerts) {
        console.log(`Alerts disabled for ${user.username}`);
        return;
      }

      console.log(`👥 New follower: ${event.userName} followed ${user.username}`);

      // Emit follow event
      this.emit('newFollower', {
        userId,
        username: user.username,
        follower: event.userName,
        timestamp: new Date().toISOString()
      });

    } catch (error) {
      console.error('Error handling follow event:', error);
    }
  }

  /**
   * Handle raid events
   */
  async handleRaidEvent(event, userId) {
    try {
      // Find user ID if not provided
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.toBroadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Raid event for unknown user: ${event.toBroadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      // Check if user has alerts feature enabled
      const hasAlerts = await database.hasFeature(userId, 'streamAlerts');
      if (!hasAlerts) {
        console.log(`Alerts disabled for ${user.username}`);
        return;
      }

      console.log(`🚨 Raid: ${event.fromBroadcasterName} raided ${user.username} with ${event.viewers} viewers`);

      // Emit raid event
      this.emit('raidReceived', {
        userId,
        username: user.username,
        raider: event.fromBroadcasterName,
        viewers: event.viewers,
        timestamp: new Date().toISOString()
      });

    } catch (error) {
      console.error('Error handling raid event:', error);
    }
  }

  /**
   * Stop the stream monitor
   */
  async stop() {
    try {
      if (this.listener) {
        this.listener.stop();
        this.listener = null;
      }

      this.connectedUsers.clear();
      console.log('🛑 Stream Monitor stopped');
    } catch (error) {
      console.error('Error stopping stream monitor:', error);
    }
  }
}

// Export singleton instance
module.exports = new StreamMonitor();

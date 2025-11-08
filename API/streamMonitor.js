const { EventSubWsListener } = require('@twurple/eventsub-ws');
const { ApiClient } = require('@twurple/api');
const { RefreshingAuthProvider } = require('@twurple/auth');
const { EventEmitter } = require('events');
const database = require('./database');
const keyVault = require('./keyVault');
const { sendDiscordNotification } = require('./userRoutes');

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
    this.connectionHealthTimers = new Map(); // userId -> timer for keepalive monitoring
    this.maxKeepaliveTimeout = 600; // 10 minutes max as per Twitch docs
    this.defaultKeepaliveTimeout = 30; // 30 seconds default
    this.pendingNotifications = new Map(); // userId -> pending notification data
    this.io = null; // Socket.io instance for real-time notifications
  }

  /**
   * Set the Socket.io instance for real-time notifications
   */
  setSocketIo(io) {
    this.io = io;
    console.log('✅ Socket.io instance set in StreamMonitor');
  }

  /**
   * Initialize the stream monitor with app credentials
   * Note: EventSub WebSocket uses user tokens, we'll create user-specific listeners
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

      // For multi-tenant EventSub WebSocket, we create user-specific listeners
      // when users subscribe (in subscribeToUser method)
      console.log('✅ Stream Monitor initialized (multi-tenant mode)');
      return true;
    } catch (error) {
      console.error('❌ Failed to initialize Stream Monitor:', error);
      return false;
    }
  }

  /**
   * Start connection health monitoring for a user
   * Based on Twitch EventSub keepalive timeout recommendations
   */
  startConnectionHealthMonitoring(userId, keepaliveTimeoutSeconds = this.defaultKeepaliveTimeout) {
    // Clear any existing timer
    this.stopConnectionHealthMonitoring(userId);

    // Set timeout slightly longer than keepalive to account for network delays
    const timeoutMs = (keepaliveTimeoutSeconds + 5) * 1000;

    const timer = setTimeout(() => {
      const userData = this.connectedUsers.get(userId);
      if (userData) {
        console.log(`⚠️  No keepalive received for ${userData.username} within ${keepaliveTimeoutSeconds}s - reconnecting`);

        // Stop current connection
        this.unsubscribeFromUser(userId);

        // Attempt to reconnect
        setTimeout(() => {
          console.log(`🔄 Attempting to reconnect for ${userData.username} after keepalive timeout...`);
          this.subscribeToUser(userId).catch(error => {
            console.error(`❌ Failed to reconnect after keepalive timeout for ${userData.username}:`, error);
          });
        }, 5000); // 5 second delay before reconnection
      }
    }, timeoutMs);

    this.connectionHealthTimers.set(userId, timer);
    console.log(`🩺 Started connection health monitoring for user ${userId} (timeout: ${keepaliveTimeoutSeconds}s)`);
  }

  /**
   * Stop connection health monitoring for a user
   */
  stopConnectionHealthMonitoring(userId) {
    const timer = this.connectionHealthTimers.get(userId);
    if (timer) {
      clearTimeout(timer);
      this.connectionHealthTimers.delete(userId);
    }
  }

  /**
   * Reset connection health monitoring - call this when receiving any message from Twitch
   */
  resetConnectionHealth(userId, keepaliveTimeoutSeconds = this.defaultKeepaliveTimeout) {
    this.startConnectionHealthMonitoring(userId, keepaliveTimeoutSeconds);
  }

  /**
   * Check if user should receive notifications based on their settings
   * @param {string} userId - The user ID to check
   * @returns {Promise<{shouldNotifyDiscord: boolean, shouldNotifyChannel: boolean, shouldNotifyAny: boolean}>}
   */
  async checkUserNotificationSettings(userId) {
    try {
      // Check Discord webhook settings
      const webhookData = await database.getUserDiscordWebhook(userId);
      const discordWebhookEnabled = !!(webhookData?.webhookUrl && webhookData?.enabled);

      // Check notification settings - simplified to webhook-only for Discord
      const notificationData = await database.getUserNotificationSettings(userId);
      const enableChannelNotifications = notificationData?.enableChannelNotifications || false;

      const shouldNotifyDiscord = discordWebhookEnabled; // Simplified: webhook presence = Discord notifications enabled
      const shouldNotifyChannel = enableChannelNotifications;
      const shouldNotifyAny = shouldNotifyDiscord || shouldNotifyChannel;

      return {
        shouldNotifyDiscord,
        shouldNotifyChannel,
        shouldNotifyAny,
        settings: notificationData || {}
      };
    } catch (error) {
      console.warn(`⚠️ Could not load notification settings for user ${userId}:`, error.message);
      return {
        shouldNotifyDiscord: false,
        shouldNotifyChannel: false,
        shouldNotifyAny: false,
        settings: {}
      };
    }
  }

  /**
   * Get EventSub connection status for all users
   * Useful for debugging and monitoring
   */
  getConnectionStatus() {
    const status = {
      totalConnections: this.connectedUsers.size,
      users: []
    };

    for (const [userId, userData] of this.connectedUsers) {
      status.users.push({
        userId,
        username: userData.username,
        twitchUserId: userData.twitchUserId,
        hasHealthMonitoring: this.connectionHealthTimers.has(userId),
        subscriptions: {
          online: !!userData.onlineSubscription,
          offline: !!userData.offlineSubscription,
          redemptions: !!userData.redemptionSubscription,
          follows: !!userData.followSubscription,
          raids: !!userData.raidSubscription,
          subscribes: !!userData.subscribeSubscription,
          subGifts: !!userData.subGiftSubscription,
          subMessages: !!userData.subMessageSubscription,
          cheers: !!userData.cheerSubscription
        }
      });
    }

    return status;
  }

  /**
   * Subscribe to stream events for a user
   * Creates a user-specific EventSub WebSocket listener (required for user auth)
   */
  async subscribeToUser(userId) {
    try {
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

      // Create user-specific auth provider
      // EventSub WebSocket REQUIRES user access tokens
      const userAuthProvider = new RefreshingAuthProvider(
        {
          clientId: this.clientId,
          clientSecret: this.clientSecret
        }
      );

      // Add user with token and scopes for EventSub (reduces subscription costs to 0)
      // These scopes make our subscriptions cost-free since the user has authorized them
      const { userId: twitchUserId } = await userAuthProvider.addUserForToken({
        accessToken: user.accessToken,
        refreshToken: user.refreshToken,
        expiresIn: 0,
        obtainmentTimestamp: 0,
        scope: [
          'user:read:email',
          'chat:read',
          'chat:edit',
          'channel:read:subscriptions',    // For subscribe, sub gift, sub message events
          'channel:read:redemptions',      // For channel point redemptions
          'moderator:read:followers',      // For follow events
          'bits:read',                     // For cheer events
          'clips:edit'
        ]
      }, []);

      // Handle token refresh
      userAuthProvider.onRefresh(async (userId, newTokenData) => {
        await database.saveUser({
          ...user,
          accessToken: newTokenData.accessToken,
          refreshToken: newTokenData.refreshToken,
          tokenExpiry: new Date(newTokenData.expiresIn * 1000 + Date.now()).toISOString()
        });
        console.log(`🔄 Token refreshed for user ${userId} (stream monitor)`);
      });

      // Create user-specific API client
      const userApiClient = new ApiClient({ authProvider: userAuthProvider });

      // Create user-specific EventSub WebSocket listener
      const userListener = new EventSubWsListener({
        apiClient: userApiClient
      });

      // Subscribe to stream online events (use our internal userId which is the Twitch user ID)
      console.log(`🎯 Subscribing to EventSub for user ID: ${userId} (${user.username})`);

      const onlineSubscription = userListener.onStreamOnline(userId, (event) => {
        this.handleStreamOnline(event, userId);
      });

      // Subscribe to stream offline events
      const offlineSubscription = userListener.onStreamOffline(userId, (event) => {
        this.handleStreamOffline(event, userId);
      });

      // Subscribe to channel update events (title/category changes)
      const channelUpdateSubscription = userListener.onChannelUpdate(userId, (event) => {
        console.log(`🔄 CHANNEL UPDATE EVENT received for ${user.username}:`, {
          broadcasterUserId: event.broadcasterUserId,
          broadcasterUserName: event.broadcasterUserName,
          streamTitle: event.streamTitle,
          categoryName: event.categoryName,
          timestamp: new Date().toISOString()
        });
        this.handleChannelUpdate(event, userId);
      });

      // Get user notification settings to determine which events to subscribe to
      console.log(`🔍 Checking notification settings for ${user.username}...`);

      // Ultra-simple notification logic: Only check if Discord webhook is configured
      let discordWebhookConfigured = false;

      try {
        // Check if user has Discord webhook configured
        const webhookData = await database.getUserDiscordWebhook(userId);
        discordWebhookConfigured = !!(webhookData?.webhookUrl);
        console.log(`🔗 Discord webhook configured for ${user.username}: ${discordWebhookConfigured ? 'yes' : 'no'}`);

        // Feature flag check removed - webhook presence is sufficient
        // discordNotificationsEnabled = await database.hasFeature(userId, 'discordNotifications');
        console.log(`🔔 EventSub subscription will be ${discordWebhookConfigured ? 'enabled' : 'disabled'} for ${user.username}`);

      } catch (error) {
        console.warn(`⚠️ Could not check Discord webhook status for ${user.username}:`, error.message);
        discordWebhookConfigured = false;
      }

      // Simple decision: Subscribe to events if Discord webhook is configured
      const shouldSubscribeToAlerts = discordWebhookConfigured;

      // Enable milestones if alerts are enabled (can be made configurable later)
      const shouldSubscribeToMilestones = shouldSubscribeToAlerts;

      console.log(`🎯 EventSub subscription decisions for ${user.username}:`);
      console.log(`   - Discord webhook configured: ${discordWebhookConfigured ? '✅' : '❌'}`);
      console.log(`   - Will create EventSub subscriptions: ${shouldSubscribeToAlerts ? '✅' : '❌'}`);
      console.log(`   - Alerts (follows, subs, raids, cheers): ${shouldSubscribeToAlerts ? '✅' : '❌'}`);
      console.log(`   - Milestones: ${shouldSubscribeToMilestones ? '✅' : '❌'}`);

      // Subscribe to channel point redemptions (if feature enabled)
      let redemptionSubscription = null;
      const hasChannelPoints = await database.hasFeature(userId, 'channelPoints');
      if (hasChannelPoints) {
        console.log(`🏆 Subscribing to channel point redemptions for ${user.username}`);
        redemptionSubscription = userListener.onChannelRedemptionAdd(userId, (event) => {
          this.handleRewardRedemption(event, userId);
        });
      }

      // Subscribe to follow events (if user wants notifications)
      let followSubscription = null;
      if (shouldSubscribeToAlerts) {
        console.log(`👥 Subscribing to follow events for ${user.username}`);
        followSubscription = userListener.onChannelFollow(userId, userId, (event) => {
          this.handleFollowEvent(event, userId);
        });
      }

      // Subscribe to raid events (if user wants notifications)
      let raidSubscription = null;
      if (shouldSubscribeToAlerts) {
        console.log(`🚨 Subscribing to raid events for ${user.username}`);
        raidSubscription = userListener.onChannelRaidTo(userId, (event) => {
          this.handleRaidEvent(event, userId);
        });
      }

      // Subscribe to subscription events (if user wants notifications)
      let subscribeSubscription = null;
      if (shouldSubscribeToAlerts) {
        console.log(`⭐ Subscribing to subscription events for ${user.username}`);
        subscribeSubscription = userListener.onChannelSubscription(userId, (event) => {
          this.handleSubscribeEvent(event, userId);
        });
      }

      // Subscribe to subscription gift events (if user wants notifications)
      let subGiftSubscription = null;
      if (shouldSubscribeToAlerts) {
        console.log(`💝 Subscribing to gift subscription events for ${user.username}`);
        subGiftSubscription = userListener.onChannelSubscriptionGift(userId, (event) => {
          this.handleSubGiftEvent(event, userId);
        });
      }

      // Subscribe to subscription message events (resubscriptions with messages)
      let subMessageSubscription = null;
      if (shouldSubscribeToAlerts) {
        console.log(`📝 Subscribing to resub message events for ${user.username}`);
        subMessageSubscription = userListener.onChannelSubscriptionMessage(userId, (event) => {
          this.handleSubMessageEvent(event, userId);
        });
      }

      // Subscribe to cheer/bits events (if user wants notifications)
      let cheerSubscription = null;
      if (shouldSubscribeToAlerts) {
        console.log(`💎 Subscribing to cheer/bits events for ${user.username}`);
        cheerSubscription = userListener.onChannelCheer(userId, (event) => {
          this.handleCheerEvent(event, userId);
        });
      }

      // Note: Connection lifecycle will be handled by Twurple internally
      // Focus on getting the basic subscriptions working first
      console.log(`✅ EventSub WebSocket listener created for ${user.username}`);

      // Note: Revocation events will be handled by Twurple internally
      console.log(`� Setting up revocation handling for ${user.username}`);

      // Start the user's listener
      userListener.start();

      // Store user data and subscriptions
      const subscriptions = {
        online: !!onlineSubscription,
        offline: !!offlineSubscription,
        channelUpdate: !!channelUpdateSubscription,
        channelPoints: !!redemptionSubscription,
        follows: !!followSubscription,
        raids: !!raidSubscription,
        subscriptions: !!subscribeSubscription,
        giftSubs: !!subGiftSubscription,
        resubMessages: !!subMessageSubscription,
        cheers: !!cheerSubscription
      };

      this.connectedUsers.set(userId, {
        twitchUserId: userId,
        username: user.username,
        userApiClient,
        userListener,
        onlineSubscription,
        offlineSubscription,
        channelUpdateSubscription,
        redemptionSubscription,
        followSubscription,
        raidSubscription,
        subscribeSubscription,
        subGiftSubscription,
        subMessageSubscription,
        cheerSubscription,
        subscriptions: Object.keys(subscriptions).filter(key => subscriptions[key])
      });

      const activeSubscriptions = Object.keys(subscriptions).filter(key => subscriptions[key]);
      console.log(`🎬 EventSub monitoring active for ${user.username} (${userId})`);
      console.log(`📊 Active subscriptions (${activeSubscriptions.length}): ${activeSubscriptions.join(', ') || 'NONE'}`);

      if (activeSubscriptions.length === 0) {
        console.log(`⚠️  No EventSub subscriptions created for ${user.username} - Discord notifications will not work!`);
        console.log(`   💡 To enable: Configure Discord webhook AND enable notification settings`);
      }

      // Create initial pending notification for Discord (will be completed when stream goes live)
      if (discordWebhookConfigured) {
        this.storePendingNotification(userId, {
          type: 'monitoring_ready',
          user: user,
          timestamp: Date.now(),
          hasChannelInfo: false,
          hasStreamInfo: false
        });
        console.log(`📋 Created pending notification for ${user.username} - ready for stream events`);
      }

      // Check current stream status
      await this.checkCurrentStreamStatus(userId, userId, userApiClient);

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
      const userData = this.connectedUsers.get(userId);
      if (!userData) return;

      // Stop connection health monitoring
      this.stopConnectionHealthMonitoring(userId);

      // Stop all subscriptions
      if (userData.onlineSubscription) {
        userData.onlineSubscription.stop();
      }
      if (userData.offlineSubscription) {
        userData.offlineSubscription.stop();
      }
      if (userData.redemptionSubscription) {
        userData.redemptionSubscription.stop();
      }
      if (userData.followSubscription) {
        userData.followSubscription.stop();
      }
      if (userData.raidSubscription) {
        userData.raidSubscription.stop();
      }
      if (userData.subscribeSubscription) {
        userData.subscribeSubscription.stop();
      }
      if (userData.subGiftSubscription) {
        userData.subGiftSubscription.stop();
      }
      if (userData.subMessageSubscription) {
        userData.subMessageSubscription.stop();
      }
      if (userData.cheerSubscription) {
        userData.cheerSubscription.stop();
      }

      // Stop the user's EventSub listener
      if (userData.userListener) {
        userData.userListener.stop();
      }

      this.connectedUsers.delete(userId);

      // Clear any pending Discord notifications for this user
      if (this.pendingNotifications.has(userId)) {
        console.log(`🧹 Clearing pending Discord notification for user ${userId}`);
        this.pendingNotifications.delete(userId);
      }

      // Reset Discord notification status to proper state
      const user = await database.getUser(userId);
      if (user) {
        const webhookData = await database.getUserDiscordWebhook(userId);
        const hasWebhook = !!(webhookData?.webhookUrl && webhookData?.enabled);

        // Emit status reset - webhook configured but monitoring stopped
        if (hasWebhook) {
          this.emitDiscordNotificationStatus(userId, 'Ready', {
            monitoringStopped: true,
            message: 'Monitoring stopped - restart to enable notifications'
          });
        }
      }

      console.log(`🎬 Stopped monitoring streams for user ${userId}`);
    } catch (error) {
      console.error(`Failed to unsubscribe from stream events for user ${userId}:`, error);
    }
  }

  /**
   * Force reconnect EventSub WebSocket for a user
   * Useful when WebSocket connection seems stuck or when prep button is pressed
   */
  async forceReconnectUser(userId) {
    try {
      const user = await database.getUser(userId);
      if (!user) {
        console.error(`Cannot reconnect: User ${userId} not found`);
        return false;
      }

      console.log(`🔄 Force reconnecting EventSub WebSocket for ${user.username}...`);

      // Disconnect existing WebSocket connection if any
      await this.unsubscribeFromUser(userId);

      // Wait a brief moment for cleanup
      await new Promise(resolve => setTimeout(resolve, 1000));

      // Reconnect with fresh WebSocket
      const success = await this.subscribeToUser(userId);

      if (success) {
        console.log(`✅ Successfully reconnected EventSub WebSocket for ${user.username}`);
        return true;
      } else {
        console.error(`❌ Failed to reconnect EventSub WebSocket for ${user.username}`);
        return false;
      }
    } catch (error) {
      console.error(`❌ Error during force reconnect for user ${userId}:`, error);
      return false;
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

      // Reset connection health - we received a message from Twitch
      this.resetConnectionHealth(userId, this.defaultKeepaliveTimeout);

      const user = await database.getUser(userId);
      if (!user) return;

      console.log(`🔴 ${user.username} went LIVE! (Stream ID: ${event.id})`);

      // Check if stream session already started to prevent duplicate notifications
      const counters = await database.getCounters(userId);
      const isNewStream = !counters.streamStarted;

      if (isNewStream) {
        // Start stream session immediately when we detect stream.online
        await database.startStream(userId);
        console.log(`🎬 Auto-started stream session for ${user.username}`);

        // Get current stream info from API to send complete notification immediately
        let streamInfo = null;
        try {
          const userData = this.connectedUsers.get(userId);
          if (userData?.userApiClient) {
            const streams = await userData.userApiClient.streams.getStreamByUserId(userId);
            if (streams) {
              streamInfo = {
                id: streams.id,
                title: streams.title || 'Untitled Stream',
                category: streams.gameName || 'Unknown Category',
                categoryId: streams.gameId,
                language: streams.language || 'en',
                viewerCount: streams.viewers || 0,
                thumbnailUrl: streams.getThumbnailUrl(320, 180),
                tags: streams.tags || [],
                isMature: streams.isMature || false,
                startedAt: event.startedAt || streams.startDate
              };
              console.log(`📊 Retrieved current stream info for ${user.username}:`, {
                title: streamInfo.title,
                category: streamInfo.category,
                viewers: streamInfo.viewerCount
              });
            }
          }
        } catch (apiError) {
          console.warn(`⚠️ Could not fetch current stream data for ${user.username}:`, apiError.message);
        }

        // Update or create notification and send immediately
        const pendingNotification = this.pendingNotifications.get(userId);
        if (pendingNotification && (pendingNotification.type === 'monitoring_ready' || pendingNotification.type === 'stream_start')) {
          // Complete the notification with all available data
          pendingNotification.streamInfo = {
            streamId: event.id,
            startedAt: event.startedAt,
            broadcasterUserId: event.broadcasterId,
            broadcasterUserName: event.broadcasterName,
            broadcasterUserLogin: event.broadcasterLogin,
            ...streamInfo
          };
          pendingNotification.hasStreamInfo = true;
          pendingNotification.hasChannelInfo = !!streamInfo; // We have channel info from API
          pendingNotification.streamStartedAt = Date.now();

          // Send notification immediately with all available data
          await this.sendPendingDiscordNotification(userId, pendingNotification);
        } else {
          console.log(`⚠️ No pending notification found for ${user.username} - creating and sending immediate notification`);
          // Create and send notification immediately
          const notificationData = {
            type: 'stream_start',
            user: user,
            streamInfo: {
              streamId: event.id,
              startedAt: event.startedAt,
              broadcasterUserId: event.broadcasterId,
              broadcasterUserName: event.broadcasterName,
              broadcasterUserLogin: event.broadcasterLogin,
              ...streamInfo
            },
            hasStreamInfo: true,
            hasChannelInfo: !!streamInfo,
            streamStartedAt: Date.now(),
            timestamp: Date.now()
          };

          // Send notification immediately
          await this.sendPendingDiscordNotification(userId, notificationData);
        }
      } else {
        console.log(`🔄 Stream already active for ${user.username} - notification already sent, skipping duplicate`);
      }

      // Emit basic event for real-time updates (detailed info will come from channel.update)
      this.emit('streamOnline', {
        userId,
        username: user.username,
        displayName: user.displayName,
        streamId: event.id,
        startedAt: event.startedAt
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

      // Reset connection health - we received a message from Twitch
      this.resetConnectionHealth(userId, this.defaultKeepaliveTimeout);

      const user = await database.getUser(userId);
      if (!user) return;

      console.log(`⚫ ${user.username} went OFFLINE`);

      // Reset duplicate detection system for next stream
      // This ensures each new stream session gets a Discord notification
      const counters = await database.getCounters(userId);
      if (counters.streamStarted) {
        await database.endStream(userId);
        console.log(`🔄 Reset duplicate detection for ${user.username} - next stream will send notification`);
      }

      // Keep monitoring active - DO NOT stop EventSub monitoring
      // User must manually stop monitoring via "Stop" button
      console.log(`📡 Monitoring continues for ${user.username} - use Stop button to end monitoring`);

      // No Discord notification for stream end - only log the event
      console.log(`📋 Stream offline event logged for ${user.username} (no notification sent)`);

      // Stream offline notifications disabled by user preference

      // Emit event for real-time updates (monitoring stays active)
      this.emit('streamOffline', {
        userId,
        username: user.username,
        displayName: user.displayName,
        monitoringActive: true  // Indicate monitoring is still active
      });

    } catch (error) {
      console.error('Error handling stream offline event:', error);
    }
  }

  /**
   * Store pending notification to be sent when channel.update arrives
   */
  storePendingNotification(userId, notificationData) {
    this.pendingNotifications.set(userId, notificationData);

    console.log(`📋 Created pending Discord notification for user ${userId}`);

    // Emit pending status to frontend
    this.emitDiscordNotificationStatus(userId, 'Pending', {
      waitingFor: ['channel', 'stream'],
      createdAt: new Date().toISOString()
    });

    // Set a timeout to clean up stale notifications (5 minutes)
    setTimeout(() => {
      if (this.pendingNotifications.has(userId)) {
        console.warn(`⚠️ Cleaning up stale notification for user ${userId}`);
        this.pendingNotifications.delete(userId);

        // Emit timeout status to frontend
        this.emitDiscordNotificationStatus(userId, 'Failed', {
          error: 'Notification timeout - no stream data received within 5 minutes'
        });
      }
    }, 5 * 60 * 1000); // 5 minutes
  }

  /**
   * Emit Discord notification status to frontend
   */
  emitDiscordNotificationStatus(userId, status, data = {}) {
    if (this.io) {
      this.io.to(`user:${userId}`).emit(`discordNotification${status}`, {
        userId,
        timestamp: new Date().toISOString(),
        ...data
      });
    }
  }

  /**
   * Send Discord notification using complete pending notification data
   */
  async sendPendingDiscordNotification(userId, pendingNotification) {
    try {
      // Check if webhook is configured
      const webhookData = await database.getUserDiscordWebhook(userId);
      const webhookUrl = webhookData?.webhookUrl || '';
      const discordWebhookConfigured = !!webhookUrl;

      console.log(`🔔 Processing complete pending Discord notification for ${pendingNotification.user.username}:`, {
        discordWebhookConfigured,
        hasWebhookUrl: !!webhookUrl,
        hasChannelInfo: pendingNotification.hasChannelInfo,
        hasStreamInfo: pendingNotification.hasStreamInfo,
        pendingFor: Date.now() - pendingNotification.timestamp + 'ms'
      });

      // Check if we have the minimum required data (stream info with title/category)
      const streamInfo = pendingNotification.streamInfo;
      const channelInfo = pendingNotification.channelInfo;

      const hasRequiredData = discordWebhookConfigured && streamInfo && (
        // Either we have separate channel info, or stream info includes title/category
        (channelInfo && channelInfo.title && channelInfo.category) ||
        (streamInfo.title && streamInfo.category)
      );

      if (hasRequiredData) {
        console.log(`📤 Sending Discord live notification for ${pendingNotification.user.username}...`);

        try {
          // Use channel info if available, otherwise fall back to stream info
          const title = channelInfo?.title || streamInfo.title || 'Untitled Stream';
          const category = channelInfo?.category || streamInfo.category || 'Unknown Category';

          // Send Discord notification with available data
          await sendDiscordNotification(pendingNotification.user, 'stream_start', {
            game: category,
            title: title,
            username: pendingNotification.user.username,
            streamId: streamInfo.streamId,
            startedAt: streamInfo.startedAt
          });

          console.log(`✅ Discord live notification sent for ${pendingNotification.user.username}`);

          // Emit success status to frontend
          this.emitDiscordNotificationStatus(userId, 'Sent', {
            streamId: pendingNotification.streamInfo.streamId,
            sentAt: new Date().toISOString()
          });

          // Clear the pending notification
          this.pendingNotifications.delete(userId);
        } catch (notificationError) {
          console.error(`❌ Discord notification send failed for ${pendingNotification.user.username}:`, notificationError);

          // Emit failure status to frontend
          this.emitDiscordNotificationStatus(userId, 'Failed', {
            error: notificationError.message || 'Failed to send notification'
          });
        }
      } else {
        if (!discordWebhookConfigured) {
          console.log(`⚠️ Discord notification skipped for ${pendingNotification.user.username}: no webhook configured`);
        } else if (!hasRequiredData) {
          console.log(`⚠️ Discord notification not ready for ${pendingNotification.user.username}: missing data (channel: ${pendingNotification.hasChannelInfo}, stream: ${pendingNotification.hasStreamInfo})`);

          // Emit pending status to frontend
          const waitingFor = [];
          if (!pendingNotification.hasChannelInfo) waitingFor.push('channel');
          if (!pendingNotification.hasStreamInfo) waitingFor.push('stream');

          this.emitDiscordNotificationStatus(userId, 'Pending', {
            waitingFor,
            streamId: pendingNotification.streamInfo?.streamId
          });
        }
      }
    } catch (error) {
      console.error(`❌ Error sending pending Discord notification for ${pendingNotification.user.username}:`, error);

      // Emit failure status to frontend
      this.emitDiscordNotificationStatus(userId, 'Failed', {
        error: error.message || 'Unknown error'
      });
    }
  }

  /**
   * Handle channel update events (title/category changes during stream)
   */
  async handleChannelUpdate(event, userId) {
    console.log(`🔄 Processing channel update for userId: ${userId}`, {
      eventBroadcasterUserId: event.broadcasterUserId,
      eventBroadcasterUserName: event.broadcasterUserName,
      streamTitle: event.streamTitle,
      categoryName: event.categoryName
    });

    try {
      // Find user ID if not provided
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcasterUserId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Channel update for unknown user: ${event.broadcasterUserName}`);
        return;
      }

      // Reset connection health - we received a message from Twitch
      this.resetConnectionHealth(userId, this.defaultKeepaliveTimeout);

      const user = await database.getUser(userId);
      if (!user) return;

      console.log(`📝 ${user.username} updated stream info:`);
      console.log(`   Title: "${event.streamTitle}"`);
      console.log(`   Category: "${event.categoryName}"`);

      // Channel update is now optional since we get stream info via API
      // This is only for live updates during stream (e.g., title/category changes)
      console.log(`� Channel update processed - this is a live stream update for ${user.username}`);

      // Check if user is currently streaming before processing update
      const counters = await database.getCounters(userId);
      if (!counters.streamStarted) {
        console.log(`📋 Channel update ignored - ${user.username} is not currently streaming`);
        return;
      }

      // Emit event for real-time updates (frontend can display title/category changes)
      this.emit('channelUpdate', {
        userId,
        username: user.username,
        displayName: user.displayName,
        title: event.streamTitle,
        category: event.categoryName,
        categoryId: event.categoryId,
        language: event.language,
        timestamp: new Date().toISOString()
      });

      console.log(`✅ Channel update processed for ${user.username}`);

    } catch (error) {
      console.error('Error handling channel update event:', error);
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

      // Reset connection health - we received a message from Twitch
      this.resetConnectionHealth(userId, this.defaultKeepaliveTimeout);

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
  /**
   * Check current stream status for a user
   */
  async checkCurrentStreamStatus(userId, twitchUserId, userApiClient) {
    try {
      const stream = await userApiClient.streams.getStreamByUserId(twitchUserId);

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
        error: error?.message
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
        error: error?.message
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

      // Check if user has notification settings that would want this alert
      let shouldNotify = false;
      try {
        // Check Discord webhook settings
        const webhookData = await database.getUserDiscordWebhook(userId);
        const discordWebhookEnabled = !!(webhookData?.webhookUrl && webhookData?.enabled);

        // Check notification settings - simplified to webhook-only for Discord
        const notificationData = await database.getUserNotificationSettings(userId);
        const enableChannelNotifications = notificationData?.enableChannelNotifications || false;

        shouldNotify = discordWebhookEnabled || enableChannelNotifications;
      } catch (error) {
        console.warn(`⚠️ Could not load notification settings for follow event (${user.username}):`, error.message);
      }

      if (!shouldNotify) {
        console.log(`📢 Follow notifications disabled for ${user.username} - skipping`);
        return;
      }

      console.log(`👥 New follower: ${event.userName} followed ${user.username}`);

      // Get alert configuration for this event type
      const alert = await database.getAlertForEvent(userId, 'channel.follow');

      // Emit follow event with alert data
      this.emit('newFollower', {
        userId,
        username: user.username,
        follower: event.userName,
        timestamp: new Date().toISOString(),
        alert: alert
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

      // Check if user has notification settings that would want this alert
      let shouldNotify = false;
      try {
        // Check Discord webhook settings
        const webhookData = await database.getUserDiscordWebhook(userId);
        const discordWebhookEnabled = !!(webhookData?.webhookUrl && webhookData?.enabled);

        // Check notification settings - simplified to webhook-only for Discord
        const notificationData = await database.getUserNotificationSettings(userId);
        const enableChannelNotifications = notificationData?.enableChannelNotifications || false;

        shouldNotify = discordWebhookEnabled || enableChannelNotifications;
      } catch (error) {
        console.warn(`⚠️ Could not load notification settings for raid event (${user.username}):`, error.message);
      }

      if (!shouldNotify) {
        console.log(`📢 Raid notifications disabled for ${user.username} - skipping`);
        return;
      }

      console.log(`🚨 Raid: ${event.fromBroadcasterName} raided ${user.username} with ${event.viewers} viewers`);

      // Get alert configuration for this event type
      const alert = await database.getAlertForEvent(userId, 'channel.raid');

      // Emit raid event with alert data
      this.emit('raidReceived', {
        userId,
        username: user.username,
        raider: event.fromBroadcasterName,
        viewers: event.viewers,
        timestamp: new Date().toISOString(),
        alert: alert
      });

    } catch (error) {
      console.error('Error handling raid event:', error);
    }
  }

  /**
   * Handle subscription events
   */
  async handleSubscribeEvent(event, userId) {
    try {
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Subscribe event for unknown user: ${event.broadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      // Check if user has notification settings that would want this alert
      let shouldNotify = false;
      try {
        // Check Discord webhook settings
        const webhookData = await database.getUserDiscordWebhook(userId);
        const discordWebhookEnabled = !!(webhookData?.webhookUrl && webhookData?.enabled);

        // Check notification settings - simplified to webhook-only for Discord
        const notificationData = await database.getUserNotificationSettings(userId);
        const enableChannelNotifications = notificationData?.enableChannelNotifications || false;

        shouldNotify = discordWebhookEnabled || enableChannelNotifications;
      } catch (error) {
        console.warn(`⚠️ Could not load notification settings for subscribe event (${user.username}):`, error.message);
      }

      if (!shouldNotify) {
        console.log(`📢 Subscription notifications disabled for ${user.username} - skipping`);
        return;
      }

      console.log(`⭐ New subscriber: ${event.userName} subscribed to ${user.username} at tier ${event.tier}`);

      // Get alert configuration for this event type
      const alert = await database.getAlertForEvent(userId, 'channel.subscribe');

      // Emit subscription event with alert data
      this.emit('newSubscription', {
        userId,
        username: user.username,
        subscriber: event.userName,
        tier: event.tier,
        isGift: event.isGift,
        timestamp: new Date().toISOString(),
        alert: alert
      });

    } catch (error) {
      console.error('Error handling subscribe event:', error);
    }
  }

  /**
   * Handle subscription gift events
   */
  async handleSubGiftEvent(event, userId) {
    try {
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Sub gift event for unknown user: ${event.broadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      // Check if user has notification settings that would want this alert
      let shouldNotify = false;
      try {
        // Check Discord webhook settings
        const webhookData = await database.getUserDiscordWebhook(userId);
        const discordWebhookEnabled = !!(webhookData?.webhookUrl && webhookData?.enabled);

        // Check notification settings - simplified to webhook-only for Discord
        const notificationData = await database.getUserNotificationSettings(userId);
        const enableChannelNotifications = notificationData?.enableChannelNotifications || false;

        shouldNotify = discordWebhookEnabled || enableChannelNotifications;
      } catch (error) {
        console.warn(`⚠️ Could not load notification settings for gift sub event (${user.username}):`, error.message);
      }

      if (!shouldNotify) {
        console.log(`📢 Gift subscription notifications disabled for ${user.username} - skipping`);
        return;
      }

      console.log(`🎁 Gift subs: ${event.userName} gifted ${event.amount || 1} tier ${event.tier} sub(s) to ${user.username}`);

      // Get alert configuration for this event type
      const alert = await database.getAlertForEvent(userId, 'channel.subscription.gift');

      // Emit gift sub event with alert data
      this.emit('newGiftSub', {
        userId,
        username: user.username,
        gifter: event.userName,
        amount: event.amount || event.total || 1,
        tier: event.tier,
        timestamp: new Date().toISOString(),
        alert: alert
      });

    } catch (error) {
      console.error('Error handling gift sub event:', error);
    }
  }

  /**
   * Handle subscription message events (resubscriptions)
   */
  async handleSubMessageEvent(event, userId) {
    try {
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Resub event for unknown user: ${event.broadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      // Check if user has notification settings that would want this alert
      let shouldNotify = false;
      try {
        // Check Discord webhook settings
        const webhookData = await database.getUserDiscordWebhook(userId);
        const discordWebhookEnabled = !!(webhookData?.webhookUrl && webhookData?.enabled);

        // Check notification settings - simplified to webhook-only for Discord
        const notificationData = await database.getUserNotificationSettings(userId);
        const enableChannelNotifications = notificationData?.enableChannelNotifications || false;

        shouldNotify = discordWebhookEnabled || enableChannelNotifications;
      } catch (error) {
        console.warn(`⚠️ Could not load notification settings for resub event (${user.username}):`, error.message);
      }

      if (!shouldNotify) {
        console.log(`📢 Resub notifications disabled for ${user.username} - skipping`);
        return;
      }

      console.log(`🔄 Resub: ${event.userName} resubscribed to ${user.username} (${event.cumulativeMonths} months)`);

      // Get alert configuration for this event type
      const alert = await database.getAlertForEvent(userId, 'channel.subscription.message');

      // Emit resub event with alert data
      this.emit('newResub', {
        userId,
        username: user.username,
        subscriber: event.userName,
        tier: event.tier,
        months: event.cumulativeMonths,
        streakMonths: event.streakMonths,
        message: event.messageText,
        timestamp: new Date().toISOString(),
        alert: alert
      });

    } catch (error) {
      console.error('Error handling resub event:', error);
    }
  }

  /**
   * Handle cheer/bits events
   */
  async handleCheerEvent(event, userId) {
    try {
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Cheer event for unknown user: ${event.broadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      // Check if user has notification settings that would want this alert
      let shouldNotify = false;
      try {
        // Check Discord webhook settings
        const webhookData = await database.getUserDiscordWebhook(userId);
        const discordWebhookEnabled = !!(webhookData?.webhookUrl && webhookData?.enabled);

        // Check notification settings - simplified to webhook-only for Discord
        const notificationData = await database.getUserNotificationSettings(userId);
        const enableChannelNotifications = notificationData?.enableChannelNotifications || false;

        shouldNotify = discordWebhookEnabled || enableChannelNotifications;
      } catch (error) {
        console.warn(`⚠️ Could not load notification settings for cheer event (${user.username}):`, error.message);
      }

      if (!shouldNotify) {
        console.log(`📢 Cheer notifications disabled for ${user.username} - skipping`);
        return;
      }

      console.log(`💎 Bits: ${event.userName || 'Anonymous'} cheered ${event.bits} bits to ${user.username}`);

      // Get alert configuration for this event type
      const alert = await database.getAlertForEvent(userId, 'channel.cheer');

      // Emit cheer event with alert data
      this.emit('newCheer', {
        userId,
        username: user.username,
        cheerer: event.userName || 'Anonymous',
        bits: event.bits,
        message: event.message,
        isAnonymous: event.isAnonymous,
        timestamp: new Date().toISOString(),
        alert: alert
      });

    } catch (error) {
      console.error('Error handling cheer event:', error);
    }
  }

  // Removed: sendDiscordNotification method
  // Now using template-aware sendDiscordNotification from userRoutes.js

  /**
   * Check if a user is currently subscribed to EventSub monitoring
   */
  isUserSubscribed(userId) {
    return this.connectedUsers.has(userId);
  }

  /**
   * Get status of stream monitor and connected users
   */
  getStatus() {
    return {
      initialized: this.clientId !== null,
      connectedUsers: Array.from(this.connectedUsers.keys()),
      userCount: this.connectedUsers.size
    };
  }

  /**
   * Get connection status for a specific user
   */
  getUserConnectionStatus(userId) {
    const userData = this.connectedUsers.get(userId);
    if (!userData) {
      return {
        connected: false,
        subscriptions: [],
        lastConnected: null,
        reconnectAttempts: 0
      };
    }

    return {
      connected: !!this.listener && this.listener.isConnected,
      subscriptions: userData.subscriptions || [],
      lastConnected: userData.lastConnected || null,
      reconnectAttempts: userData.reconnectAttempts || 0,
      hasValidToken: !!userData.apiClient,
      userId: userId
    };
  }

  /**
   * Manual reset for stream state - clear streamStarted flag
   * Use this when duplicate detection is stuck
   */
  async resetStreamState(userId) {
    try {
      const database = require('./database');
      console.log(`🔄 Manual reset of stream state for ${userId}`);

      // Force reset streamStarted to false
      await database.endStream(userId);

      console.log(`✅ Stream state reset for ${userId} - fresh notifications enabled`);
      return true;
    } catch (error) {
      console.error(`❌ Error resetting stream state for ${userId}:`, error);
      return false;
    }
  }

  /**
   * Check subscription costs and clean up expensive subscriptions
   * This helps debug and resolve subscription limit issues
   */
  async checkAndCleanupSubscriptions() {
    try {
      console.log('🔍 Checking EventSub subscription costs...');

      for (const [userId, userData] of this.connectedUsers) {
        try {
          const userApiClient = userData.userApiClient;
          if (!userApiClient) continue;

          // Get all subscriptions for this user
          const subscriptions = await userApiClient.eventSub.getSubscriptions();

          console.log(`📊 User ${userData.username} subscriptions:`, {
            total: subscriptions.data.length,
            totalCost: subscriptions.totalCost,
            maxTotalCost: subscriptions.maxTotalCost
          });

          // Log each subscription with its cost
          for (const sub of subscriptions.data) {
            console.log(`  - ${sub.type} (v${sub.version}): cost=${sub.cost}, status=${sub.status}`);

            // If subscription has cost > 0, it means the user lacks proper authorization
            if (sub.cost > 0) {
              console.log(`    ⚠️ Costly subscription detected! User needs to re-authenticate with proper scopes`);
            }
          }

          // If total cost is too high, suggest cleanup
          if (subscriptions.totalCost > subscriptions.maxTotalCost * 0.8) {
            console.log(`⚠️ High subscription cost for ${userData.username}: ${subscriptions.totalCost}/${subscriptions.maxTotalCost}`);
            console.log(`   💡 User should re-authenticate to get cost-free subscriptions with proper OAuth scopes`);
          }

        } catch (error) {
          console.error(`Error checking subscriptions for ${userData.username}:`, error.message);
        }
      }

      return true;
    } catch (error) {
      console.error('Error during subscription cleanup:', error);
      return false;
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

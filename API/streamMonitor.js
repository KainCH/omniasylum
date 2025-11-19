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
    this.defaultKeepaliveTimeout = 120; // 2 minutes default (increased from 30s)
    this.pendingNotifications = new Map(); // userId -> pending notification data
    this.persistedNotificationStates = new Map(); // userId -> notification state (survives reconnections)
    this.pendingSubscriptions = new Set(); // userId -> boolean (true if subscription setup is in progress)
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

    const timer = setTimeout(async () => {
      const userData = this.connectedUsers.get(userId);
      if (userData) {
        console.log(`⚠️  No keepalive received for ${userData.username} within ${keepaliveTimeoutSeconds}s - initiating automatic reconnection`);

        // Check if user has an active stream to provide context
        try {
          const counters = await database.getCounters(userId);
          const lastNotifiedStreamId = await database.getLastNotifiedStreamId(userId);
          if (counters.streamStarted && lastNotifiedStreamId) {
            console.log(`🎬 User ${userData.username} has active stream (ID: ${lastNotifiedStreamId}) - reconnection will preserve notification state`);
          } else {
            console.log(`📴 User ${userData.username} has no active stream - standard reconnection`);
          }
        } catch (error) {
          console.warn(`⚠️ Could not check stream state for reconnection context:`, error.message);
        }

        // Stop current connection (automatic reconnection)
        this.unsubscribeFromUser(userId, false);

        // Attempt to reconnect
        setTimeout(() => {
          console.log(`🔄 Attempting to reconnect EventSub WebSocket for ${userData.username} after keepalive timeout...`);
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
   * Check if a specific user is connected to EventSub monitoring
   * @param {string} userId - The user ID to check
   * @returns {boolean} - True if user has active EventSub connection
   */
  isUserConnected(userId) {
    const userData = this.connectedUsers.get(userId);
    if (!userData) {
      return false;
    }

    // Check if at least one subscription is active
    const hasActiveSubscriptions = !!(
      userData.onlineSubscription ||
      userData.offlineSubscription ||
      userData.redemptionSubscription ||
      userData.followSubscription ||
      userData.raidSubscription ||
      userData.subscribeSubscription ||
      userData.subGiftSubscription ||
      userData.subMessageSubscription ||
      userData.cheerSubscription
    );

    return hasActiveSubscriptions;
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

      // Check if subscription setup is already in progress
      if (this.pendingSubscriptions.has(userId)) {
        console.log(`⏳ Subscription setup already in progress for ${user.username} - skipping duplicate request`);
        return true;
      }
      this.pendingSubscriptions.add(userId);

      // Provide context about whether this is a reconnection or new subscription
      try {
        const counters = await database.getCounters(userId);
        const lastNotifiedStreamId = await database.getLastNotifiedStreamId(userId);
        const hasPersistedState = this.persistedNotificationStates.has(userId);

        if (counters.streamStarted && lastNotifiedStreamId) {
          console.log(`🔄 Subscribing to EventSub for ${user.username} - RECONNECTION during active stream (ID: ${lastNotifiedStreamId})`);
          console.log(`   💾 Persisted notification state: ${hasPersistedState ? 'Yes' : 'No'}`);
        } else {
          console.log(`🆕 Subscribing to EventSub for ${user.username} - NEW subscription (no active stream)`);
        }
      } catch (error) {
        console.warn(`⚠️ Could not determine subscription context for ${user.username}:`, error.message);
        console.log(`🔌 Subscribing to EventSub for ${user.username} - context unknown`);
      }

      // Create user-specific auth provider
      // EventSub WebSocket REQUIRES user access tokens
      const userAuthProvider = new RefreshingAuthProvider(
        {
          clientId: this.clientId,
          clientSecret: this.clientSecret
        }
      );

      // Calculate proper token expiry information
      const tokenExpiryDate = new Date(user.tokenExpiry);
      const currentTime = Date.now();
      const expiresInSeconds = Math.max(0, Math.floor((tokenExpiryDate.getTime() - currentTime) / 1000));
      const obtainmentTimestamp = Math.floor((tokenExpiryDate.getTime() - (4 * 60 * 60 * 1000)) / 1000); // Assume 4-hour token life

      console.log(`🔐 Setting up EventSub auth for ${user.username}:`, {
        tokenExpiry: user.tokenExpiry,
        expiresInSeconds,
        isExpired: expiresInSeconds <= 0
      });

      // If token is already expired, log warning but let RefreshingAuthProvider handle refresh
      if (expiresInSeconds <= 0) {
        console.log(`⚠️  Token for ${user.username} appears expired, RefreshingAuthProvider will attempt refresh`);
      }

      // Add user with token and scopes for EventSub (reduces subscription costs to 0)
      // These scopes make our subscriptions cost-free since the user has authorized them
      const { userId: twitchUserId } = await userAuthProvider.addUserForToken({
        accessToken: user.accessToken,
        refreshToken: user.refreshToken,
        expiresIn: expiresInSeconds,
        obtainmentTimestamp: obtainmentTimestamp,
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

      // Verify EventSub API compliance before proceeding
      await this.verifyEventSubApiCompliance(userApiClient, user.username);

      // Clean up any existing subscriptions to avoid rate limits
      await this.cleanupExistingSubscriptions(userApiClient, userId, user.username);

      // Subscribe to stream online events (use our internal userId which is the Twitch user ID)
      console.log(`🎯 Subscribing to EventSub for user ID: ${userId} (${user.username})`);

      // Subscribe to stream online events with retry logic
      const onlineSubscription = await this.createSubscriptionWithRetry(
        () => userListener.onStreamOnline(userId, (event) => {
          this.handleStreamOnline(event, userId);
        }),
        'stream online',
        user.username
      );

      // Subscribe to stream offline events with retry logic
      const offlineSubscription = await this.createSubscriptionWithRetry(
        () => userListener.onStreamOffline(userId, (event) => {
          this.handleStreamOffline(event, userId);
        }),
        'stream offline',
        user.username
      );

      // Channel update subscription removed - we now fetch channel info directly via API
      // This eliminates the dependency on EventSub channel.update events for Discord notifications
      console.log(`� Skipping channel.update EventSub subscription for ${user.username} - using direct API calls instead`);

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
        redemptionSubscription = await this.createSubscriptionWithRetry(
          () => userListener.onChannelRedemptionAdd(userId, (event) => {
            this.handleRewardRedemption(event, userId);
          }),
          'channel point redemptions',
          user.username
        );
      }

      // Subscribe to follow events (if user wants notifications)
      let followSubscription = null;
      if (shouldSubscribeToAlerts) {
        console.log(`👥 Subscribing to follow events for ${user.username}`);
        followSubscription = await this.createSubscriptionWithRetry(
          () => userListener.onChannelFollow(userId, userId, (event) => {
            this.handleFollowEvent(event, userId);
          }),
          'follow events',
          user.username
        );
      }

      // Legacy subscriptions (raid, subscribe, gift, resub) are now handled by channel.chat.notification
      // This reduces WebSocket connection load and provides better data

      // Subscribe to chat notification events (if user wants notifications)
      let chatNotificationSubscription = null;
      if (shouldSubscribeToAlerts) {
        console.log(`💬 Subscribing to chat notification events for ${user.username}`);
        chatNotificationSubscription = await this.createSubscriptionWithRetry(
          () => userListener.onChannelChatNotification(userId, userId, (event) => {
            this.handleChatNotificationEvent(event, userId);
          }),
          'chat notification events',
          user.username
        );
      }

      // Subscribe to bits use events (if user wants notifications)
      let cheerSubscription = null;
      if (shouldSubscribeToAlerts) {
        console.log(`💎 Subscribing to bits use events for ${user.username}`);
        // TODO: Update to onChannelBitsUse when Twurple library supports it
        // For now, use onChannelCheer which will be converted by handleCheerEvent
        cheerSubscription = await this.createSubscriptionWithRetry(
          () => userListener.onChannelCheer(userId, (event) => {
            this.handleCheerEvent(event, userId);
          }),
          'bits use events',
          user.username
        );
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
        // channelUpdate removed - using direct API calls instead
        channelPoints: !!redemptionSubscription,
        follows: !!followSubscription,
        chatNotifications: !!chatNotificationSubscription,
        cheers: !!cheerSubscription
      };

      this.connectedUsers.set(userId, {
        twitchUserId: userId,
        username: user.username,
        userApiClient,
        userListener,
        onlineSubscription,
        offlineSubscription,
        // channelUpdateSubscription removed - no longer needed
        redemptionSubscription,
        followSubscription,
        chatNotificationSubscription,
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

      // Discord notification setup - now using direct API calls instead of EventSub
      if (discordWebhookConfigured) {
        console.log(`� Discord notifications enabled for ${user.username} - will use direct API calls`);

        // Emit ready status immediately since we don't need to wait for EventSub channel.update
        this.emitDiscordNotificationStatus(userId, 'Ready', {
          message: 'Discord notifications ready - will fetch channel info via API when stream starts',
          apiMode: true // Indicate we're using API mode, not EventSub
        });
      }

      // Check current stream status
      await this.checkCurrentStreamStatus(userId, userId, userApiClient);

      this.pendingSubscriptions.delete(userId);
      return true;
    } catch (error) {
      this.pendingSubscriptions.delete(userId);
      console.error(`Failed to subscribe to stream events for user ${userId}:`, error);
      return false;
    }
  }

  /**
   * Unsubscribe from stream events for a user
   * @param {string} userId - The user ID
   * @param {boolean} isManualStop - True if manually stopped, false for automatic reconnection
   */
  async unsubscribeFromUser(userId, isManualStop = true) {
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
      if (userData.chatNotificationSubscription) {
        userData.chatNotificationSubscription.stop();
      }
      if (userData.cheerSubscription) {
        userData.cheerSubscription.stop();
      }

      // Stop the user's EventSub listener
      if (userData.userListener) {
        userData.userListener.stop();
      }

      this.connectedUsers.delete(userId);

      // Handle notification state based on stop type
      if (this.pendingNotifications.has(userId)) {
        const pendingNotification = this.pendingNotifications.get(userId);

        if (!isManualStop && pendingNotification.hasChannelInfo) {
          // Automatic reconnection with channel info - preserve state
          this.persistedNotificationStates.set(userId, {
            type: pendingNotification.type,
            channelInfo: pendingNotification.channelInfo,
            hasChannelInfo: pendingNotification.hasChannelInfo,
            timestamp: pendingNotification.timestamp,
            user: pendingNotification.user
          });
          console.log(`💾 Preserving notification state for user ${userId} during reconnection`);
        } else {
          // Manual stop or no valuable state - clear completely
          this.persistedNotificationStates.delete(userId);
          console.log(`🧹 Clearing pending Discord notification for user ${userId}`);
        }

        this.pendingNotifications.delete(userId);
      } else if (isManualStop) {
        // Manual stop - ensure persisted state is also cleared
        this.persistedNotificationStates.delete(userId);
      }

      // Preserve Discord notification tracking during automatic reconnections
      if (!isManualStop) {
        try {
          const lastNotifiedStreamId = await database.getLastNotifiedStreamId(userId);
          if (lastNotifiedStreamId) {
            console.log(`💾 Preserving last notified stream ID ${lastNotifiedStreamId} for user ${userId} during reconnection`);
            // Note: The stream ID stays in the database, no need to store separately
            // This ensures the duplicate detection logic will work after reconnection
          }
        } catch (error) {
          console.warn(`⚠️ Could not retrieve last notified stream ID for user ${userId}:`, error.message);
        }
      }

      // Reset Discord notification status to proper state
      if (isManualStop) {
        // Reset stream state completely when manually stopping monitoring
        await database.endStream(userId);
        console.log(`🔄 Reset stream session state for user ${userId}`);

        const user = await database.getUser(userId);
        if (user) {
          const webhookData = await database.getUserDiscordWebhook(userId);
          const hasWebhook = !!(webhookData?.webhookUrl && webhookData?.enabled);

          // Emit status reset based on webhook configuration
          if (hasWebhook) {
            this.emitDiscordNotificationStatus(userId, 'Ready', {
              monitoringStopped: true,
              message: 'Monitoring stopped - all notification states cleared',
              hasWebhook: true,
              pendingChannelInfo: false,
              pendingStreamInfo: false,
              lastNotificationSent: null,
              currentStreamId: null
            });
          } else {
            this.emitDiscordNotificationStatus(userId, 'NotConfigured', {
              monitoringStopped: true,
              message: 'Monitoring stopped - webhook not configured',
              hasWebhook: false,
              setupSteps: ['Configure Discord webhook']
            });
          }
        }

        console.log(`🧹 All notification states cleared for user ${userId} - fresh start ready`);
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

      // Check if we should send a notification for this stream.online event
      const counters = await database.getCounters(userId);
      const pendingNotification = this.pendingNotifications.get(userId);

      // DUPLICATE DETECTION: Check if this is a reconnection for the same stream
      const lastNotifiedStreamId = await database.getLastNotifiedStreamId(userId);
      if (lastNotifiedStreamId && lastNotifiedStreamId === event.id) {
        console.log(`🔄 Reconnection detected for ${user.username} - same stream ID ${event.id} already notified, skipping duplicate notification`);
        console.log(`   💡 This is likely an EventSub WebSocket reconnection during an active stream`);

        // Reset connection health - we received a valid message from Twitch
        this.resetConnectionHealth(userId, this.defaultKeepaliveTimeout);

        // Still emit basic events for real-time updates
        this.emit('streamOnline', {
          userId,
          username: user.username,
          streamId: event.id,
          startedAt: event.startedAt,
          broadcasterUserId: event.broadcasterId,
          broadcasterUserName: event.broadcasterName,
          broadcasterUserLogin: event.broadcasterLogin,
          isReconnection: true // Flag to indicate this is a reconnection
        });

        return; // Skip Discord notification processing
      }

      // Send notification if:
      // 1. Stream session not started yet (new stream), OR
      // 2. We have a pending notification ready to be completed (waiting for stream start)
      const shouldSendNotification = !counters.streamStarted ||
        (pendingNotification && (pendingNotification.type === 'monitoring_ready' || pendingNotification.type === 'stream_start') && !pendingNotification.hasStreamInfo);

      if (shouldSendNotification) {
        // Start stream session if not already started
        if (!counters.streamStarted) {
          await database.startStream(userId);
          console.log(`🎬 Auto-started stream session for ${user.username}`);
        } else {
          console.log(`🎬 Stream session already active for ${user.username} - sending immediate notification`);
        }

        // Update stream status to 'live' for overlay display
        await database.updateStreamStatus(userId, 'live');
        console.log(`🔴 Updated stream status to 'live' for ${user.username}`);

        // Emit stream status change to connected clients
        if (this.io) {
          console.log(`🎯 Emitting streamStatusChanged to room 'user:${userId}' - status: 'live'`);
          this.io.to(`user:${userId}`).emit('streamStatusChanged', {
            userId,
            username: user.username,
            streamStatus: 'live',
            timestamp: new Date().toISOString()
          });
          console.log(`📡 StreamStatusChanged event emitted for live stream`);
        }

        // Get current stream info from API
        let streamInfo = null;
        try {
          const userData = this.connectedUsers.get(userId);
          if (userData?.userApiClient) {
            const streams = await userData.userApiClient.streams.getStreamByUserId(userId);
            if (streams) {
              streamInfo = {
                streamId: event.id,
                id: streams.id,
                title: streams.title || 'Untitled Stream',
                category: streams.gameName || 'Unknown Category',
                categoryId: streams.gameId,
                language: streams.language || 'en',
                viewerCount: streams.viewers || 0,
                thumbnailUrl: streams.getThumbnailUrl(640, 360) + `?t=${Date.now()}`, // Add timestamp for fresh image
                tags: streams.tags || [],
                isMature: streams.isMature || false,
                startedAt: event.startedAt || streams.startDate,
                broadcasterUserId: event.broadcasterId,
                broadcasterUserName: event.broadcasterName,
                broadcasterUserLogin: event.broadcasterLogin
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
          // Create basic stream info from event data
          streamInfo = {
            streamId: event.id,
            startedAt: event.startedAt,
            broadcasterUserId: event.broadcasterId,
            broadcasterUserName: event.broadcasterName,
            broadcasterUserLogin: event.broadcasterLogin,
            title: 'Live Stream', // Fallback title
            category: 'Unknown Category', // Fallback category
            viewerCount: 0,
            thumbnailUrl: `https://static-cdn.jtvnw.net/previews-ttv/live_user_${user.username.toLowerCase()}-640x360.jpg?t=${Date.now()}` // Fallback with fresh timestamp
          };
        }

        // Create and send notification immediately using new method
        const notificationData = {
          type: 'stream_start',
          streamInfo: streamInfo
        };

        console.log(`🚀 Creating immediate Discord notification for ${user.username} (no EventSub dependency)`);
        await this.createAndSendDiscordNotification(userId, notificationData);

        // Store the stream ID to prevent duplicate notifications during reconnections
        await database.setLastNotifiedStreamId(userId, event.id);
        console.log(`✅ Stored last notified stream ID ${event.id} for user ${userId} to prevent reconnection duplicates`);
      } else {
        console.log(`🔄 Stream already active for ${user.username} - notification already sent, skipping duplicate`);
        console.log(`   💡 Debug: streamStarted=${counters.streamStarted}, pendingNotification=${!!pendingNotification}, hasStreamInfo=${pendingNotification?.hasStreamInfo}`);
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
        const lastNotifiedStreamId = await database.getLastNotifiedStreamId(userId);
        await database.endStream(userId);
        console.log(`🔄 Stream session ended for ${user.username} - cleared notification tracking (was: ${lastNotifiedStreamId || 'none'})`);
        console.log(`✅ Next stream start will send Discord notification (duplicate detection reset)`);

        // Update stream status to 'offline' for overlay display
        await database.updateStreamStatus(userId, 'offline');
        console.log(`⚫ Updated stream status to 'offline' for ${user.username}`);

        // Emit stream status change to connected clients
        if (this.io) {
          console.log(`🎯 Emitting streamStatusChanged to room 'user:${userId}' - status: 'offline'`);
          this.io.to(`user:${userId}`).emit('streamStatusChanged', {
            userId,
            username: user.username,
            streamStatus: 'offline',
            timestamp: new Date().toISOString()
          });
          console.log(`📡 StreamStatusChanged event emitted for offline stream`);
        }

        // Reset Discord notification status to ready for next stream
        this.emitDiscordNotificationStatus(userId, 'Reset', {
          message: 'Stream ended - ready for next stream notification',
          reason: 'Stream went offline',
          nextAction: 'Will send notification when you go live again'
        });
        console.log(`🔔 Discord notification status reset for ${user.username} - ready for next stream`);
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
   * Fetch channel information directly from Twitch API
   * This replaces the need for EventSub channel.update subscription
   */
  async fetchChannelInformation(userId, userApiClient) {
    try {
      console.log(`🔍 Fetching channel information for userId: ${userId}`);

      // Use the Twitch API to get channel information
      const channelInfo = await userApiClient.channels.getChannelInfoById(userId);

      if (channelInfo) {
        const result = {
          title: channelInfo.title || 'Untitled Stream',
          category: channelInfo.gameName || 'No Category',
          categoryId: channelInfo.gameId || '',
          language: channelInfo.broadcasterLanguage || 'en',
          tags: channelInfo.tags || [],
          isMature: false // Not available in channel info endpoint
        };

        console.log(`✅ Retrieved channel info for userId ${userId}:`, {
          title: result.title,
          category: result.category,
          language: result.language
        });

        return result;
      }
    } catch (error) {
      console.warn(`⚠️ Could not fetch channel information for userId ${userId}:`, error.message);
    }

    return null;
  }

  /**
   * Create and send Discord notification immediately with API-fetched channel info
   * This replaces the pending notification system that waited for channel.update
   */
  async createAndSendDiscordNotification(userId, notificationData) {
    try {
      const user = await database.getUser(userId);
      if (!user) {
        console.warn(`⚠️ User not found for Discord notification: ${userId}`);
        this.emitDiscordNotificationStatus(userId, 'Failed', {
          reason: 'User not found',
          message: 'User data not found in database'
        });
        return false;
      }

      console.log(`🔔 Creating Discord notification for ${user.username}...`);

      // Check if Discord webhook is configured
      const webhookData = await database.getUserDiscordWebhook(userId);
      const webhookUrl = webhookData?.webhookUrl || '';

      if (!webhookUrl) {
        console.log(`❌ No Discord webhook configured for ${user.username} - skipping notification`);
        this.emitDiscordNotificationStatus(userId, 'Failed', {
          reason: 'No webhook configured',
          message: 'Please configure a Discord webhook to receive notifications'
        });
        return false;
      }

      // Get API client for this user
      const userData = this.connectedUsers.get(userId);
      if (!userData?.userApiClient) {
        console.warn(`⚠️ No API client available for ${user.username} - cannot fetch channel info`);
        this.emitDiscordNotificationStatus(userId, 'Failed', {
          reason: 'No API client',
          message: 'Twitch API client not available - try restarting monitoring'
        });
        return false;
      }

      // Emit status: Fetching channel data via API
      this.emitDiscordNotificationStatus(userId, 'Fetching', {
        message: 'Fetching channel information via Twitch API...',
        step: 'channel_info'
      });

      // Fetch current channel information via API
      const channelInfo = await this.fetchChannelInformation(userId, userData.userApiClient);

      if (!channelInfo) {
        console.warn(`⚠️ Failed to fetch channel information for ${user.username}`);
        this.emitDiscordNotificationStatus(userId, 'Failed', {
          reason: 'Channel info fetch failed',
          message: 'Could not retrieve channel information from Twitch API'
        });
        return false;
      }

      // Emit status: Processing notification data
      this.emitDiscordNotificationStatus(userId, 'Processing', {
        message: `Processing notification: "${channelInfo.title}" in ${channelInfo.category}`,
        step: 'notification_prep',
        channelInfo: {
          title: channelInfo.title,
          category: channelInfo.category
        }
      });

      // Create complete notification data
      const completeNotification = {
        type: notificationData.type || 'stream_start',
        user: user,
        streamInfo: notificationData.streamInfo,
        channelInfo: channelInfo,
        hasStreamInfo: !!notificationData.streamInfo,
        hasChannelInfo: !!channelInfo,
        timestamp: Date.now(),
        streamStartedAt: Date.now()
      };

      console.log(`📤 Sending Discord notification for ${user.username} with:`, {
        hasStreamInfo: completeNotification.hasStreamInfo,
        hasChannelInfo: completeNotification.hasChannelInfo,
        title: channelInfo?.title || 'No title',
        category: channelInfo?.category || 'No category'
      });

      // Send the notification
      await this.sendCompleteDiscordNotification(userId, completeNotification);

      // Emit final status: Notification sent successfully
      this.emitDiscordNotificationStatus(userId, 'Sent', {
        message: `Discord notification sent successfully for "${channelInfo.title}"`,
        timestamp: new Date().toISOString(),
        channelInfo: {
          title: channelInfo.title,
          category: channelInfo.category
        }
      });

      return true;

    } catch (error) {
      console.error('❌ Error creating Discord notification:', error);

      // Emit error status
      this.emitDiscordNotificationStatus(userId, 'Failed', {
        reason: 'notification_error',
        message: `Failed to create Discord notification: ${error.message}`,
        error: error.message
      });

      return false;
    }
  }

  /**
   * Store pending notification to be sent when channel.update arrives
   * @deprecated - Now we fetch channel info directly via API
   */
  storePendingNotification(userId, notificationData) {
    this.pendingNotifications.set(userId, notificationData);

    console.log(`📋 Created pending Discord notification for user ${userId}`);

    // Emit pending status to frontend
    this.emitDiscordNotificationStatus(userId, 'Pending', {
      waitingFor: ['channel', 'stream'],
      createdAt: new Date().toISOString(),
      message: 'Waiting for channel info - Edit your stream title/category on Twitch and click Done to proceed'
    });

    // Note: No automatic timeout - notifications persist until monitoring is stopped
    // Cleanup only happens when user explicitly stops monitoring via unsubscribeFromUser()
  }

  /**
   * Emit Discord notification status to frontend
   */
  emitDiscordNotificationStatus(userId, status, data = {}) {
    if (this.io) {
      const eventName = `discordNotification${status}`;
      const eventData = {
        userId,
        timestamp: new Date().toISOString(),
        ...data
      };
      console.log(`📡 Emitting ${eventName} to user:${userId}:`, eventData);
      this.io.to(`user:${userId}`).emit(eventName, eventData);
    } else {
      console.warn(`⚠️ Cannot emit notification status - Socket.io not available`);
    }
  }

  /**
   * Send complete Discord notification with all required data
   */
  async sendCompleteDiscordNotification(userId, notificationData) {
    try {
      // Check if webhook is configured
      const webhookData = await database.getUserDiscordWebhook(userId);
      const webhookUrl = webhookData?.webhookUrl || '';

      if (!webhookUrl) {
        console.log(`❌ No Discord webhook configured for ${notificationData.user.username} - cannot send notification`);
        this.emitDiscordNotificationStatus(userId, 'Failed', {
          reason: 'No Discord webhook configured',
          message: 'Please configure a Discord webhook to receive notifications'
        });
        return false;
      }

      console.log(`🔔 Processing Discord notification for ${notificationData.user.username}:`, {
        hasWebhookUrl: !!webhookUrl,
        hasChannelInfo: notificationData.hasChannelInfo,
        hasStreamInfo: notificationData.hasStreamInfo
      });

      // Use channel info if available, otherwise fall back to stream info
      const channelInfo = notificationData.channelInfo;
      const streamInfo = notificationData.streamInfo;

      const title = channelInfo?.title || streamInfo?.title || 'Untitled Stream';
      const category = channelInfo?.category || streamInfo?.category || 'No Category';

      console.log(`📤 Sending Discord live notification for ${notificationData.user.username}...`);

      try {
        // Get user object with Discord webhook URL
        const user = await database.getUser(userId);
        const webhookData = await database.getUserDiscordWebhook(userId);
        const userWithWebhook = {
          ...user,
          discordWebhookUrl: webhookData?.webhookUrl
        };

        // Send Discord notification via existing function
        await sendDiscordNotification(userWithWebhook, 'stream_start', {
          title: title,
          category: category,
          game: category, // Add game alias for compatibility
          streamId: streamInfo?.streamId || 'unknown',
          startedAt: streamInfo?.startedAt || new Date().toISOString(),
          thumbnailUrl: streamInfo?.thumbnailUrl || null,
          viewerCount: streamInfo?.viewerCount || 0
        });

        // Remove pending notification
        this.pendingNotifications.delete(userId);

        // Emit success status to frontend
        this.emitDiscordNotificationStatus(userId, 'Sent', {
          title: title,
          category: category,
          sentAt: new Date().toISOString(),
          message: `Notification sent successfully for "${title}"`
        });

        console.log(`✅ Discord notification sent successfully for ${notificationData.user.username}`);
        return true;

      } catch (sendError) {
        console.error(`❌ Failed to send Discord notification for ${notificationData.user.username}:`, sendError);

        // Emit error status to frontend
        this.emitDiscordNotificationStatus(userId, 'Failed', {
          reason: 'Send failed',
          error: sendError.message,
          message: 'Failed to send Discord notification - check webhook URL'
        });

        return false;
      }

    } catch (error) {
      console.error('❌ Error processing Discord notification:', error);
      this.emitDiscordNotificationStatus(userId, 'Failed', {
        reason: 'Processing error',
        error: error.message
      });
      return false;
    }
  }

  /**
   * Send Discord notification using complete pending notification data
   * @deprecated - Use sendCompleteDiscordNotification instead
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
        // Either we have separate channel info with title, or stream info includes title
        // Category is optional and will default to "No Category" if empty
        (channelInfo && channelInfo.title) ||
        (streamInfo.title)
      );

      if (hasRequiredData) {
        console.log(`📤 Sending Discord live notification for ${pendingNotification.user.username}...`);

        try {
          // Use channel info if available, otherwise fall back to stream info
          const title = channelInfo?.title || streamInfo.title || 'Untitled Stream';
          const category = (channelInfo?.category && channelInfo.category.trim()) ||
                          (streamInfo.category && streamInfo.category.trim()) ||
                          'No Category';

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
        } else {
          // Determine what we're waiting for and set appropriate status
          const waitingFor = [];
          if (!pendingNotification.hasChannelInfo) waitingFor.push('channel');
          if (!pendingNotification.hasStreamInfo) waitingFor.push('stream');

          if (waitingFor.length === 0) {
            // Should not happen, but handle edge case
            console.log(`⚠️ Discord notification has all data but failed validation for ${pendingNotification.user.username}`);
            this.emitDiscordNotificationStatus(userId, 'Failed', {
              error: 'All data present but validation failed'
            });
          } else if (waitingFor.includes('channel') && waitingFor.includes('stream')) {
            // Waiting for both channel and stream info
            console.log(`⏳ Discord notification waiting for both channel and stream data for ${pendingNotification.user.username}`);
            this.emitDiscordNotificationStatus(userId, 'Pending', {
              waitingFor,
              message: 'Waiting for channel info - Edit your stream title/category on Twitch and click Done to proceed'
            });
          } else if (waitingFor.includes('stream')) {
            // Have channel info, waiting for stream info
            console.log(`⏳ Discord notification waiting for stream info for ${pendingNotification.user.username}`);
            this.emitDiscordNotificationStatus(userId, 'Pending', {
              waitingFor,
              message: 'Stream detected! Start streaming to send notification',
              channelInfo: {
                title: channelInfo?.title,
                category: channelInfo?.category || 'No Category'
              }
            });
          } else if (waitingFor.includes('channel')) {
            // Have stream info, waiting for channel info
            console.log(`⏳ Discord notification waiting for channel info for ${pendingNotification.user.username}`);
            this.emitDiscordNotificationStatus(userId, 'Pending', {
              waitingFor,
              message: 'Stream detected! Edit your stream title/category on Twitch and click Done to send notification',
              streamId: pendingNotification.streamInfo?.streamId
            });
          }
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
   * Handle cheer/bits events
   */
  async handleBitsUseEvent(event, userId) {
    try {
      if (!userId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === event.broadcaster_user_id || sub.twitchUserId === event.broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Bits use event for unknown user: ${event.broadcaster_user_name || event.broadcasterName}`);
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
        console.warn(`⚠️ Could not load notification settings for bits use event (${user.username}):`, error.message);
      }

      if (!shouldNotify) {
        console.log(`📢 Bits use notifications disabled for ${user.username} - skipping`);
        return;
      }

      // Extract bits count and message from the new format
      const bits = event.bits;
      const username = event.user_name || event.userName || 'Anonymous';
      const messageText = event.message?.text || event.message || '';
      const eventType = event.type || 'cheer'; // 'cheer', 'power-up', 'combo', etc.

      console.log(`💎 Bits Use: ${username} used ${bits} bits (${eventType}) in ${user.username}'s channel`);

      // Get alert configuration for this event type
      const alert = await database.getAlertForEvent(userId, 'channel.bits.use');

      // Emit bits use event with alert data
      this.emit('newBitsUse', {
        userId,
        username: user.username,
        user: username,
        bits: bits,
        message: messageText,
        eventType: eventType, // cheer, power-up, combo, etc.
        isAnonymous: event.is_anonymous || event.isAnonymous || false,
        timestamp: new Date().toISOString(),
        alert: alert
      });

    } catch (error) {
      console.error('Error handling bits use event:', error);
    }
  }

  // Legacy method for backward compatibility
  async handleCheerEvent(event, userId) {
    // Convert old cheer event format to new bits use format for compatibility
    const bitsUseEvent = {
      bits: event.bits,
      user_name: event.userName,
      broadcaster_user_id: event.broadcasterId,
      broadcaster_user_name: event.broadcasterName,
      message: { text: event.message },
      type: 'cheer',
      is_anonymous: event.isAnonymous
    };
    return this.handleBitsUseEvent(bitsUseEvent, userId);
  }

  /**
   * Handle chat notification events (channel.chat.notification)
   * This covers subs, resubs, gifts, raids, announcements, etc.
   */
  async handleChatNotificationEvent(event, userId) {
    try {
      // Extract basic info
      const { broadcasterId, noticeType, chatterId, chatterName, systemMessage } = event;

      // If userId wasn't passed correctly, try to find it
      if (!userId && broadcasterId) {
        for (const [uid, sub] of this.connectedUsers) {
          if (sub.twitchUserId === broadcasterId) {
            userId = uid;
            break;
          }
        }
      }

      if (!userId) {
        console.log(`Chat notification event for unknown user: ${event.broadcasterName}`);
        return;
      }

      const user = await database.getUser(userId);
      if (!user) return;

      console.log(`💬 Chat Notification (${noticeType}) for ${user.username}: ${systemMessage}`);

      // Map notice types to our internal event types
      const eventTypeMapping = {
        'sub': 'chat_notification_subscribe',
        'resub': 'chat_notification_resub',
        'sub_gift': 'chat_notification_gift_sub',
        'community_sub_gift': 'chat_notification_community_gift',
        'gift_paid_upgrade': 'chat_notification_gift_upgrade',
        'prime_paid_upgrade': 'chat_notification_prime_upgrade',
        'raid': 'chat_notification_raid',
        'unraid': 'chat_notification_unraid',
        'pay_it_forward': 'chat_notification_pay_it_forward',
        'announcement': 'chat_notification_announcement',
        'bits_badge_tier': 'chat_notification_bits_badge',
        'charity_donation': 'chat_notification_charity_donation'
      };

      const mappedEventType = eventTypeMapping[noticeType] || `chat_notification_${noticeType}`;

      // Get alert configuration for this event type
      const alert = await database.getAlertForEvent(userId, mappedEventType);

      // Prepare event data
      const eventData = {
        type: 'chat_notification',
        noticeType,
        eventType: mappedEventType,
        userId,
        username: user.username,
        chatter: chatterName || 'Anonymous',
        chatterId,
        message: systemMessage,
        timestamp: new Date().toISOString(),
        alert: alert,
        details: {}
      };

      // Add type-specific details
      switch (noticeType) {
        case 'sub':
          eventData.details = event.sub;
          break;
        case 'resub':
          eventData.details = event.resub;
          break;
        case 'sub_gift':
          eventData.details = event.subGift;
          break;
        case 'community_sub_gift':
          eventData.details = event.communitySubGift;
          break;
        case 'gift_paid_upgrade':
          eventData.details = event.giftPaidUpgrade;
          break;
        case 'prime_paid_upgrade':
          eventData.details = event.primePaidUpgrade;
          break;
        case 'raid':
          eventData.details = event.raid;
          break;
        case 'announcement':
          eventData.details = event.announcement;
          break;
        case 'bits_badge_tier':
          eventData.details = event.bitsBadgeTier;
          break;
        case 'charity_donation':
          eventData.details = event.charityDonation;
          break;
      }

      // Emit generic chat notification event
      this.emit('chatNotification', eventData);

      // Send to Socket.io
      if (this.io) {
        this.io.to(`user:${userId}`).emit('chatNotificationEvent', eventData);
      }

    } catch (error) {
      console.error('Error handling chat notification event:', error);
    }
  }

  /**
   * Check if a user is currently subscribed to EventSub monitoring
   */
  isUserSubscribed(userId) {
    return this.connectedUsers.has(userId);
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
   * Verify EventSub API compatibility by checking subscription details
   */
  async verifyEventSubApiCompliance(userApiClient, username) {
    try {
      console.log(`🔍 Verifying EventSub API compliance for ${username}...`);

      // Get existing subscriptions to verify API response format
      const subscriptions = await userApiClient.eventSub.getSubscriptions();

      console.log(`📊 EventSub API Response Structure:`);
      console.log(`  - Total subscriptions: ${subscriptions.data.length}`);
      console.log(`  - Max total cost: ${subscriptions.maxTotalCost}`);
      console.log(`  - Current total cost: ${subscriptions.totalCost}`);

      if (subscriptions.data.length > 0) {
        const sampleSub = subscriptions.data[0];
        console.log(`📋 Sample subscription structure:`, {
          id: sampleSub.id,
          type: sampleSub.type,
          version: sampleSub.version,
          status: sampleSub.status,
          cost: sampleSub.cost,
          condition: Object.keys(sampleSub.condition || {}),
          transport: sampleSub.transport?.method || 'unknown'
        });
      }

      // Verify this matches Twitch API specification
      const isCompliant = (
        typeof subscriptions.totalCost === 'number' &&
        typeof subscriptions.maxTotalCost === 'number' &&
        Array.isArray(subscriptions.data)
      );

      console.log(`✅ EventSub API compliance check: ${isCompliant ? 'PASSED' : 'FAILED'}`);
      return isCompliant;

    } catch (error) {
      console.warn(`⚠️ EventSub API compliance check failed:`, error.message);
      return false;
    }
  }

  /**
   * Create a subscription with rate limit handling
   */
  async createSubscriptionWithRetry(subscriptionFunction, subscriptionName, username, maxRetries = 3) {
    for (let attempt = 1; attempt <= maxRetries; attempt++) {
      try {
        const subscription = await subscriptionFunction();
        console.log(`✅ Created ${subscriptionName} subscription for ${username}`);

        // Log subscription details to verify API compliance
        if (subscription && typeof subscription === 'object') {
          console.log(`📋 Subscription details:`, {
            hasId: !!subscription.id,
            hasType: !!subscription.type,
            hasStatus: !!subscription.status,
            objectType: subscription.constructor.name
          });
        }

        return subscription;
      } catch (error) {
        const isRateLimit = error.message.includes('Too Many Requests') || error.message.includes('429');

        if (isRateLimit) {
          console.warn(`🚫 Rate limit hit for ${subscriptionName} subscription (attempt ${attempt}/${maxRetries})`);

          if (attempt < maxRetries) {
            const delay = Math.pow(2, attempt) * 1000; // Exponential backoff: 2s, 4s, 8s
            console.log(`⏳ Waiting ${delay}ms before retry...`);
            await new Promise(resolve => setTimeout(resolve, delay));
          } else {
            console.error(`❌ Failed to create ${subscriptionName} subscription after ${maxRetries} attempts`);
            return null;
          }
        } else {
          console.warn(`⚠️ Failed to create ${subscriptionName} subscription for ${username}:`, error.message);
          return null;
        }
      }
    }
    return null;
  }

  /**
   * Clean up existing EventSub subscriptions for a user to avoid rate limits
   * This removes all existing subscriptions before creating new ones
   */
  async cleanupExistingSubscriptions(userApiClient, userId, username) {
    try {
      console.log(`🧹 Cleaning up existing EventSub subscriptions for ${username}...`);

      // Get all existing subscriptions
      const subscriptions = await userApiClient.eventSub.getSubscriptions();

      console.log(`📊 Found ${subscriptions.data.length} existing subscriptions for ${username}`);
      console.log(`📈 Total cost: ${subscriptions.totalCost}/${subscriptions.maxTotalCost}`);

      if (subscriptions.data.length > 0) {
        console.log(`🗑️ Removing ${subscriptions.data.length} existing subscriptions...`);

        // Group subscriptions by type for better logging
        const subscriptionsByType = {};
        subscriptions.data.forEach(sub => {
          if (!subscriptionsByType[sub.type]) {
            subscriptionsByType[sub.type] = [];
          }
          subscriptionsByType[sub.type].push(sub);
        });

        // Log what we're about to delete
        console.log(`📝 Subscription types to delete:`, Object.keys(subscriptionsByType));

        // Delete all existing subscriptions
        let deletedCount = 0;
        let failedCount = 0;

        for (const subscription of subscriptions.data) {
          try {
            await userApiClient.eventSub.deleteSubscription(subscription.id);
            console.log(`  ✅ Removed: ${subscription.type} (ID: ${subscription.id})`);
            deletedCount++;

            // Small delay between deletions to avoid rate limiting on deletion
            await new Promise(resolve => setTimeout(resolve, 100));
          } catch (error) {
            console.warn(`  ⚠️ Failed to remove ${subscription.type} (${subscription.id}): ${error.message}`);
            failedCount++;
          }
        }

        console.log(`📊 Cleanup results: ${deletedCount} deleted, ${failedCount} failed`);

        // Wait longer for Twitch to process the deletions
        console.log(`⏳ Waiting for Twitch to process deletions...`);
        await new Promise(resolve => setTimeout(resolve, 3000));

        // Verify cleanup worked
        const remainingSubscriptions = await userApiClient.eventSub.getSubscriptions();
        console.log(`🔍 Verification: ${remainingSubscriptions.data.length} subscriptions remaining`);

        console.log(`✅ Cleanup complete for ${username}`);
      } else {
        console.log(`✅ No existing subscriptions found for ${username} - cleanup not needed`);
      }

    } catch (error) {
      console.warn(`⚠️ Error during subscription cleanup for ${username}:`, error.message);
      // Continue anyway - we'll handle rate limits in subscription creation
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

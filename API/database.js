const { TableClient, AzureNamedKeyCredential } = require('@azure/data-tables');
const { DefaultAzureCredential, ManagedIdentityCredential } = require('@azure/identity');
const fs = require('fs');
const path = require('path');

/**
 * Database abstraction layer supporting both:
 * - Azure Table Storage (production)
 * - Local JSON file (development)
 */
class Database {
  constructor() {
    this.mode = process.env.DB_MODE || 'local'; // 'local' or 'azure'
    this.usersClient = null;
    this.countersClient = null;
    this.alertsClient = null;
    this.seriesSavesClient = null;
    this.localDataDir = path.join(__dirname, 'data');
    this.localUsersFile = path.join(this.localDataDir, 'users.json');
    this.localCountersFile = path.join(this.localDataDir, 'counters.json');
    this.localSeriesSavesFile = path.join(this.localDataDir, 'series_saves.json');

    this.initialize();
  }

  /**
   * Initialize database connection
   */
  async initialize() {
    if (this.mode === 'azure') {
      await this.initializeAzureTables();
    } else {
      this.initializeLocalStorage();
    }
  }

  /**
   * Initialize Azure Table Storage
   */
  async initializeAzureTables() {
    try {
      const accountName = process.env.AZURE_STORAGE_ACCOUNT;

      if (!accountName) {
        console.log('⚠️  Azure Storage account not configured, falling back to local storage');
        this.mode = 'local';
        this.initializeLocalStorage();
        return;
      }

      // Use managed identity for authentication
      // First try the specific user-assigned managed identity, then fallback to DefaultAzureCredential
      const credential = new ManagedIdentityCredential('b72c3d28-61d4-4c35-bac8-5e4928de2c7e');
      const serviceUrl = `https://${accountName}.table.core.windows.net`;

      this.usersClient = new TableClient(serviceUrl, 'users', credential);
      this.countersClient = new TableClient(serviceUrl, 'counters', credential);
      this.alertsClient = new TableClient(serviceUrl, 'alerts', credential);
      this.seriesSavesClient = new TableClient(serviceUrl, 'seriessaves', credential);

      // Create tables if they don't exist
      await this.usersClient.createTable();
      await this.countersClient.createTable();
      await this.alertsClient.createTable();
      await this.seriesSavesClient.createTable();

      console.log('✅ Connected to Azure Table Storage');
    } catch (error) {
      console.error('❌ Failed to initialize Azure Tables:', error);
      console.log('Falling back to local storage');
      this.mode = 'local';
      this.initializeLocalStorage();
    }
  }

  /**
   * Initialize local JSON file storage
   */
  initializeLocalStorage() {
    if (!fs.existsSync(this.localDataDir)) {
      fs.mkdirSync(this.localDataDir, { recursive: true });
    }
    if (!fs.existsSync(this.localUsersFile)) {
      fs.writeFileSync(this.localUsersFile, JSON.stringify({}), 'utf8');
    }
    if (!fs.existsSync(this.localCountersFile)) {
      fs.writeFileSync(this.localCountersFile, JSON.stringify({}), 'utf8');
    }
    // Initialize rewards file for channel point rewards
    const localRewardsFile = path.join(this.localDataDir, 'rewards.json');
    if (!fs.existsSync(localRewardsFile)) {
      fs.writeFileSync(localRewardsFile, JSON.stringify({}), 'utf8');
    }
    // Initialize series saves file
    if (!fs.existsSync(this.localSeriesSavesFile)) {
      fs.writeFileSync(this.localSeriesSavesFile, JSON.stringify({}), 'utf8');
    }
    console.log('✅ Using local JSON storage');
  }

  // ==================== USER OPERATIONS ====================

  /**
   * Get user by Twitch user ID
   */
  async getUser(twitchUserId) {
    // Validate input
    if (!twitchUserId || typeof twitchUserId !== 'string') {
      console.warn('⚠️ getUser called with invalid twitchUserId:', twitchUserId);
      return null;
    }

    if (this.mode === 'azure') {
      try {
        const entity = await this.usersClient.getEntity('user', twitchUserId);
        return entity;
      } catch (error) {
        if (error.statusCode === 404) return null;
        throw error;
      }
    } else {
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      return users[twitchUserId] || null;
    }
  }

  /**
   * Create or update user
   */
  async saveUser(userData) {
    // Get existing user data to preserve webhook URL and other settings
    const existingUser = await this.getUser(userData.twitchUserId);

    // Determine user role - riress is always admin
    let role = 'streamer';
    const safeUsername = userData.username ? userData.username.toString().toLowerCase() : '';
    if (safeUsername === 'riress') {
      role = 'admin';
    }

    // Default feature flags for new users
    const defaultFeatures = {
      chatCommands: true,
      channelPoints: false,
      autoClip: false,
      customCommands: false,
      analytics: false,
      webhooks: false,
      bitsIntegration: false,
      streamOverlay: false,
      alertAnimations: false,
      discordNotifications: true,
      discordWebhook: false,
      templateStyle: 'asylum_themed',
      streamAlerts: true
    };

    // Default overlay settings for new users
    const defaultOverlaySettings = {
      enabled: false,
      position: 'top-right',
      counters: {
        deaths: true,
        swears: true,
        bits: false
      },
      theme: {
        backgroundColor: 'rgba(0, 0, 0, 0.7)',
        borderColor: '#d4af37',
        textColor: 'white'
      },
      animations: {
        enabled: true,
        showAlerts: true,
        celebrationEffects: true
      }
    };

    const user = {
      partitionKey: 'user',
      rowKey: userData.twitchUserId,
      twitchUserId: userData.twitchUserId,
      username: userData.username,
      displayName: userData.displayName,
      email: userData.email || existingUser?.email || '',
      profileImageUrl: userData.profileImageUrl || existingUser?.profileImageUrl || '',
      accessToken: userData.accessToken,
      refreshToken: userData.refreshToken,
      tokenExpiry: userData.tokenExpiry,
      role: userData.role || existingUser?.role || role,
      features: JSON.stringify(userData.features || (existingUser?.features ? JSON.parse(existingUser.features) : defaultFeatures)),
      overlaySettings: JSON.stringify(userData.overlaySettings || (existingUser?.overlaySettings ? JSON.parse(existingUser.overlaySettings) : defaultOverlaySettings)),
      discordWebhookUrl: userData.discordWebhookUrl || existingUser?.discordWebhookUrl || '',
      discordInviteLink: userData.discordInviteLink || existingUser?.discordInviteLink || '',
      isActive: userData.isActive !== undefined ? userData.isActive : (existingUser?.isActive !== undefined ? existingUser.isActive : true),
      streamStatus: userData.streamStatus || existingUser?.streamStatus || 'offline', // 'offline' | 'prepping' | 'live' | 'ending'
      createdAt: userData.createdAt || existingUser?.createdAt || new Date().toISOString(),
      lastLogin: new Date().toISOString()
    };

    if (this.mode === 'azure') {
      await this.usersClient.upsertEntity(user, 'Replace');
    } else {
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      users[user.twitchUserId] = user;
      fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
    }

    return user;
  }

  /**
   * Delete user
   */
  async deleteUser(twitchUserId) {
    if (this.mode === 'azure') {
      await this.usersClient.deleteEntity('user', twitchUserId);
    } else {
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      delete users[twitchUserId];
      fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
    }
  }

  /**
   * Delete user by table keys (for broken records without valid twitchUserId)
   */
  async deleteUserByKeys(partitionKey, rowKey) {
    if (this.mode === 'azure') {
      await this.usersClient.deleteEntity(partitionKey, rowKey);
    } else {
      // In local mode, try to delete by rowKey as the user ID
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      delete users[rowKey];
      fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
    }
  }

  // ==================== COUNTER OPERATIONS ====================

  /**
   * Get counters for a specific user
   */
  async getCounters(twitchUserId) {
    if (this.mode === 'azure') {
      try {
        const entity = await this.countersClient.getEntity(twitchUserId, 'counters');
        return {
          deaths: entity.deaths || 0,
          swears: entity.swears || 0,
          screams: entity.screams || 0,
          bits: entity.bits || 0,
          lastUpdated: entity.lastUpdated,
          streamStarted: entity.streamStarted || null
        };
      } catch (error) {
        if (error.statusCode === 404) {
          return {
            deaths: 0,
            swears: 0,
            screams: 0,
            bits: 0,
            lastUpdated: new Date().toISOString(),
            streamStarted: null
          };
        }
        throw error;
      }
    } else {
      const counters = JSON.parse(fs.readFileSync(this.localCountersFile, 'utf8'));
      return counters[twitchUserId] || {
        deaths: 0,
        swears: 0,
        screams: 0,
        bits: 0,
        lastUpdated: new Date().toISOString(),
        streamStarted: null
      };
    }
  }

  /**
   * Save counters for a specific user
   */
  async saveCounters(twitchUserId, counterData) {
    const data = {
      partitionKey: twitchUserId,
      rowKey: 'counters',
      deaths: counterData.deaths || 0,
      swears: counterData.swears || 0,
      bits: counterData.bits || 0,
      streamStarted: counterData.streamStarted || null,
      lastUpdated: new Date().toISOString()
    };

    if (this.mode === 'azure') {
      await this.countersClient.upsertEntity(data, 'Replace');
    } else {
      const counters = JSON.parse(fs.readFileSync(this.localCountersFile, 'utf8'));
      counters[twitchUserId] = data;
      fs.writeFileSync(this.localCountersFile, JSON.stringify(counters, null, 2), 'utf8');
    }

    return data;
  }

  /**
   * Increment death counter
   */
  async incrementDeaths(twitchUserId) {
    const oldCounters = await this.getCounters(twitchUserId);
    const newCounters = { ...oldCounters, deaths: oldCounters.deaths + 1 };
    const saved = await this.saveCounters(twitchUserId, newCounters);

    return {
      ...saved,
      change: { deaths: 1, swears: 0, screams: 0 }
    };
  }

  /**
   * Decrement death counter
   */
  async decrementDeaths(twitchUserId) {
    const oldCounters = await this.getCounters(twitchUserId);
    const change = oldCounters.deaths > 0 ? -1 : 0;
    const newCounters = { ...oldCounters, deaths: Math.max(0, oldCounters.deaths - 1) };
    const saved = await this.saveCounters(twitchUserId, newCounters);

    return {
      ...saved,
      change: { deaths: change, swears: 0, screams: 0 }
    };
  }

  /**
   * Increment swear counter
   */
  async incrementSwears(twitchUserId) {
    const oldCounters = await this.getCounters(twitchUserId);
    const newCounters = { ...oldCounters, swears: oldCounters.swears + 1 };
    const saved = await this.saveCounters(twitchUserId, newCounters);

    return {
      ...saved,
      change: { deaths: 0, swears: 1, screams: 0 }
    };
  }

  /**
   * Decrement swear counter
   */
  async decrementSwears(twitchUserId) {
    const oldCounters = await this.getCounters(twitchUserId);
    const change = oldCounters.swears > 0 ? -1 : 0;
    const newCounters = { ...oldCounters, swears: Math.max(0, oldCounters.swears - 1) };
    const saved = await this.saveCounters(twitchUserId, newCounters);

    return {
      ...saved,
      change: { deaths: 0, swears: change, screams: 0 }
    };
  }

  /**
   * Increment scream counter
   */
  async incrementScreams(twitchUserId) {
    const oldCounters = await this.getCounters(twitchUserId);
    const newCounters = { ...oldCounters, screams: oldCounters.screams + 1 };
    const saved = await this.saveCounters(twitchUserId, newCounters);

    return {
      ...saved,
      change: { deaths: 0, swears: 0, screams: 1 }
    };
  }

  /**
   * Decrement scream counter
   */
  async decrementScreams(twitchUserId) {
    const oldCounters = await this.getCounters(twitchUserId);
    const change = oldCounters.screams > 0 ? -1 : 0;
    const newCounters = { ...oldCounters, screams: Math.max(0, oldCounters.screams - 1) };
    const saved = await this.saveCounters(twitchUserId, newCounters);

    return {
      ...saved,
      change: { deaths: 0, swears: 0, screams: change }
    };
  }

  /**
   * Reset all counters
   */
  async resetCounters(twitchUserId) {
    const oldCounters = await this.getCounters(twitchUserId);
    const saved = await this.saveCounters(twitchUserId, {
      deaths: 0,
      swears: 0,
      screams: 0,
      bits: oldCounters.bits, // Keep bits counter
      streamStarted: oldCounters.streamStarted
    });

    return {
      ...saved,
      change: { deaths: -oldCounters.deaths, swears: -oldCounters.swears, screams: -oldCounters.screams, bits: 0 }
    };
  }

  /**
   * Add bits to counter
   */
  async addBits(twitchUserId, amount) {
    const oldCounters = await this.getCounters(twitchUserId);
    const newCounters = {
      ...oldCounters,
      bits: oldCounters.bits + amount
    };
    const saved = await this.saveCounters(twitchUserId, newCounters);

    return {
      ...saved,
      change: { deaths: 0, swears: 0, screams: 0, bits: amount }
    };
  }

  /**
   * Start a new stream session (resets bits counter)
   */
  async startStream(twitchUserId) {
    const oldCounters = await this.getCounters(twitchUserId);
    const saved = await this.saveCounters(twitchUserId, {
      ...oldCounters,
      bits: 0,
      streamStarted: new Date().toISOString()
    });

    console.log(`🎬 Stream started for user ${twitchUserId} - bits counter reset`);

    return {
      ...saved,
      change: { deaths: 0, swears: 0, screams: 0, bits: -oldCounters.bits }
    };
  }

  /**
   * End stream session
   */
  async endStream(twitchUserId) {
    const oldCounters = await this.getCounters(twitchUserId);
    const saved = await this.saveCounters(twitchUserId, {
      ...oldCounters,
      streamStarted: null
    });

    console.log(`🎬 Stream ended for user ${twitchUserId}`);
    return saved;
  }

  // ==================== SERIES SAVE STATE OPERATIONS ====================

  /**
   * Save current counter state as a series save point
   * @param {string} twitchUserId - User's Twitch ID
   * @param {string} seriesName - Name of the series (e.g., "Elden Ring Episode 5")
   * @param {string} description - Optional description
   * @returns {Object} The saved series data
   */
  async saveSeries(twitchUserId, seriesName, description = '') {
    const currentCounters = await this.getCounters(twitchUserId);
    const seriesId = `${Date.now()}_${seriesName.replace(/[^a-zA-Z0-9]/g, '_')}`;

    const saveData = {
      partitionKey: twitchUserId,
      rowKey: seriesId,
      seriesName: seriesName,
      description: description,
      deaths: currentCounters.deaths,
      swears: currentCounters.swears,
      bits: currentCounters.bits,
      savedAt: new Date().toISOString()
    };

    if (this.mode === 'azure') {
      await this.seriesSavesClient.upsertEntity(saveData, 'Replace');
    } else {
      const saves = JSON.parse(fs.readFileSync(this.localSeriesSavesFile, 'utf8'));
      if (!saves[twitchUserId]) {
        saves[twitchUserId] = {};
      }
      saves[twitchUserId][seriesId] = saveData;
      fs.writeFileSync(this.localSeriesSavesFile, JSON.stringify(saves, null, 2), 'utf8');
    }

    console.log(`💾 Series save created for user ${twitchUserId}: "${seriesName}"`);
    return saveData;
  }

  /**
   * Load a series save state and restore counters
   * @param {string} twitchUserId - User's Twitch ID
   * @param {string} seriesId - The ID of the series save to load
   * @returns {Object} The loaded counter data
   */
  async loadSeries(twitchUserId, seriesId) {
    let saveData = null;

    if (this.mode === 'azure') {
      try {
        saveData = await this.seriesSavesClient.getEntity(twitchUserId, seriesId);
      } catch (error) {
        if (error.statusCode === 404) {
          throw new Error('Series save not found');
        }
        throw error;
      }
    } else {
      const saves = JSON.parse(fs.readFileSync(this.localSeriesSavesFile, 'utf8'));
      if (!saves[twitchUserId] || !saves[twitchUserId][seriesId]) {
        throw new Error('Series save not found');
      }
      saveData = saves[twitchUserId][seriesId];
    }

    // Restore the counters from the save
    const restoredCounters = {
      deaths: saveData.deaths || 0,
      swears: saveData.swears || 0,
      bits: saveData.bits || 0,
      streamStarted: null
    };

    await this.saveCounters(twitchUserId, restoredCounters);

    console.log(`📂 Series save loaded for user ${twitchUserId}: "${saveData.seriesName}"`);
    return {
      ...restoredCounters,
      seriesName: saveData.seriesName,
      description: saveData.description,
      savedAt: saveData.savedAt,
      lastUpdated: new Date().toISOString()
    };
  }

  /**
   * List all series saves for a user
   * @param {string} twitchUserId - User's Twitch ID
   * @returns {Array} List of series saves
   */
  async listSeriesSaves(twitchUserId) {
    if (this.mode === 'azure') {
      try {
        const entities = this.seriesSavesClient.listEntities({
          queryOptions: { filter: `PartitionKey eq '${twitchUserId}'` }
        });

        const saves = [];
        for await (const entity of entities) {
          saves.push({
            seriesId: entity.rowKey,
            seriesName: entity.seriesName,
            description: entity.description,
            deaths: entity.deaths,
            swears: entity.swears,
            bits: entity.bits,
            savedAt: entity.savedAt
          });
        }

        // Sort by savedAt descending (most recent first)
        saves.sort((a, b) => new Date(b.savedAt) - new Date(a.savedAt));
        return saves;
      } catch (error) {
        console.error('Error listing series saves:', error);
        return [];
      }
    } else {
      const saves = JSON.parse(fs.readFileSync(this.localSeriesSavesFile, 'utf8'));
      const userSaves = saves[twitchUserId] || {};

      const savesList = Object.keys(userSaves).map(seriesId => ({
        seriesId: seriesId,
        seriesName: userSaves[seriesId].seriesName,
        description: userSaves[seriesId].description,
        deaths: userSaves[seriesId].deaths,
        swears: userSaves[seriesId].swears,
        bits: userSaves[seriesId].bits,
        savedAt: userSaves[seriesId].savedAt
      }));

      // Sort by savedAt descending (most recent first)
      savesList.sort((a, b) => new Date(b.savedAt) - new Date(a.savedAt));
      return savesList;
    }
  }

  /**
   * Delete a series save
   * @param {string} twitchUserId - User's Twitch ID
   * @param {string} seriesId - The ID of the series save to delete
   */
  async deleteSeries(twitchUserId, seriesId) {
    if (this.mode === 'azure') {
      try {
        await this.seriesSavesClient.deleteEntity(twitchUserId, seriesId);
      } catch (error) {
        if (error.statusCode === 404) {
          throw new Error('Series save not found');
        }
        throw error;
      }
    } else {
      const saves = JSON.parse(fs.readFileSync(this.localSeriesSavesFile, 'utf8'));
      if (!saves[twitchUserId] || !saves[twitchUserId][seriesId]) {
        throw new Error('Series save not found');
      }
      delete saves[twitchUserId][seriesId];
      fs.writeFileSync(this.localSeriesSavesFile, JSON.stringify(saves, null, 2), 'utf8');
    }

    console.log(`🗑️  Series save deleted for user ${twitchUserId}: ${seriesId}`);
  }

  /**
   * Get current stream session if active
   */
  async getCurrentStreamSession(twitchUserId) {
    const counters = await this.getCounters(twitchUserId);
    if (counters.streamStarted) {
      return {
        startTime: counters.streamStarted,
        isActive: true,
        bits: counters.bits || 0,
        deaths: counters.deaths,
        swears: counters.swears
      };
    }
    return null;
  }

  /**
   * Get/Set stream settings for bits thresholds
   */
  async getStreamSettings(twitchUserId) {
    // For now, store in user features, but could be separate table
    const user = await this.getUser(twitchUserId);
    if (!user) return null;

    const features = typeof user.features === 'string' ? JSON.parse(user.features) : user.features;
    return features.streamSettings || {
      bitThresholds: {
        death: 100,    // bits needed to increment death counter
        swear: 50,     // bits needed to increment swear counter
        celebration: 10 // minimum bits for celebration effect
      },
      autoStartStream: false, // automatically detect stream start
      resetOnStreamStart: true // reset bits when stream starts
    };
  }

  async updateStreamSettings(twitchUserId, settings) {
    const user = await this.getUser(twitchUserId);
    if (!user) throw new Error('User not found');

    const features = typeof user.features === 'string' ? JSON.parse(user.features) : user.features;
    features.streamSettings = settings;

    user.features = JSON.stringify(features);
    return await this.saveUser(user);
  }

  // ==================== CHANNEL POINT REWARDS ====================

  /**
   * Get channel point reward configuration
   */
  async getChannelPointReward(userId, rewardId) {
    if (this.mode === 'azure') {
      try {
        const entity = await this.countersClient.getEntity(userId, `reward-${rewardId}`);
        return {
          userId: entity.partitionKey,
          rewardId: entity.rewardId,
          rewardTitle: entity.rewardTitle,
          cost: entity.cost,
          action: entity.action,
          isEnabled: entity.isEnabled,
          createdAt: entity.createdAt
        };
      } catch (error) {
        if (error.statusCode === 404) {
          return null;
        }
        throw error;
      }
    } else {
      const rewardsFile = path.join(this.localDataDir, 'rewards.json');
      if (!fs.existsSync(rewardsFile)) {
        return null;
      }
      const rewards = JSON.parse(fs.readFileSync(rewardsFile, 'utf8'));
      const key = `${userId}-${rewardId}`;
      return rewards[key] || null;
    }
  }

  /**
   * Save channel point reward configuration
   */
  async saveChannelPointReward(rewardConfig) {
    if (this.mode === 'azure') {
      const entity = {
        partitionKey: rewardConfig.userId,
        rowKey: `reward-${rewardConfig.rewardId}`,
        rewardId: rewardConfig.rewardId,
        rewardTitle: rewardConfig.rewardTitle,
        cost: rewardConfig.cost,
        action: rewardConfig.action,
        isEnabled: rewardConfig.isEnabled,
        createdAt: rewardConfig.createdAt
      };
      await this.countersClient.upsertEntity(entity);
      return entity;
    } else {
      const rewardsFile = path.join(this.localDataDir, 'rewards.json');
      let rewards = {};
      if (fs.existsSync(rewardsFile)) {
        rewards = JSON.parse(fs.readFileSync(rewardsFile, 'utf8'));
      }
      const key = `${rewardConfig.userId}-${rewardConfig.rewardId}`;
      rewards[key] = rewardConfig;
      fs.writeFileSync(rewardsFile, JSON.stringify(rewards, null, 2), 'utf8');
      return rewardConfig;
    }
  }

  /**
   * Delete channel point reward configuration
   */
  async deleteChannelPointReward(userId, rewardId) {
    if (this.mode === 'azure') {
      try {
        await this.countersClient.deleteEntity(userId, `reward-${rewardId}`);
        return true;
      } catch (error) {
        if (error.statusCode === 404) {
          return false;
        }
        throw error;
      }
    } else {
      const rewardsFile = path.join(this.localDataDir, 'rewards.json');
      if (!fs.existsSync(rewardsFile)) {
        return false;
      }
      const rewards = JSON.parse(fs.readFileSync(rewardsFile, 'utf8'));
      const key = `${userId}-${rewardId}`;
      if (rewards[key]) {
        delete rewards[key];
        fs.writeFileSync(rewardsFile, JSON.stringify(rewards, null, 2), 'utf8');
        return true;
      }
      return false;
    }
  }

  /**
   * Get all channel point rewards for a user
   */
  async getUserChannelPointRewards(userId) {
    if (this.mode === 'azure') {
      const rewards = [];
      const entities = this.countersClient.listEntities({
        queryOptions: {
          filter: `PartitionKey eq '${userId}' and RowKey ge 'reward-' and RowKey lt 'reward.'`
        }
      });
      for await (const entity of entities) {
        rewards.push({
          userId: entity.partitionKey,
          rewardId: entity.rewardId,
          rewardTitle: entity.rewardTitle,
          cost: entity.cost,
          action: entity.action,
          isEnabled: entity.isEnabled,
          createdAt: entity.createdAt
        });
      }
      return rewards;
    } else {
      const rewardsFile = path.join(this.localDataDir, 'rewards.json');
      if (!fs.existsSync(rewardsFile)) {
        return [];
      }
      const rewards = JSON.parse(fs.readFileSync(rewardsFile, 'utf8'));
      return Object.values(rewards).filter(reward => reward.userId === userId);
    }
  }

  // ==================== ADMIN OPERATIONS ====================

  /**
   * Get all users (admin only)
   */
  async getAllUsers() {
    if (this.mode === 'azure') {
      const users = [];
      const entities = this.usersClient.listEntities();
      for await (const entity of entities) {
        users.push(entity);
      }
      return users;
    } else {
      const usersData = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      return Object.values(usersData);
    }
  }

  /**
   * Update user features (admin only)
   */
  async updateUserFeatures(twitchUserId, features) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    // Update only the features field
    user.features = JSON.stringify(features);

    if (this.mode === 'azure') {
      // Ensure we have the required Azure Table Storage fields
      const entity = {
        partitionKey: user.partitionKey || 'user',
        rowKey: user.rowKey || twitchUserId,
        features: user.features
      };

      // Use Merge mode to update only the features field
      await this.usersClient.upsertEntity(entity, 'Merge');
    } else {
      // Update in local JSON file
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      if (users[twitchUserId]) {
        users[twitchUserId].features = JSON.stringify(features);
        fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
      } else {
        throw new Error('User not found in local storage');
      }
    }

    return user;
  }

  /**
   * Update user status (admin only)
   */
  async updateUserStatus(twitchUserId, isActive) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    // Update only the isActive field
    user.isActive = isActive;

    if (this.mode === 'azure') {
      await this.usersClient.upsertEntity(user, 'Merge');
    } else {
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      if (users[twitchUserId]) {
        users[twitchUserId].isActive = isActive;
        fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
      } else {
        throw new Error('User not found in local storage');
      }
    }

    return user;
  }

  /**
   * Update user stream status
   */
  async updateStreamStatus(twitchUserId, streamStatus) {
    const validStatuses = ['offline', 'prepping', 'live', 'ending'];
    if (!validStatuses.includes(streamStatus)) {
      throw new Error(`Invalid stream status. Must be one of: ${validStatuses.join(', ')}`);
    }

    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    // Update streamStatus and maintain isActive for backward compatibility
    user.streamStatus = streamStatus;
    // isActive is true when streaming (prepping, live) or false when offline/ending
    user.isActive = ['prepping', 'live'].includes(streamStatus);

    if (this.mode === 'azure') {
      await this.usersClient.upsertEntity(user, 'Merge');
    } else {
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      if (users[twitchUserId]) {
        users[twitchUserId].streamStatus = streamStatus;
        users[twitchUserId].isActive = user.isActive;
        fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
      } else {
        throw new Error('User not found in local storage');
      }
    }

    return user;
  }

  /**
   * Update user role (admin only)
   */
  async updateUserRole(twitchUserId, role) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    user.role = role;

    if (this.mode === 'azure') {
      await this.usersClient.upsertEntity(user, 'Merge');
    } else {
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      if (users[twitchUserId]) {
        users[twitchUserId].role = role;
        fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
      } else {
        throw new Error('User not found in local storage');
      }
    }

    return user;
  }

  /**
   * Get user features
   */
  async getUserFeatures(twitchUserId) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      return null;
    }

    try {
      return typeof user.features === 'string' ? JSON.parse(user.features) : user.features;
    } catch (error) {
      console.error('Error parsing user features:', error);
      return {
        chatCommands: true,
        channelPoints: false,
        autoClip: false,
        customCommands: false,
        analytics: false,
        webhooks: false
      };
    }
  }

  /**
   * Check if user has a specific feature enabled
   */
  async hasFeature(twitchUserId, featureName) {
    if (!twitchUserId) {
      return false;
    }
    const features = await this.getUserFeatures(twitchUserId);
    return features && features[featureName] === true;
  }

  /**
   * Get user overlay settings
   */
  async getUserOverlaySettings(twitchUserId) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      return null;
    }

    try {
      const settings = typeof user.overlaySettings === 'string' ? JSON.parse(user.overlaySettings) : user.overlaySettings;

      // Return default settings if not set or malformed
      if (!settings) {
        return {
          enabled: false,
          position: 'top-right',
          counters: {
            deaths: true,
            swears: true,
            screams: true,
            bits: false
          },
          theme: {
            backgroundColor: 'rgba(0, 0, 0, 0.7)',
            borderColor: '#d4af37',
            textColor: 'white'
          },
          animations: {
            enabled: true,
            showAlerts: true,
            celebrationEffects: true
          }
        };
      }

      return settings;
    } catch (error) {
      console.error('❌ Error parsing overlay settings:', error);
      // Return default settings on parse error
      return {
        enabled: false,
        position: 'top-right',
        counters: {
          deaths: true,
          swears: true,
          screams: true,
          bits: false
        },
        theme: {
          backgroundColor: 'rgba(0, 0, 0, 0.7)',
          borderColor: '#d4af37',
          textColor: 'white'
        },
        animations: {
          enabled: true,
          showAlerts: true,
          celebrationEffects: true
        }
      };
    }
  }

  /**
   * Update user overlay settings
   */
  async updateUserOverlaySettings(twitchUserId, overlaySettings) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    user.overlaySettings = JSON.stringify(overlaySettings);

    if (this.mode === 'azure') {
      await this.usersClient.upsertEntity(user, 'Merge');
    } else {
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      if (users[twitchUserId]) {
        users[twitchUserId].overlaySettings = JSON.stringify(overlaySettings);
        fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
      } else {
        throw new Error('User not found in local storage');
      }
    }

    return user;
  }

  /**
   * Update user's Discord webhook URL
   */
  async updateUserDiscordWebhook(twitchUserId, webhookUrl) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    console.log(`🔑 updateUserDiscordWebhook - User keys: partitionKey=${user.partitionKey}, rowKey=${user.rowKey}, twitchUserId=${user.twitchUserId}`);

    if (this.mode === 'azure') {
      // IMPORTANT: Use the actual partition/row keys from the user record, not the twitchUserId
      const actualPartitionKey = user.partitionKey || twitchUserId;
      const actualRowKey = user.rowKey || twitchUserId;

      const updateEntity = {
        partitionKey: actualPartitionKey,
        rowKey: actualRowKey,
        discordWebhookUrl: webhookUrl || ''
      };

      try {

        // Use Merge mode which will fail if entity doesn't exist
        await this.usersClient.updateEntity(updateEntity, 'Merge');
        console.log(`✅ Azure Table webhook update succeeded`);
      } catch (error) {
        console.error(`❌ updateUserDiscordWebhook - Azure error:`, error);
        console.error(`❌ Error details:`, {
          statusCode: error.statusCode,
          message: error.message,
          code: error.code
        });
        if (error.statusCode === 404) {
          throw new Error('User not found in storage');
        }
        throw error;
      }

      // Update local user object for return
      user.discordWebhookUrl = webhookUrl || '';
    } else {
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      if (users[twitchUserId]) {
        users[twitchUserId].discordWebhookUrl = webhookUrl || '';
        fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
      } else {
        throw new Error('User not found in local storage');
      }
      user.discordWebhookUrl = webhookUrl || '';
    }

    return user;
  }

  /**
   * Update user's Discord notification settings
   */
  async updateUserDiscordSettings(twitchUserId, settings) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    console.log(`🔔 updateUserDiscordSettings - Updating settings for user ${twitchUserId}`);

    if (this.mode === 'azure') {
      const actualPartitionKey = user.partitionKey || twitchUserId;
      const actualRowKey = user.rowKey || twitchUserId;

      const updateEntity = {
        partitionKey: actualPartitionKey,
        rowKey: actualRowKey,
        discordSettings: JSON.stringify(settings)
      };

      try {
        await this.usersClient.updateEntity(updateEntity, 'Merge');
      } catch (error) {
        console.error(`❌ updateUserDiscordSettings - Azure error:`, error);
        if (error.statusCode === 404) {
          throw new Error('User not found in storage');
        }
        throw error;
      }

      user.discordSettings = JSON.stringify(settings);
    } else {
      // Local mode implementation
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      if (users[twitchUserId]) {
        users[twitchUserId].discordSettings = JSON.stringify(settings);
        fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
      } else {
        throw new Error('User not found in local storage');
      }
      user.discordSettings = JSON.stringify(settings);
    }

    return user;
  }

  /**
   * Update user's Discord template style preference
   */
  async updateUserTemplateStyle(twitchUserId, templateStyle) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    console.log(`🎨 updateUserTemplateStyle - Updating template style for user ${twitchUserId} to ${templateStyle}`);

    if (this.mode === 'azure') {
      const actualPartitionKey = user.partitionKey || twitchUserId;
      const actualRowKey = user.rowKey || twitchUserId;

      const updateEntity = {
        partitionKey: actualPartitionKey,
        rowKey: actualRowKey,
        templateStyle: templateStyle
      };

      try {
        await this.usersClient.updateEntity(updateEntity, 'Merge');
      } catch (error) {
        console.error(`❌ updateUserTemplateStyle - Azure error:`, error);
        if (error.statusCode === 404) {
          throw new Error('User not found in storage');
        }
        throw error;
      }

      user.templateStyle = templateStyle;
    } else {
      // Local mode implementation
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      if (users[twitchUserId]) {
        users[twitchUserId].templateStyle = templateStyle;
        fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
      } else {
        throw new Error('User not found in local storage');
      }
      user.templateStyle = templateStyle;
    }

    return user;
  }

  /**
   * Get user's Discord webhook configuration
   */
  async getUserDiscordWebhook(twitchUserId) {
    // Validate input
    if (!twitchUserId || typeof twitchUserId !== 'string') {
      console.warn('⚠️ getUserDiscordWebhook called with invalid twitchUserId:', twitchUserId);
      return null;
    }

    const user = await this.getUser(twitchUserId);
    if (!user) {
      return null;
    }

    const result = {
      webhookUrl: user.discordWebhookUrl || '',
      enabled: !!(user.discordWebhookUrl) // Consider webhook enabled if URL exists
    };

    return result;
  }

  /**
   * Get user's Discord invite link
   */
  async getUserDiscordInviteLink(twitchUserId) {
    // Validate input
    if (!twitchUserId || typeof twitchUserId !== 'string') {
      console.warn('⚠️ getUserDiscordInviteLink called with invalid twitchUserId:', twitchUserId);
      return null;
    }

    const user = await this.getUser(twitchUserId);
    if (!user) {
      return null;
    }

    return user.discordInviteLink || '';
  }

  /**
   * Update user's Discord invite link
   */
  async updateUserDiscordInviteLink(twitchUserId, inviteLink) {
    // Validate input
    if (!twitchUserId || typeof twitchUserId !== 'string') {
      throw new Error('Invalid twitchUserId provided');
    }

    // Validate Discord invite link format
    if (inviteLink && !this.isValidDiscordInvite(inviteLink)) {
      throw new Error('Invalid Discord invite link format');
    }

    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    // Update the user's Discord invite link
    user.discordInviteLink = inviteLink || '';

    if (this.mode === 'azure') {
      await this.usersClient.upsertEntity(user, 'Replace');
    } else {
      const users = JSON.parse(fs.readFileSync(this.localUsersFile, 'utf8'));
      users[twitchUserId] = user;
      fs.writeFileSync(this.localUsersFile, JSON.stringify(users, null, 2), 'utf8');
    }

    console.log(`✅ Updated Discord invite link for user ${twitchUserId}: ${inviteLink ? 'Set' : 'Removed'}`);
    return true;
  }

  /**
   * Validate Discord invite link format
   */
  isValidDiscordInvite(inviteLink) {
    if (!inviteLink) return false;

    // Discord invite patterns
    const patterns = [
      /^https?:\/\/discord\.gg\/[a-zA-Z0-9]+$/,
      /^https?:\/\/discord\.com\/invite\/[a-zA-Z0-9]+$/,
      /^https?:\/\/discordapp\.com\/invite\/[a-zA-Z0-9]+$/
    ];

    return patterns.some(pattern => pattern.test(inviteLink));
  }

  /**
   * Get user's notification settings
   */
  async getUserNotificationSettings(twitchUserId) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      return null;
    }

    // Parse Discord settings from user object, with defaults
    let settings = {
      enableDiscordNotifications: false,
      enableChannelNotifications: false,
      deathMilestoneEnabled: false,
      swearMilestoneEnabled: false,
      deathThresholds: '10,25,50,100,250,500,1000',
      swearThresholds: '25,50,100,250,500,1000,2500'
    };

    if (user.discordSettings) {
      try {
        const parsedSettings = JSON.parse(user.discordSettings);
        settings = { ...settings, ...parsedSettings };
      } catch (error) {
        console.warn(`⚠️ Could not parse Discord settings for user ${twitchUserId}:`, error.message);
      }
    }

    return settings;
  }

  // ==================== ALERT MANAGEMENT ====================

  /**
   * Get default alert templates for Omni's asylum theme
   * Enhanced with advanced visual effects: CSS keyframes, SVG masks, canvas filters
   */
  getDefaultAlertTemplates() {
    return [
      {
        id: 'follow',
        type: 'follow',
        name: 'New Follower',
        visualCue: 'A door creaks open slowly',
        sound: 'door-creak',
        soundDescription: 'Heavy door creaking open',
        textPrompt: '🚪 A new patient has arrived… [User]',
        duration: 5000,
        backgroundColor: '#1a0d0d',
        textColor: '#ff6b6b',
        borderColor: '#8b1538',
        isDefault: true,
        effects: {
          animation: 'doorCreak',
          svgMask: 'fog',
          particle: 'dust',
          screenShake: true,
          soundTrigger: 'doorCreak.mp3'
        }
      },
      {
        id: 'subscription',
        type: 'subscription',
        name: 'New Subscriber',
        visualCue: 'Restraints snap shut',
        sound: 'electroshock-buzz',
        soundDescription: 'Electroshock buzz or echoing scream',
        textPrompt: '⚡ They\'ve committed for the long stay. [User] - Tier [Tier]',
        duration: 6000,
        backgroundColor: '#1a0d1a',
        textColor: '#9147ff',
        borderColor: '#6441a5',
        isDefault: true,
        effects: {
          animation: 'electricPulse',
          svgMask: 'glassDistortion',
          particle: 'sparks',
          screenFlicker: true,
          glowIntensity: 'high',
          soundTrigger: 'electroshock.mp3'
        }
      },
      {
        id: 'resub',
        type: 'resub',
        name: 'Resubscription',
        visualCue: 'A file slams shut on a desk',
        sound: 'typewriter-ding',
        soundDescription: 'Pen scribble + typewriter ding',
        textPrompt: '📋 Case file reopened: [User] returns. [Months] months confined.',
        duration: 5500,
        backgroundColor: '#0d1a0d',
        textColor: '#00ff88',
        borderColor: '#1db954',
        isDefault: true,
        effects: {
          animation: 'typewriter',
          svgMask: 'paperTexture',
          particle: 'ink',
          textScramble: true,
          soundTrigger: 'typewriter.mp3'
        }
      },
      {
        id: 'bits',
        type: 'bits',
        name: 'Bits Donation',
        visualCue: 'Pills scatter across the floor',
        sound: 'pill-rattle',
        soundDescription: 'Pill bottle rattle + distorted laugh',
        textPrompt: '💊 [User] offers their dosage: [Bits] bits',
        duration: 4500,
        backgroundColor: '#1a1a0d',
        textColor: '#ffd700',
        borderColor: '#ffcc00',
        isDefault: true,
        effects: {
          animation: 'pillScatter',
          svgMask: 'none',
          particle: 'pills',
          bounce: true,
          colorShift: true,
          soundTrigger: 'pillRattle.mp3'
        }
      },
      {
        id: 'raid',
        type: 'raid',
        name: 'Raid Alert',
        visualCue: 'Sirens blare, lights flicker red',
        sound: 'asylum-alarm',
        soundDescription: 'Alarm + overlapping voices',
        textPrompt: '🚨 THE WARD IS BREACHED — [X] INTRUDERS!',
        duration: 7000,
        backgroundColor: '#1a0000',
        textColor: '#ff0000',
        borderColor: '#cc0000',
        isDefault: true,
        effects: {
          animation: 'sirenFlash',
          svgMask: 'none',
          particle: 'chaos',
          screenShake: true,
          screenFlicker: true,
          redAlert: true,
          soundTrigger: 'alarm.mp3'
        }
      },
      {
        id: 'giftsub',
        type: 'giftsub',
        name: 'Gifted Subscription',
        visualCue: 'A nurse silhouette appears behind glass',
        sound: 'heart-monitor-flatline',
        soundDescription: 'Heart monitor flatline',
        textPrompt: '💉 [User] sedates another soul.',
        duration: 5500,
        backgroundColor: '#0d0d1a',
        textColor: '#88ddff',
        borderColor: '#0099cc',
        isDefault: true,
        effects: {
          animation: 'heartbeatPulse',
          svgMask: 'glassDistortion',
          particle: 'heartbeats',
          silhouette: true,
          heartbeatLine: true,
          soundTrigger: 'heartMonitor.mp3'
        }
      },
      {
        id: 'hypetrain',
        type: 'hypetrain',
        name: 'Hype Train',
        visualCue: 'Wheelchair rolls down a hallway',
        sound: 'train-screech-heartbeat',
        soundDescription: 'Rising heartbeat + train screech',
        textPrompt: '🎢 THE ASYLUM STIRS… THE FRENZY BEGINS!',
        duration: 8000,
        backgroundColor: '#1a1a1a',
        textColor: '#ffffff',
        borderColor: '#ff6600',
        isDefault: true,
        effects: {
          animation: 'wheelchairRoll',
          svgMask: 'hallwayPerspective',
          particle: 'smoke',
          screenShake: true,
          crescendo: true,
          soundTrigger: 'hypeTrain.mp3'
        }
      }
    ];
  }

  /**
   * Get user's custom alerts
   */
  async getUserAlerts(twitchUserId) {
    if (this.mode === 'azure') {
      try {
        const alerts = [];
        const entities = this.alertsClient.listEntities({
          queryOptions: { filter: `PartitionKey eq '${twitchUserId}'` }
        });

        for await (const entity of entities) {
          alerts.push({
            id: entity.rowKey,
            userId: entity.PartitionKey,
            type: entity.type,
            name: entity.name,
            visualCue: entity.visualCue,
            sound: entity.sound,
            soundDescription: entity.soundDescription || '',
            textPrompt: entity.textPrompt,
            duration: entity.duration || 4000,
            backgroundColor: entity.backgroundColor || '#1a0d0d',
            textColor: entity.textColor || '#ffffff',
            borderColor: entity.borderColor || '#666666',
            isEnabled: entity.isEnabled !== false,
            isDefault: entity.isDefault === true,
            createdAt: entity.createdAt,
            updatedAt: entity.updatedAt
          });
        }

        return alerts;
      } catch (error) {
        console.error('Error getting user alerts from Azure:', error);
        return [];
      }
    } else {
      // Local storage implementation
      const alertsFile = path.join(this.localDataDir, 'alerts.json');

      if (!fs.existsSync(alertsFile)) {
        fs.writeFileSync(alertsFile, '{}', 'utf8');
        return [];
      }

      try {
        const allAlerts = JSON.parse(fs.readFileSync(alertsFile, 'utf8'));
        return Object.values(allAlerts[twitchUserId] || {});
      } catch (error) {
        console.error('Error reading local alerts:', error);
        return [];
      }
    }
  }

  /**
   * Save/update a custom alert
   */
  async saveAlert(alertConfig) {
    const alertId = alertConfig.id || `alert_${Date.now()}`;

    if (this.mode === 'azure') {
      const entity = {
        partitionKey: alertConfig.userId,
        rowKey: alertId,
        type: alertConfig.type,
        name: alertConfig.name,
        visualCue: alertConfig.visualCue || '',
        sound: alertConfig.sound || '',
        soundDescription: alertConfig.soundDescription || '',
        textPrompt: alertConfig.textPrompt,
        duration: alertConfig.duration || 4000,
        backgroundColor: alertConfig.backgroundColor || '#1a0d0d',
        textColor: alertConfig.textColor || '#ffffff',
        borderColor: alertConfig.borderColor || '#666666',
        isEnabled: alertConfig.isEnabled !== false,
        isDefault: alertConfig.isDefault === true,
        createdAt: alertConfig.createdAt || new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };

      await this.alertsClient.upsertEntity(entity, 'Merge');
    } else {
      // Local storage implementation
      const alertsFile = path.join(this.localDataDir, 'alerts.json');

      let allAlerts = {};
      if (fs.existsSync(alertsFile)) {
        try {
          allAlerts = JSON.parse(fs.readFileSync(alertsFile, 'utf8'));
        } catch (error) {
          console.error('Error reading alerts file:', error);
          allAlerts = {};
        }
      }

      if (!allAlerts[alertConfig.userId]) {
        allAlerts[alertConfig.userId] = {};
      }

      allAlerts[alertConfig.userId][alertId] = {
        id: alertId,
        userId: alertConfig.userId,
        type: alertConfig.type,
        name: alertConfig.name,
        visualCue: alertConfig.visualCue || '',
        sound: alertConfig.sound || '',
        soundDescription: alertConfig.soundDescription || '',
        textPrompt: alertConfig.textPrompt,
        duration: alertConfig.duration || 4000,
        backgroundColor: alertConfig.backgroundColor || '#1a0d0d',
        textColor: alertConfig.textColor || '#ffffff',
        borderColor: alertConfig.borderColor || '#666666',
        isEnabled: alertConfig.isEnabled !== false,
        isDefault: alertConfig.isDefault === true,
        createdAt: alertConfig.createdAt || new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };

      fs.writeFileSync(alertsFile, JSON.stringify(allAlerts, null, 2), 'utf8');
    }

    return alertId;
  }

  /**
   * Delete a custom alert
   */
  async deleteAlert(userId, alertId) {
    if (this.mode === 'azure') {
      await this.alertsClient.deleteEntity(userId, alertId);
    } else {
      const alertsFile = path.join(this.localDataDir, 'alerts.json');

      if (!fs.existsSync(alertsFile)) {
        return;
      }

      try {
        const allAlerts = JSON.parse(fs.readFileSync(alertsFile, 'utf8'));
        if (allAlerts[userId] && allAlerts[userId][alertId]) {
          delete allAlerts[userId][alertId];
          fs.writeFileSync(alertsFile, JSON.stringify(allAlerts, null, 2), 'utf8');
        }
      } catch (error) {
        console.error('Error deleting alert:', error);
      }
    }
  }

  /**
   * Initialize default alerts for a user
   */
  async initializeUserAlerts(twitchUserId) {
    const existingAlerts = await this.getUserAlerts(twitchUserId);

    // Only add default alerts if user has none
    if (existingAlerts.length === 0) {
      const defaultTemplates = this.getDefaultAlertTemplates();

      for (const template of defaultTemplates) {
        await this.saveAlert({
          ...template,
          userId: twitchUserId,
          id: `${twitchUserId}_${template.id}`
        });
      }

      console.log(`✅ Initialized ${defaultTemplates.length} default alerts for user ${twitchUserId}`);
    }

    // Initialize default event mappings
    await this.initializeEventMappings(twitchUserId);
  }

  /**
   * Get alert configuration for a specific event type
   */
  async getAlertForEventType(userId, eventType) {
    const alerts = await this.getUserAlerts(userId);
    return alerts.find(alert => alert.type === eventType && alert.isEnabled) || null;
  }

  // ==================== EVENT-TO-ALERT MAPPING ====================

  /**
   * Get all available EventSub events (both enabled and disabled)
   */
  getAllAvailableEvents() {
    return [
      'channel.follow',
      'channel.subscribe',
      'channel.subscription.gift',
      'channel.subscription.message',
      'channel.bits.use'
    ];
  }

  /**
   * Get available alert types (including "none" for disabling)
   */
  getAvailableAlertTypes() {
    return [
      'none',        // Special value to disable alerts
      'follow',
      'subscription',
      'giftsub',
      'resub',
      'bits'
    ];
  }

  /**
   * Get default event-to-alert mappings for EventSub events
   */
  getDefaultEventMappings() {
    return {
      'channel.follow': 'follow',
      'channel.subscribe': 'subscription',
      'channel.subscription.gift': 'giftsub',
      'channel.subscription.message': 'resub',
      'channel.bits.use': 'bits'
    };
  }

  /**
   * Initialize default event mappings for a user
   */
  async initializeEventMappings(twitchUserId) {
    const existingMappings = await this.getEventMappings(twitchUserId);

    // Only initialize if no mappings exist
    if (!existingMappings || Object.keys(existingMappings).length === 0) {
      const defaultMappings = this.getDefaultEventMappings();
      await this.saveEventMappings(twitchUserId, defaultMappings);
    }
  }

  /**
   * Get event-to-alert mappings for a user
   */
  async getEventMappings(twitchUserId) {
    let mappings;

    if (this.mode === 'azure') {
      try {
        const entity = await this.usersClient.getEntity(twitchUserId, 'event-mappings');
        mappings = JSON.parse(entity.mappings || '{}');
      } catch (error) {
        if (error.statusCode === 404) {
          mappings = {};
        } else {
          throw error;
        }
      }
    } else {
      const mappingsFile = path.join(this.localDataDir, 'event-mappings.json');

      if (!fs.existsSync(mappingsFile)) {
        mappings = {};
      } else {
        try {
          const allMappings = JSON.parse(fs.readFileSync(mappingsFile, 'utf8'));
          mappings = allMappings[twitchUserId] || {};
        } catch (error) {
          console.error('Error reading event mappings:', error);
          mappings = {};
        }
      }
    }

    return mappings;
  }

  /**
   * Save event-to-alert mappings for a user
   */
  async saveEventMappings(twitchUserId, mappings) {
    if (this.mode === 'azure') {
      const entity = {
        partitionKey: twitchUserId,
        rowKey: 'event-mappings',
        mappings: JSON.stringify(mappings),
        updatedAt: new Date().toISOString()
      };

      await this.usersClient.upsertEntity(entity, 'Merge');
    } else {
      const mappingsFile = path.join(this.localDataDir, 'event-mappings.json');

      let allMappings = {};
      if (fs.existsSync(mappingsFile)) {
        try {
          allMappings = JSON.parse(fs.readFileSync(mappingsFile, 'utf8'));
        } catch (error) {
          console.error('Error reading event mappings file:', error);
          allMappings = {};
        }
      }

      allMappings[twitchUserId] = mappings;
      fs.writeFileSync(mappingsFile, JSON.stringify(allMappings, null, 2), 'utf8');
    }
  }

  /**
   * Migrate all users from channel.cheer to channel.bits.use
   * This is a one-time migration function
   */
  async migrateAllCheerToBitsUse() {
    console.log('🔄 Starting bulk migration from channel.cheer to channel.bits.use...');
    let migratedCount = 0;

    if (this.mode === 'azure') {
      // Azure Table Storage migration
      try {
        const entities = this.usersClient.listEntities({
          queryOptions: { filter: "PartitionKey ne ''" }
        });

        for await (const entity of entities) {
          const mappings = JSON.parse(entity.mappings || '{}');
          if (mappings['channel.cheer'] && !mappings['channel.bits.use']) {
            mappings['channel.bits.use'] = mappings['channel.cheer'];
            delete mappings['channel.cheer'];

            await this.usersClient.updateEntity({
              partitionKey: entity.partitionKey,
              rowKey: entity.rowKey,
              mappings: JSON.stringify(mappings)
            });

            migratedCount++;
            console.log(`✅ Migrated user ${entity.partitionKey}`);
          }
        }
      } catch (error) {
        console.error('Error during Azure migration:', error);
      }
    } else {
      // Local JSON migration
      const mappingsFile = path.join(this.localDataDir, 'event-mappings.json');
      if (fs.existsSync(mappingsFile)) {
        try {
          const allMappings = JSON.parse(fs.readFileSync(mappingsFile, 'utf8'));

          for (const [userId, mappings] of Object.entries(allMappings)) {
            if (mappings['channel.cheer'] && !mappings['channel.bits.use']) {
              mappings['channel.bits.use'] = mappings['channel.cheer'];
              delete mappings['channel.cheer'];
              migratedCount++;
              console.log(`✅ Migrated user ${userId}`);
            }
          }

          if (migratedCount > 0) {
            fs.writeFileSync(mappingsFile, JSON.stringify(allMappings, null, 2), 'utf8');
          }
        } catch (error) {
          console.error('Error during local migration:', error);
        }
      }
    }

    console.log(`🎉 Migration completed! Migrated ${migratedCount} users from channel.cheer to channel.bits.use`);
    return migratedCount;
  }

  /**
   * Get alert configuration for a specific EventSub event
   */
  async getAlertForEvent(twitchUserId, eventType) {
    const mappings = await this.getEventMappings(twitchUserId);
    const alertType = mappings[eventType];

    if (!alertType) {
      console.log(`No alert mapping for event type: ${eventType}`);
      return null;
    }

    // Check if alerts are disabled for this event
    if (alertType === 'none') {
      console.log(`Alerts disabled for event type: ${eventType}`);
      return null;
    }

    const alerts = await this.getUserAlerts(twitchUserId);
    const alert = alerts.find(alert => alert.type === alertType && alert.isEnabled);

    if (!alert) {
      console.log(`No enabled alert found for type: ${alertType}`);
    }

    return alert || null;
  }

  // ==================== TEMPLATE MANAGEMENT ====================

  /**
   * Get available template definitions
   */
  getAvailableTemplates() {
    return {
      asylum_themed: {
        name: 'Asylum Themed',
        description: 'Dark horror-themed template with blood effects and creepy animations',
        type: 'built-in',
        config: {
          colors: {
            primary: '#8B0000',
            secondary: '#DC143C',
            background: '#1a0000',
            text: '#FFFFFF',
            accent: '#FF6B6B'
          },
          fonts: {
            primary: 'Creepster, cursive',
            secondary: 'Arial, sans-serif'
          },
          animations: {
            bloodDrip: true,
            screenshake: true,
            fadeEffects: true,
            particleEffects: true
          },
          sounds: {
            death: 'scream.mp3',
            swear: 'bleep.mp3',
            milestone: 'creepy_bell.mp3'
          }
        }
      },
      modern_minimal: {
        name: 'Modern Minimal',
        description: 'Clean, modern design with smooth animations',
        type: 'built-in',
        config: {
          colors: {
            primary: '#6366f1',
            secondary: '#8b5cf6',
            background: '#ffffff',
            text: '#1f2937',
            accent: '#3b82f6'
          },
          fonts: {
            primary: 'Inter, sans-serif',
            secondary: 'SF Pro Display, sans-serif'
          },
          animations: {
            slideIn: true,
            fadeEffects: true,
            bounceOnUpdate: true,
            glassmorphism: true
          },
          sounds: {
            death: 'notification.mp3',
            swear: 'pop.mp3',
            milestone: 'achievement.mp3'
          }
        }
      },
      streamer_pro: {
        name: 'Streamer Pro',
        description: 'Professional streaming template with customizable colors',
        type: 'built-in',
        config: {
          colors: {
            primary: '#9146ff',
            secondary: '#772ce8',
            background: 'rgba(0, 0, 0, 0.8)',
            text: '#ffffff',
            accent: '#00f5ff'
          },
          fonts: {
            primary: 'Roboto, sans-serif',
            secondary: 'Open Sans, sans-serif'
          },
          animations: {
            slideIn: true,
            typewriter: true,
            neonGlow: true,
            particleTrails: true
          },
          sounds: {
            death: 'game_over.mp3',
            swear: 'censored.mp3',
            milestone: 'level_up.mp3'
          }
        }
      }
    };
  }

  /**
   * Get user's template configuration
   */
  async getUserTemplate(twitchUserId) {
    try {
      const user = await this.getUser(twitchUserId);
      if (!user) return null;

      const features = typeof user.features === 'string' ? JSON.parse(user.features) : user.features || {};
      const templateStyle = features.templateStyle || 'asylum_themed';

      // Check if user has a custom template
      const customTemplate = await this.getUserCustomTemplate(twitchUserId);
      if (customTemplate && templateStyle === 'custom') {
        return {
          type: 'custom',
          name: customTemplate.name,
          config: customTemplate.config,
          templateStyle: 'custom'
        };
      }

      // Return built-in template
      const availableTemplates = this.getAvailableTemplates();
      const template = availableTemplates[templateStyle];

      if (!template) {
        console.log(`⚠️ Template ${templateStyle} not found, falling back to asylum_themed`);
        return availableTemplates.asylum_themed;
      }

      return {
        ...template,
        templateStyle
      };
    } catch (error) {
      console.error('❌ Error getting user template:', error);
      return this.getAvailableTemplates().asylum_themed;
    }
  }

  /**
   * Get user's custom template (if any)
   */
  async getUserCustomTemplate(twitchUserId) {
    try {
      if (this.dbMode === 'azure') {
        const entity = await this.tableClient.getEntity(twitchUserId, 'customTemplate');
        return JSON.parse(entity.templateConfig);
      } else {
        const customTemplatesFile = path.join(this.dataDir, 'customTemplates.json');
        if (!fs.existsSync(customTemplatesFile)) {
          return null;
        }
        const templates = JSON.parse(fs.readFileSync(customTemplatesFile, 'utf8'));
        return templates[twitchUserId] || null;
      }
    } catch (error) {
      if (error.statusCode !== 404) {
        console.error('❌ Error getting custom template:', error);
      }
      return null;
    }
  }

  /**
   * Save user's custom template
   */
  async saveUserCustomTemplate(twitchUserId, templateData) {
    try {
      if (this.dbMode === 'azure') {
        await this.tableClient.upsertEntity({
          partitionKey: twitchUserId,
          rowKey: 'customTemplate',
          templateConfig: JSON.stringify(templateData),
          lastUpdated: new Date().toISOString()
        });
      } else {
        const customTemplatesFile = path.join(this.dataDir, 'customTemplates.json');
        let templates = {};

        if (fs.existsSync(customTemplatesFile)) {
          templates = JSON.parse(fs.readFileSync(customTemplatesFile, 'utf8'));
        }

        templates[twitchUserId] = {
          ...templateData,
          lastUpdated: new Date().toISOString()
        };

        fs.writeFileSync(customTemplatesFile, JSON.stringify(templates, null, 2));
      }

      console.log(`✅ Custom template saved for user ${twitchUserId}`);
      return true;
    } catch (error) {
      console.error('❌ Error saving custom template:', error);
      throw error;
    }
  }

  // ==================== CUSTOM COUNTERS MANAGEMENT ====================

  /**
   * Get user's custom counters
   */
  async getUserCustomCounters(twitchUserId) {
    try {
      if (this.dbMode === 'azure') {
        const entity = await this.tableClient.getEntity(twitchUserId, 'customCounters');
        return JSON.parse(entity.countersConfig);
      } else {
        const customCountersFile = path.join(this.dataDir, 'customCounters.json');
        if (!fs.existsSync(customCountersFile)) {
          return {};
        }
        const counters = JSON.parse(fs.readFileSync(customCountersFile, 'utf8'));
        return counters[twitchUserId] || {};
      }
    } catch (error) {
      if (error.statusCode !== 404) {
        console.error('❌ Error getting custom counters:', error);
      }
      return {};
    }
  }

  /**
   * Save user's custom counters configuration
   */
  async saveUserCustomCounters(twitchUserId, countersConfig) {
    try {
      if (this.dbMode === 'azure') {
        await this.tableClient.upsertEntity({
          partitionKey: twitchUserId,
          rowKey: 'customCounters',
          countersConfig: JSON.stringify(countersConfig),
          lastUpdated: new Date().toISOString()
        });
      } else {
        const customCountersFile = path.join(this.dataDir, 'customCounters.json');
        let counters = {};

        if (fs.existsSync(customCountersFile)) {
          counters = JSON.parse(fs.readFileSync(customCountersFile, 'utf8'));
        }

        counters[twitchUserId] = {
          ...countersConfig,
          lastUpdated: new Date().toISOString()
        };

        fs.writeFileSync(customCountersFile, JSON.stringify(counters, null, 2));
      }

      console.log(`✅ Custom counters saved for user ${twitchUserId}`);
      return true;
    } catch (error) {
      console.error('❌ Error saving custom counters:', error);
      throw error;
    }
  }

  // ==================== CUSTOM CHAT COMMANDS ====================

  /**
   * Get user's custom chat commands
   */
  async getUserChatCommands(twitchUserId) {
    try {
      if (this.dbMode === 'azure') {
        const entity = await this.tableClient.getEntity(twitchUserId, 'chatCommands');
        return JSON.parse(entity.commandsConfig);
      } else {
        const chatCommandsFile = path.join(this.dataDir, 'chatCommands.json');
        if (!fs.existsSync(chatCommandsFile)) {
          return this.getDefaultChatCommands();
        }
        const commands = JSON.parse(fs.readFileSync(chatCommandsFile, 'utf8'));
        return commands[twitchUserId] || this.getDefaultChatCommands();
      }
    } catch (error) {
      if (error.statusCode !== 404) {
        console.error('❌ Error getting chat commands:', error);
      }
      return this.getDefaultChatCommands();
    }
  }

  /**
   * Get default chat commands
   */
  getDefaultChatCommands() {
    return {
      // Public commands
      '!deaths': {
        response: 'Current death count: {{deaths}}',
        permission: 'everyone',
        cooldown: 5,
        enabled: true
      },
      '!swears': {
        response: 'Current swear count: {{swears}}',
        permission: 'everyone',
        cooldown: 5,
        enabled: true
      },
      '!screams': {
        response: 'Current scream count: {{screams}}',
        permission: 'everyone',
        cooldown: 5,
        enabled: true
      },
      '!stats': {
        response: 'Deaths: {{deaths}}, Swears: {{swears}}, Screams: {{screams}}, Bits: {{bits}}',
        permission: 'everyone',
        cooldown: 10,
        enabled: true
      },

      // Moderator commands
      '!death+': {
        action: 'increment',
        counter: 'deaths',
        permission: 'moderator',
        cooldown: 1,
        enabled: true
      },
      '!death-': {
        action: 'decrement',
        counter: 'deaths',
        permission: 'moderator',
        cooldown: 1,
        enabled: true
      },
      '!swear+': {
        action: 'increment',
        counter: 'swears',
        permission: 'moderator',
        cooldown: 1,
        enabled: true
      },
      '!swear-': {
        action: 'decrement',
        counter: 'swears',
        permission: 'moderator',
        cooldown: 1,
        enabled: true
      },
      '!scream+': {
        action: 'increment',
        counter: 'screams',
        permission: 'moderator',
        cooldown: 1,
        enabled: true
      },
      '!scream-': {
        action: 'decrement',
        counter: 'screams',
        permission: 'moderator',
        cooldown: 1,
        enabled: true
      },
      '!resetcounters': {
        action: 'reset',
        permission: 'moderator',
        cooldown: 30,
        enabled: true
      }
    };
  }

  /**
   * Save user's custom chat commands
   */
  async saveUserChatCommands(twitchUserId, commandsConfig) {
    try {
      if (this.dbMode === 'azure') {
        await this.tableClient.upsertEntity({
          partitionKey: twitchUserId,
          rowKey: 'chatCommands',
          commandsConfig: JSON.stringify(commandsConfig),
          lastUpdated: new Date().toISOString()
        });
      } else {
        const chatCommandsFile = path.join(this.dataDir, 'chatCommands.json');
        let commands = {};

        if (fs.existsSync(chatCommandsFile)) {
          commands = JSON.parse(fs.readFileSync(chatCommandsFile, 'utf8'));
        }

        commands[twitchUserId] = {
          ...commandsConfig,
          lastUpdated: new Date().toISOString()
        };

        fs.writeFileSync(chatCommandsFile, JSON.stringify(commands, null, 2));
      }

      console.log(`✅ Chat commands saved for user ${twitchUserId}`);
      return true;
    } catch (error) {
      console.error('❌ Error saving chat commands:', error);
      throw error;
    }
  }
}

// Export singleton instance
module.exports = new Database();

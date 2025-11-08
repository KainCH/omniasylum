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
    if (this.mode === 'azure') {
      try {
        const entity = await this.usersClient.getEntity('user', twitchUserId);

        // Debug logging to see the actual Azure Table Storage entity structure
        console.log(`🔍 Azure entity for user ${twitchUserId}:`, {
          keys: Object.keys(entity),
          discordWebhookUrl: entity.discordWebhookUrl,
          discordWebhookUrlType: typeof entity.discordWebhookUrl,
          discordWebhookUrlValue: entity.discordWebhookUrl ? `${entity.discordWebhookUrl.toString().substring(0, 50)}...` : 'EMPTY_OR_UNDEFINED'
        });

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
      discordNotifications: true
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
      email: userData.email || '',
      profileImageUrl: userData.profileImageUrl || '',
      accessToken: userData.accessToken,
      refreshToken: userData.refreshToken,
      tokenExpiry: userData.tokenExpiry,
      role: userData.role || role,
      features: JSON.stringify(userData.features || defaultFeatures),
      overlaySettings: JSON.stringify(userData.overlaySettings || defaultOverlaySettings),
      discordWebhookUrl: userData.discordWebhookUrl || '',
      isActive: userData.isActive !== undefined ? userData.isActive : true,
      streamStatus: userData.streamStatus || 'offline', // 'offline' | 'prepping' | 'live' | 'ending'
      createdAt: userData.createdAt || new Date().toISOString(),
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
          bits: entity.bits || 0,
          lastUpdated: entity.lastUpdated,
          streamStarted: entity.streamStarted || null
        };
      } catch (error) {
        if (error.statusCode === 404) {
          return {
            deaths: 0,
            swears: 0,
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
      change: { deaths: 1, swears: 0 }
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
      change: { deaths: change, swears: 0 }
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
      change: { deaths: 0, swears: 1 }
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
      change: { deaths: 0, swears: change }
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
      bits: oldCounters.bits, // Keep bits counter
      streamStarted: oldCounters.streamStarted
    });

    return {
      ...saved,
      change: { deaths: -oldCounters.deaths, swears: -oldCounters.swears, bits: 0 }
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
      change: { deaths: 0, swears: 0, bits: amount }
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
      change: { deaths: 0, swears: 0, bits: -oldCounters.bits }
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

      console.log(`💾 updateUserDiscordWebhook - Using keys: partitionKey=${actualPartitionKey}, rowKey=${actualRowKey}`);

      try {
        console.log(`🔄 Attempting Azure Table update for webhook:`, {
          partitionKey: actualPartitionKey,
          rowKey: actualRowKey,
          webhookUrl: webhookUrl ? `${webhookUrl.substring(0, 50)}...` : 'EMPTY'
        });

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
      console.log(`📝 Local user object updated with webhook URL`);
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
   * Get user's Discord webhook configuration
   */
  async getUserDiscordWebhook(twitchUserId) {
    console.log(`🔍 getUserDiscordWebhook called for user: ${twitchUserId}`);

    const user = await this.getUser(twitchUserId);
    if (!user) {
      console.log(`❌ getUserDiscordWebhook: User not found for ID: ${twitchUserId}`);
      return null;
    }

    console.log(`📋 getUserDiscordWebhook: User found, checking webhook data:`, {
      userId: twitchUserId,
      username: user.username || 'NO_USERNAME',
      hasDiscordWebhookUrl: !!user.discordWebhookUrl,
      discordWebhookUrl: user.discordWebhookUrl ? `${user.discordWebhookUrl.substring(0, 50)}...` : 'EMPTY',
      userKeys: Object.keys(user).filter(k => k.includes('discord') || k.includes('webhook')),
      allKeys: Object.keys(user) // Show ALL keys to debug Azure Table Storage structure
    });

    // Check for Azure Table Storage field variations
    console.log(`🔍 Checking all possible webhook field variations:`, {
      'user.discordWebhookUrl': user.discordWebhookUrl,
      'user.DiscordWebhookUrl': user.DiscordWebhookUrl,
      'user["discordWebhookUrl"]': user['discordWebhookUrl'],
      'typeOfDiscordWebhookUrl': typeof user.discordWebhookUrl
    });

    const result = {
      webhookUrl: user.discordWebhookUrl || '',
      enabled: !!(user.discordWebhookUrl) // Consider webhook enabled if URL exists
    };

    console.log(`📤 getUserDiscordWebhook returning:`, {
      webhookUrl: result.webhookUrl ? `${result.webhookUrl.substring(0, 50)}...` : 'EMPTY',
      enabled: result.enabled
    });

    return result;
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
        sound: 'distant-footsteps',
        soundDescription: 'Distant footsteps or whisper',
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
   * Get default event-to-alert mappings for EventSub events
   */
  getDefaultEventMappings() {
    return {
      'channel.follow': 'follow',
      'channel.subscribe': 'subscription',
      'channel.subscription.gift': 'giftsub',
      'channel.subscription.message': 'resub',
      'channel.cheer': 'bits',
      'channel.bits.use': 'bits'  // NEW bits event type
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
    if (this.mode === 'azure') {
      try {
        const entity = await this.usersClient.getEntity(twitchUserId, 'event-mappings');
        return JSON.parse(entity.mappings || '{}');
      } catch (error) {
        if (error.statusCode === 404) {
          return {};
        }
        throw error;
      }
    } else {
      const mappingsFile = path.join(this.localDataDir, 'event-mappings.json');

      if (!fs.existsSync(mappingsFile)) {
        return {};
      }

      try {
        const allMappings = JSON.parse(fs.readFileSync(mappingsFile, 'utf8'));
        return allMappings[twitchUserId] || {};
      } catch (error) {
        console.error('Error reading event mappings:', error);
        return {};
      }
    }
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
   * Get alert configuration for a specific EventSub event
   */
  async getAlertForEvent(twitchUserId, eventType) {
    const mappings = await this.getEventMappings(twitchUserId);
    const alertType = mappings[eventType];

    if (!alertType) {
      console.log(`No alert mapping for event type: ${eventType}`);
      return null;
    }

    const alerts = await this.getUserAlerts(twitchUserId);
    const alert = alerts.find(alert => alert.type === alertType && alert.isEnabled);

    if (!alert) {
      console.log(`No enabled alert found for type: ${alertType}`);
    }

    return alert || null;
  }
}

// Export singleton instance
module.exports = new Database();

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
    this.localDataDir = path.join(__dirname, 'data');
    this.localUsersFile = path.join(this.localDataDir, 'users.json');
    this.localCountersFile = path.join(this.localDataDir, 'counters.json');

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

      // Create tables if they don't exist
      await this.usersClient.createTable();
      await this.countersClient.createTable();

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
      alertAnimations: false
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
      isActive: userData.isActive !== undefined ? userData.isActive : true,
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

    user.features = JSON.stringify(features);
    return await this.saveUser(user);
  }

  /**
   * Update user status (admin only)
   */
  async updateUserStatus(twitchUserId, isActive) {
    const user = await this.getUser(twitchUserId);
    if (!user) {
      throw new Error('User not found');
    }

    user.isActive = isActive;
    return await this.saveUser(user);
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
    return await this.saveUser(user);
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
    return await this.saveUser(user);
  }
}

// Export singleton instance
module.exports = new Database();

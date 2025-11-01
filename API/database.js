const { TableClient, AzureNamedKeyCredential } = require('@azure/data-tables');
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
      const accountKey = process.env.AZURE_STORAGE_KEY;
      
      if (!accountName || !accountKey) {
        console.log('⚠️  Azure Storage credentials not configured, falling back to local storage');
        this.mode = 'local';
        this.initializeLocalStorage();
        return;
      }

      const credential = new AzureNamedKeyCredential(accountName, accountKey);
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
          lastUpdated: entity.lastUpdated
        };
      } catch (error) {
        if (error.statusCode === 404) {
          return { deaths: 0, swears: 0, lastUpdated: new Date().toISOString() };
        }
        throw error;
      }
    } else {
      const counters = JSON.parse(fs.readFileSync(this.localCountersFile, 'utf8'));
      return counters[twitchUserId] || { deaths: 0, swears: 0, lastUpdated: new Date().toISOString() };
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
    const counters = await this.getCounters(twitchUserId);
    counters.deaths += 1;
    return await this.saveCounters(twitchUserId, counters);
  }

  /**
   * Decrement death counter
   */
  async decrementDeaths(twitchUserId) {
    const counters = await this.getCounters(twitchUserId);
    if (counters.deaths > 0) {
      counters.deaths -= 1;
    }
    return await this.saveCounters(twitchUserId, counters);
  }

  /**
   * Increment swear counter
   */
  async incrementSwears(twitchUserId) {
    const counters = await this.getCounters(twitchUserId);
    counters.swears += 1;
    return await this.saveCounters(twitchUserId, counters);
  }

  /**
   * Decrement swear counter
   */
  async decrementSwears(twitchUserId) {
    const counters = await this.getCounters(twitchUserId);
    if (counters.swears > 0) {
      counters.swears -= 1;
    }
    return await this.saveCounters(twitchUserId, counters);
  }

  /**
   * Reset all counters
   */
  async resetCounters(twitchUserId) {
    return await this.saveCounters(twitchUserId, { deaths: 0, swears: 0 });
  }
}

// Export singleton instance
module.exports = new Database();

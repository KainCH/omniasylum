/**
 * Consolidated API & Authentication Utilities
 * Centralized auth management and API calls for consistent behavior across all components
 */

// Configuration
const AUTH_TOKEN_KEY = 'authToken';
const API_BASE = process.env.NODE_ENV === 'production'
  ? '' // Use relative URLs in production
  : 'http://localhost:3000'; // Development server

// Custom Error Classes
export class APIError extends Error {
  constructor(message, status, data) {
    super(message);
    this.name = 'APIError';
    this.status = status;
    this.data = data;
  }
}

// ============================================================================
// AUTH TOKEN MANAGEMENT
// ============================================================================

/**
 * Get the authentication token from localStorage
 * @returns {string|null} The auth token or null if not found
 */
export const getAuthToken = () => {
  return localStorage.getItem(AUTH_TOKEN_KEY);
};

/**
 * Set the authentication token in localStorage
 * @param {string} token - The auth token to store
 */
export const setAuthToken = (token) => {
  if (token) {
    localStorage.setItem(AUTH_TOKEN_KEY, token);
  } else {
    localStorage.removeItem(AUTH_TOKEN_KEY);
  }
};

/**
 * Remove the authentication token from localStorage
 */
export const removeAuthToken = () => {
  localStorage.removeItem(AUTH_TOKEN_KEY);
};

/**
 * Check if user is authenticated (has a valid token)
 * @returns {boolean} True if authenticated, false otherwise
 */
export const isAuthenticated = () => {
  return !!getAuthToken();
};

/**
 * Get headers for authenticated API requests
 * @param {Object} additionalHeaders - Additional headers to include
 * @returns {Object} Headers object with Authorization and other headers
 */
export const getAuthHeaders = (additionalHeaders = {}) => {
  const token = getAuthToken();
  const headers = {
    'Content-Type': 'application/json',
    ...additionalHeaders
  };

  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  return headers;
};

// ============================================================================
// CORE API REQUEST FUNCTIONS
// ============================================================================

/**
 * Generic API request wrapper with enhanced error handling
 * @param {string} url - The API endpoint URL (with or without API_BASE)
 * @param {Object} options - Fetch options (method, body, etc.)
 * @returns {Promise<Response>} The fetch response
 */
export const apiRequest = async (url, options = {}) => {
  const fullUrl = url.startsWith('http') ? url : `${API_BASE}${url}`;

  const config = {
    credentials: 'include',
    headers: {
      ...getAuthHeaders(),
      ...options.headers
    },
    ...options
  };

  try {
    const response = await fetch(fullUrl, config);

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new APIError(
        errorData?.error || `HTTP ${response.status}`,
        response.status,
        errorData
      );
    }

    return response;
  } catch (error) {
    if (error instanceof APIError) {
      throw error;
    }

    // Network or other errors
    throw new APIError(
      `Network error: ${error.message}`,
      0,
      { originalError: error }
    );
  }
};

/**
 * Make an authenticated API request
 * @param {string} url - The API endpoint URL
 * @param {Object} options - Fetch options (method, body, etc.)
 * @param {boolean} includeCredentials - Whether to include credentials (default: true)
 * @returns {Promise<Response>} The fetch response
 */
export const makeAuthenticatedRequest = async (url, options = {}, includeCredentials = true) => {
  const requestOptions = {
    ...options,
    headers: {
      ...getAuthHeaders(),
      ...(options.headers || {})
    }
  };

  if (includeCredentials) {
    requestOptions.credentials = 'include';
  }

  return apiRequest(url, requestOptions);
};

/**
 * Make an authenticated API request and return JSON response
 * @param {string} url - The API endpoint URL
 * @param {Object} options - Fetch options
 * @param {boolean} includeCredentials - Whether to include credentials
 * @returns {Promise<Object>} Parsed JSON response or throws error
 */
export const makeAuthenticatedJsonRequest = async (url, options = {}, includeCredentials = true) => {
  const response = await makeAuthenticatedRequest(url, options, includeCredentials);

  if (response.status === 401) {
    // Token might be expired or invalid
    console.warn('Authentication failed, redirecting to login...');
    removeAuthToken();
    window.location.href = '/auth/twitch';
    throw new APIError('Authentication required', 401);
  }

  const contentType = response.headers.get('content-type');
  if (contentType && contentType.includes('application/json')) {
    return response.json();
  }

  return response.text();
};

// ============================================================================
// USER MANAGEMENT & SESSION
// ============================================================================

/**
 * Get user info from stored token (if available)
 * Note: This is a basic implementation. For production, consider JWT decoding
 * @returns {Object|null} User info or null if not available
 */
export const getCurrentUser = () => {
  // This could be enhanced to decode JWT token if needed
  // For now, we rely on the server-side validation
  return isAuthenticated() ? { authenticated: true } : null;
};

/**
 * Logout user by removing token and redirecting
 */
export const logout = () => {
  removeAuthToken();
  window.location.href = '/';
};

/**
 * Handle authentication errors consistently
 * @param {Error} error - The error that occurred
 * @param {string} context - Context where the error occurred (for logging)
 */
export const handleAuthError = (error, context = 'API call') => {
  console.error(`Authentication error in ${context}:`, error);

  if (error instanceof APIError && (error.status === 401 || error.status === 403)) {
    removeAuthToken();
    window.location.href = '/auth/twitch';
    return;
  }

  if (error.message.includes('Authentication required') || error.message.includes('401')) {
    // Already handled by makeAuthenticatedJsonRequest
    return;
  }

  // Handle other auth-related errors
  alert(`Authentication error: ${error.message}`);
};

// ============================================================================
// SPECIFIC API ENDPOINTS
// ============================================================================

// User API
export const userAPI = {
  // Get current user data
  getCurrentUser: async () => {
    return await makeAuthenticatedJsonRequest('/api/user');
  },

  // Update user data
  updateUser: async (userData) => {
    return await makeAuthenticatedJsonRequest('/api/user', {
      method: 'PUT',
      body: JSON.stringify(userData)
    });
  },

  // Get user features
  getFeatures: async () => {
    return await makeAuthenticatedJsonRequest('/api/user/features');
  },

  // Update user features
  updateFeatures: async (features) => {
    return await makeAuthenticatedJsonRequest('/api/user/features', {
      method: 'PUT',
      body: JSON.stringify(features)
    });
  },

  // Get user settings
  getSettings: async () => {
    return await makeAuthenticatedJsonRequest('/api/user/settings');
  },

  // Update user settings
  updateSettings: async (settings) => {
    return await makeAuthenticatedJsonRequest('/api/user/settings', {
      method: 'PUT',
      body: JSON.stringify(settings)
    });
  },

  // Get overlay settings
  getOverlaySettings: async () => {
    return await makeAuthenticatedJsonRequest('/api/overlay-settings');
  },

  // Update overlay settings
  updateOverlaySettings: async (settings) => {
    return await makeAuthenticatedJsonRequest('/api/overlay-settings', {
      method: 'PUT',
      body: JSON.stringify(settings)
    });
  },

  // Discord webhook management
  getDiscordWebhook: async () => {
    return await makeAuthenticatedJsonRequest('/api/user/discord-webhook');
  },

  updateDiscordWebhook: async (webhookData) => {
    return await makeAuthenticatedJsonRequest('/api/user/discord-webhook', {
      method: 'PUT',
      body: JSON.stringify(webhookData)
    });
  },

  testDiscordWebhook: async (webhookData) => {
    return await makeAuthenticatedJsonRequest('/api/user/discord-webhook/test', {
      method: 'POST',
      body: JSON.stringify(webhookData)
    });
  },

  // Discord settings
  getDiscordSettings: async () => {
    return await makeAuthenticatedJsonRequest('/api/user/discord-settings');
  },

  updateDiscordSettings: async (settings) => {
    return await makeAuthenticatedJsonRequest('/api/user/discord-settings', {
      method: 'PUT',
      body: JSON.stringify(settings)
    });
  },

  // Discord invite management
  getDiscordInvite: async () => {
    return await makeAuthenticatedJsonRequest('/api/user/discord-invite');
  },

  updateDiscordInvite: async (inviteData) => {
    return await makeAuthenticatedJsonRequest('/api/user/discord-invite', {
      method: 'PUT',
      body: JSON.stringify(inviteData)
    });
  }
};

// Counter API
export const counterAPI = {
  // Get counters
  getCounters: async () => {
    return await makeAuthenticatedJsonRequest('/api/counters');
  },

  // Update counter
  updateCounter: async (type, value) => {
    return await makeAuthenticatedJsonRequest('/api/counters', {
      method: 'PUT',
      body: JSON.stringify({ type, value })
    });
  },

  // Reset counters
  resetCounters: async () => {
    return await makeAuthenticatedJsonRequest('/api/counters/reset', {
      method: 'POST'
    });
  }
};

// Admin API
export const adminAPI = {
  // Get all users
  getUsers: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/users');
  },

  // Get specific user by ID
  getUser: async (userId) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}`);
  },

  // Get user diagnostics (for broken user manager)
  getUserDiagnostics: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/users/diagnostics');
  },

  // Delete broken user by partition/row key
  deleteBrokenUser: async (partitionKey, rowKey) => {
    return await makeAuthenticatedJsonRequest(
      `/api/admin/users/broken/${encodeURIComponent(partitionKey)}/${encodeURIComponent(rowKey)}`,
      { method: 'DELETE' }
    );
  },

  // Get admin stats/dashboard data
  getStats: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/stats');
  },

  // Get features list
  getFeatures: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/features');
  },

  // Get roles list
  getRoles: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/roles');
  },

  // Get permissions list
  getPermissions: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/permissions');
  },

  // Get streams data
  getStreams: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/streams');
  },

  // Toggle user active status
  toggleUserStatus: async (userId, isActive) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/toggle`, {
      method: 'PUT',
      body: JSON.stringify({ isActive })
    });
  },

  // Update user features
  updateUserFeatures: async (userId, feature, enabled) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/features`, {
      method: 'PUT',
      body: JSON.stringify({ feature, enabled })
    });
  },

  // Delete user completely
  deleteUser: async (userId) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}`, {
      method: 'DELETE'
    });
  },

  // Approve user request
  approveUser: async (userId) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/approve`, {
      method: 'POST'
    });
  },

  // Reject user request
  rejectUser: async (userId) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/reject`, {
      method: 'POST'
    });
  },

  // Get user requests/pending approvals
  getUserRequests: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/user-requests');
  },

  // Discord webhook management (Admin)
  getDiscordWebhook: async (userId) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/discord-webhook`);
  },

  updateDiscordWebhook: async (userId, webhookData) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/discord-webhook`, {
      method: 'PUT',
      body: JSON.stringify(webhookData)
    });
  },

  testDiscordWebhook: async (userId, webhookData) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/discord-webhook/test`, {
      method: 'POST',
      body: JSON.stringify(webhookData)
    });
  },

  // Discord settings management (Admin)
  getDiscordSettings: async (userId) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/discord-settings`);
  },

  updateDiscordSettings: async (userId, settings) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/discord-settings`, {
      method: 'PUT',
      body: JSON.stringify(settings)
    });
  },

  // Discord invite management (Admin)
  getDiscordInvite: async (userId) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/discord-invite`);
  },

  updateDiscordInvite: async (userId, inviteData) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}/discord-invite`, {
      method: 'PUT',
      body: JSON.stringify(inviteData)
    });
  },

  // Create new user
  createUser: async (userData) => {
    return await makeAuthenticatedJsonRequest('/api/admin/users', {
      method: 'POST',
      body: JSON.stringify(userData)
    });
  },

  // Restore series save (Debug)
  restoreSeriesSave: async (restoreData) => {
    return await makeAuthenticatedJsonRequest('/api/debug/restore-series-save', {
      method: 'POST',
      body: JSON.stringify(restoreData)
    });
  }
};

// Stream API
export const streamAPI = {
  // Get stream status
  getStatus: async () => {
    return await makeAuthenticatedJsonRequest('/api/stream/status');
  },

  // Get stream statistics
  getStats: async () => {
    return await makeAuthenticatedJsonRequest('/api/stream/stats');
  },

  // Get stream sessions
  getSessions: async () => {
    return await makeAuthenticatedJsonRequest('/api/stream/sessions');
  }
};

// Alerts API
export const alertsAPI = {
  // Get alert settings
  getSettings: async () => {
    return await makeAuthenticatedJsonRequest('/api/alerts/settings');
  },

  // Update alert settings
  updateSettings: async (settings) => {
    return await makeAuthenticatedJsonRequest('/api/alerts/settings', {
      method: 'PUT',
      body: JSON.stringify(settings)
    });
  },

  // Get alert events
  getEvents: async () => {
    return await makeAuthenticatedJsonRequest('/api/alerts/events');
  },

  // Create alert event
  createEvent: async (eventData) => {
    return await makeAuthenticatedJsonRequest('/api/alerts/events', {
      method: 'POST',
      body: JSON.stringify(eventData)
    });
  },

  // Update alert event
  updateEvent: async (eventId, eventData) => {
    return await makeAuthenticatedJsonRequest(`/api/alerts/events/${eventId}`, {
      method: 'PUT',
      body: JSON.stringify(eventData)
    });
  },

  // Delete alert event
  deleteEvent: async (eventId) => {
    return await makeAuthenticatedJsonRequest(`/api/alerts/events/${eventId}`, {
      method: 'DELETE'
    });
  }
};

// Rewards API (Channel Points)
export const rewardsAPI = {
  // Get channel point rewards
  getRewards: async () => {
    return await makeAuthenticatedJsonRequest('/api/rewards');
  },

  // Create channel point reward
  createReward: async (rewardData) => {
    return await makeAuthenticatedJsonRequest('/api/rewards', {
      method: 'POST',
      body: JSON.stringify(rewardData)
    });
  },

  // Update channel point reward
  updateReward: async (rewardId, rewardData) => {
    return await makeAuthenticatedJsonRequest(`/api/rewards/${rewardId}`, {
      method: 'PUT',
      body: JSON.stringify(rewardData)
    });
  },

  // Delete channel point reward
  deleteReward: async (rewardId) => {
    return await makeAuthenticatedJsonRequest(`/api/rewards/${rewardId}`, {
      method: 'DELETE'
    });
  }
};

// Series Save API
export const seriesAPI = {
  // Get series saves
  getSaves: async () => {
    return await makeAuthenticatedJsonRequest('/api/series/saves');
  },

  // Create series save
  createSave: async (saveData) => {
    return await makeAuthenticatedJsonRequest('/api/series/saves', {
      method: 'POST',
      body: JSON.stringify(saveData)
    });
  },

  // Update series save
  updateSave: async (saveId, saveData) => {
    return await makeAuthenticatedJsonRequest(`/api/series/saves/${saveId}`, {
      method: 'PUT',
      body: JSON.stringify(saveData)
    });
  },

  // Delete series save
  deleteSave: async (saveId) => {
    return await makeAuthenticatedJsonRequest(`/api/series/saves/${saveId}`, {
      method: 'DELETE'
    });
  },

  // Load series save
  loadSave: async (saveId) => {
    return await makeAuthenticatedJsonRequest(`/api/series/saves/${saveId}/load`, {
      method: 'POST'
    });
  }
};

// Health & Status API
export const healthAPI = {
  // Get application health
  getHealth: async () => {
    return await makeAuthenticatedJsonRequest('/api/health');
  },

  // Get Twitch bot status
  getTwitchStatus: async () => {
    return await makeAuthenticatedJsonRequest('/api/twitch/status');
  }
};

// ============================================================================
// UNIFIED API FUNCTIONS
// ============================================================================

/**
 * Unified Discord settings lookup that works for both admin and user modes
 * Automatically determines whether to use admin API (when looking up another user)
 * or user API (when accessing own settings)
 * @param {Object|null} targetUser - User object to look up settings for (null = current user)
 * @returns {Promise<Object>} Discord settings data
 */
export const getDiscordSettingsUnified = async (targetUser = null) => {
  try {
    // Get current user context from token
    const token = localStorage.getItem('authToken');
    if (!token) {
      throw new APIError('No authentication token found', 401);
    }

    const payload = JSON.parse(atob(token.split('.')[1]));
    const currentUserId = payload.userId || payload.twitchUserId;
    const isCurrentUserAdmin = payload.role === 'admin';

    // Determine if we're in admin mode (admin looking up another user's settings)
    const targetUserId = targetUser?.twitchUserId || targetUser?.userId;
    const isAdminMode = isCurrentUserAdmin && targetUserId && currentUserId !== targetUserId;

    console.log('🔍 Unified Discord settings lookup:', {
      currentUserId,
      targetUserId,
      isCurrentUserAdmin,
      isAdminMode,
      targetUser: targetUser ? `${targetUser.displayName} (${targetUserId})` : 'current user'
    });

    if (isAdminMode) {
      // Admin viewing another user's settings - use admin API
      console.log('👑 Using admin API for unified Discord settings lookup');
      const response = await adminAPI.getDiscordSettings(targetUserId);
      console.log('👑 Admin API raw response:', response);
      console.log('👑 Extracting discordSettings:', response.discordSettings);
      // Admin API returns { discordSettings: {...} }, extract the inner object
      const extracted = response.discordSettings || response;
      console.log('👑 Final extracted data:', extracted);
      return extracted;
    } else {
      // User viewing their own settings - use user API
      console.log('👤 Using user API for unified Discord settings lookup');
      const result = await userAPI.getDiscordSettings();
      console.log('👤 User API raw response:', result);
      return result;
    }
  } catch (error) {
    console.error('❌ Error in unified Discord settings lookup:', error);
    throw error;
  }
};

/**
 * Unified Discord webhook lookup that works for both admin and user modes
 * @param {Object|null} targetUser - User object to look up webhook for (null = current user)
 * @returns {Promise<Object>} Discord webhook data
 */
export const getDiscordWebhookUnified = async (targetUser = null) => {
  try {
    const token = localStorage.getItem('authToken');
    if (!token) {
      throw new APIError('No authentication token found', 401);
    }

    const payload = JSON.parse(atob(token.split('.')[1]));
    const currentUserId = payload.userId || payload.twitchUserId;
    const isCurrentUserAdmin = payload.role === 'admin';

    const targetUserId = targetUser?.twitchUserId || targetUser?.userId;
    const isAdminMode = isCurrentUserAdmin && targetUserId && currentUserId !== targetUserId;

    console.log('🔍 Unified Discord webhook lookup:', {
      currentUserId,
      targetUserId,
      isCurrentUserAdmin,
      isAdminMode
    });

    if (isAdminMode) {
      console.log('👑 Using admin API for unified Discord webhook lookup');
      return await adminAPI.getDiscordWebhook(targetUserId);
    } else {
      console.log('👤 Using user API for unified Discord webhook lookup');
      return await userAPI.getDiscordWebhook();
    }
  } catch (error) {
    console.error('❌ Error in unified Discord webhook lookup:', error);
    throw error;
  }
};

/**
 * Unified Discord invite lookup that works for both admin and user modes
 * @param {Object|null} targetUser - User object to look up invite for (null = current user)
 * @returns {Promise<Object>} Discord invite data
 */
export const getDiscordInviteUnified = async (targetUser = null) => {
  try {
    const token = localStorage.getItem('authToken');
    if (!token) {
      throw new APIError('No authentication token found', 401);
    }

    const payload = JSON.parse(atob(token.split('.')[1]));
    const currentUserId = payload.userId || payload.twitchUserId;
    const isCurrentUserAdmin = payload.role === 'admin';

    const targetUserId = targetUser?.twitchUserId || targetUser?.userId;
    const isAdminMode = isCurrentUserAdmin && targetUserId && currentUserId !== targetUserId;

    console.log('🔍 Unified Discord invite lookup:', {
      currentUserId,
      targetUserId,
      isCurrentUserAdmin,
      isAdminMode
    });

    if (isAdminMode) {
      console.log('👑 Using admin API for unified Discord invite lookup');
      return await adminAPI.getDiscordInvite(targetUserId);
    } else {
      console.log('👤 Using user API for unified Discord invite lookup');
      return await userAPI.getDiscordInvite();
    }
  } catch (error) {
    console.error('❌ Error in unified Discord invite lookup:', error);
    throw error;
  }
};

/**
 * Unified function to update Discord settings (webhook, notifications, invite)
 * @param {Object|null} targetUser - User to update settings for (null = current user)
 * @param {Object} webhookData - Webhook settings to update
 * @param {Object} notificationSettings - Notification settings to update
 * @param {Object} inviteData - Invite settings to update
 * @returns {Promise<Object>} Update results
 */
export const updateDiscordSettingsUnified = async (targetUser = null, webhookData = null, notificationSettings = null, inviteData = null) => {
  try {
    const token = localStorage.getItem('authToken');
    if (!token) {
      throw new APIError('No authentication token found', 401);
    }

    const payload = JSON.parse(atob(token.split('.')[1]));
    const currentUserId = payload.userId || payload.twitchUserId;
    const isCurrentUserAdmin = payload.role === 'admin';

    const targetUserId = targetUser?.twitchUserId || targetUser?.userId;
    const isAdminMode = isCurrentUserAdmin && targetUserId && currentUserId !== targetUserId;

    const results = {};

    if (isAdminMode) {
      console.log('👑 Using admin API for unified Discord settings update');

      if (webhookData) {
        results.webhook = await adminAPI.updateDiscordWebhook(targetUserId, webhookData);
      }

      if (notificationSettings) {
        results.notifications = await adminAPI.updateDiscordSettings(targetUserId, notificationSettings);
      }

      if (inviteData) {
        results.invite = await adminAPI.updateDiscordInvite(targetUserId, inviteData);
      }
    } else {
      console.log('👤 Using user API for unified Discord settings update');

      if (webhookData) {
        results.webhook = await userAPI.updateDiscordWebhook(webhookData);
      }

      if (notificationSettings) {
        results.notifications = await userAPI.updateDiscordSettings(notificationSettings);
      }

      if (inviteData) {
        results.invite = await userAPI.updateDiscordInvite(inviteData);
      }
    }

    return results;
  } catch (error) {
    console.error('❌ Error in unified Discord settings update:', error);
    throw error;
  }
};

// ============================================================================
// UTILITY FUNCTIONS
// ============================================================================

/**
 * Debounce function for search/input
 * @param {Function} func - Function to debounce
 * @param {number} wait - Wait time in milliseconds
 * @returns {Function} Debounced function
 */
export const debounce = (func, wait) => {
  let timeout;
  return function executedFunction(...args) {
    const later = () => {
      clearTimeout(timeout);
      func(...args);
    };
    clearTimeout(timeout);
    timeout = setTimeout(later, wait);
  };
};

// Export all utilities as a default object for convenience
export default {
  // Auth functions
  getAuthToken,
  setAuthToken,
  removeAuthToken,
  isAuthenticated,
  getAuthHeaders,
  getCurrentUser,
  logout,
  handleAuthError,

  // API functions
  apiRequest,
  makeAuthenticatedRequest,
  makeAuthenticatedJsonRequest,

  // API endpoints
  userAPI,
  counterAPI,
  adminAPI,
  streamAPI,
  alertsAPI,
  rewardsAPI,
  seriesAPI,
  healthAPI,

  // Utilities
  debounce,

  // Error class
  APIError
};

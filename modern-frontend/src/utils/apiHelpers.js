/**
 * API Helper Functions for Notification Management
 * Centralized API calls with consistent error handling
 */

// Base API configuration
const API_BASE = process.env.NODE_ENV === 'production'
  ? '' // Use relative URLs in production
  : 'http://localhost:3000'; // Development server

// Helper to get authentication headers
const getAuthHeaders = () => ({
  'Content-Type': 'application/json',
  'Authorization': `Bearer ${localStorage.getItem('authToken') || ''}`
});

// Generic API error handler
class APIError extends Error {
  constructor(message, status, data) {
    super(message);
    this.name = 'APIError';
    this.status = status;
    this.data = data;
  }
}

// Generic fetch wrapper with error handling
const apiRequest = async (url, options = {}) => {
  const config = {
    credentials: 'include',
    headers: {
      ...getAuthHeaders(),
      ...options.headers
    },
    ...options
  };

  try {
    const response = await fetch(`${API_BASE}${url}`, config);

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new APIError(
        errorData?.error || `HTTP ${response.status}`,
        response.status,
        errorData
      );
    }

    const contentType = response.headers.get('content-type');
    if (contentType && contentType.includes('application/json')) {
      return await response.json();
    }

    return await response.text();
  } catch (error) {
    if (error instanceof APIError) {
      throw error;
    }
    throw new APIError('Network error', 0, { originalError: error });
  }
};

// Notification Settings API
export const notificationAPI = {
  // Admin methods (for managing other users)
  getSettings: async (userId) => {
    return await apiRequest(`/api/admin/users/${userId}/discord-settings`);
  },

  updateSettings: async (userId, settings) => {
    return await apiRequest(`/api/admin/users/${userId}/discord-settings`, {
      method: 'PUT',
      body: JSON.stringify(settings)
    });
  },

  getWebhook: async (userId) => {
    return await apiRequest(`/api/admin/users/${userId}/discord-webhook`);
  },

  updateWebhook: async (userId, webhookData) => {
    return await apiRequest(`/api/admin/users/${userId}/discord-webhook`, {
      method: 'PUT',
      body: JSON.stringify(webhookData)
    });
  },

  testWebhook: async (userId, webhookData) => {
    return await apiRequest(`/api/admin/users/${userId}/discord-webhook/test`, {
      method: 'POST',
      body: JSON.stringify(webhookData)
    });
  },

  getDiscordInvite: async (userId) => {
    return await apiRequest(`/api/admin/users/${userId}/discord-invite`);
  },

  updateDiscordInvite: async (userId, inviteData) => {
    return await apiRequest(`/api/admin/users/${userId}/discord-invite`, {
      method: 'PUT',
      body: JSON.stringify(inviteData)
    });
  },

  // User methods (for self-management)
  getUserSettings: async () => {
    return await apiRequest('/api/user/discord-settings');
  },

  updateUserSettings: async (settings) => {
    return await apiRequest('/api/user/discord-settings', {
      method: 'PUT',
      body: JSON.stringify(settings)
    });
  },

  updateTemplateStyle: async (templateData) => {
    return await apiRequest('/api/user/template-style', {
      method: 'PUT',
      body: JSON.stringify(templateData)
    });
  }
};

// User Management API
export const userAPI = {
  // Admin methods
  getUser: async (userId) => {
    return await apiRequest(`/api/admin/users/${userId}`);
  },

  updateFeatures: async (userId, features) => {
    return await apiRequest(`/api/admin/users/${userId}/features`, {
      method: 'PUT',
      body: JSON.stringify(features)
    });
  },

  updateOverlaySettings: async (userId, settings) => {
    return await apiRequest(`/api/admin/users/${userId}/overlay`, {
      method: 'PUT',
      body: JSON.stringify(settings)
    });
  },

  getOverlaySettings: async (userId) => {
    return await apiRequest(`/api/admin/users/${userId}/overlay`);
  },

  getAllUsers: async () => {
    return await apiRequest('/api/admin/users');
  },

  deleteUser: async (userId) => {
    return await apiRequest(`/api/admin/users/${userId}`, {
      method: 'DELETE'
    });
  },

  // User methods (for self-management)
  getDiscordWebhook: async () => {
    // Add cache-busting parameter to prevent browser caching
    const timestamp = Date.now();
    return await apiRequest(`/api/user/discord-webhook?_t=${timestamp}`);
  },

  updateDiscordWebhook: async (webhookData) => {
    return await apiRequest('/api/user/discord-webhook', {
      method: 'PUT',
      body: JSON.stringify(webhookData)
    });
  },

  testDiscordWebhook: async (webhookData) => {
    return await apiRequest('/api/user/discord-webhook/test', {
      method: 'POST',
      body: JSON.stringify(webhookData)
    });
  },

  // Discord invite link methods
  getDiscordInvite: async () => {
    const timestamp = Date.now();
    return await apiRequest(`/api/user/discord-invite?_t=${timestamp}`);
  },

  updateDiscordInvite: async (inviteData) => {
    return await apiRequest('/api/user/discord-invite', {
      method: 'PUT',
      body: JSON.stringify(inviteData)
    });
  }
};

// Counter API
export const counterAPI = {
  // Get counter data
  getCounters: async () => {
    return await apiRequest('/api/counters');
  },

  // Increment death counter
  incrementDeaths: async () => {
    return await apiRequest('/api/counters/deaths/increment', {
      method: 'POST'
    });
  },

  // Decrement death counter
  decrementDeaths: async () => {
    return await apiRequest('/api/counters/deaths/decrement', {
      method: 'POST'
    });
  },

  // Increment swear counter
  incrementSwears: async () => {
    return await apiRequest('/api/counters/swears/increment', {
      method: 'POST'
    });
  },

  // Decrement swear counter
  decrementSwears: async () => {
    return await apiRequest('/api/counters/swears/decrement', {
      method: 'POST'
    });
  },

  // Reset counters
  resetCounters: async () => {
    return await apiRequest('/api/counters/reset', {
      method: 'POST'
    });
  }
};

// Stream API
export const streamAPI = {
  // Get stream status
  getStreamStatus: async () => {
    return await apiRequest('/api/stream/status');
  },

  // Start stream
  startStream: async () => {
    return await apiRequest('/api/stream/start', {
      method: 'POST'
    });
  },

  // End stream
  endStream: async () => {
    return await apiRequest('/api/stream/end', {
      method: 'POST'
    });
  }
};

// Health check API
export const healthAPI = {
  // Check API health
  checkHealth: async () => {
    return await apiRequest('/api/health');
  },

  // Get system status
  getSystemStatus: async () => {
    return await apiRequest('/api/system/status');
  }
};

// WebSocket helper for real-time updates
export class WebSocketManager {
  constructor(userId) {
    this.userId = userId;
    this.socket = null;
    this.reconnectAttempts = 0;
    this.maxReconnectAttempts = 5;
    this.reconnectInterval = 1000;
    this.listeners = new Map();
  }

  connect() {
    try {
      const wsProtocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
      const wsHost = process.env.NODE_ENV === 'production'
        ? window.location.host
        : 'localhost:3000';

      this.socket = new WebSocket(`${wsProtocol}//${wsHost}`);

      this.socket.onopen = () => {
        console.log('✅ WebSocket connected');
        this.reconnectAttempts = 0;

        // Join user's room
        if (this.userId) {
          this.socket.send(JSON.stringify({
            type: 'join-room',
            room: `user:${this.userId}`
          }));
        }
      };

      this.socket.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          this.handleMessage(data);
        } catch (error) {
          console.error('Failed to parse WebSocket message:', error);
        }
      };

      this.socket.onclose = () => {
        console.log('❌ WebSocket disconnected');
        this.attemptReconnect();
      };

      this.socket.onerror = (error) => {
        console.error('WebSocket error:', error);
      };

    } catch (error) {
      console.error('Failed to connect WebSocket:', error);
      this.attemptReconnect();
    }
  }

  disconnect() {
    if (this.socket) {
      this.socket.close();
      this.socket = null;
    }
  }

  attemptReconnect() {
    if (this.reconnectAttempts < this.maxReconnectAttempts) {
      this.reconnectAttempts++;
      setTimeout(() => {
        console.log(`🔄 Attempting to reconnect WebSocket (${this.reconnectAttempts}/${this.maxReconnectAttempts})`);
        this.connect();
      }, this.reconnectInterval * this.reconnectAttempts);
    } else {
      console.error('💀 Max WebSocket reconnect attempts reached');
    }
  }

  handleMessage(data) {
    const { type, ...payload } = data;

    // Call all listeners for this message type
    const listeners = this.listeners.get(type) || [];
    listeners.forEach(callback => {
      try {
        callback(payload);
      } catch (error) {
        console.error(`Error in WebSocket listener for ${type}:`, error);
      }
    });

    // Call wildcard listeners
    const wildcardListeners = this.listeners.get('*') || [];
    wildcardListeners.forEach(callback => {
      try {
        callback(data);
      } catch (error) {
        console.error('Error in WebSocket wildcard listener:', error);
      }
    });
  }

  on(eventType, callback) {
    if (!this.listeners.has(eventType)) {
      this.listeners.set(eventType, []);
    }
    this.listeners.get(eventType).push(callback);
  }

  off(eventType, callback) {
    const listeners = this.listeners.get(eventType);
    if (listeners) {
      const index = listeners.indexOf(callback);
      if (index > -1) {
        listeners.splice(index, 1);
      }
    }
  }

  send(data) {
    if (this.socket && this.socket.readyState === WebSocket.OPEN) {
      this.socket.send(JSON.stringify(data));
    } else {
      console.warn('WebSocket not connected, message not sent:', data);
    }
  }
}

// Export error class for handling in components
export { APIError };

// Debug API functions
export const debugAPI = {
  // Get comprehensive Discord diagnostics for a user
  getDiscordDiagnostics: async (userId) => {
    const endpoint = userId ? `/api/debug/discord-diagnostics/${userId}` : '/api/debug/discord-diagnostics';
    return await apiRequest(endpoint);
  },

  // Test Discord webhook
  testDiscordWebhook: async (userId) => {
    const endpoint = userId ? `/api/debug/test-discord-webhook/${userId}` : '/api/debug/test-discord-webhook';
    return await apiRequest(endpoint, {
      method: 'POST'
    });
  },

  // Get system health overview
  getSystemHealth: async () => {
    return await apiRequest('/api/debug/system-health');
  },

  // Clean Discord webhook data
  cleanDiscordWebhook: async () => {
    return await apiRequest('/api/debug/clean-discord-webhook', {
      method: 'POST'
    });
  },

  // Test webhook save functionality
  testWebhookSave: async () => {
    return await apiRequest('/api/debug/test-webhook-save', {
      method: 'POST'
    });
  },

  // Test webhook read functionality
  testWebhookRead: async () => {
    return await apiRequest('/api/debug/test-webhook-read');
  },

  // Clean up user data
  cleanupUserData: async () => {
    return await apiRequest('/api/debug/cleanup-user-data', {
      method: 'POST'
    });
  },

  // Check subscription costs
  checkSubscriptionCosts: async () => {
    return await apiRequest('/api/debug/subscription-costs');
  },

  // Test EventSub API compliance
  testEventSubAPI: async () => {
    return await apiRequest('/api/debug/test-eventsub-api', {
      method: 'POST'
    });
  },

  // Test notification system
  testNotification: async (userId, eventType) => {
    return await apiRequest(`/api/debug/test-notification/${userId}/${eventType}`, {
      method: 'POST'
    });
  },

  // Test stream status
  testStreamStatus: async (userId, status) => {
    return await apiRequest(`/api/debug/test-stream-status/${userId}/${status}`, {
      method: 'POST'
    });
  },

  // Test all notifications for a user
  testAllNotifications: async (userId) => {
    return await apiRequest(`/api/debug/test-all-notifications/${userId}`, {
      method: 'POST'
    });
  },

  // Start monitoring for a user
  startMonitoring: async () => {
    return await apiRequest('/api/debug/start-monitoring', {
      method: 'POST'
    });
  }
};

// Moderator API
export const moderatorAPI = {
  // Streamer management of moderators
  getMyModerators: async () => {
    return await apiRequest('/api/moderator/my-moderators');
  },

  grantModeratorAccess: async (moderatorUserId) => {
    return await apiRequest('/api/moderator/grant-access', {
      method: 'POST',
      body: JSON.stringify({ moderatorUserId })
    });
  },

  revokeModeratorAccess: async (moderatorUserId) => {
    return await apiRequest(`/api/moderator/revoke-access/${moderatorUserId}`, {
      method: 'DELETE'
    });
  },

  searchUsers: async (query) => {
    return await apiRequest(`/api/moderator/search-users?q=${encodeURIComponent(query)}`);
  },

  // Moderator management of streamers
  getManagedStreamers: async () => {
    return await apiRequest('/api/moderator/managed-streamers');
  },

  getStreamerDetails: async (streamerId) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}`);
  },

  updateStreamerFeatures: async (streamerId, features) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}/features`, {
      method: 'PUT',
      body: JSON.stringify({ features })
    });
  },

  getStreamerOverlaySettings: async (streamerId) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}/overlay`);
  },

  updateStreamerOverlaySettings: async (streamerId, overlaySettings) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}/overlay`, {
      method: 'PUT',
      body: JSON.stringify({ overlaySettings })
    });
  },

  getStreamerDiscordWebhook: async (streamerId) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}/discord-webhook`);
  },

  updateStreamerDiscordWebhook: async (streamerId, webhookData) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}/discord-webhook`, {
      method: 'PUT',
      body: JSON.stringify(webhookData)
    });
  },

  // Moderator Series Save Management
  getStreamerSeriesSaves: async (streamerId) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}/series-saves`);
  },

  createStreamerSeriesSave: async (streamerId, seriesData) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}/series-saves`, {
      method: 'POST',
      body: JSON.stringify(seriesData)
    });
  },

  loadStreamerSeriesSave: async (streamerId, seriesId) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}/series-saves/${seriesId}/load`, {
      method: 'POST'
    });
  },

  deleteStreamerSeriesSave: async (streamerId, seriesId) => {
    return await apiRequest(`/api/moderator/streamers/${streamerId}/series-saves/${seriesId}`, {
      method: 'DELETE'
    });
  }
};

// Utility functions for common operations
export const utils = {
  // Format error messages for display
  formatErrorMessage: (error) => {
    if (error instanceof APIError) {
      return error.message;
    }
    return 'An unexpected error occurred';
  },

  // Check if user is authenticated
  isAuthenticated: () => {
    return !!localStorage.getItem('authToken');
  },

  // Handle authentication errors
  handleAuthError: (error) => {
    if (error instanceof APIError && (error.status === 401 || error.status === 403)) {
      localStorage.removeItem('authToken');
      window.location.href = '/auth/twitch';
    }
  },

  // Debounce function for search/input
  debounce: (func, wait) => {
    let timeout;
    return function executedFunction(...args) {
      const later = () => {
        clearTimeout(timeout);
        func(...args);
      };
      clearTimeout(timeout);
      timeout = setTimeout(later, wait);
    };
  }
};

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

  // Get user diagnostics
  getUserDiagnostics: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/users/diagnostics');
  },

  // Delete broken user
  deleteBrokenUser: async (partitionKey, rowKey) => {
    return await makeAuthenticatedJsonRequest(
      `/api/admin/users/broken/${encodeURIComponent(partitionKey)}/${encodeURIComponent(rowKey)}`,
      { method: 'DELETE' }
    );
  },

  // Get admin stats
  getStats: async () => {
    return await makeAuthenticatedJsonRequest('/api/admin/stats');
  },

  // Toggle user status
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

  // Delete user
  deleteUser: async (userId) => {
    return await makeAuthenticatedJsonRequest(`/api/admin/users/${userId}`, {
      method: 'DELETE'
    });
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

  // Utilities
  debounce,

  // Error class
  APIError
};

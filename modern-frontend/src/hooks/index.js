/**
 * Custom React Hooks for Common Patterns
 * Reusable hooks for state management, API calls, and UI interactions
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { notificationAPI, userAPI, counterAPI, APIError, utils } from '../utils/apiHelpers';

/**
 * Hook for managing notification settings
 */
export const useNotificationSettings = (userId) => {
  const [settings, setSettings] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [saving, setSaving] = useState(false);

  // Load settings
  const loadSettings = useCallback(async () => {
    if (!userId) return;

    setLoading(true);
    setError(null);

    try {
      const data = await notificationAPI.getDiscordSettings(userId);
      setSettings(data?.discordSettings || null);
    } catch (err) {
      setError(utils.formatErrorMessage(err));
      utils.handleAuthError(err);
    } finally {
      setLoading(false);
    }
  }, [userId]);

  // Save settings
  const saveSettings = useCallback(async (newSettings) => {
    setSaving(true);
    setError(null);

    try {
      await notificationAPI.updateDiscordSettings(userId, newSettings);
      setSettings(newSettings?.discordSettings);
      return { success: true };
    } catch (err) {
      const errorMessage = utils.formatErrorMessage(err);
      setError(errorMessage);
      utils.handleAuthError(err);
      return { success: false, error: errorMessage };
    } finally {
      setSaving(false);
    }
  }, [userId]);

  // Test webhook
  const testWebhook = useCallback(async (webhookUrl) => {
    try {
      await notificationAPI.testDiscordWebhook(webhookUrl);
      return { success: true };
    } catch (err) {
      const errorMessage = utils.formatErrorMessage(err);
      return { success: false, error: errorMessage };
    }
  }, []);

  // Load settings when userId changes
  useEffect(() => {
    loadSettings();
  }, [loadSettings]);

  return {
    settings,
    loading,
    error,
    saving,
    saveSettings,
    testWebhook,
    reloadSettings: loadSettings
  };
};

/**
 * Hook for managing counter state
 */
export const useCounters = () => {
  const [counters, setCounters] = useState({ deaths: 0, swears: 0 });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  // Load counters
  const loadCounters = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const data = await counterAPI.getCounters();
      setCounters(data);
    } catch (err) {
      setError(utils.formatErrorMessage(err));
      utils.handleAuthError(err);
    } finally {
      setLoading(false);
    }
  }, []);

  // Counter operations
  const incrementDeaths = useCallback(async () => {
    try {
      const data = await counterAPI.incrementDeaths();
      setCounters(data);
      return { success: true };
    } catch (err) {
      setError(utils.formatErrorMessage(err));
      return { success: false, error: utils.formatErrorMessage(err) };
    }
  }, []);

  const decrementDeaths = useCallback(async () => {
    try {
      const data = await counterAPI.decrementDeaths();
      setCounters(data);
      return { success: true };
    } catch (err) {
      setError(utils.formatErrorMessage(err));
      return { success: false, error: utils.formatErrorMessage(err) };
    }
  }, []);

  const incrementSwears = useCallback(async () => {
    try {
      const data = await counterAPI.incrementSwears();
      setCounters(data);
      return { success: true };
    } catch (err) {
      setError(utils.formatErrorMessage(err));
      return { success: false, error: utils.formatErrorMessage(err) };
    }
  }, []);

  const decrementSwears = useCallback(async () => {
    try {
      const data = await counterAPI.decrementSwears();
      setCounters(data);
      return { success: true };
    } catch (err) {
      setError(utils.formatErrorMessage(err));
      return { success: false, error: utils.formatErrorMessage(err) };
    }
  }, []);

  const resetCounters = useCallback(async () => {
    try {
      const data = await counterAPI.resetCounters();
      setCounters(data);
      return { success: true };
    } catch (err) {
      setError(utils.formatErrorMessage(err));
      return { success: false, error: utils.formatErrorMessage(err) };
    }
  }, []);

  // Load counters on mount
  useEffect(() => {
    loadCounters();
  }, [loadCounters]);

  return {
    counters,
    loading,
    error,
    incrementDeaths,
    decrementDeaths,
    incrementSwears,
    decrementSwears,
    resetCounters,
    reloadCounters: loadCounters
  };
};

/**
 * Hook for managing user data
 */
export const useUserData = (userId) => {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const loadUser = useCallback(async () => {
    if (!userId) return;

    setLoading(true);
    setError(null);

    try {
      const userData = await userAPI.getUser(userId);
      setUser(userData);
    } catch (err) {
      setError(utils.formatErrorMessage(err));
      utils.handleAuthError(err);
    } finally {
      setLoading(false);
    }
  }, [userId]);

  const updateUserFeatures = useCallback(async (features) => {
    try {
      await userAPI.updateUserFeatures(userId, features);
      setUser(current => ({ ...current, features }));
      return { success: true };
    } catch (err) {
      const errorMessage = utils.formatErrorMessage(err);
      setError(errorMessage);
      return { success: false, error: errorMessage };
    }
  }, [userId]);

  useEffect(() => {
    loadUser();
  }, [loadUser]);

  return {
    user,
    loading,
    error,
    updateUserFeatures,
    reloadUser: loadUser
  };
};

/**
 * Hook for managing WebSocket connections
 */
export const useWebSocket = (userId) => {
  const [connected, setConnected] = useState(false);
  const wsRef = useRef(null);
  const listenersRef = useRef(new Map());

  // Initialize WebSocket
  useEffect(() => {
    if (!userId) return;

    import('../utils/apiHelpers').then(({ WebSocketManager }) => {
      wsRef.current = new WebSocketManager(userId);

      // Listen for connection status
      wsRef.current.on('connected', () => setConnected(true));
      wsRef.current.on('disconnected', () => setConnected(false));

      wsRef.current.connect();
    });

    return () => {
      if (wsRef.current) {
        wsRef.current.disconnect();
      }
    };
  }, [userId]);

  // Subscribe to events
  const subscribe = useCallback((eventType, callback) => {
    if (wsRef.current) {
      wsRef.current.on(eventType, callback);

      // Track listeners for cleanup
      if (!listenersRef.current.has(eventType)) {
        listenersRef.current.set(eventType, []);
      }
      listenersRef.current.get(eventType).push(callback);
    }
  }, []);

  // Unsubscribe from events
  const unsubscribe = useCallback((eventType, callback) => {
    if (wsRef.current) {
      wsRef.current.off(eventType, callback);

      // Remove from tracked listeners
      const listeners = listenersRef.current.get(eventType);
      if (listeners) {
        const index = listeners.indexOf(callback);
        if (index > -1) {
          listeners.splice(index, 1);
        }
      }
    }
  }, []);

  // Send message
  const send = useCallback((data) => {
    if (wsRef.current) {
      wsRef.current.send(data);
    }
  }, []);

  return {
    connected,
    subscribe,
    unsubscribe,
    send
  };
};

/**
 * Hook for managing form state with validation
 */
export const useFormState = (initialState, validationRules = {}) => {
  const [values, setValues] = useState(initialState);
  const [errors, setErrors] = useState({});
  const [touched, setTouchedState] = useState({});

  // Update field value
  const setValue = useCallback((field, value) => {
    setValues(current => ({ ...current, [field]: value }));

    // Clear error when user starts typing
    if (errors[field]) {
      setErrors(current => ({ ...current, [field]: null }));
    }
  }, [errors]);

  // Mark field as touched
  const setTouched = useCallback((field) => {
    setTouchedState(current => ({ ...current, [field]: true }));
  }, []);

  // Validate form
  const validate = useCallback(() => {
    const newErrors = {};

    Object.keys(validationRules).forEach(field => {
      const rule = validationRules[field];
      const value = values[field];

      if (rule.required && (!value || value.toString().trim() === '')) {
        newErrors[field] = `${field} is required`;
      } else if (rule.minLength && value && value.length < rule.minLength) {
        newErrors[field] = `${field} must be at least ${rule.minLength} characters`;
      } else if (rule.pattern && value && !rule.pattern.test(value)) {
        newErrors[field] = rule.message || `${field} format is invalid`;
      } else if (rule.custom && value) {
        const customError = rule.custom(value, values);
        if (customError) {
          newErrors[field] = customError;
        }
      }
    });

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  }, [values, validationRules]);

  // Reset form
  const reset = useCallback(() => {
    setValues(initialState);
    setErrors({});
    setTouchedState({});
  }, [initialState]);

  return {
    values,
    errors,
    touched,
    setValue,
    setTouched,
    validate,
    reset,
    isValid: Object.keys(errors).length === 0
  };
};

/**
 * Hook for debounced values
 */
export const useDebounce = (value, delay) => {
  const [debouncedValue, setDebouncedValue] = useState(value);

  useEffect(() => {
    const handler = setTimeout(() => {
      setDebouncedValue(value);
    }, delay);

    return () => {
      clearTimeout(handler);
    };
  }, [value, delay]);

  return debouncedValue;
};

/**
 * Hook for managing loading states
 */
export const useLoading = () => {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const withLoading = useCallback(async (asyncFunction) => {
    setLoading(true);
    setError(null);

    try {
      const result = await asyncFunction();
      return result;
    } catch (err) {
      setError(utils.formatErrorMessage(err));
      utils.handleAuthError(err);
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const clearError = useCallback(() => {
    setError(null);
  }, []);

  return {
    loading,
    error,
    withLoading,
    clearError
  };
};

/**
 * Hook for managing toast notifications
 */
export const useToast = () => {
  const [toasts, setToasts] = useState([]);

  const addToast = useCallback((message, type = 'info', duration = 5000) => {
    const id = Date.now();
    const toast = { id, message, type, duration };

    setToasts(current => [...current, toast]);

    if (duration > 0) {
      setTimeout(() => {
        setToasts(current => current.filter(t => t.id !== id));
      }, duration);
    }

    return id;
  }, []);

  const removeToast = useCallback((id) => {
    setToasts(current => current.filter(t => t.id !== id));
  }, []);

  const clearAllToasts = useCallback(() => {
    setToasts([]);
  }, []);

  return {
    toasts,
    addToast,
    removeToast,
    clearAllToasts,
    success: (message, duration) => addToast(message, 'success', duration),
    error: (message, duration) => addToast(message, 'error', duration),
    warning: (message, duration) => addToast(message, 'warning', duration),
    info: (message, duration) => addToast(message, 'info', duration)
  };
};

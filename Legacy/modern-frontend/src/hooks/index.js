/**
 * Custom React Hooks for Common Patterns
 * Reusable hooks for state management, API calls, and UI interactions
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { userAPI, counterAPI, APIError, debounce, handleAuthError } from '../utils/authUtils';

/**
 * Hook for managing loading states
 */
export const useLoading = (initialState = false) => {
  const [loading, setLoading] = useState(initialState);

  const withLoading = useCallback(async (asyncFunction) => {
    setLoading(true);
    try {
      const result = await asyncFunction();
      return result;
    } finally {
      setLoading(false);
    }
  }, []);

  return { loading, setLoading, withLoading };
};

/**
 * Hook for managing toast notifications
 */
export const useToast = () => {
  const [toasts, setToasts] = useState([]);

  const addToast = useCallback((message, type = 'info', duration = 5000) => {
    const id = Date.now() + Math.random();
    const toast = { id, message, type };

    setToasts(prev => [...prev, toast]);

    if (duration > 0) {
      setTimeout(() => {
        setToasts(prev => prev.filter(t => t.id !== id));
      }, duration);
    }

    return id;
  }, []);

  const removeToast = useCallback((id) => {
    setToasts(prev => prev.filter(t => t.id !== id));
  }, []);

  const success = useCallback((message) => addToast(message, 'success'), [addToast]);
  const error = useCallback((message) => addToast(message, 'error'), [addToast]);
  const warning = useCallback((message) => addToast(message, 'warning'), [addToast]);
  const info = useCallback((message) => addToast(message, 'info'), [addToast]);

  return { toasts, addToast, removeToast, success, error, warning, info };
};

/**
 * Hook for managing user data
 */
export const useUserData = () => {
  const [userData, setUserData] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const loadUserData = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const data = await userAPI.getCurrentUser();
      setUserData(data);
      return data;
    } catch (err) {
      const errorMessage = err instanceof APIError ? err.message : 'Failed to load user data';
      setError(errorMessage);
      handleAuthError(err, 'loading user data');
      return null;
    } finally {
      setLoading(false);
    }
  }, []);

  const updateUserData = useCallback(async (updates) => {
    try {
      const updatedData = await userAPI.updateUser(updates);
      setUserData(updatedData);
      return { success: true, data: updatedData };
    } catch (err) {
      const errorMessage = err instanceof APIError ? err.message : 'Failed to update user data';
      setError(errorMessage);
      handleAuthError(err, 'updating user data');
      return { success: false, error: errorMessage };
    }
  }, []);

  return {
    userData,
    loading,
    error,
    loadUserData,
    updateUserData,
    setUserData,
    clearError: () => setError(null)
  };
};

/**
 * Hook for managing counter data
 */
export const useCounters = () => {
  const [counters, setCounters] = useState({ deaths: 0, swears: 0 });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const loadCounters = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const data = await counterAPI.getCounters();
      setCounters(data);
      return data;
    } catch (err) {
      const errorMessage = err instanceof APIError ? err.message : 'Failed to load counters';
      setError(errorMessage);
      handleAuthError(err, 'loading counters');
      return null;
    } finally {
      setLoading(false);
    }
  }, []);

  const updateCounter = useCallback(async (type, value) => {
    try {
      const updatedCounters = await counterAPI.updateCounter(type, value);
      setCounters(updatedCounters);
      return { success: true, data: updatedCounters };
    } catch (err) {
      const errorMessage = err instanceof APIError ? err.message : 'Failed to update counter';
      setError(errorMessage);
      handleAuthError(err, 'updating counter');
      return { success: false, error: errorMessage };
    }
  }, []);

  const resetCounters = useCallback(async () => {
    try {
      const resetData = await counterAPI.resetCounters();
      setCounters(resetData);
      return { success: true, data: resetData };
    } catch (err) {
      const errorMessage = err instanceof APIError ? err.message : 'Failed to reset counters';
      setError(errorMessage);
      handleAuthError(err, 'resetting counters');
      return { success: false, error: errorMessage };
    }
  }, []);

  return {
    counters,
    loading,
    error,
    loadCounters,
    updateCounter,
    resetCounters,
    setCounters,
    clearError: () => setError(null)
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
 * Hook for managing async operations with status
 */
export const useAsync = (asyncFunction, immediate = true) => {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(immediate);
  const [error, setError] = useState(null);

  const execute = useCallback(async (...args) => {
    setLoading(true);
    setError(null);

    try {
      const result = await asyncFunction(...args);
      setData(result);
      return result;
    } catch (err) {
      const errorMessage = err instanceof APIError ? err.message : 'Operation failed';
      setError(errorMessage);
      handleAuthError(err, 'async operation');
      throw err;
    } finally {
      setLoading(false);
    }
  }, [asyncFunction]);

  useEffect(() => {
    if (immediate) {
      execute();
    }
  }, [execute, immediate]);

  return { data, loading, error, execute, setData, clearError: () => setError(null) };
};

/**
 * Hook for managing form state
 */
export const useFormState = (initialState = {}) => {
  const [formState, setFormState] = useState(initialState);
  const [isDirty, setIsDirty] = useState(false);

  const updateField = useCallback((field, value) => {
    setFormState(prev => ({
      ...prev,
      [field]: value
    }));
    setIsDirty(true);
  }, []);

  const updateFields = useCallback((updates) => {
    setFormState(prev => ({
      ...prev,
      ...updates
    }));
    setIsDirty(true);
  }, []);

  const resetForm = useCallback((newState = initialState) => {
    setFormState(newState);
    setIsDirty(false);
  }, [initialState]);

  const clearForm = useCallback(() => {
    setFormState(initialState);
    setIsDirty(false);
  }, [initialState]);

  return {
    formState,
    isDirty,
    updateField,
    updateFields,
    resetForm,
    clearForm,
    setFormState
  };
};

// Export Discord settings hook
export { useDiscordSettings } from './useDiscordSettings';

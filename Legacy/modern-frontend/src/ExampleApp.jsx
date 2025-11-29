import React from 'react';
import UserManagementModalNew from './components/UserManagementModalNew';
import { useCounters, useToast } from './hooks';
import { ActionButton } from './components/ui/CommonControls';
import './styles/CommonControls.css';

/**
 * Example App Component showing how to use the new utilities
 */
function ExampleApp() {
  const {
    counters,
    loading: countersLoading,
    incrementDeaths,
    incrementSwears,
    resetCounters
  } = useCounters();

  const toast = useToast();

  // Example user data
  const exampleUser = {
    twitchUserId: '123456789',
    username: 'testuser',
    displayName: 'Test User',
    features: {
      discordNotifications: true,
      streamOverlay: true,
      chatCommands: false
    }
  };

  const handleCounterAction = async (action, actionName) => {
    const result = await action();
    if (result.success) {
      toast.success(`${actionName} successful!`);
    } else {
      toast.error(`${actionName} failed: ${result.error}`);
    }
  };

  return (
    <div className="example-app">
      <h1>🎮 OmniAsylum Stream Counter - New UI Example</h1>

      {/* Counter Display */}
      <div className="counter-display">
        <h2>Stream Counters</h2>
        <div className="counters-grid">
          <div className="counter-item">
            <span className="counter-icon">💀</span>
            <span className="counter-value">{counters.deaths}</span>
            <span className="counter-label">Deaths</span>
          </div>
          <div className="counter-item">
            <span className="counter-icon">🤬</span>
            <span className="counter-value">{counters.swears}</span>
            <span className="counter-label">Swears</span>
          </div>
        </div>

        <div className="counter-actions">
          <ActionButton
            onClick={() => handleCounterAction(incrementDeaths, 'Death increment')}
            disabled={countersLoading}
            variant="danger"
            icon="💀"
          >
            Add Death
          </ActionButton>

          <ActionButton
            onClick={() => handleCounterAction(incrementSwears, 'Swear increment')}
            disabled={countersLoading}
            variant="warning"
            icon="🤬"
          >
            Add Swear
          </ActionButton>

          <ActionButton
            onClick={() => handleCounterAction(resetCounters, 'Counter reset')}
            disabled={countersLoading}
            variant="secondary"
            icon="🔄"
          >
            Reset All
          </ActionButton>
        </div>
      </div>

      {/* User Management Example */}
      <div className="user-management-example">
        <h2>User Management</h2>
        <p>This demonstrates the new UserManagementModal with helper functions:</p>

        <UserManagementModalNew
          user={exampleUser}
          onClose={() => console.log('Modal closed')}
          onUpdate={(updatedUser) => console.log('User updated:', updatedUser)}
        />
      </div>

      {/* Toast Container */}
      <div className="toast-container">
        {toast.toasts.map(toastItem => (
          <div
            key={toastItem.id}
            className={`toast toast-${toastItem.type}`}
            onClick={() => toast.removeToast(toastItem.id)}
          >
            <span>{toastItem.message}</span>
            <button
              className="toast-close"
              onClick={(e) => {
                e.stopPropagation();
                toast.removeToast(toastItem.id);
              }}
            >
              ×
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}

export default ExampleApp;

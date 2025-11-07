# Frontend Helper Functions and Utilities

This document outlines the new helper functions, factories, and reusable UI components created for the OmniAsylum Stream Counter frontend.

## 📁 File Structure

```text
modern-frontend/src/
├── utils/
│   ├── notificationHelpers.js    # Notification settings factories & helpers
│   └── apiHelpers.js             # API calls and WebSocket management
├── hooks/
│   └── index.js                  # Custom React hooks
├── components/
│   ├── ui/
│   │   └── CommonControls.jsx    # Reusable UI components
│   ├── NotificationSettings.jsx  # New notification settings component
│   └── UserManagementModalNew.jsx # Example of using new utilities
├── styles/
│   └── CommonControls.css        # CSS for reusable components
└── ExampleApp.jsx                # Example implementation
```

## 🛠️ Helper Functions & Factories

### Notification Helpers (`utils/notificationHelpers.js`)

Factory functions for creating and managing notification settings:

```javascript
import {
  createDefaultNotificationSettings,
  createNotificationTypes,
  createTemplateStyles,
  createNotificationHandlers,
  updateNotificationSetting,
  parseThresholdString
} from '../utils/notificationHelpers';

// Create default settings
const defaultSettings = createDefaultNotificationSettings();

// Get notification types with metadata
const notificationTypes = createNotificationTypes();

// Create handlers for a React component
const handlers = createNotificationHandlers(setSettings);
handlers.updateDiscordNotification('death_milestone', true);
```

### API Helpers (`utils/apiHelpers.js`)

Centralized API calls with consistent error handling:

```javascript
import { notificationAPI, userAPI, counterAPI, WebSocketManager } from '../utils/apiHelpers';

// Notification API
const settings = await notificationAPI.getDiscordSettings(userId);
await notificationAPI.updateDiscordSettings(userId, newSettings);
await notificationAPI.testDiscordWebhook(webhookUrl);

// User API
const user = await userAPI.getUser(userId);
await userAPI.updateUserFeatures(userId, features);

// Counter API
const counters = await counterAPI.getCounters();
await counterAPI.incrementDeaths();

// WebSocket
const wsManager = new WebSocketManager(userId);
wsManager.connect();
wsManager.on('counterUpdate', (data) => console.log('Counter updated:', data));
```

## 🎣 Custom Hooks

### useNotificationSettings

Manages notification settings state and API calls:

```javascript
import { useNotificationSettings } from '../hooks';

function MyComponent({ userId }) {
  const {
    settings,
    loading,
    saving,
    saveSettings,
    testWebhook,
    error
  } = useNotificationSettings(userId);

  const handleSave = async (newSettings) => {
    const result = await saveSettings(newSettings);
    if (result.success) {
      console.log('Saved successfully!');
    }
  };

  return (
    <div>
      {loading && <div>Loading...</div>}
      {error && <div>Error: {error}</div>}
      {/* Your UI here */}
    </div>
  );
}
```

### useCounters

Manages counter state and operations:

```javascript
import { useCounters } from '../hooks';

function CounterComponent() {
  const {
    counters,
    loading,
    error,
    incrementDeaths,
    incrementSwears,
    resetCounters
  } = useCounters();

  return (
    <div>
      <div>Deaths: {counters.deaths}</div>
      <div>Swears: {counters.swears}</div>
      <button onClick={incrementDeaths}>Add Death</button>
      <button onClick={incrementSwears}>Add Swear</button>
      <button onClick={resetCounters}>Reset</button>
    </div>
  );
}
```

### useToast

Manages toast notifications:

```javascript
import { useToast } from '../hooks';

function MyComponent() {
  const toast = useToast();

  const handleSuccess = () => {
    toast.success('Operation completed!', 3000);
  };

  const handleError = () => {
    toast.error('Something went wrong!');
  };

  return (
    <div>
      <button onClick={handleSuccess}>Show Success</button>
      <button onClick={handleError}>Show Error</button>

      {/* Toast container */}
      <div className="toast-container">
        {toast.toasts.map(item => (
          <div key={item.id} className={`toast toast-${item.type}`}>
            {item.message}
          </div>
        ))}
      </div>
    </div>
  );
}
```

### useFormState

Manages form state with validation:

```javascript
import { useFormState } from '../hooks';

function MyForm() {
  const {
    values,
    errors,
    setValue,
    validate,
    reset,
    isValid
  } = useFormState(
    { name: '', email: '' }, // Initial state
    {
      name: { required: true, minLength: 2 },
      email: {
        required: true,
        pattern: /^[^\s@]+@[^\s@]+\.[^\s@]+$/,
        message: 'Invalid email format'
      }
    }
  );

  const handleSubmit = (e) => {
    e.preventDefault();
    if (validate()) {
      console.log('Form is valid:', values);
    }
  };

  return (
    <form onSubmit={handleSubmit}>
      <input
        value={values.name}
        onChange={(e) => setValue('name', e.target.value)}
        placeholder="Name"
      />
      {errors.name && <span>{errors.name}</span>}

      <button type="submit" disabled={!isValid}>
        Submit
      </button>
    </form>
  );
}
```

## 🧩 Reusable UI Components

### ActionButton

Consistent button component with variants and loading states:

```javascript
import { ActionButton } from '../components/ui/CommonControls';

<ActionButton
  onClick={handleClick}
  variant="primary"    // primary, secondary, danger, success
  size="medium"        // small, medium, large
  loading={isLoading}
  disabled={isDisabled}
  icon="💾"
>
  Save Settings
</ActionButton>
```

### ToggleSwitch

Modern toggle switch component:

```javascript
import { ToggleSwitch } from '../components/ui/CommonControls';

<ToggleSwitch
  checked={isEnabled}
  onChange={(e) => setIsEnabled(e.target.checked)}
  label="Enable feature"
  size="medium"
  disabled={false}
/>
```

### FormSection

Collapsible form sections:

```javascript
import { FormSection } from '../components/ui/CommonControls';

<FormSection
  title="Settings"
  description="Configure your preferences"
  icon="⚙️"
  collapsible={true}
  defaultExpanded={true}
>
  {/* Your form content */}
</FormSection>
```

### StatusBadge

Status indicators:

```javascript
import { StatusBadge } from '../components/ui/CommonControls';

<StatusBadge status="success" icon="✅">
  Active
</StatusBadge>
```

### InputGroup

Form input wrapper with labels and errors:

```javascript
import { InputGroup } from '../components/ui/CommonControls';

<InputGroup
  label="Username"
  error={errors.username}
  required={true}
>
  <input
    value={username}
    onChange={(e) => setUsername(e.target.value)}
  />
</InputGroup>
```

## 🎨 Notification Settings Component

Complete component for managing Discord/channel notifications:

```javascript
import NotificationSettings from '../components/NotificationSettings';

<NotificationSettings
  user={user}
  initialSettings={settings}
  onSave={handleSave}
  onTest={handleTest}
  saving={isSaving}
  testing={isTesting}
/>
```

## 📱 Example Usage

See `ExampleApp.jsx` for a complete implementation showing:

- Counter management with `useCounters`
- Toast notifications with `useToast`
- Reusable UI components
- User management modal integration

## 🔧 Benefits

1. **Consistency**: All components use the same design system
2. **Maintainability**: Centralized logic in helpers and hooks
3. **Reusability**: Components can be used across different parts of the app
4. **Type Safety**: Clear interfaces and error handling
5. **Performance**: Optimized with React best practices (useCallback, useMemo)
6. **Accessibility**: Components include proper ARIA labels and keyboard navigation

## 🚀 Getting Started

1. Import the CSS:
```javascript
import '../styles/CommonControls.css';
```

2. Use hooks for state management:
```javascript
import { useNotificationSettings, useCounters, useToast } from '../hooks';
```

3. Use components for UI:
```javascript
import { ActionButton, ToggleSwitch, FormSection } from '../components/ui/CommonControls';
```

4. Use helpers for logic:
```javascript
import { createDefaultNotificationSettings, parseThresholdString } from '../utils/notificationHelpers';
```

This architecture provides a solid foundation for scalable React development with consistent UI patterns and centralized business logic.

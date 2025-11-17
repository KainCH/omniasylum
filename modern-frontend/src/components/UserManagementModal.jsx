import React, { useState, useEffect } from 'react'
import './UserManagementModal.css'
import './AdminDashboard.css'
import '../styles/CommonControls.css'
import { ToggleSwitch, ActionButton, FormSection, InputGroup, StatusBadge, NotificationTypeCard } from './ui/CommonControls'
import { useUserData, useNotificationSettings, useFormState, useToast, useLoading } from '../hooks'
import {
  createDefaultNotificationSettings,
  validateNotificationSettings,
  parseThresholdString
} from '../utils/notificationHelpers'
import { userAPI, adminAPI, APIError } from '../utils/authUtils'

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
  streamAlerts: true,
  discordNotifications: true
}

// Professional feature configuration
const getFeatureConfig = (featureKey) => {
  // Ensure featureKey is valid
  if (!featureKey || typeof featureKey !== 'string') {
    return {
      icon: '🔧',
      title: 'Unknown Feature',
      description: 'Feature configuration unavailable'
    }
  }

  const configs = {
    chatCommands: {
      icon: '💬',
      title: 'Chat Commands',
      description: 'Enable interactive chat commands for viewers to check counters and stats'
    },
    channelPoints: {
      icon: '🏆',
      title: 'Channel Points',
      description: 'Allow viewers to spend channel points to interact with stream counters'
    },
    autoClip: {
      icon: '🎬',
      title: 'Auto Clip',
      description: 'Automatically create Twitch clips when reaching milestone achievements'
    },
    streamOverlay: {
      icon: '📺',
      title: 'Stream Overlay',
      description: 'Display live counters and notifications directly on your stream'
    },
    discordWebhook: {
      icon: '🔔',
      title: 'Discord Webhook',
      description: 'Send automated notifications to your Discord server for milestones'
    },
    customCommands: {
      icon: '⚙️',
      title: 'Custom Commands',
      description: 'Create personalized chat commands with custom responses and actions'
    },
    analytics: {
      icon: '📊',
      title: 'Analytics',
      description: 'Track detailed statistics and generate insights about your stream'
    },
    webhooks: {
      icon: '🔗',
      title: 'Webhooks',
      description: 'Connect with external services and tools through custom webhooks'
    },
    bitsIntegration: {
      icon: '💎',
      title: 'Bits Integration',
      description: 'Enable Twitch Bits/Cheers integration for interactive stream features'
    },
    alertAnimations: {
      icon: '✨',
      title: 'Alert Animations',
      description: 'Add visual effects and animations to stream alerts and notifications'
    },
    streamAlerts: {
      icon: '🚨',
      title: 'Stream Alerts',
      description: 'Display customizable alerts for follows, subs, raids, and other Twitch events'
    },
    discordNotifications: {
      icon: '🔔',
      title: 'Discord Notifications',
      description: 'Send automated milestone and event notifications to your Discord server'
    }
  }

  return configs[featureKey] || {
    icon: '🔧',
    title: featureKey.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase()),
    description: 'Advanced feature configuration'
  }
}

const UserManagementModal = ({
  isOpen,
  onClose,
  users = [],
  onRefresh,
  onEditUser,
  onToggleUser,
  onToggleFeature,
  onDeleteUser,
  onShowOverlay,
  onShowAlerts,
  onShowDiscord,
  onShowSeries,
  // Legacy single-user props for backward compatibility
  user,
  onUpdate,
  token
}) => {
  const { showToast } = useToast()
  const { isLoading, withLoading } = useLoading()

  // Form state management using our new hooks
  const { values: formState, setValue: updateField, reset: resetForm } = useFormState({
    features: { ...defaultFeatures },
    overlaySettings: null,
    discordWebhook: ''
  })

  // Notification settings with our new helper - initialized with default
  const [notificationSettings, setNotificationSettings] = useState(() =>
    createDefaultNotificationSettings()
  )

  const updateNotificationSetting = (key, value) => {
    setNotificationSettings(prev => ({ ...prev, [key]: value }))
  }

  const [originalUser, setOriginalUser] = useState(null)
  const [message, setMessage] = useState({ text: '', type: '' })
  const [showAddUserForm, setShowAddUserForm] = useState(false)
  const [newUserData, setNewUserData] = useState({
    username: '',
    displayName: '',
    email: '',
    twitchUserId: ''
  })

  const showMessage = (text, type) => {
    setMessage({ text, type })
    setTimeout(() => setMessage({ text: '', type: '' }), 3000)
  }

  // Local function to reset notification settings to defaults
  const resetToDefaults = (newSettings = null) => {
    const defaults = newSettings || createDefaultNotificationSettings()
    updateField('notificationSettings', defaults)
  }

  // Load user data when component mounts or user changes
  useEffect(() => {
    if (user) {
      setOriginalUser({ ...user })
      loadUserData()
    }
  }, [user])

  const loadUserData = async () => {
    if (!user) return

    try {
      await withLoading(async () => {
        // Load features with safe access
        const userFeatures = user?.features
          ? (typeof user?.features === 'string'
              ? JSON.parse(user?.features)
              : user?.features)
          : {}
        updateField('features', { ...defaultFeatures, ...userFeatures })

        // Load overlay settings if enabled
        if (userFeatures?.streamOverlay) {
          try {
            const overlayData = await userAPI.getOverlaySettings(user?.twitchUserId)
            updateField('overlaySettings', overlayData)
          } catch (error) {
            console.error('Error loading overlay settings:', error)
            updateField('overlaySettings', { enabled: false })
          }
        } else {
          updateField('overlaySettings', null)
        }

        // Load Discord settings if enabled
        if (userFeatures?.discordWebhook) {
          try {
            // Load notification settings
            const discordData = await notificationAPI.getSettings(user?.twitchUserId)

            // Use our notification helper to create proper settings
            const settings = createDefaultNotificationSettings()
            Object.assign(settings, {
              enableChannelNotifications: discordData?.enableChannelNotifications || false,
              deathMilestoneEnabled: discordData?.deathMilestoneEnabled || false,
              swearMilestoneEnabled: discordData?.swearMilestoneEnabled || false,
              deathThresholds: discordData?.deathThresholds || settings?.deathThresholds || '',
              swearThresholds: discordData?.swearThresholds || settings?.swearThresholds || ''
            })

            setNotificationSettings(settings)

            // Load webhook URL
            const webhookData = await notificationAPI.getWebhook(user?.twitchUserId)
            updateField('discordWebhook', webhookData?.webhookUrl || '')
          } catch (error) {
            console.error('Error loading Discord settings:', error)
            setNotificationSettings(createDefaultNotificationSettings())
            updateField('discordWebhook', '')
          }
        } else {
          setNotificationSettings(createDefaultNotificationSettings())
          updateField('discordWebhook', '')
        }
      })
    } catch (error) {
      console.error('Error loading user data:', error)
      showMessage('Failed to load user data', 'error')
    }
  }

  const handleFeatureChange = (feature, value) => {
    const currentFeatures = formState?.features || { ...defaultFeatures }
    const newFeatures = { ...currentFeatures, [feature]: value }
    updateField('features', newFeatures)
  }

  const saveFeatures = async () => {
    try {
      await withLoading(async () => {
        // Merge current features with original user features to preserve any features not shown in UI
        const originalFeatures = originalUser?.features
          ? (typeof originalUser?.features === 'string'
              ? JSON.parse(originalUser?.features)
              : originalUser?.features)
          : {}
        const updatedFeatures = { ...originalFeatures, ...(formState?.features || {}) }

        await userAPI.updateFeatures(user?.twitchUserId, updatedFeatures)

        showMessage('Features updated successfully!', 'success')
        if (onUpdate) onUpdate()
      })
    } catch (error) {
      console.error('Error saving features:', error)
      showMessage('Failed to save features', 'error')
    }
  }

  const saveOverlaySettings = async () => {
    if (!formState?.overlaySettings) return

    try {
      await withLoading(async () => {
        await userAPI.updateOverlaySettings(user?.twitchUserId, formState?.overlaySettings)

        showMessage('Overlay settings saved!', 'success')
        if (onUpdate) onUpdate()
      })
    } catch (error) {
      console.error('Error saving overlay settings:', error)
      showMessage('Failed to save overlay settings', 'error')
    }
  }

  const saveDiscordSettings = async () => {
    try {
      await withLoading(async () => {
        // Validate webhook URL
        if (formState?.discordWebhook && !formState.discordWebhook.startsWith('https://discord.com/api/webhooks/')) {
          showMessage('Invalid Discord webhook URL format', 'error')
          return
        }

        // Validate notification settings
        const validationResult = validateNotificationSettings(notificationSettings)
        if (!validationResult?.isValid) {
          showMessage(validationResult?.errors?.[0] || 'Validation failed', 'error')
          return
        }

        // Save webhook URL
        await notificationAPI.updateWebhook(user?.twitchUserId, { webhookUrl: formState?.discordWebhook || '' })

        // Save notification settings
        await notificationAPI.updateSettings(user?.twitchUserId, notificationSettings)

        showMessage('All Discord settings saved successfully!', 'success')
        if (onUpdate) onUpdate()
      })
    } catch (error) {
      console.error('Error saving Discord settings:', error)
      showMessage('Failed to save Discord settings', 'error')
    }
  }

  const testDiscordWebhook = async () => {
    if (!formState?.discordWebhook) {
      showMessage('Please enter a Discord webhook URL first', 'error')
      return
    }

    if (!formState?.discordWebhook?.startsWith('https://discord.com/api/webhooks/')) {
      showMessage('Invalid Discord webhook URL format', 'error')
      return
    }

    try {
      await withLoading(async () => {
        await notificationAPI.testWebhook(user?.twitchUserId, { webhookUrl: formState?.discordWebhook || '' })

        showMessage('Test message sent to Discord!', 'success')
      })
    } catch (error) {
      console.error('Error testing Discord webhook:', error)
      showMessage(`Failed to test webhook: ${error?.message || 'Unknown error'}`, 'error')
    }
  }

  const handleCreateUser = async () => {
    // Validate required fields
    if (!newUserData.username || !newUserData.twitchUserId) {
      showToast('Username and Twitch User ID are required', 'error')
      return
    }

    try {
      await withLoading(async () => {
        const result = await adminAPI.createUser(newUserData)
        showToast('User created successfully!', 'success')

        // Reset form and hide it
        setNewUserData({
          username: '',
          displayName: '',
          email: '',
          twitchUserId: ''
        })
        setShowAddUserForm(false)

        // Refresh user list
        if (onRefresh) {
          onRefresh()
        }
      })
    } catch (error) {
      console.error('Error creating user:', error)
      showToast(`Failed to create user: ${error?.message || 'Unknown error'}`, 'error')
    }
  }

  const handleOverlayChange = (key, value) => {
    const currentSettings = formState?.overlaySettings || {}
    const newSettings = { ...currentSettings, [key]: value }
    updateField('overlaySettings', newSettings)
  }

  // If not open, don't render
  if (!isOpen) return null

  // If we have users array, render admin list view
  if (users && users.length >= 0 && !user) {
    return (
      <div className="modal-overlay" onClick={onClose}>
        <div className="modal-content" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '1200px', width: '95vw' }}>
          <div className="modal-header">
            <h2>👥 User Management</h2>
            <button
              onClick={onClose}
              className="close-btn"
              aria-label="Close"
            >
              ×
            </button>
          </div>
          <div className="modal-body">
            <div style={{ marginBottom: '20px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <h3 style={{ color: '#fff', margin: 0 }}>Manage Users ({users.length})</h3>
              <div style={{ display: 'flex', gap: '10px' }}>
                <button
                  onClick={() => setShowAddUserForm(true)}
                  style={{
                    padding: '8px 16px',
                    background: '#00ff88',
                    color: 'black',
                    border: 'none',
                    borderRadius: '4px',
                    cursor: 'pointer',
                    fontWeight: 'bold'
                  }}
                >
                  ➕ Add User
                </button>
                <button
                  onClick={onRefresh}
                  style={{
                    padding: '8px 16px',
                    background: '#9146ff',
                    color: 'white',
                    border: 'none',
                    borderRadius: '4px',
                    cursor: 'pointer'
                  }}
                >
                  🔄 Refresh
                </button>
              </div>
            </div>

            {/* Add User Form */}
            {showAddUserForm && (
              <div style={{
                background: '#1a1a1a',
                padding: '20px',
                borderRadius: '8px',
                border: '2px solid #00ff88',
                marginBottom: '20px'
              }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '15px' }}>
                  <h4 style={{ color: '#00ff88', margin: 0 }}>➕ Add New User</h4>
                  <button
                    onClick={() => setShowAddUserForm(false)}
                    style={{
                      background: 'transparent',
                      border: 'none',
                      color: '#ccc',
                      cursor: 'pointer',
                      fontSize: '18px'
                    }}
                  >
                    ×
                  </button>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '15px', marginBottom: '15px' }}>
                  <div>
                    <label style={{ color: '#fff', display: 'block', marginBottom: '5px' }}>
                      Username (required) *
                    </label>
                    <input
                      type="text"
                      value={newUserData.username}
                      onChange={(e) => setNewUserData(prev => ({ ...prev, username: e.target.value }))}
                      placeholder="Enter Twitch username"
                      style={{
                        width: '100%',
                        padding: '8px',
                        border: '1px solid #444',
                        borderRadius: '4px',
                        background: '#2a2a2a',
                        color: '#fff'
                      }}
                    />
                  </div>

                  <div>
                    <label style={{ color: '#fff', display: 'block', marginBottom: '5px' }}>
                      Twitch User ID (required) *
                    </label>
                    <input
                      type="text"
                      value={newUserData.twitchUserId}
                      onChange={(e) => setNewUserData(prev => ({ ...prev, twitchUserId: e.target.value }))}
                      placeholder="Enter Twitch User ID"
                      style={{
                        width: '100%',
                        padding: '8px',
                        border: '1px solid #444',
                        borderRadius: '4px',
                        background: '#2a2a2a',
                        color: '#fff'
                      }}
                    />
                  </div>

                  <div>
                    <label style={{ color: '#fff', display: 'block', marginBottom: '5px' }}>
                      Display Name
                    </label>
                    <input
                      type="text"
                      value={newUserData.displayName}
                      onChange={(e) => setNewUserData(prev => ({ ...prev, displayName: e.target.value }))}
                      placeholder="Enter display name"
                      style={{
                        width: '100%',
                        padding: '8px',
                        border: '1px solid #444',
                        borderRadius: '4px',
                        background: '#2a2a2a',
                        color: '#fff'
                      }}
                    />
                  </div>

                  <div>
                    <label style={{ color: '#fff', display: 'block', marginBottom: '5px' }}>
                      Email
                    </label>
                    <input
                      type="email"
                      value={newUserData.email}
                      onChange={(e) => setNewUserData(prev => ({ ...prev, email: e.target.value }))}
                      placeholder="Enter email address"
                      style={{
                        width: '100%',
                        padding: '8px',
                        border: '1px solid #444',
                        borderRadius: '4px',
                        background: '#2a2a2a',
                        color: '#fff'
                      }}
                    />
                  </div>
                </div>

                <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
                  <button
                    onClick={() => setShowAddUserForm(false)}
                    style={{
                      padding: '8px 16px',
                      background: '#666',
                      color: 'white',
                      border: 'none',
                      borderRadius: '4px',
                      cursor: 'pointer'
                    }}
                  >
                    Cancel
                  </button>
                  <button
                    onClick={handleCreateUser}
                    disabled={isLoading || !newUserData.username || !newUserData.twitchUserId}
                    style={{
                      padding: '8px 16px',
                      background: (!newUserData.username || !newUserData.twitchUserId) ? '#666' : '#00ff88',
                      color: (!newUserData.username || !newUserData.twitchUserId) ? '#999' : 'black',
                      border: 'none',
                      borderRadius: '4px',
                      cursor: (!newUserData.username || !newUserData.twitchUserId) ? 'not-allowed' : 'pointer',
                      fontWeight: 'bold'
                    }}
                  >
                    {isLoading ? '⏳ Creating...' : '✅ Create User'}
                  </button>
                </div>
              </div>
            )}

            <div style={{ display: 'grid', gap: '15px' }}>
              {users.map(user => (
                <div key={user.userId} style={{
                  background: '#2a2a2a',
                  padding: '20px',
                  borderRadius: '8px',
                  border: '2px solid #444',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between'
                }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
                    <img
                      src={user.profileImageUrl || '/default-avatar.png'}
                      alt={user.displayName}
                      style={{ width: '48px', height: '48px', borderRadius: '50%' }}
                    />
                    <div>
                      <h4 style={{ color: '#fff', margin: '0 0 5px 0' }}>
                        {user.displayName || user.username}
                      </h4>
                      <div style={{ color: '#aaa', fontSize: '12px' }}>
                        {user.email} • {user.isActive ? '✅ Active' : '❌ Inactive'}
                      </div>
                    </div>
                  </div>

                  <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
                    <button
                      onClick={() => onEditUser(user)}
                      style={{
                        padding: '6px 12px',
                        background: '#007bff',
                        color: 'white',
                        border: 'none',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        fontSize: '12px'
                      }}
                    >
                      ✏️ Edit
                    </button>

                    <button
                      onClick={() => onToggleUser(user.userId, user.isActive)}
                      style={{
                        padding: '6px 12px',
                        background: user.isActive ? '#dc3545' : '#28a745',
                        color: 'white',
                        border: 'none',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        fontSize: '12px'
                      }}
                    >
                      {user.isActive ? '❌ Deactivate' : '✅ Activate'}
                    </button>

                    {onDeleteUser && (
                      <button
                        onClick={() => onDeleteUser(user)}
                        style={{
                          padding: '6px 12px',
                          background: '#e74c3c',
                          color: 'white',
                          border: 'none',
                          borderRadius: '4px',
                          cursor: 'pointer',
                          fontSize: '12px',
                          transition: 'background-color 0.2s'
                        }}
                        onMouseOver={(e) => e.target.style.backgroundColor = '#c0392b'}
                        onMouseOut={(e) => e.target.style.backgroundColor = '#e74c3c'}
                        title="Permanently delete this user and all their data"
                      >
                        🗑️ Delete
                      </button>
                    )}

                    <button
                      onClick={() => onShowOverlay(user)}
                      style={{
                        padding: '6px 12px',
                        background: '#9146ff',
                        color: 'white',
                        border: 'none',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        fontSize: '12px'
                      }}
                    >
                      🎨 Overlay
                    </button>

                    <button
                      onClick={() => onShowAlerts(user)}
                      style={{
                        padding: '6px 12px',
                        background: '#fd7e14',
                        color: 'white',
                        border: 'none',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        fontSize: '12px'
                      }}
                    >
                      🚨 Alerts
                    </button>

                    <button
                      onClick={() => onShowDiscord(user)}
                      style={{
                        padding: '6px 12px',
                        background: '#5865f2',
                        color: 'white',
                        border: 'none',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        fontSize: '12px'
                      }}
                    >
                      🎮 Discord
                    </button>

                    <button
                      onClick={() => onShowSeries(user)}
                      style={{
                        padding: '6px 12px',
                        background: '#6f42c1',
                        color: 'white',
                        border: 'none',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        fontSize: '12px'
                      }}
                    >
                      💾 Series
                    </button>
                  </div>
                </div>
              ))}

              {users.length === 0 && (
                <div style={{
                  textAlign: 'center',
                  padding: '40px',
                  color: '#aaa'
                }}>
                  <h4>No users found</h4>
                  <p>Users will appear here once they authenticate with the system.</p>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    )
  }

  // Single user edit mode (legacy)
  if (!user) return null

  return (
    <div className="user-management-modal-overlay" onClick={onClose}>
      <div className="user-management-modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div className="user-info">
            <img
              src={user?.profileImageUrl || 'data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNjQiIGhlaWdodD0iNjQiIHZpZXdCb3g9IjAgMCA2NCA2NCIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KPHJlY3Qgd2lkdGg9IjY0IiBoZWlnaHQ9IjY0IiBmaWxsPSIjNzI4OWRhIi8+Cjx0ZXh0IHg9IjMyIiB5PSI0MCIgZm9udC1mYW1pbHk9IkFyaWFsLCBzYW5zLXNlcmlmIiBmb250LXNpemU9IjI0IiBmaWxsPSJ3aGl0ZSIgdGV4dC1hbmNob3I9Im1pZGRsZSI+8J+RpDwvdGV4dD4KICA8L3N2Zz4='}
              alt={user?.displayName || 'User'}
              className="user-avatar"
            />
            <div>
              <h3>{user?.displayName || 'Unknown User'}</h3>
              <StatusBadge
                status={user?.isActive ? 'active' : 'inactive'}
                text={user?.isActive ? 'Active' : 'Inactive'}
              />
            </div>
          </div>
          <ActionButton
            variant="ghost"
            onClick={onClose}
            disabled={isLoading}
          >
            ✕
          </ActionButton>
        </div>

        {message.text && (
          <div className={`message ${message.type}`}>
            {message.text}
          </div>
        )}

        <div className="modal-body">
          {/* Feature Management Section */}
          <FormSection title="🎛️ Feature Management" collapsible defaultExpanded>
            <div className="professional-features-grid">
              {Object.entries(defaultFeatures || {}).map(([feature, defaultValue]) => {
                // Ensure feature is valid
                if (!feature || typeof feature !== 'string' || feature === 'templateStyle') return null

                const isEnabled = formState?.features?.[feature] ?? defaultValue
                const featureConfig = getFeatureConfig(feature)

                return (
                  <div key={feature} className={`professional-feature-card ${isEnabled ? 'enabled' : 'disabled'}`}>
                    <div className="feature-info-section">
                      <div className="feature-header">
                        <div className="feature-icon-container">
                          <span className="feature-icon">{featureConfig?.icon || '🔧'}</span>
                          <div className={`status-indicator ${isEnabled ? 'active' : 'inactive'}`}>
                            {isEnabled ? '✅' : '❌'}
                          </div>
                        </div>
                        <div className="feature-details">
                          <h4 className="feature-title">{featureConfig?.title || 'Unknown Feature'}</h4>
                          <p className="feature-description">{featureConfig?.description || 'Feature configuration unavailable'}</p>
                        </div>
                      </div>
                      <div className={`feature-status-badge ${isEnabled ? 'enabled' : 'disabled'}`}>
                        {isEnabled ? 'ENABLED' : 'DISABLED'}
                      </div>
                    </div>

                    <div className="professional-slider-container">
                      <div className={`professional-slider ${isEnabled ? 'enabled' : 'disabled'} ${isLoading ? 'loading' : ''}`}>
                        <input
                          type="checkbox"
                          id={`feature-${feature}`}
                          checked={isEnabled}
                          onChange={(e) => handleFeatureChange(feature, e.target.checked)}
                          disabled={isLoading}
                          className="slider-input"
                        />
                        <label htmlFor={`feature-${feature}`} className="slider-track">
                          <span className="slider-thumb">
                            <span className="slider-thumb-icon">
                              {isEnabled ? '●' : '○'}
                            </span>
                          </span>
                        </label>
                      </div>
                    </div>
                  </div>
                )
              })}
            </div>

            <div className="section-actions">
              <ActionButton
                variant="primary"
                onClick={saveFeatures}
                loading={isLoading}
                disabled={isLoading}
              >
                💾 Save Features
              </ActionButton>
            </div>
          </FormSection>

          {/* Stream Overlay Settings */}
          {formState?.features?.streamOverlay && (
            <FormSection title="📺 Stream Overlay Settings" collapsible>
              {formState?.overlaySettings && (
                <div className="overlay-settings">
                  <ToggleSwitch
                    id="overlay-enabled"
                    checked={formState?.overlaySettings?.enabled || false}
                    onChange={(checked) => handleOverlayChange('enabled', checked)}
                    label="Enable Stream Overlay"
                    disabled={isLoading}
                  />

                  <div className="section-actions">
                    <ActionButton
                      variant="primary"
                      onClick={saveOverlaySettings}
                      loading={isLoading}
                      disabled={isLoading}
                    >
                      💾 Save Overlay Settings
                    </ActionButton>
                  </div>
                </div>
              )}
            </FormSection>
          )}

          {/* Discord Notification Settings */}
          {formState?.features?.discordWebhook && (
            <FormSection title="🔔 Discord Notifications" collapsible>
              <div className="discord-settings">
                {/* Webhook URL Input */}
                <InputGroup
                  label="Discord Webhook URL"
                  type="url"
                  value={formState?.discordWebhook || ''}
                  onChange={(e) => updateField('discordWebhook', e.target.value)}
                  placeholder="https://discord.com/api/webhooks/..."
                  disabled={isLoading}
                  hint="Must start with https://discord.com/api/webhooks/"
                />

                {/* Notification Type Toggles */}
                <div className="notification-types">
                  <h4>Notification Types</h4>
                  <div className="notification-grid">
                    <NotificationTypeCard
                      notificationType={{
                        id: 'channelNotifications',
                        title: 'Twitch Chat Notifications',
                        description: 'Send notifications to Twitch chat',
                        icon: '💬',
                        supportsChannel: true,
                        supportsDiscord: false
                      }}
                      discordEnabled={false}
                      channelEnabled={notificationSettings?.enableChannelNotifications || false}
                      onDiscordChange={() => {}}
                      onChannelChange={(enabled) => updateNotificationSetting('enableChannelNotifications', enabled)}
                      disabled={isLoading}
                    />
                  </div>
                  <div className="discord-notification-info">
                    <p><strong>ℹ️ Discord Notifications:</strong> Automatically enabled when webhook URL is configured above.</p>
                  </div>
                </div>

                {/* Milestone Settings */}
                <div className="milestone-settings">
                  <h4>Milestone Notifications</h4>

                  <ToggleSwitch
                    id="death-milestones"
                    checked={notificationSettings?.deathMilestoneEnabled || false}
                    onChange={(checked) => updateNotificationSetting('deathMilestoneEnabled', checked)}
                    label="Death Milestones"
                    disabled={isLoading}
                  />

                  {notificationSettings?.deathMilestoneEnabled && (
                    <InputGroup
                      label="Death Thresholds"
                      value={notificationSettings?.deathThresholds || ''}
                      onChange={(e) => updateNotificationSetting('deathThresholds', e.target.value)}
                      placeholder="10,25,50,100,250,500,1000"
                      disabled={isLoading}
                      hint="Comma-separated numbers for milestone notifications"
                    />
                  )}

                  <ToggleSwitch
                    id="swear-milestones"
                    checked={notificationSettings?.swearMilestoneEnabled || false}
                    onChange={(checked) => updateNotificationSetting('swearMilestoneEnabled', checked)}
                    label="Swear Milestones"
                    disabled={isLoading}
                  />

                  {notificationSettings?.swearMilestoneEnabled && (
                    <InputGroup
                      label="Swear Thresholds"
                      value={notificationSettings?.swearThresholds || ''}
                      onChange={(e) => updateNotificationSetting('swearThresholds', e.target.value)}
                      placeholder="25,50,100,250,500,1000,2500"
                      disabled={isLoading}
                      hint="Comma-separated numbers for milestone notifications"
                    />
                  )}
                </div>

                {/* Action Buttons */}
                <div className="section-actions">
                  <ActionButton
                    variant="primary"
                    onClick={saveDiscordSettings}
                    loading={isLoading}
                    disabled={isLoading}
                  >
                    💾 Save All Settings
                  </ActionButton>

                  <ActionButton
                    variant="secondary"
                    onClick={testDiscordWebhook}
                    loading={isLoading}
                    disabled={isLoading || !formState?.discordWebhook}
                  >
                    🧪 Send Test
                  </ActionButton>
                </div>
              </div>
            </FormSection>
          )}
        </div>
      </div>
    </div>
  )
}

export default UserManagementModal

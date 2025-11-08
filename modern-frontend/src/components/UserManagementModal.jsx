import React, { useState, useEffect } from 'react'
import './UserManagementModal.css'
import { ToggleSwitch, ActionButton, FormSection, InputGroup, StatusBadge, NotificationTypeCard } from './ui/CommonControls'
import { useUserData, useNotificationSettings, useFormState, useToast, useLoading } from '../hooks'
import {
  createDefaultNotificationSettings,
  validateNotificationSettings,
  parseThresholdString
} from '../utils/notificationHelpers'
import { userAPI, notificationAPI, APIError } from '../utils/apiHelpers'

// Default feature flags for new users
const defaultFeatures = {
  chatCommands: true,
  channelPoints: false,
  autoClip: false,
  streamOverlay: false,
  discordWebhook: false,
  templateStyle: 'asylum_themed',
  customCommands: false,
  analytics: false,
  webhooks: false
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
    }
  }

  return configs[featureKey] || {
    icon: '🔧',
    title: featureKey.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase()),
    description: 'Advanced feature configuration'
  }
}

const UserManagementModal = ({ user, onClose, onUpdate, token }) => {
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
              enableDiscordNotifications: discordData?.enableDiscordNotifications || false,
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

        await userAPI.updateFeatures(user?.twitchUserId, { features: updatedFeatures })

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

  const handleOverlayChange = (key, value) => {
    const currentSettings = formState?.overlaySettings || {}
    const newSettings = { ...currentSettings, [key]: value }
    updateField('overlaySettings', newSettings)
  }

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
                      title="Discord Notifications"
                      description="Send notifications to Discord channel"
                      enabled={notificationSettings?.enableDiscordNotifications || false}
                      onChange={(enabled) => updateNotificationSetting('enableDiscordNotifications', enabled)}
                      icon="🔔"
                      disabled={isLoading}
                    />

                    <NotificationTypeCard
                      title="Twitch Chat Notifications"
                      description="Send notifications to Twitch chat"
                      enabled={notificationSettings?.enableChannelNotifications || false}
                      onChange={(enabled) => updateNotificationSetting('enableChannelNotifications', enabled)}
                      icon="💬"
                      disabled={isLoading}
                    />
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

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

const UserManagementModal = ({ user, onClose, onUpdate, token }) => {
  const { showToast } = useToast()
  const { isLoading, withLoading } = useLoading()

  // Form state management using our new hooks
  const { formState, updateField, resetForm } = useFormState({
    features: { ...defaultFeatures },
    overlaySettings: null,
    discordWebhook: ''
  })

  // Notification settings with our new helper
  const {
    notificationSettings,
    updateNotificationSetting,
    validateSettings,
    resetToDefaults
  } = useNotificationSettings()

  const [originalUser, setOriginalUser] = useState(null)
  const [message, setMessage] = useState({ text: '', type: '' })

  const showMessage = (text, type) => {
    setMessage({ text, type })
    setTimeout(() => setMessage({ text: '', type: '' }), 3000)
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
        // Load features
        updateField('features', { ...defaultFeatures, ...user.features })

        // Load overlay settings if enabled
        if (user.features?.streamOverlay) {
          try {
            const overlayData = await userAPI.getOverlaySettings(user.twitchUserId)
            updateField('overlaySettings', overlayData)
          } catch (error) {
            console.error('Error loading overlay settings:', error)
            updateField('overlaySettings', { enabled: false })
          }
        } else {
          updateField('overlaySettings', null)
        }

        // Load Discord settings if enabled
        if (user.features?.discordWebhook) {
          try {
            // Load notification settings
            const discordData = await notificationAPI.getSettings(user.twitchUserId)

            // Use our notification helper to create proper settings
            const settings = createDefaultNotificationSettings()
            Object.assign(settings, {
              enableDiscordNotifications: discordData.enableDiscordNotifications || false,
              enableChannelNotifications: discordData.enableChannelNotifications || false,
              deathMilestoneEnabled: discordData.deathMilestoneEnabled || false,
              swearMilestoneEnabled: discordData.swearMilestoneEnabled || false,
              deathThresholds: discordData.deathThresholds || settings.deathThresholds,
              swearThresholds: discordData.swearThresholds || settings.swearThresholds
            })

            resetToDefaults(settings)

            // Load webhook URL
            const webhookData = await notificationAPI.getWebhook(user.twitchUserId)
            updateField('discordWebhook', webhookData.webhookUrl || '')
          } catch (error) {
            console.error('Error loading Discord settings:', error)
            resetToDefaults()
            updateField('discordWebhook', '')
          }
        } else {
          resetToDefaults()
          updateField('discordWebhook', '')
        }
      })
    } catch (error) {
      console.error('Error loading user data:', error)
      showMessage('Failed to load user data', 'error')
    }
  }

  const handleFeatureChange = (feature, value) => {
    const newFeatures = { ...formState.features, [feature]: value }
    updateField('features', newFeatures)
  }

  const saveFeatures = async () => {
    try {
      await withLoading(async () => {
        // Merge current features with original user features to preserve any features not shown in UI
        const updatedFeatures = { ...originalUser.features, ...formState.features }

        await userAPI.updateFeatures(user.twitchUserId, { features: updatedFeatures })

        showMessage('Features updated successfully!', 'success')
        if (onUpdate) onUpdate()
      })
    } catch (error) {
      console.error('Error saving features:', error)
      showMessage('Failed to save features', 'error')
    }
  }

  const saveOverlaySettings = async () => {
    if (!formState.overlaySettings) return

    try {
      await withLoading(async () => {
        await userAPI.updateOverlaySettings(user.twitchUserId, formState.overlaySettings)

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
        if (formState.discordWebhook && !formState.discordWebhook.startsWith('https://discord.com/api/webhooks/')) {
          showMessage('Invalid Discord webhook URL format', 'error')
          return
        }

        // Validate notification settings
        const validationResult = validateNotificationSettings(notificationSettings)
        if (!validationResult.isValid) {
          showMessage(validationResult.errors[0], 'error')
          return
        }

        // Save webhook URL
        await notificationAPI.updateWebhook(user.twitchUserId, { webhookUrl: formState.discordWebhook })

        // Save notification settings
        await notificationAPI.updateSettings(user.twitchUserId, notificationSettings)

        showMessage('All Discord settings saved successfully!', 'success')
        if (onUpdate) onUpdate()
      })
    } catch (error) {
      console.error('Error saving Discord settings:', error)
      showMessage('Failed to save Discord settings', 'error')
    }
  }

  const testDiscordWebhook = async () => {
    if (!formState.discordWebhook) {
      showMessage('Please enter a Discord webhook URL first', 'error')
      return
    }

    if (!formState.discordWebhook.startsWith('https://discord.com/api/webhooks/')) {
      showMessage('Invalid Discord webhook URL format', 'error')
      return
    }

    try {
      await withLoading(async () => {
        await notificationAPI.testWebhook(user.twitchUserId, { webhookUrl: formState.discordWebhook })

        showMessage('Test message sent to Discord!', 'success')
      })
    } catch (error) {
      console.error('Error testing Discord webhook:', error)
      showMessage(`Failed to test webhook: ${error.message}`, 'error')
    }
  }

  const handleOverlayChange = (key, value) => {
    const newSettings = { ...formState.overlaySettings, [key]: value }
    updateField('overlaySettings', newSettings)
  }

  if (!user) return null

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div className="user-info">
            <img
              src={user.profileImageUrl}
              alt={user.displayName}
              className="user-avatar"
            />
            <div>
              <h3>{user.displayName}</h3>
              <StatusBadge
                status={user.isActive ? 'active' : 'inactive'}
                text={user.isActive ? 'Active' : 'Inactive'}
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
            <div className="features-grid">
              {Object.entries(defaultFeatures).map(([feature, defaultValue]) => {
                if (feature === 'templateStyle') return null // Skip template style in main features

                return (
                  <div key={feature} className="feature-item">
                    <ToggleSwitch
                      id={`feature-${feature}`}
                      checked={formState.features[feature] ?? defaultValue}
                      onChange={(checked) => handleFeatureChange(feature, checked)}
                      label={feature.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase())}
                      disabled={isLoading}
                    />
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
          {formState.features.streamOverlay && (
            <FormSection title="📺 Stream Overlay Settings" collapsible>
              {formState.overlaySettings && (
                <div className="overlay-settings">
                  <ToggleSwitch
                    id="overlay-enabled"
                    checked={formState.overlaySettings.enabled || false}
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
          {formState.features.discordWebhook && (
            <FormSection title="🔔 Discord Notifications" collapsible>
              <div className="discord-settings">
                {/* Webhook URL Input */}
                <InputGroup
                  label="Discord Webhook URL"
                  type="url"
                  value={formState.discordWebhook}
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
                      enabled={notificationSettings.enableDiscordNotifications}
                      onChange={(enabled) => updateNotificationSetting('enableDiscordNotifications', enabled)}
                      icon="🔔"
                      disabled={isLoading}
                    />

                    <NotificationTypeCard
                      title="Twitch Chat Notifications"
                      description="Send notifications to Twitch chat"
                      enabled={notificationSettings.enableChannelNotifications}
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
                    checked={notificationSettings.deathMilestoneEnabled}
                    onChange={(checked) => updateNotificationSetting('deathMilestoneEnabled', checked)}
                    label="Death Milestones"
                    disabled={isLoading}
                  />

                  {notificationSettings.deathMilestoneEnabled && (
                    <InputGroup
                      label="Death Thresholds"
                      value={notificationSettings.deathThresholds}
                      onChange={(e) => updateNotificationSetting('deathThresholds', e.target.value)}
                      placeholder="10,25,50,100,250,500,1000"
                      disabled={isLoading}
                      hint="Comma-separated numbers for milestone notifications"
                    />
                  )}

                  <ToggleSwitch
                    id="swear-milestones"
                    checked={notificationSettings.swearMilestoneEnabled}
                    onChange={(checked) => updateNotificationSetting('swearMilestoneEnabled', checked)}
                    label="Swear Milestones"
                    disabled={isLoading}
                  />

                  {notificationSettings.swearMilestoneEnabled && (
                    <InputGroup
                      label="Swear Thresholds"
                      value={notificationSettings.swearThresholds}
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
                    disabled={isLoading || !formState.discordWebhook}
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

import { useState, useEffect } from 'react'
import './DiscordWebhookSettings.css'
import { ToggleSwitch, ActionButton, FormSection, InputGroup, StatusBadge, NotificationTypeCard } from './ui/CommonControls'
import { useFormState, useToast, useLoading } from '../hooks'
import {
  createDefaultNotificationSettings,
  validateNotificationSettings,
  parseThresholdString
} from '../utils/notificationHelpers'
import { userAPI, notificationAPI, APIError } from '../utils/apiHelpers'

function DiscordWebhookSettings({ user }) {
  const { showToast } = useToast()
  const { isLoading, withLoading } = useLoading()

  // Form state management using our new hooks
  const { formState, updateField, resetForm } = useFormState({
    webhookUrl: '',
    enabled: false
  })

  // Notification settings with proper initialization
  const [notificationSettings, setNotificationSettings] = useState({
    enableDiscordNotifications: false,
    enableChannelNotifications: false,
    deathMilestoneEnabled: false,
    swearMilestoneEnabled: false,
    deathThresholds: '10,25,50,100,250,500,1000',
    swearThresholds: '25,50,100,250,500,1000,2500'
  })

  // Helper functions for notification settings
  const updateNotificationSetting = (key, value) => {
    setNotificationSettings(prev => ({
      ...prev,
      [key]: value
    }))
  }

  const validateSettings = () => {
    return validateNotificationSettings(notificationSettings)
  }

  const resetToDefaults = (newSettings = null) => {
    if (newSettings) {
      setNotificationSettings(newSettings)
    } else {
      setNotificationSettings({
        enableDiscordNotifications: false,
        enableChannelNotifications: false,
        deathMilestoneEnabled: false,
        swearMilestoneEnabled: false,
        deathThresholds: '10,25,50,100,250,500,1000',
        swearThresholds: '25,50,100,250,500,1000,2500'
      })
    }
  }

  const [message, setMessage] = useState('')
  const [messageType, setMessageType] = useState('') // 'success' | 'error'

  useEffect(() => {
    if (user) {
      loadUserDiscordSettings()
    }
  }, [user])

  const showMessage = (text, type) => {
    setMessage(text)
    setMessageType(type)
    setTimeout(() => setMessage(''), 3000)
  }

  const loadUserDiscordSettings = async () => {
    try {
      await withLoading(async () => {
        // Load webhook URL
        try {
          const webhookData = await userAPI.getDiscordWebhook()
          updateField('webhookUrl', webhookData.webhookUrl || '')
          updateField('enabled', webhookData.enabled || false)
        } catch (error) {
          console.error('Error loading webhook:', error)
        }

        // Load notification settings
        try {
          const discordData = await notificationAPI.getUserSettings()

          // Set notification settings with proper defaults
          const settings = {
            enableDiscordNotifications: discordData?.enableDiscordNotifications || false,
            enableChannelNotifications: discordData?.enableChannelNotifications || false,
            deathMilestoneEnabled: discordData?.deathMilestoneEnabled || false,
            swearMilestoneEnabled: discordData?.swearMilestoneEnabled || false,
            deathThresholds: discordData?.deathThresholds || '10,25,50,100,250,500,1000',
            swearThresholds: discordData?.swearThresholds || '25,50,100,250,500,1000,2500'
          }

          resetToDefaults(settings)
        } catch (error) {
          console.error('Error loading notification settings:', error)
          resetToDefaults()
        }
      })
    } catch (error) {
      console.error('Error loading Discord settings:', error)
      showMessage('Failed to load Discord settings', 'error')
    }
  }

  const saveAllSettings = async () => {
    try {
      await withLoading(async () => {
        // Validate webhook URL
        if (formState.webhookUrl && !formState.webhookUrl.startsWith('https://discord.com/api/webhooks/')) {
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
        await userAPI.updateDiscordWebhook({
          webhookUrl: formState.webhookUrl,
          enabled: formState.enabled
        })

        // Save notification settings
        await notificationAPI.updateUserSettings(notificationSettings)

        showMessage('All Discord settings saved successfully!', 'success')
      })
    } catch (error) {
      console.error('Error saving Discord settings:', error)
      showMessage('Failed to save Discord settings', 'error')
    }
  }

  const testWebhook = async () => {
    if (!formState.webhookUrl) {
      showMessage('Please enter a webhook URL first', 'error')
      return
    }

    if (!formState.webhookUrl.startsWith('https://discord.com/api/webhooks/')) {
      showMessage('Invalid Discord webhook URL format', 'error')
      return
    }

    try {
      await withLoading(async () => {
        await userAPI.testDiscordWebhook({ webhookUrl: formState.webhookUrl })
        showMessage('Test notification sent! Check your Discord channel.', 'success')
      })
    } catch (error) {
      console.error('Error testing Discord webhook:', error)
      showMessage(`Failed to test webhook: ${error.message}`, 'error')
    }
  }

  return (
    <div className="discord-webhook-settings">
      <div className="settings-header">
        <h3>🔔 Discord Notifications</h3>
        <StatusBadge
          status={formState.enabled ? 'active' : 'inactive'}
          text={formState.enabled ? 'Enabled' : 'Disabled'}
        />
      </div>

      {message && (
        <div className={`message ${messageType}`}>
          {message}
        </div>
      )}

      <div className="discord-settings">
        {/* Basic Webhook Setup */}
        <FormSection title="🔧 Webhook Setup" collapsible defaultExpanded>
          <div className="settings-info">
            <p>Connect your Discord server to receive stream notifications!</p>
          </div>

          <div className="webhook-setup">
            <h4>Setup Instructions</h4>
            <ol>
              <li>Go to your Discord server settings</li>
              <li>Click "Integrations" → "Webhooks"</li>
              <li>Click "New Webhook" or select an existing one</li>
              <li>Copy the webhook URL and paste it below</li>
            </ol>
          </div>

          <InputGroup
            label="Discord Webhook URL"
            type="url"
            value={formState.webhookUrl}
            onChange={(e) => updateField('webhookUrl', e.target.value)}
            placeholder="https://discord.com/api/webhooks/..."
            disabled={isLoading}
            hint="Must start with https://discord.com/api/webhooks/"
          />

          <div className="webhook-actions">
            <ActionButton
              variant="primary"
              onClick={saveAllSettings}
              loading={isLoading}
              disabled={isLoading || !formState.webhookUrl}
            >
              💾 Save Settings
            </ActionButton>

            <ActionButton
              variant="secondary"
              onClick={testWebhook}
              loading={isLoading}
              disabled={isLoading || !formState.webhookUrl}
            >
              🧪 Send Test
            </ActionButton>
          </div>
        </FormSection>

        {/* Notification Type Toggles */}
        <FormSection title="📢 Notification Types" collapsible>
          <div className="notification-types">
            <div className="notification-grid">
              <NotificationTypeCard
                title="Discord Notifications"
                description="Send notifications to Discord channel"
                enabled={notificationSettings?.enableDiscordNotifications || false}
                onChange={(enabled) => updateNotificationSetting('enableDiscordNotifications', enabled)}
                icon="🔔"
                disabled={isLoading || !formState.webhookUrl}
              />

              <NotificationTypeCard
                title="Twitch Chat Notifications"
                description="Send notifications to your Twitch chat"
                enabled={notificationSettings?.enableChannelNotifications || false}
                onChange={(enabled) => updateNotificationSetting('enableChannelNotifications', enabled)}
                icon="💬"
                disabled={isLoading}
              />
            </div>
          </div>
        </FormSection>

        {/* Milestone Settings */}
        <FormSection title="🎯 Milestone Notifications" collapsible>
          <div className="milestone-settings">
            <ToggleSwitch
              id="death-milestones"
              checked={notificationSettings?.deathMilestoneEnabled || false}
              onChange={(checked) => updateNotificationSetting('deathMilestoneEnabled', checked)}
              label="Death Count Milestones"
              disabled={isLoading}
            />

            {(notificationSettings?.deathMilestoneEnabled) && (
              <InputGroup
                label="Death Milestone Thresholds"
                value={notificationSettings?.deathThresholds || '10,25,50,100,250,500,1000'}
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
              label="Swear Count Milestones"
              disabled={isLoading}
            />

            {(notificationSettings?.swearMilestoneEnabled) && (
              <InputGroup
                label="Swear Milestone Thresholds"
                value={notificationSettings?.swearThresholds || '25,50,100,250,500,1000,2500'}
                onChange={(e) => updateNotificationSetting('swearThresholds', e.target.value)}
                placeholder="25,50,100,250,500,1000,2500"
                disabled={isLoading}
                hint="Comma-separated numbers for milestone notifications"
              />
            )}
          </div>
        </FormSection>

        {/* Preview Section */}
        <FormSection title="👀 Preview" collapsible>
          <div className="webhook-preview">
            <p>Here's what notifications look like in Discord:</p>
            <div className="preview-card">
              <div className="discord-message-preview">
                <div className="discord-embed">
                  <div className="embed-header">
                    <img src={user?.profileImageUrl || '/default-avatar.png'} alt="Profile" className="embed-thumbnail" />
                    <div>
                      <strong>🔴 {user?.displayName || 'Your Name'} just went LIVE on Twitch!</strong>
                    </div>
                  </div>
                  <div className="embed-content">
                    <div className="embed-title">Your Stream Title Here</div>
                    <div className="embed-description">
                      Playing <strong>Game/Category</strong>
                    </div>
                    <div className="embed-link">🎮 Watch Now!</div>
                  </div>
                  <div className="embed-footer">
                    <small>Twitch • Just now</small>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </FormSection>
      </div>
    </div>
  )
}

export default DiscordWebhookSettings

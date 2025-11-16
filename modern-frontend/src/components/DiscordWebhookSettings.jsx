import { useState, useEffect } from 'react'
import './DiscordWebhookSettings.css'
import { ToggleSwitch, ActionButton, FormSection, InputGroup, StatusBadge } from './ui/CommonControls'
import { useFormState, useToast, useLoading } from '../hooks'
import {
  createDefaultNotificationSettings,
  validateNotificationSettings,
  parseThresholdString
} from '../utils/notificationHelpers'
import { userAPI, APIError } from '../utils/authUtils'

function DiscordWebhookSettings({ user }) {
  const { showToast } = useToast()
  const { isLoading, withLoading } = useLoading()

  // Tab state management
  const [activeTab, setActiveTab] = useState('notifications')

  // Form state management using our new hooks
  const { values: formState, setValue: updateField, reset: resetForm } = useFormState({
    webhookUrl: '',
    enabled: false
  })

  // Discord invite link state
  const [discordInvite, setDiscordInvite] = useState('')

  // Helper function to get the correct user ID
  const getUserId = () => {
    return user?.twitchUserId || user?.userId
  }

  // Debug form state changes
  useEffect(() => {
    console.log('🔄 FORM STATE CHANGED:', {
      webhookUrl: formState.webhookUrl,
      enabled: formState.enabled,
      timestamp: new Date().toISOString()
    })
  }, [formState])

  // Notification settings with proper initialization
  const [notificationSettings, setNotificationSettings] = useState({
    enableChannelNotifications: false,
    deathMilestoneEnabled: false,
    swearMilestoneEnabled: false,
    deathThresholds: '10,25,50,100,250,500,1000',
    swearThresholds: '25,50,100,250,500,1000,2500'
  })

  // Message template settings
  // Template system removed - using standard format for all notifications

  // Helper functions for notification settings
  const updateNotificationSetting = (key, value) => {
    console.log('🔧 updateNotificationSetting called:', { key, value })
    console.log('🔧 Current notification settings before update:', notificationSettings)

    setNotificationSettings(prevSettings => {
      const updatedSettings = {
        ...prevSettings,
        [key]: value
      }
      console.log('� Updated notification settings:', updatedSettings)
      return updatedSettings
    })

    console.log('🔧 State update dispatched for key:', key, 'value:', value)
  }

  const validateSettings = () => {
    return validateNotificationSettings(notificationSettings)
  }

  const resetToDefaults = (newSettings = null) => {
    if (newSettings) {
      setNotificationSettings(newSettings)
    } else {
      setNotificationSettings({
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
    console.log('🎬 DiscordWebhookSettings component mounted/updated')
    console.log('👤 User:', user)
    if (user) {
      console.log('📥 Loading ALL Discord data for user on modal open')
      loadAllDiscordData()
    }
  }, [user])

  const showMessage = (text, type) => {
    setMessage(text)
    setMessageType(type)
    setTimeout(() => setMessage(''), 3000)
  }

  // Load webhook configuration specifically for the configuration tab
  const loadWebhookConfiguration = async () => {
    console.log('⚙️ Loading webhook configuration for configuration tab...')
    const adminMode = isAdminMode()
    console.log('🔍 Loading webhook configuration in admin mode:', adminMode)

    try {
      await withLoading(async () => {
        console.log('🔗 Fetching configuration from consolidated notification settings...')
        let discordData

        if (adminMode) {
          // Admin viewing another user's settings - use admin API
          console.log('👑 Using admin API to load webhook configuration for user:', getUserId())
          discordData = await notificationAPI.getSettings(getUserId())
          // Admin API returns { discordSettings: {...} }, so extract the inner object
          discordData = discordData?.discordSettings || discordData
        } else {
          // User viewing their own settings - use user API
          console.log('👤 Using user API to load own webhook configuration')
          discordData = await notificationAPI.getUserSettings()
        }

        console.log('🔗 Consolidated data received:', discordData)
        console.log('🔗 Extracted webhook URL:', discordData?.webhookUrl)
        console.log('🔗 Extracted enabled status:', discordData?.enabled)

        updateField('webhookUrl', discordData?.webhookUrl || '')
        updateField('enabled', discordData?.enabled || false)

        if (discordData?.webhookUrl) {
          console.log('✅ Existing webhook configuration loaded')
          showMessage(`Configuration loaded: Webhook ${discordData?.enabled ? 'enabled' : 'disabled'}`, 'success')
        } else {
          console.log('ℹ️ No existing webhook configuration found')
          showMessage('No existing configuration found - you can set one up below', 'info')
        }
      })
    } catch (error) {
      console.error('❌ Error loading webhook configuration:', error)
      console.error('❌ Full error details:', error?.message || 'Unknown error', error?.stack || 'No stack trace')
      showMessage('Failed to load webhook configuration', 'error')
    }
  }

  // Helper function to determine if we're in admin mode (admin viewing another user's settings)
  const isAdminMode = () => {
    try {
      const token = localStorage.getItem('authToken')
      if (token) {
        const payload = JSON.parse(atob(token.split('.')[1]))
        const currentUserId = payload.userId || payload.twitchUserId
        const isCurrentUserAdmin = payload.role === 'admin'
        const targetUserId = getUserId()

        console.log('🔍 Admin mode check:', {
          currentUserId,
          targetUserId,
          isCurrentUserAdmin,
          isAdminMode: isCurrentUserAdmin && currentUserId !== targetUserId
        })

        return isCurrentUserAdmin && currentUserId !== targetUserId
      }
    } catch (error) {
      console.error('❌ Error checking admin mode:', error)
    }
    return false
  }

  const loadUserDiscordSettings = async () => {
    console.log('📥 Starting to load Discord settings...')
    const adminMode = isAdminMode()
    console.log('🔍 Loading settings in admin mode:', adminMode)

    try {
      await withLoading(async () => {
        // Load webhook URL
        try {
          console.log('🔗 Loading webhook URL...')
          let webhookData

          if (adminMode) {
            // Admin viewing another user's settings - use admin API
            console.log('👑 Using admin API to load webhook for user:', getUserId())
            webhookData = await notificationAPI.getWebhook(getUserId())
          } else {
            // User viewing their own settings - use user API
            console.log('� Using user API to load own webhook')
            webhookData = await userAPI.getDiscordWebhook()
          }

          console.log('🔗 Webhook data received:', webhookData)
          console.log('� Extracted webhook URL:', webhookData?.webhookUrl)
          console.log('🔗 Extracted enabled status:', webhookData?.enabled)
          console.log('🔧 UPDATING FORM FIELDS:', {
            webhookUrl: webhookData?.webhookUrl || '',
            enabled: webhookData?.enabled || false,
            rawData: webhookData
          })
          updateField('webhookUrl', webhookData?.webhookUrl || '')
          updateField('enabled', webhookData?.enabled || false)
          console.log('✅ Webhook URL loaded successfully, form updated')
        } catch (error) {
          console.error('❌ Error loading webhook:', error)
          console.error('❌ Full error details:', error?.message || 'Unknown error', error?.stack || 'No stack trace')
          showMessage(`Failed to load Discord settings: ${error?.message || 'Unknown error'}`, 'error')
        }

        // Load notification settings
        try {
          console.log('📢 Loading notification settings...')
          let discordData

          if (adminMode) {
            // Admin viewing another user's settings - use admin API
            console.log('👑 Using admin API to load notification settings for user:', getUserId())
            discordData = await notificationAPI.getSettings(getUserId())
            // Admin API returns { discordSettings: {...} }, so extract the inner object
            discordData = discordData?.discordSettings || discordData
          } else {
            // User viewing their own settings - use user API
            console.log('👤 Using user API to load own notification settings')
            discordData = await notificationAPI.getUserSettings()
          }

          console.log('📢 Notification data received:', discordData)

          // Extract webhook data from the consolidated response
          if (discordData?.webhookUrl !== undefined || discordData?.enabled !== undefined) {
            console.log('🔗 Webhook data found in notification settings:', {
              webhookUrl: discordData.webhookUrl ? `${discordData.webhookUrl.substring(0, 50)}...` : 'EMPTY',
              enabled: discordData.enabled
            })
            updateField('webhookUrl', discordData.webhookUrl || '')
            updateField('enabled', discordData.enabled || false)
            console.log('🔧 Webhook fields updated from notification settings')
          }

          // Set notification settings with proper defaults
          const settings = {
            enableChannelNotifications: discordData?.enableChannelNotifications || false,
            deathMilestoneEnabled: discordData?.deathMilestoneEnabled || false,
            swearMilestoneEnabled: discordData?.swearMilestoneEnabled || false,
            deathThresholds: discordData?.deathThresholds || '10,25,50,100,250,500,1000',
            swearThresholds: discordData?.swearThresholds || '25,50,100,250,500,1000,2500'
          }

          console.log('📊 Final settings to apply:', settings)
          // Use setNotificationSettings instead of resetToDefaults for notification settings
          setNotificationSettings(settings)

          // Load template style preference
          if (discordData?.templateStyle) {
            console.log('🎨 Setting template style from server:', discordData.templateStyle)
            // Template system removed - standard format used for all notifications
          }

          console.log('✅ Notification settings loaded successfully')
        } catch (error) {
          console.error('❌ Error loading notification settings:', error)
          // Set default notification settings on error
          setNotificationSettings({
            enableChannelNotifications: false,
            deathMilestoneEnabled: false,
            swearMilestoneEnabled: false,
            deathThresholds: '10,25,50,100,250,500,1000',
            swearThresholds: '25,50,100,250,500,1000,2500'
          })
          // Set default template style on error
          // Template system removed
        }
      })
    } catch (error) {
      console.error('❌ Error loading Discord settings:', error)
      showMessage('Failed to load Discord settings', 'error')
    }
  }

  // Load ALL Discord data when modal opens (webhook, settings, invite)
  const loadAllDiscordData = async () => {
    console.log('📥 Starting to load ALL Discord data...')
    const adminMode = isAdminMode()
    console.log('🔍 Loading all data in admin mode:', adminMode)

    try {
      await withLoading(async () => {
        // Load webhook URL and enabled status
        try {
          console.log('🔗 Loading webhook URL and enabled status...')
          let webhookData

          if (adminMode) {
            // Admin viewing another user's settings - use admin API
            const targetUserId = user?.twitchUserId || getUserId()
            console.log('👑 Using admin API to load webhook for user:', targetUserId)
            webhookData = await notificationAPI.getWebhook(targetUserId)
          } else {
            // User viewing their own settings - use user API
            console.log('👤 Using user API to load own webhook')
            webhookData = await userAPI.getDiscordWebhook()
          }

          console.log('🔗 Webhook data received:', webhookData)
          console.log('🔗 Extracted webhook URL:', webhookData?.webhookUrl)
          console.log('🔗 Extracted enabled status:', webhookData?.enabled)
          console.log('🔧 UPDATING FORM FIELDS:', {
            webhookUrl: webhookData?.webhookUrl || '',
            enabled: webhookData?.enabled || false,
            rawData: webhookData
          })
          updateField('webhookUrl', webhookData?.webhookUrl || '')
          updateField('enabled', webhookData?.enabled || false)
          console.log('✅ Webhook URL loaded successfully, form updated')
        } catch (error) {
          console.error('❌ Error loading webhook:', error)
          console.error('❌ Full error details:', error?.message || 'Unknown error', error?.stack || 'No stack trace')
          showMessage(`Failed to load Discord webhook: ${error?.message || 'Unknown error'}`, 'error')
        }

        // Load notification settings
        try {
          console.log('📢 Loading notification settings...')
          let discordData

          if (adminMode) {
            // Admin viewing another user's settings - use admin API
            console.log('👑 Using admin API to load notification settings for user:', getUserId())
            discordData = await notificationAPI.getSettings(getUserId())
            // Admin API returns { discordSettings: {...} }, so extract the inner object
            discordData = discordData?.discordSettings || discordData
          } else {
            // User viewing their own settings - use user API
            console.log('👤 Using user API to load own notification settings')
            discordData = await notificationAPI.getUserSettings()
          }

          console.log('📢 Notification data received:', discordData)

          // Set notification settings with proper defaults
          const settings = {
            enableChannelNotifications: discordData?.enableChannelNotifications || false,
            deathMilestoneEnabled: discordData?.deathMilestoneEnabled || false,
            swearMilestoneEnabled: discordData?.swearMilestoneEnabled || false,
            deathThresholds: discordData?.deathThresholds || '10,25,50,100,250,500,1000',
            swearThresholds: discordData?.swearThresholds || '25,50,100,250,500,1000,2500'
          }

          console.log('📊 Final settings to apply:', settings)
          setNotificationSettings(settings)
          console.log('✅ Discord notification settings loaded successfully')
        } catch (error) {
          console.error('❌ Error loading notification settings:', error)
          // Set default notification settings on error
          setNotificationSettings({
            enableChannelNotifications: false,
            deathMilestoneEnabled: false,
            swearMilestoneEnabled: false,
            deathThresholds: '10,25,50,100,250,500,1000',
            swearThresholds: '25,50,100,250,500,1000,2500'
          })
          showMessage(`Failed to load notification settings: ${error?.message || 'Unknown error'}`, 'error')
        }

        // Load Discord invite link
        try {
          console.log('🎮 Loading Discord invite link...')
          let inviteData

          if (adminMode) {
            // Admin viewing another user's settings - use admin API
            console.log('👑 Using admin API to load Discord invite for user:', getUserId())
            inviteData = await notificationAPI.getDiscordInvite(getUserId())
          } else {
            // User viewing their own settings - use user API
            console.log('👤 Using user API to load own Discord invite')
            inviteData = await userAPI.getDiscordInvite()
          }

          console.log('🎮 Discord invite data received:', inviteData)
          setDiscordInvite(inviteData?.discordInviteLink || '')
          console.log('✅ Discord invite loaded successfully')
        } catch (error) {
          console.error('❌ Error loading Discord invite:', error)
          showMessage(`Failed to load Discord invite: ${error?.message || 'Unknown error'}`, 'error')
        }
      })
    } catch (error) {
      console.error('❌ Error in loadAllDiscordData:', error)
      showMessage(`Failed to load Discord data: ${error?.message || 'Unknown error'}`, 'error')
    }
  }

  // Load Discord invite link
  const loadDiscordInvite = async () => {
    console.log('🎮 Loading Discord invite link...')
    try {
      await withLoading(async () => {
        const inviteData = isAdminMode()
          ? await notificationAPI.getDiscordInvite(getUserId())
          : await userAPI.getDiscordInvite()
        console.log('🎮 Discord invite data received:', inviteData)
        setDiscordInvite(inviteData?.discordInviteLink || '')

        if (inviteData?.discordInviteLink) {
          console.log('✅ Discord invite link loaded successfully')
          showMessage('Discord invite link loaded', 'success')
        } else {
          console.log('ℹ️ No Discord invite link configured')
          showMessage('No Discord invite link configured', 'info')
        }
      })
    } catch (error) {
      console.error('❌ Error loading Discord invite:', error)
      showMessage('Failed to load Discord invite link', 'error')
    }
  }

  // Save Discord invite link
  const saveDiscordInvite = async () => {
    console.log('🎮 Saving Discord invite link...')
    try {
      await withLoading(async () => {
        // Validate Discord invite URL format
        if (discordInvite && !isValidDiscordInviteUrl(discordInvite)) {
          showMessage('Invalid Discord invite URL format. Use discord.gg/... or discord.com/invite/...', 'error')
          return
        }

        if (isAdminMode()) {
          await notificationAPI.updateDiscordInvite(getUserId(), { discordInviteLink: discordInvite })
        } else {
          await userAPI.updateDiscordInvite({ discordInviteLink: discordInvite })
        }
        console.log('✅ Discord invite link saved successfully')
        showMessage('Discord invite link saved successfully!', 'success')
      })
    } catch (error) {
      console.error('❌ Error saving Discord invite:', error)
      showMessage('Failed to save Discord invite link', 'error')
    }
  }

  // Remove Discord invite link
  const removeDiscordInvite = async () => {
    console.log('🎮 Removing Discord invite link...')
    try {
      await withLoading(async () => {
        await userAPI.updateDiscordInvite({ discordInviteLink: '' })
        setDiscordInvite('')
        console.log('✅ Discord invite link removed successfully')
        showMessage('Discord invite link removed successfully!', 'success')
      })
    } catch (error) {
      console.error('❌ Error removing Discord invite:', error)
      showMessage('Failed to remove Discord invite link', 'error')
    }
  }

  // Validate Discord invite URL format
  const isValidDiscordInviteUrl = (url) => {
    if (!url) return true // Allow empty URLs for removal
    const discordInviteRegex = /^https?:\/\/(discord\.gg\/|discord\.com\/invite\/|discordapp\.com\/invite\/)/
    return discordInviteRegex.test(url)
  }

  const saveWebhookConfiguration = async () => {
    console.log('� Save Webhook Configuration button clicked')
    const adminMode = isAdminMode()
    console.log('🔍 Saving webhook in admin mode:', adminMode)
    console.log('📊 Form state:', formState)
    console.log('📊 Values to save:')
    console.log('   - Webhook URL:', formState?.webhookUrl)
    console.log('   - Webhook enabled:', formState?.enabled)

    try {
      await withLoading(async () => {
        // Validate webhook URL
        if (formState?.webhookUrl && !formState?.webhookUrl.startsWith('https://discord.com/api/webhooks/')) {
          console.log('❌ Webhook URL validation failed')
          showMessage('Invalid Discord webhook URL format', 'error')
          return
        }
        console.log('✅ Webhook URL validation passed')

        // Save webhook URL only
        console.log('🔗 Saving webhook configuration...')
        const webhookPayload = {
          webhookUrl: formState?.webhookUrl || '',
          enabled: formState?.enabled || false
        }
        console.log('🔗 Webhook payload:', webhookPayload)

        if (adminMode) {
          // Admin saving another user's webhook - use admin API
          console.log('👑 Using admin API to save webhook for user:', getUserId())
          await notificationAPI.updateWebhook(getUserId(), webhookPayload)
        } else {
          // User saving their own webhook - use user API
          console.log('👤 Using user API to save own webhook')
          await userAPI.updateDiscordWebhook(webhookPayload)
        }

        console.log('✅ Webhook configuration saved successfully')
        showMessage('Webhook configuration saved successfully!', 'success')
      })
    } catch (error) {
      console.error('❌ Error saving webhook configuration:', error)
      showMessage('Failed to save webhook configuration', 'error')
    }
  }

  const saveNotificationSettings = async () => {
    try {
      console.log('📢 Saving notification settings only...')
      console.log('📢 Settings payload:', notificationSettings)

      await withLoading(async () => {
        // Validate notification settings
        const validationResult = validateNotificationSettings(notificationSettings)
        if (!validationResult?.isValid) {
          console.log('❌ Notification settings validation failed:', validationResult?.errors || [])
          showMessage(validationResult?.errors?.[0] || 'Validation failed', 'error')
          return
        }
        console.log('✅ Notification settings validation passed')

        // Save notification settings only
        const adminMode = isAdminMode()
        console.log('🔍 Saving notification settings in admin mode:', adminMode)

        if (adminMode) {
          // Admin saving another user's notification settings - use admin API
          console.log('👑 Using admin API to save notification settings for user:', getUserId())
          await notificationAPI.updateSettings(getUserId(), notificationSettings)
        } else {
          // User saving their own notification settings - use user API
          console.log('👤 Using user API to save own notification settings')
          await notificationAPI.updateUserSettings(notificationSettings)
        }

        console.log('✅ Notification settings saved successfully')

        showMessage('Notification settings saved successfully!', 'success')
      })
    } catch (error) {
      console.error('❌ Error saving notification settings:', error)
      showMessage('Failed to save notification settings', 'error')
    }
  }

  // Template settings function removed - standard format used for all notifications

  const testWebhook = async () => {
    if (!formState?.webhookUrl) {
      showMessage('Please enter a webhook URL first', 'error')
      return
    }

    if (!formState?.webhookUrl?.startsWith('https://discord.com/api/webhooks/')) {
      showMessage('Invalid Discord webhook URL format', 'error')
      return
    }

    try {
      const adminMode = isAdminMode()
      console.log('🔍 Testing webhook in admin mode:', adminMode)

      await withLoading(async () => {
        if (adminMode) {
          // Admin testing another user's webhook - use admin API
          console.log('👑 Using admin API to test webhook for user:', getUserId())
          await notificationAPI.testWebhook(getUserId(), { webhookUrl: formState?.webhookUrl || '' })
        } else {
          // User testing their own webhook - use user API
          console.log('👤 Using user API to test own webhook')
          await userAPI.testDiscordWebhook({ webhookUrl: formState?.webhookUrl || '' })
        }

        showMessage('Test notification sent! Check your Discord channel.', 'success')
      })
    } catch (error) {
      console.error('Error testing Discord webhook:', error)
      showMessage(`Failed to test webhook: ${error?.message || 'Unknown error'}`, 'error')
    }
  }



  return (
    <div className="discord-webhook-settings">
      <div className="settings-header">
        <h3>🔔 Discord Integration</h3>
        {formState && (
          <StatusBadge
            status={formState?.enabled ? 'success' : 'info'}
          >
            {formState?.enabled ? 'Enabled' : 'Disabled'}
          </StatusBadge>
        )}
      </div>

      {message && (
        <div className={`message ${messageType}`}>
          {message}
        </div>
      )}

      {/* Tab Navigation */}
      <div className="tab-navigation">
        <button
          className={`tab-button ${activeTab === 'notifications' ? 'active' : ''}`}
          onClick={() => setActiveTab('notifications')}
        >
          📢 Notifications
        </button>
        <button
          className={`tab-button ${activeTab === 'counters' ? 'active' : ''}`}
          onClick={() => setActiveTab('counters')}
        >
          🎯 Counters & Milestones
        </button>
        <button
          className={`tab-button ${activeTab === 'configuration' ? 'active' : ''}`}
          onClick={() => {
            console.log('⚙️ Configuration tab clicked, switching and loading configuration...')
            setActiveTab('configuration')
          }}
        >
          ⚙️ Configuration
        </button>
      </div>

      {/* Tab Content */}
      <div className="tab-content">

        {/* Tab 1: Notification Settings */}
        {activeTab === 'notifications' && (
          <div className="tab-panel">
            <FormSection title="🔔 Basic Notification Types">
              <div className="settings-info">
                <p>Choose which types of notifications you want to receive from your stream!</p>
              </div>


              {/* Notification Type Toggles */}
          <div className="notification-settings-grid">

            {/* Discord Notifications - Auto-enabled when webhook is configured */}
            <div className="notification-info">
              <div className="option-header">
                <div className="option-icon">🔔</div>
                <div className="option-content">
                  <h4>Discord Notifications</h4>
                  <p>Automatically enabled when webhook URL is configured above</p>
                </div>
              </div>
            </div>

            {/* Twitch Chat Notifications */}
            <div
              className={`notification-option ${notificationSettings?.enableChannelNotifications ? 'active' : ''}`}
              onClick={() => {
                console.log('🖱️ Twitch chat notification card clicked')
                console.log('📋 Current state:', {
                  isLoading,
                  currentValue: notificationSettings?.enableChannelNotifications
                })
                if (!isLoading) {
                  const newValue = !notificationSettings?.enableChannelNotifications
                  console.log('🔄 Toggling Twitch notifications to:', newValue)
                  updateNotificationSetting('enableChannelNotifications', newValue)
                }
              }}
            >
              <div className="option-header">
                <div className="option-icon">💬</div>
                <div className="option-content">
                  <h4>Twitch Chat Notifications</h4>
                  <p>Announce milestones and events in your Twitch chat</p>
                </div>
              </div>
              <div className="option-toggle">
                <ToggleSwitch
                  checked={notificationSettings?.enableChannelNotifications || false}
                  onChange={(e) => {
                    console.log('🔧 Twitch ToggleSwitch onChange fired:', e.target.checked)
                    updateNotificationSetting('enableChannelNotifications', e.target.checked)
                  }}
                  disabled={isLoading}
                />
              </div>
            </div>

          </div>

          {/* Save Notification Preferences */}
          <div className="notification-actions">
            <ActionButton
              variant="primary"
              onClick={() => {
                console.log('💾 Save Notification Settings button clicked')
                console.log('📊 Notification settings:', notificationSettings)
                saveNotificationSettings()
              }}
              loading={isLoading}
            >
              💾 Save Notification Settings
            </ActionButton>
          </div>

          {/* Requirements Notice */}
          {(!formState?.webhookUrl) && (
            <div className="requirement-notice">
              <div className="notice-icon">⚠️</div>
              <div className="notice-text">
                <strong>Discord webhook required:</strong> Configure your webhook in the Configuration tab to enable Discord notifications.
              </div>
            </div>
          )}

        </FormSection>
          </div>
        )}

        {/* Tab 2: Counter & Milestone Settings */}
        {activeTab === 'counters' && (
          <div className="tab-panel">
            <FormSection title="🎯 Milestone Notifications">
              <div className="milestone-settings">
                <div className="milestone-group">
                  <div className="milestone-header">
                    <div className="milestone-icon">💀</div>
                    <ToggleSwitch
                      checked={notificationSettings?.deathMilestoneEnabled || false}
                      onChange={(e) => updateNotificationSetting('deathMilestoneEnabled', e.target.checked)}
                      label="Death Count Milestones"
                      disabled={isLoading}
                    />
                  </div>
                  {(notificationSettings?.deathMilestoneEnabled) && (
                    <div className="milestone-config">
                      <InputGroup
                        label="Milestone Thresholds"
                        value={notificationSettings?.deathThresholds || '10,25,50,100,250,500,1000'}
                        onChange={(e) => updateNotificationSetting('deathThresholds', e.target.value)}
                        placeholder="10,25,50,100,250,500,1000"
                        disabled={isLoading}
                        hint="Comma-separated numbers (e.g., 10,25,50,100)"
                      />
                    </div>
                  )}
                </div>

                <div className="milestone-group">
                  <div className="milestone-header">
                    <div className="milestone-icon">🤬</div>
                    <ToggleSwitch
                      checked={notificationSettings?.swearMilestoneEnabled || false}
                      onChange={(e) => updateNotificationSetting('swearMilestoneEnabled', e.target.checked)}
                      label="Swear Count Milestones"
                      disabled={isLoading}
                    />
                  </div>
                  {(notificationSettings?.swearMilestoneEnabled) && (
                    <div className="milestone-config">
                      <InputGroup
                        label="Milestone Thresholds"
                        value={notificationSettings?.swearThresholds || '25,50,100,250,500,1000,2500'}
                        onChange={(e) => updateNotificationSetting('swearThresholds', e.target.value)}
                        placeholder="25,50,100,250,500,1000,2500"
                        disabled={isLoading}
                        hint="Comma-separated numbers (e.g., 25,50,100,250)"
                      />
                    </div>
                  )}
                </div>
              </div>

              {/* Save Milestone Settings */}
              <div className="milestone-actions">
                <ActionButton
                  variant="primary"
                  onClick={() => {
                    console.log('💾 Save Milestone Settings button clicked')
                    console.log('📊 Milestone settings:', {
                      deathMilestoneEnabled: notificationSettings?.deathMilestoneEnabled,
                      swearMilestoneEnabled: notificationSettings?.swearMilestoneEnabled,
                      deathThresholds: notificationSettings?.deathThresholds,
                      swearThresholds: notificationSettings?.swearThresholds
                    })
                    saveNotificationSettings()
                  }}
                  loading={isLoading}
                >
                  💾 Save Milestone Settings
                </ActionButton>
              </div>
            </FormSection>
          </div>
        )}

        {/* Tab 3: Configuration & Templates */}
        {activeTab === 'configuration' && (
          <div className="tab-panel">
            <FormSection title={`🔧 Webhook Setup ${isLoading ? '(Loading...)' : ''}`}>
              <div className="settings-info">
                <p>Connect your Discord server to receive stream notifications!</p>
                {formState?.webhookUrl && !isLoading && (
                  <div className="info-box" style={{
                    background: '#22c55e1a',
                    border: '1px solid #22c55e',
                    color: '#22c55e',
                    padding: '8px 12px',
                    borderRadius: '6px',
                    fontSize: '0.875rem',
                    marginTop: '10px'
                  }}>
                    ✅ Existing webhook configuration detected - URL loaded from server
                  </div>
                )}
                {isLoading && (
                  <div className="info-box" style={{
                    background: '#fbbf241a',
                    border: '1px solid #fbbf24',
                    color: '#fbbf24',
                    padding: '8px 12px',
                    borderRadius: '6px',
                    fontSize: '0.875rem',
                    marginTop: '10px'
                  }}>
                    🔄 Loading existing configuration from server...
                  </div>
                )}
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
                label={`Discord Webhook URL ${formState?.enabled ? '(✅ Enabled)' : '(❌ Disabled)'}`}
                required={true}
              >
                <input
                  type="url"
                  value={formState?.webhookUrl || ''}
                  onChange={(e) => updateField('webhookUrl', e.target.value)}
                  placeholder="https://discord.com/api/webhooks/..."
                  disabled={isLoading}
                  style={{
                    width: '100%',
                    padding: '12px',
                    background: '#2a2a2a',
                    border: `2px solid ${formState?.webhookUrl ? '#22c55e' : '#444'}`,
                    borderRadius: '8px',
                    color: '#fff',
                    fontSize: '1rem',
                    fontFamily: 'Courier New, monospace'
                  }}
                />
                <small style={{
                  color: '#888',
                  fontSize: '0.875rem',
                  display: 'block',
                  marginTop: '5px'
                }}>
                  Must start with https://discord.com/api/webhooks/
                </small>
                {formState?.webhookUrl && (
                  <div style={{
                    marginTop: '8px',
                    padding: '6px 10px',
                    background: formState?.enabled ? '#22c55e1a' : '#ef44441a',
                    border: `1px solid ${formState?.enabled ? '#22c55e' : '#ef4444'}`,
                    borderRadius: '4px',
                    fontSize: '0.8rem',
                    color: formState?.enabled ? '#22c55e' : '#ef4444'
                  }}>
                    Status: {formState?.enabled ? '✅ Webhook is enabled and ready to receive notifications' : '❌ Webhook is disabled - notifications will not be sent'}
                  </div>
                )}
              </InputGroup>

              <div className="webhook-actions">
                <ActionButton
                  variant="primary"
                  onClick={() => {
                    console.log('� Save Webhook Configuration button clicked')
                    console.log('📊 Form state:', formState)
                    saveWebhookConfiguration()
                  }}
                  loading={isLoading}
                >
                  � Save Webhook Configuration
                </ActionButton>

                <ActionButton
                  variant="secondary"
                  onClick={() => {
                    console.log('🧪 Test Webhook button clicked')
                    console.log('🔗 Webhook URL:', formState?.webhookUrl)
                    testWebhook()
                  }}
                  loading={isLoading}
                  disabled={isLoading || !formState?.webhookUrl}
                >
                  🧪 Send Test
                </ActionButton>

                <ActionButton
                  variant="info"
                  onClick={() => {
                    console.log('🔄 Refresh Configuration button clicked - loading all Discord data')
                    loadAllDiscordData()
                  }}
                  loading={isLoading}
                >
                  🔄 Refresh Configuration
                </ActionButton>
              </div>
            </FormSection>

            <FormSection title="📋 Notification Format" collapsible>
              <div style={{
                background: 'rgba(34, 197, 94, 0.1)',
                border: '1px solid #22c55e',
                borderRadius: '8px',
                padding: '16px',
                color: '#22c55e'
              }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                  <span style={{ fontSize: '18px' }}>✅</span>
                  <strong>Standard Format Active</strong>
                </div>
                <p style={{ margin: 0, fontSize: '14px', lineHeight: '1.5' }}>
                  All Discord notifications now use a clean, consistent format:<br/>
                  • Clear titles with your display name<br/>
                  • Category/game prominently displayed<br/>
                  • Watch Now buttons for stream notifications<br/>
                  • Progress tracking for milestone achievements
                </p>
                <p style={{ margin: '12px 0 0 0', fontSize: '12px', opacity: 0.8 }}>
                  📋 Customizable templates will be available in a future update!
                </p>
              </div>
            </FormSection>

            <FormSection title="🎮 Discord Server Invite" collapsible>
              <div style={{
                background: 'rgba(88, 101, 242, 0.1)',
                border: '1px solid #5865f2',
                borderRadius: '8px',
                padding: '16px',
                marginBottom: '20px',
                color: '#5865f2'
              }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px' }}>
                  <span style={{ fontSize: '20px' }}>💬</span>
                  <strong>Chat Commands Available</strong>
                </div>
                <div style={{ fontSize: '14px', lineHeight: '1.6' }}>
                  <p style={{ margin: '0 0 8px 0' }}>
                    <strong>For Viewers:</strong> <code style={{ background: '#2a2a2a', padding: '2px 6px', borderRadius: '4px' }}>!discord</code> - Get the Discord server invite link
                  </p>
                  <p style={{ margin: '0 0 8px 0' }}>
                    <strong>For Viewers:</strong> <code style={{ background: '#2a2a2a', padding: '2px 6px', borderRadius: '4px' }}>!help</code> - Show all available chat commands
                  </p>
                  <p style={{ margin: '0 0 8px 0' }}>
                    <strong>For Moderators:</strong> <code style={{ background: '#2a2a2a', padding: '2px 6px', borderRadius: '4px' }}>!setdiscord &lt;invite_url&gt;</code> - Set the invite link
                  </p>
                  <p style={{ margin: '0' }}>
                    <strong>For Moderators:</strong> <code style={{ background: '#2a2a2a', padding: '2px 6px', borderRadius: '4px' }}>!removediscord</code> - Remove the invite link
                  </p>
                </div>
              </div>

              <InputGroup
                label="Discord Server Invite URL"
                required={false}
              >
                <input
                  type="url"
                  value={discordInvite}
                  onChange={(e) => setDiscordInvite(e.target.value)}
                  placeholder="https://discord.gg/YOUR_INVITE_CODE"
                  disabled={isLoading}
                  style={{
                    width: '100%',
                    padding: '12px',
                    background: '#2a2a2a',
                    border: `2px solid ${discordInvite ? '#5865f2' : '#444'}`,
                    borderRadius: '8px',
                    color: '#fff',
                    fontSize: '1rem',
                    fontFamily: 'Courier New, monospace'
                  }}
                />
                <small style={{
                  color: '#888',
                  fontSize: '0.875rem',
                  display: 'block',
                  marginTop: '5px'
                }}>
                  Supported formats: discord.gg/..., discord.com/invite/..., or discordapp.com/invite/...
                </small>
                {discordInvite && (
                  <div style={{
                    marginTop: '8px',
                    padding: '6px 10px',
                    background: isValidDiscordInviteUrl(discordInvite) ? '#5865f21a' : '#ef44441a',
                    border: `1px solid ${isValidDiscordInviteUrl(discordInvite) ? '#5865f2' : '#ef4444'}`,
                    borderRadius: '4px',
                    fontSize: '0.8rem',
                    color: isValidDiscordInviteUrl(discordInvite) ? '#5865f2' : '#ef4444'
                  }}>
                    {isValidDiscordInviteUrl(discordInvite)
                      ? '✅ Valid Discord invite URL format'
                      : '❌ Invalid format - please use a proper Discord invite URL'
                    }
                  </div>
                )}
              </InputGroup>

              <div className="webhook-actions">
                <ActionButton
                  variant="primary"
                  onClick={saveDiscordInvite}
                  loading={isLoading}
                  disabled={isLoading || (discordInvite && !isValidDiscordInviteUrl(discordInvite))}
                >
                  💾 Save Discord Invite
                </ActionButton>

                <ActionButton
                  variant="danger"
                  onClick={removeDiscordInvite}
                  loading={isLoading}
                  disabled={isLoading || !discordInvite}
                >
                  🗑️ Remove Invite
                </ActionButton>

                <ActionButton
                  variant="info"
                  onClick={() => {
                    console.log('🔄 Refresh Discord invite button clicked - loading all Discord data')
                    loadAllDiscordData()
                  }}
                  loading={isLoading}
                >
                  🔄 Refresh
                </ActionButton>
              </div>

              {discordInvite && (
                <div style={{
                  marginTop: '20px',
                  padding: '16px',
                  background: 'rgba(34, 197, 94, 0.1)',
                  border: '1px solid #22c55e',
                  borderRadius: '8px',
                  color: '#22c55e'
                }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                    <span style={{ fontSize: '18px' }}>🎉</span>
                    <strong>Invite Link Active!</strong>
                  </div>
                  <p style={{ margin: '0 0 8px 0', fontSize: '14px' }}>
                    Viewers can now use <code style={{ background: '#2a2a2a', padding: '2px 6px', borderRadius: '4px' }}>!discord</code> in chat to get your server invite.
                  </p>
                  <p style={{ margin: '0', fontSize: '12px', opacity: 0.8 }}>
                    Current invite: <span style={{ fontFamily: 'Courier New, monospace' }}>{discordInvite}</span>
                  </p>
                </div>
              )}
            </FormSection>
          </div>
        )}

      </div>
    </div>
  )
}

export default DiscordWebhookSettings

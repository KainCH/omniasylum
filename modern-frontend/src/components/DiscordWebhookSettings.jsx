import { useState, useEffect } from 'react'
import './DiscordWebhookSettings.css'
import { ToggleSwitch, ActionButton, FormSection, InputGroup, StatusBadge } from './ui/CommonControls'
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

  // Tab state management
  const [activeTab, setActiveTab] = useState('notifications')

  // Form state management using our new hooks
  const { values: formState, setValue: updateField, reset: resetForm } = useFormState({
    webhookUrl: '',
    enabled: false,
    discordInviteLink: ''
  })

  // Debug form state changes
  useEffect(() => {
    console.log('🔄 FORM STATE CHANGED:', {
      webhookUrl: formState.webhookUrl,
      enabled: formState.enabled,
      discordInviteLink: formState.discordInviteLink,
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
      console.log('📥 Loading Discord settings for user')
      loadUserDiscordSettings()
    }
  }, [user])

  // Load configuration when switching to configuration tab
  useEffect(() => {
    if (activeTab === 'configuration' && user) {
      console.log('⚙️ Configuration tab opened, loading webhook configuration...')
      loadWebhookConfiguration()
    }
  }, [activeTab, user])

  const showMessage = (text, type) => {
    setMessage(text)
    setMessageType(type)
    setTimeout(() => setMessage(''), 3000)
  }

  // Load webhook configuration specifically for the configuration tab
  const loadWebhookConfiguration = async () => {
    console.log('⚙️ Loading webhook configuration for configuration tab...')
    try {
      await withLoading(async () => {
        console.log('🔗 Fetching configuration from consolidated notification settings...')
        const discordData = await notificationAPI.getUserSettings()
        console.log('🔗 Consolidated data received:', discordData)
        console.log('🔗 Extracted webhook URL:', discordData?.webhookUrl)
        console.log('🔗 Extracted enabled status:', discordData?.enabled)

        updateField('webhookUrl', discordData?.webhookUrl || '')
        updateField('enabled', discordData?.enabled || false)

        // Load Discord invite link
        console.log('🔗 Fetching Discord invite link...')
        const inviteData = await userAPI.getDiscordInvite()
        console.log('🔗 Discord invite data received:', inviteData)
        updateField('discordInviteLink', inviteData?.discordInviteLink || '')

        if (discordData?.webhookUrl || inviteData?.discordInviteLink) {
          console.log('✅ Existing Discord configuration loaded')
          showMessage(`Configuration loaded: Webhook ${discordData?.enabled ? 'enabled' : 'disabled'}, Invite ${inviteData?.configured ? 'configured' : 'not configured'}`, 'success')
        } else {
          console.log('ℹ️ No existing Discord configuration found')
          showMessage('No existing configuration found - you can set one up below', 'info')
        }
      })
    } catch (error) {
      console.error('❌ Error loading Discord configuration:', error)
      console.error('❌ Full error details:', error?.message || 'Unknown error', error?.stack || 'No stack trace')
      showMessage('Failed to load Discord configuration', 'error')
    }
  }

  const loadUserDiscordSettings = async () => {
    console.log('📥 Starting to load Discord settings...')
    try {
      await withLoading(async () => {
        // Load webhook URL
        try {
          console.log('🔗 Loading webhook URL...')
          const webhookData = await userAPI.getDiscordWebhook()
          console.log('🔗 Webhook data received:', webhookData)
          console.log('🔗 Extracted webhook URL:', webhookData?.webhookUrl)
          console.log('🔗 Extracted enabled status:', webhookData?.enabled)
          console.log('🔧 UPDATING FORM FIELDS:', {
            webhookUrl: webhookData?.webhookUrl || '',
            enabled: webhookData?.enabled || false,
            rawData: webhookData
          })
          console.log('🔧 UPDATING FORM FIELDS (loadUserDiscordSettings):', {
            webhookUrl: webhookData?.webhookUrl || '',
            enabled: webhookData?.enabled || false,
            rawData: webhookData
          })
          updateField('webhookUrl', webhookData?.webhookUrl || '')
          updateField('enabled', webhookData?.enabled || false)
          console.log('🔧 FORM FIELDS UPDATED (loadUserDiscordSettings) - checking state next...')
          console.log('🔧 FORM FIELDS UPDATED - checking state next...')
          console.log('✅ Webhook URL loaded successfully, form updated')
        } catch (error) {
          console.error('❌ Error loading webhook:', error)
          console.error('❌ Full error details:', error?.message || 'Unknown error', error?.stack || 'No stack trace')
          showMessage(`Failed to load Discord settings: ${error?.message || 'Unknown error'}`, 'error')
        }

        // Load notification settings
        try {
          console.log('📢 Loading notification settings...')
          const discordData = await notificationAPI.getUserSettings()
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

  const saveWebhookConfiguration = async () => {
    console.log('� Save Webhook Configuration button clicked')
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
        await userAPI.updateDiscordWebhook(webhookPayload)
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
        await notificationAPI.updateUserSettings(notificationSettings)
        console.log('✅ Notification settings saved successfully')

        showMessage('Notification settings saved successfully!', 'success')
      })
    } catch (error) {
      console.error('❌ Error saving notification settings:', error)
      showMessage('Failed to save notification settings', 'error')
    }
  }

  // Template settings function removed - standard format used for all notifications

  const saveDiscordInviteLink = async () => {
    console.log('🔗 Save Discord Invite Link button clicked')
    console.log('📊 Invite link to save:', formState?.discordInviteLink)

    try {
      await withLoading(async () => {
        // Validate invite link format (optional validation)
        if (formState?.discordInviteLink && !formState?.discordInviteLink.match(/^https:\/\/(discord\.gg\/|discord\.com\/invite\/)/)) {
          console.log('❌ Discord invite URL validation failed')
          showMessage('Invalid Discord invite URL format. Should start with https://discord.gg/ or https://discord.com/invite/', 'error')
          return
        }
        console.log('✅ Discord invite URL validation passed')

        // Save invite link
        console.log('🔗 Saving Discord invite link...')
        const payload = {
          discordInviteLink: formState?.discordInviteLink || ''
        }
        console.log('🔗 Invite payload:', payload)
        await userAPI.updateDiscordInvite(payload)
        console.log('✅ Discord invite link saved successfully')

        showMessage('Discord invite link saved successfully!', 'success')
      })
    } catch (error) {
      console.error('❌ Error saving Discord invite link:', error)
      showMessage('Failed to save Discord invite link', 'error')
    }
  }

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
      await withLoading(async () => {
        await userAPI.testDiscordWebhook({ webhookUrl: formState?.webhookUrl || '' })
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
            console.log('⚙️ Discord Settings tab clicked, switching and loading configuration...')
            setActiveTab('configuration')
          }}
        >
          ⚙️ Discord Settings
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
                    console.log('🔄 Refresh Configuration button clicked')
                    loadWebhookConfiguration()
                  }}
                  loading={isLoading}
                >
                  🔄 Refresh Configuration
                </ActionButton>
              </div>
            </FormSection>

            <FormSection title="� Discord Invite Link">
              <div className="settings-info">
                <p>Configure a Discord invite link for the !discord chat command!</p>
              </div>

              <div className="invite-setup">
                <h4>How it works</h4>
                <ol>
                  <li>Set your Discord server invite link below</li>
                  <li>When viewers type <code>!discord</code> in chat, the bot will post your invite</li>
                  <li>The message includes special effects and your custom invite link</li>
                </ol>
              </div>

              <InputGroup
                label="Discord Invite Link"
                required={false}
              >
                <input
                  type="url"
                  value={formState?.discordInviteLink || ''}
                  onChange={(e) => updateField('discordInviteLink', e.target.value)}
                  placeholder="https://discord.gg/yourserver or https://discord.com/invite/yourserver"
                  disabled={isLoading}
                  style={{
                    width: '100%',
                    padding: '12px',
                    background: '#2a2a2a',
                    border: `2px solid ${formState?.discordInviteLink ? '#22c55e' : '#444'}`,
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
                  Discord invite URLs typically start with https://discord.gg/ or https://discord.com/invite/
                </small>
                {formState?.discordInviteLink && (
                  <div style={{
                    marginTop: '8px',
                    padding: '6px 10px',
                    background: '#22c55e1a',
                    border: '1px solid #22c55e',
                    borderRadius: '4px',
                    fontSize: '0.8rem',
                    color: '#22c55e'
                  }}>
                    ✅ Discord invite configured - !discord command will work in chat
                  </div>
                )}
              </InputGroup>

              <div className="invite-actions">
                <ActionButton
                  variant="primary"
                  onClick={() => {
                    console.log('🔗 Save Discord Invite Link button clicked')
                    console.log('📊 Invite link:', formState?.discordInviteLink)
                    saveDiscordInviteLink()
                  }}
                  loading={isLoading}
                >
                  💾 Save Invite Link
                </ActionButton>
              </div>
            </FormSection>

            <FormSection title="�📋 Notification Format" collapsible>
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
          </div>
        )}

      </div>
    </div>
  )
}

export default DiscordWebhookSettings

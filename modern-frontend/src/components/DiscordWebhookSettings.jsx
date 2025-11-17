import { useState, useEffect } from 'react'
import './DiscordWebhookSettings.css'
import { ToggleSwitch, ActionButton, FormSection, StatusBadge } from './ui/CommonControls'
import { useDiscordSettings } from '../hooks'

function DiscordWebhookSettings({ user }) {
  // Early return if user data is not available yet
  if (!user) {
    return (
      <div className="discord-settings-loading">
        <p>Loading user data...</p>
      </div>
    )
  }

  // Use our Discord settings hook
  const {
    webhookData,
    notificationSettings,
    discordInvite,
    message,
    messageType,
    isLoading,
    updateNotificationSetting,
    updateWebhookData,
    saveDiscordSettings,
    testDiscordWebhook,
    resetToDefaults,
    validateSettings,
    showMessage
  } = useDiscordSettings(user)

  // Tab state management
  const [activeTab, setActiveTab] = useState('notifications')

  // Debug tab state changes
  useEffect(() => {
    console.log('🔄 ACTIVE TAB CHANGED:', activeTab)
  }, [activeTab])

  // Debug notification settings changes
  useEffect(() => {
    console.log('🔔 NOTIFICATION SETTINGS CHANGED:', {
      deathMilestoneEnabled: notificationSettings?.deathMilestoneEnabled,
      swearMilestoneEnabled: notificationSettings?.swearMilestoneEnabled,
      deathThresholds: notificationSettings?.deathThresholds,
      swearThresholds: notificationSettings?.swearThresholds,
      fullSettings: notificationSettings
    })
  }, [notificationSettings])

  return (
    <div className="discord-webhook-settings">
      <div className="settings-header">
        <h3>🔔 Discord Settings</h3>
        {webhookData && (
          <StatusBadge
            status={webhookData?.enabled ? 'success' : 'info'}
          >
            {webhookData?.enabled ? 'Enabled' : 'Disabled'}
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
          onClick={() => {
            console.log('📢 Notifications tab clicked, current:', activeTab)
            setActiveTab('notifications')
          }}
        >
          📢 Notifications
        </button>
        <button
          className={`tab-button ${activeTab === 'counters' ? 'active' : ''}`}
          onClick={() => {
            console.log('🎯 Counters tab clicked, current:', activeTab)
            setActiveTab('counters')
          }}
        >
          🎯 Counters & Milestones
        </button>
        <button
          className={`tab-button ${activeTab === 'configuration' ? 'active' : ''}`}
          onClick={() => {
            console.log('⚙️ Configuration tab clicked, current:', activeTab)
            setActiveTab('configuration')
          }}
        >
          ⚙️ Configuration
        </button>
      </div>

      {/* Tab Content */}
      <div className="tab-content">
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
                      label="Enable Chat Notifications"
                      disabled={isLoading}
                      size="medium"
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
                    saveDiscordSettings()
                  }}
                  loading={isLoading}
                >
                  💾 Save Notification Settings
                </ActionButton>
              </div>

              {/* Requirements Notice */}
              {(!webhookData?.webhookUrl) && (
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

        {activeTab === 'counters' && (
          <div className="tab-panel">
            <FormSection title="🎯 Milestone Notifications">
              <div className="milestone-settings">
                <div className="milestone-group">
                  <div className="milestone-header">
                    <div className="milestone-icon">💀</div>
                    <div className="milestone-title">
                      <h4>Death Count Milestones</h4>
                      <p>Trigger notifications when death count reaches specific milestones</p>
                    </div>
                    <div className="milestone-toggle">
                      <ToggleSwitch
                        checked={notificationSettings?.deathMilestoneEnabled || false}
                        onChange={(e) => updateNotificationSetting('deathMilestoneEnabled', e.target.checked)}
                        label="Enable"
                        disabled={isLoading}
                        size="medium"
                      />
                    </div>
                  </div>
                  <div className="milestone-config">
                    <div className="input-group">
                      <label className="input-label">Milestone Thresholds</label>
                      <input
                        type="text"
                        value={notificationSettings?.deathThresholds || '10,25,50,100,250,500'}
                        onChange={(e) => updateNotificationSetting('deathThresholds', e.target.value)}
                        placeholder="10,25,50,100,250,500"
                        disabled={isLoading || !notificationSettings?.deathMilestoneEnabled}
                        className="threshold-input"
                      />
                      <div className="input-hint">
                        {notificationSettings?.deathMilestoneEnabled
                          ? "Comma-separated numbers (e.g., 10,25,50,100,250,500)"
                          : "Enable Death Count Milestones above to edit these thresholds"}
                      </div>
                    </div>
                  </div>
                </div>

                <div className="milestone-group">
                  <div className="milestone-header">
                    <div className="milestone-icon">🤬</div>
                    <div className="milestone-title">
                      <h4>Swear Count Milestones</h4>
                      <p>Trigger notifications when swear count reaches specific milestones</p>
                    </div>
                    <div className="milestone-toggle">
                      <ToggleSwitch
                        checked={notificationSettings?.swearMilestoneEnabled || false}
                        onChange={(e) => updateNotificationSetting('swearMilestoneEnabled', e.target.checked)}
                        label="Enable"
                        disabled={isLoading}
                        size="medium"
                      />
                    </div>
                  </div>
                  <div className="milestone-config">
                    <div className="input-group">
                      <label className="input-label">Milestone Thresholds</label>
                      <input
                        type="text"
                        value={notificationSettings?.swearThresholds || '25,50,100,200,500'}
                        onChange={(e) => updateNotificationSetting('swearThresholds', e.target.value)}
                        placeholder="25,50,100,200,500"
                        disabled={isLoading || !notificationSettings?.swearMilestoneEnabled}
                        className="threshold-input"
                      />
                      <div className="input-hint">
                        {notificationSettings?.swearMilestoneEnabled
                          ? "Comma-separated numbers (e.g., 25,50,100,200,500)"
                          : "Enable Swear Count Milestones above to edit these thresholds"}
                      </div>
                    </div>
                  </div>
                </div>
              </div>

              {/* Save Milestone Settings */}
              <div className="milestone-actions">
                <ActionButton
                  variant="primary"
                  onClick={() => {
                    console.log('� Save Milestone Settings button clicked')
                    console.log('📊 Milestone settings:', {
                      deathMilestoneEnabled: notificationSettings?.deathMilestoneEnabled,
                      swearMilestoneEnabled: notificationSettings?.swearMilestoneEnabled,
                      deathThresholds: notificationSettings?.deathThresholds,
                      swearThresholds: notificationSettings?.swearThresholds
                    })
                    saveDiscordSettings()
                  }}
                  loading={isLoading}
                >
                  💾 Save Milestone Settings
                </ActionButton>
              </div>
            </FormSection>
          </div>
        )}

        {activeTab === 'configuration' && (
          <div className="tab-panel">
            <FormSection title={`🔧 Webhook Setup ${isLoading ? '(Loading...)' : ''}`}>
              <div className="settings-info">
                <p>Connect your Discord server to receive stream notifications!</p>
                {webhookData?.webhookUrl && !isLoading && (
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

              <div className="input-group">
                <label className="input-label">
                  Discord Webhook URL {webhookData?.enabled ? '(✅ Enabled)' : '(❌ Disabled)'}
                </label>
                <input
                  type="url"
                  value={webhookData?.webhookUrl || ''}
                  onChange={(e) => updateWebhookData('webhookUrl', e.target.value)}
                  placeholder="https://discord.com/api/webhooks/..."
                  disabled={isLoading}
                  style={{
                    width: '100%',
                    padding: '12px',
                    background: '#2a2a2a',
                    border: `2px solid ${webhookData?.webhookUrl ? '#22c55e' : '#444'}`,
                    borderRadius: '8px',
                    color: '#fff',
                    fontSize: '1rem',
                    fontFamily: 'monospace'
                  }}
                />
              </div>

              <div className="webhook-actions">
                <ActionButton
                  variant="primary"
                  onClick={() => {
                    console.log('💾 Save Webhook Configuration button clicked')
                    console.log('📊 Webhook data:', webhookData)
                    // Enable webhook when URL is provided
                    if (webhookData?.webhookUrl && !webhookData?.enabled) {
                      updateWebhookData('enabled', true)
                    }
                    saveDiscordSettings()
                  }}
                  loading={isLoading}
                >
                  💾 Save Webhook Configuration
                </ActionButton>

                <ActionButton
                  variant="secondary"
                  onClick={() => {
                    console.log('🧪 Test Webhook button clicked')
                    testDiscordWebhook()
                  }}
                  loading={isLoading}
                  disabled={isLoading || !webhookData?.webhookUrl}
                >
                  🧪 Send Test
                </ActionButton>
              </div>
            </FormSection>

            <FormSection title="🎮 Discord Server Invite">
              <div className="input-group">
                <label className="input-label">Discord Server Invite URL</label>
                <input
                  type="url"
                  value={discordInvite || ''}
                  onChange={(e) => {
                    // This would need to be handled by the hook
                    console.log('Discord invite changed:', e.target.value)
                  }}
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
                    fontFamily: 'monospace'
                  }}
                />
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
                    Viewers can use <code style={{ background: '#2a2a2a', padding: '2px 6px', borderRadius: '4px' }}>!discord</code> in chat to get your server invite.
                  </p>
                  <p style={{ margin: '0', fontSize: '12px', opacity: 0.8 }}>
                    Current invite: <span style={{ fontFamily: 'monospace' }}>{discordInvite}</span>
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

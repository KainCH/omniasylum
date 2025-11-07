import { useState, useEffect } from 'react'
import './UserManagementModal.css'

function UserManagementModal({ user, features, onClose, onUpdate }) {
  const [activeTab, setActiveTab] = useState('features')
  const [userFeatures, setUserFeatures] = useState({})
  const [overlaySettings, setOverlaySettings] = useState(null)
  const [discordWebhook, setDiscordWebhook] = useState('')
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState('')
  const [messageType, setMessageType] = useState('')

  useEffect(() => {
    if (user) {
      loadUserData()
    }
  }, [user])

  // Reload Discord webhook when user prop changes
  useEffect(() => {
    if (user && user.features?.discordNotifications) {
      setDiscordWebhook(user.discordWebhookUrl || '')
    }
  }, [user.discordWebhookUrl])

  const getAuthHeaders = () => {
    const token = localStorage.getItem('authToken')
    return {
      'Content-Type': 'application/json',
      'Authorization': token ? `Bearer ${token}` : ''
    }
  }

  const loadUserData = async () => {
    try {
      // Load features
      setUserFeatures(user.features || {})

      // Load overlay settings if streamOverlay feature is enabled
      if (user.features?.streamOverlay) {
        try {
          const overlayRes = await fetch(`/api/admin/users/${user.twitchUserId}/overlay`, {
            headers: getAuthHeaders(),
            credentials: 'include'
          })
          if (overlayRes.ok) {
            const data = await overlayRes.json()
            setOverlaySettings(data.overlaySettings || getDefaultOverlaySettings())
          } else {
            // If API call fails, set default settings
            setOverlaySettings(getDefaultOverlaySettings())
          }
        } catch (error) {
          console.error('Error loading overlay settings:', error)
          // On error, set default settings so UI still works
          setOverlaySettings(getDefaultOverlaySettings())
        }
      } else {
        // Clear overlay settings if feature is disabled
        setOverlaySettings(null)
      }

      // Load Discord webhook
      if (user.features?.discordNotifications) {
        setDiscordWebhook(user.discordWebhookUrl || '')
      }
    } catch (error) {
      console.error('Error loading user data:', error)
    }
  }

  const getDefaultOverlaySettings = () => ({
    enabled: true,
    position: 'top-right',
    size: 'medium',
    counters: {
      deaths: true,
      swears: true,
      bits: false,
      channelPoints: false
    },
    animations: {
      enabled: true,
      showAlerts: true,
      celebrationEffects: false,
      bounceOnUpdate: true,
      fadeTransitions: true
    },
    theme: {
      borderColor: '#9146ff',
      textColor: '#ffffff',
      backgroundColor: 'rgba(0, 0, 0, 0.8)'
    }
  })

  const toggleFeature = (featureKey, currentValue) => {
    const newValue = !currentValue
    setUserFeatures(prev => ({
      ...prev,
      [featureKey]: newValue
    }))
  }

  const saveFeatures = async () => {
    try {
      setSaving(true)

      // Merge current features with original user features to preserve any features not shown in UI
      const originalFeatures = user.features || {}
      const updatedFeatures = { ...originalFeatures, ...userFeatures }

      const response = await fetch(`/api/admin/users/${user.twitchUserId}/features`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        credentials: 'include',
        body: JSON.stringify({
          features: updatedFeatures
        })
      })

      if (response.ok) {
        showMessage('Features saved successfully!', 'success')
        if (onUpdate) onUpdate()
      } else {
        throw new Error('Failed to save features')
      }
    } catch (error) {
      console.error('Error saving features:', error)
      showMessage('Failed to save features', 'error')
    } finally {
      setSaving(false)
    }
  }

  const saveOverlaySettings = async () => {
    try {
      setSaving(true)
      const response = await fetch(`/api/admin/users/${user.twitchUserId}/overlay`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        credentials: 'include',
        body: JSON.stringify({ overlaySettings })
      })

      if (response.ok) {
        showMessage('Overlay settings saved!', 'success')
        if (onUpdate) onUpdate()
      } else {
        throw new Error('Failed to save overlay settings')
      }
    } catch (error) {
      console.error('Error saving overlay settings:', error)
      showMessage('Failed to save overlay settings', 'error')
    } finally {
      setSaving(false)
    }
  }

  const saveDiscordWebhook = async () => {
    try {
      setSaving(true)

      // Validate webhook URL
      if (discordWebhook && !discordWebhook.startsWith('https://discord.com/api/webhooks/')) {
        showMessage('Invalid Discord webhook URL format', 'error')
        return
      }

      const response = await fetch(`/api/admin/users/${user.twitchUserId}/discord-webhook`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        credentials: 'include',
        body: JSON.stringify({ webhookUrl: discordWebhook })
      })

      if (response.ok) {
        showMessage('Discord webhook saved!', 'success')
        if (onUpdate) onUpdate()
      } else {
        throw new Error('Failed to save Discord webhook')
      }
    } catch (error) {
      console.error('Error saving Discord webhook:', error)
      showMessage('Failed to save Discord webhook', 'error')
    } finally {
      setSaving(false)
    }
  }

  const testDiscordWebhook = async () => {
    if (!discordWebhook) {
      showMessage('Please save a webhook URL first', 'error')
      return
    }

    try {
      setSaving(true)
      const response = await fetch(`/api/admin/users/${user.twitchUserId}/discord-webhook/test`, {
        method: 'POST',
        headers: getAuthHeaders(),
        credentials: 'include'
      })

      if (response.ok) {
        showMessage('Test notification sent! Check Discord.', 'success')
      } else {
        const error = await response.json()
        showMessage(error.error || 'Failed to send test', 'error')
      }
    } catch (error) {
      console.error('Error testing webhook:', error)
      showMessage('Failed to send test notification', 'error')
    } finally {
      setSaving(false)
    }
  }

  const showMessage = (text, type) => {
    setMessage(text)
    setMessageType(type)
    setTimeout(() => {
      setMessage('')
      setMessageType('')
    }, 3000)
  }

  if (!user) return null

  return (
    <div className="user-management-modal-overlay" onClick={onClose}>
      <div className="user-management-modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div className="user-title">
            <img
              src={user.profileImageUrl || `https://ui-avatars.com/api/?name=${encodeURIComponent(user.displayName || user.username)}`}
              alt={user.displayName}
              className="user-avatar-small"
            />
            <div>
              <h2>{user.displayName}</h2>
              <p className="username">@{user.username}</p>
            </div>
          </div>
          <button onClick={onClose} className="btn-close">✕</button>
        </div>

        {message && (
          <div className={`message-banner ${messageType}`}>
            {message}
          </div>
        )}

        <div className="modal-tabs">
          <button
            className={`tab ${activeTab === 'features' ? 'active' : ''}`}
            onClick={() => setActiveTab('features')}
          >
            🎛️ Features
          </button>
          {userFeatures.streamOverlay && (
            <button
              className={`tab ${activeTab === 'overlay' ? 'active' : ''}`}
              onClick={() => setActiveTab('overlay')}
            >
              🎨 Overlay Settings
            </button>
          )}
          {userFeatures.discordNotifications && (
            <button
              className={`tab ${activeTab === 'discord' ? 'active' : ''}`}
              onClick={() => setActiveTab('discord')}
            >
              🔔 Discord Notifications
            </button>
          )}
        </div>

        <div className="modal-content">
          {activeTab === 'features' && (
            <div className="features-panel">
              <h3>Feature Assignment</h3>
              <p className="panel-description">
                Enable or disable features for this user. Enabled features will appear in their user portal.
              </p>
              <div className="features-list">
                {features.map(feature => {
                  const isEnabled = userFeatures[feature.id] || false
                  return (
                    <div key={feature.id} className="feature-item">
                      <div className="feature-info">
                        <div className="feature-header">
                          <span className="feature-icon">{feature.icon || '📦'}</span>
                          <strong>{feature.name}</strong>
                          <span className={`status-badge ${isEnabled ? 'enabled' : 'disabled'}`}>
                            {isEnabled ? '✅ Enabled' : '❌ Disabled'}
                          </span>
                        </div>
                        <p className="feature-description">{feature.description}</p>
                      </div>
                      <label className="toggle-switch">
                        <input
                          type="checkbox"
                          checked={isEnabled}
                          onChange={() => toggleFeature(feature.id, isEnabled)}
                          disabled={saving}
                        />
                        <span className="slider"></span>
                      </label>
                    </div>
                  )
                })}
              </div>
              <div className="panel-actions">
                <button
                  className="btn-save-features"
                  onClick={saveFeatures}
                  disabled={saving}
                >
                  {saving ? '💾 Saving...' : '💾 Save Features'}
                </button>
              </div>
            </div>
          )}

          {activeTab === 'overlay' && (
            <div className="overlay-panel">
              {!overlaySettings ? (
                <div className="loading-message">
                  <p>Loading overlay settings...</p>
                </div>
              ) : (
                <>
                  <h3>Overlay Settings</h3>
                  <p className="panel-description">
                    Configure the stream overlay appearance for {user.displayName}.
                  </p>

                  <div className="settings-section">
                    <h4>Position & Size</h4>
                    <div className="settings-grid">
                      <div className="setting-group">
                        <label>Position</label>
                        <select
                      value={overlaySettings.position}
                      onChange={(e) => setOverlaySettings({...overlaySettings, position: e.target.value})}
                    >
                      <option value="top-left">Top Left</option>
                      <option value="top-right">Top Right</option>
                      <option value="bottom-left">Bottom Left</option>
                      <option value="bottom-right">Bottom Right</option>
                    </select>
                  </div>
                  <div className="setting-group">
                    <label>Size</label>
                    <select
                      value={overlaySettings.size}
                      onChange={(e) => setOverlaySettings({...overlaySettings, size: e.target.value})}
                    >
                      <option value="small">Small</option>
                      <option value="medium">Medium</option>
                      <option value="large">Large</option>
                    </select>
                  </div>
                </div>
              </div>

              <div className="settings-section">
                <h4>Counters</h4>
                <div className="checkbox-grid">
                  <label>
                    <input
                      type="checkbox"
                      checked={overlaySettings.counters.deaths}
                      onChange={(e) => setOverlaySettings({
                        ...overlaySettings,
                        counters: {...overlaySettings.counters, deaths: e.target.checked}
                      })}
                    />
                    💀 Deaths
                  </label>
                  <label>
                    <input
                      type="checkbox"
                      checked={overlaySettings.counters.swears}
                      onChange={(e) => setOverlaySettings({
                        ...overlaySettings,
                        counters: {...overlaySettings.counters, swears: e.target.checked}
                      })}
                    />
                    🤬 Swears
                  </label>
                  <label>
                    <input
                      type="checkbox"
                      checked={overlaySettings.counters.bits}
                      onChange={(e) => setOverlaySettings({
                        ...overlaySettings,
                        counters: {...overlaySettings.counters, bits: e.target.checked}
                      })}
                    />
                    💎 Bits
                  </label>
                  <label>
                    <input
                      type="checkbox"
                      checked={overlaySettings.counters.channelPoints}
                      onChange={(e) => setOverlaySettings({
                        ...overlaySettings,
                        counters: {...overlaySettings.counters, channelPoints: e.target.checked}
                      })}
                    />
                    🎯 Channel Points
                  </label>
                </div>
              </div>

              <div className="settings-section">
                <h4>Animations</h4>
                <div className="checkbox-grid">
                  <label>
                    <input
                      type="checkbox"
                      checked={overlaySettings.animations.enabled}
                      onChange={(e) => setOverlaySettings({
                        ...overlaySettings,
                        animations: {...overlaySettings.animations, enabled: e.target.checked}
                      })}
                    />
                    Enable Animations
                  </label>
                  <label>
                    <input
                      type="checkbox"
                      checked={overlaySettings.animations.showAlerts}
                      onChange={(e) => setOverlaySettings({
                        ...overlaySettings,
                        animations: {...overlaySettings.animations, showAlerts: e.target.checked}
                      })}
                    />
                    Show Alerts
                  </label>
                  <label>
                    <input
                      type="checkbox"
                      checked={overlaySettings.animations.celebrationEffects}
                      onChange={(e) => setOverlaySettings({
                        ...overlaySettings,
                        animations: {...overlaySettings.animations, celebrationEffects: e.target.checked}
                      })}
                    />
                    Celebration Effects
                  </label>
                  <label>
                    <input
                      type="checkbox"
                      checked={overlaySettings.animations.bounceOnUpdate}
                      onChange={(e) => setOverlaySettings({
                        ...overlaySettings,
                        animations: {...overlaySettings.animations, bounceOnUpdate: e.target.checked}
                      })}
                    />
                    Bounce on Update
                  </label>
                </div>
              </div>

              <div className="settings-section">
                <h4>Theme</h4>
                <div className="settings-grid">
                  <div className="setting-group">
                    <label>Border Color</label>
                    <div className="color-input-wrapper">
                      <input
                        type="color"
                        value={overlaySettings.theme.borderColor}
                        onChange={(e) => setOverlaySettings({
                          ...overlaySettings,
                          theme: {...overlaySettings.theme, borderColor: e.target.value}
                        })}
                      />
                      <span className="color-value">{overlaySettings.theme.borderColor}</span>
                    </div>
                  </div>
                  <div className="setting-group">
                    <label>Text Color</label>
                    <div className="color-input-wrapper">
                      <input
                        type="color"
                        value={overlaySettings.theme.textColor}
                        onChange={(e) => setOverlaySettings({
                          ...overlaySettings,
                          theme: {...overlaySettings.theme, textColor: e.target.value}
                        })}
                      />
                      <span className="color-value">{overlaySettings.theme.textColor}</span>
                    </div>
                  </div>
                  <div className="setting-group">
                    <label>Background Color</label>
                    <input
                      type="text"
                      value={overlaySettings.theme.backgroundColor}
                      onChange={(e) => setOverlaySettings({
                        ...overlaySettings,
                        theme: {...overlaySettings.theme, backgroundColor: e.target.value}
                      })}
                      placeholder="rgba(0, 0, 0, 0.8)"
                    />
                  </div>
                </div>
              </div>

              <div className="panel-actions">
                <button
                  onClick={saveOverlaySettings}
                  disabled={saving}
                  className="btn btn-primary"
                >
                  {saving ? '💾 Saving...' : '💾 Save Overlay Settings'}
                </button>
              </div>
                </>
              )}
            </div>
          )}

          {activeTab === 'discord' && (
            <div className="discord-panel">
              <h3>Discord Notifications</h3>
              <p className="panel-description">
                Configure Discord webhook for {user.displayName}'s stream notifications.
              </p>

              <div className="discord-setup">
                <div className="setup-instructions">
                  <h4>📖 Setup Instructions</h4>
                  <ol>
                    <li>Go to your Discord server settings</li>
                    <li>Navigate to <strong>Integrations → Webhooks</strong></li>
                    <li>Click <strong>New Webhook</strong></li>
                    <li>Name it (e.g., "Stream Notifications")</li>
                    <li>Select the channel for notifications</li>
                    <li>Copy the webhook URL and paste below</li>
                  </ol>
                </div>

                <div className="webhook-config">
                  <label>
                    <strong>Discord Webhook URL</strong>
                    <input
                      type="url"
                      value={discordWebhook}
                      onChange={(e) => setDiscordWebhook(e.target.value)}
                      placeholder="https://discord.com/api/webhooks/..."
                      className="webhook-input"
                    />
                  </label>
                  <small className="input-hint">
                    Must start with https://discord.com/api/webhooks/
                  </small>
                </div>

                <div className="webhook-preview">
                  <h4>📬 Preview</h4>
                  <div className="discord-message-preview">
                    <div className="discord-embed">
                      <div className="embed-header">
                        <img src={user.profileImageUrl} alt="" className="embed-avatar" />
                        <strong>{user.displayName} is now live on Twitch!</strong>
                      </div>
                      <div className="embed-content">
                        <p><strong>Game:</strong> [Current Game]</p>
                        <p><strong>Title:</strong> [Stream Title]</p>
                        <a href={`https://twitch.tv/${user.username}`} className="twitch-link">
                          Watch Stream →
                        </a>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="panel-actions">
                  <button
                    onClick={saveDiscordWebhook}
                    disabled={saving}
                    className="btn btn-primary"
                  >
                    {saving ? '💾 Saving...' : '💾 Save Webhook'}
                  </button>
                  <button
                    onClick={testDiscordWebhook}
                    disabled={saving || !discordWebhook}
                    className="btn btn-secondary"
                  >
                    {saving ? '⏳ Sending...' : '🧪 Send Test'}
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

export default UserManagementModal

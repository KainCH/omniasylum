import { useState, useEffect } from 'react'
import './DiscordWebhookSettings.css'

function DiscordWebhookSettings({ user }) {
  const [webhookUrl, setWebhookUrl] = useState('')
  const [enabled, setEnabled] = useState(false)
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [message, setMessage] = useState('')
  const [messageType, setMessageType] = useState('') // 'success' | 'error'

  useEffect(() => {
    fetchWebhookConfig()
  }, [user])

  const getAuthHeaders = () => {
    const token = localStorage.getItem('authToken')
    return {
      'Content-Type': 'application/json',
      'Authorization': token ? `Bearer ${token}` : ''
    }
  }

  const fetchWebhookConfig = async () => {
    try {
      const response = await fetch('/api/user/discord-webhook', {
        headers: getAuthHeaders()
      })

      if (response.ok) {
        const data = await response.json()
        setWebhookUrl(data.webhookUrl || '')
        setEnabled(data.enabled || false)
      }
    } catch (error) {
      console.error('Error fetching Discord webhook config:', error)
    }
  }

  const saveWebhook = async () => {
    setSaving(true)
    setMessage('')

    try {
      const response = await fetch('/api/user/discord-webhook', {
        method: 'PUT',
        headers: getAuthHeaders(),
        body: JSON.stringify({ webhookUrl })
      })

      if (response.ok) {
        setMessage('Discord webhook saved successfully!')
        setMessageType('success')
      } else {
        const error = await response.json()
        setMessage(error.error || 'Failed to save webhook')
        setMessageType('error')
      }
    } catch (error) {
      console.error('Error saving Discord webhook:', error)
      setMessage('Failed to save webhook')
      setMessageType('error')
    } finally {
      setSaving(false)
      setTimeout(() => setMessage(''), 3000)
    }
  }

  const testWebhook = async () => {
    if (!webhookUrl) {
      setMessage('Please save a webhook URL first')
      setMessageType('error')
      setTimeout(() => setMessage(''), 3000)
      return
    }

    setTesting(true)
    setMessage('')

    try {
      const response = await fetch('/api/user/discord-webhook/test', {
        method: 'POST',
        headers: getAuthHeaders()
      })

      if (response.ok) {
        setMessage('Test notification sent! Check your Discord channel.')
        setMessageType('success')
      } else {
        const error = await response.json()
        setMessage(error.error || 'Failed to send test notification')
        setMessageType('error')
      }
    } catch (error) {
      console.error('Error testing Discord webhook:', error)
      setMessage('Failed to send test notification')
      setMessageType('error')
    } finally {
      setTesting(false)
      setTimeout(() => setMessage(''), 5000)
    }
  }

  return (
    <div className="discord-webhook-settings">
      <div className="settings-header">
        <h3>🔔 Discord Notifications</h3>
        <div className="feature-status-badge">
          {enabled ? (
            <span className="badge badge-success">✅ Enabled</span>
          ) : (
            <span className="badge badge-disabled">❌ Disabled</span>
          )}
        </div>
      </div>

      <div className="settings-info">
        <p>Get notified in Discord when you go live on Twitch!</p>
        {!enabled && (
          <div className="info-box warning">
            <strong>⚠️ Feature not enabled:</strong> Enable "Discord Notifications" in your features to use this.
          </div>
        )}
      </div>

      <div className="webhook-setup">
        <h4>Setup Instructions</h4>
        <ol>
          <li>Go to your Discord server settings</li>
          <li>Click "Integrations" → "Webhooks"</li>
          <li>Click "New Webhook" or select an existing one</li>
          <li>Copy the webhook URL</li>
          <li>Paste it below and click Save</li>
        </ol>
      </div>

      <div className="webhook-input-group">
        <label htmlFor="discord-webhook-url">
          <strong>Discord Webhook URL</strong>
        </label>
        <input
          id="discord-webhook-url"
          type="text"
          value={webhookUrl}
          onChange={(e) => setWebhookUrl(e.target.value)}
          placeholder="https://discord.com/api/webhooks/..."
          className="webhook-input"
          disabled={!enabled}
        />
        <small className="input-hint">
          URL must start with: https://discord.com/api/webhooks/
        </small>
      </div>

      <div className="webhook-actions">
        <button
          onClick={saveWebhook}
          disabled={saving || !enabled || !webhookUrl}
          className="btn btn-primary"
        >
          {saving ? '💾 Saving...' : '💾 Save Webhook'}
        </button>
        <button
          onClick={testWebhook}
          disabled={testing || !enabled || !webhookUrl}
          className="btn btn-secondary"
        >
          {testing ? '🧪 Testing...' : '🧪 Send Test'}
        </button>
      </div>

      {message && (
        <div className={`message-box ${messageType}`}>
          {messageType === 'success' ? '✅' : '❌'} {message}
        </div>
      )}

      <div className="webhook-preview">
        <h4>What gets posted to Discord?</h4>
        <div className="preview-card">
          <div className="discord-message-preview">
            <div className="discord-embed">
              <div className="embed-header">
                <img src={user?.profileImageUrl} alt="Profile" className="embed-thumbnail" />
                <div>
                  <strong>🔴 {user?.displayName} just went LIVE on Twitch!</strong>
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
    </div>
  )
}

export default DiscordWebhookSettings

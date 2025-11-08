import { useState, useEffect } from 'react'
import './AlertEventManager.css'

function AlertEventManager({ userId, username, onClose }) {
  const [alerts, setAlerts] = useState([])
  const [eventMappings, setEventMappings] = useState({})
  const [defaultMappings, setDefaultMappings] = useState({})
  const [availableEvents, setAvailableEvents] = useState([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [testingEvent, setTestingEvent] = useState(null)
  const [showPreview, setShowPreview] = useState(false)
  const [previewData, setPreviewData] = useState({})

  const eventDescriptions = {
    'channel.follow': {
      name: 'Follow',
      icon: '👥',
      description: 'User follows the channel',
      variables: ['[User]']
    },
    'channel.subscribe': {
      name: 'Subscription',
      icon: '⭐',
      description: 'New subscription or gift sub',
      variables: ['[User]', '[Tier]']
    },
    'channel.subscription.gift': {
      name: 'Gift Subscription',
      icon: '🎁',
      description: 'Community gift subs',
      variables: ['[User]', '[Amount]', '[Tier]']
    },
    'channel.subscription.message': {
      name: 'Resub Message',
      icon: '🔄',
      description: 'Resub with message',
      variables: ['[User]', '[Months]', '[Streak]', '[Message]', '[Tier]']
    },
    'channel.cheer': {
      name: 'Cheer/Bits',
      icon: '💎',
      description: 'Bits/cheers from viewers',
      variables: ['[User]', '[Bits]', '[Message]']
    },
    'channel.raid': {
      name: 'Raid',
      icon: '🚨',
      description: 'Channel receives raid',
      variables: ['[User]', '[Viewers]']
    }
  }

  useEffect(() => {
    if (userId) {
      fetchData()
    }
  }, [userId])

  const fetchData = async () => {
    try {
      setLoading(true)
      const token = localStorage.getItem('token')

      // Fetch user's alerts
      const alertsRes = await fetch(`/api/alerts/user/${userId}`, {
        headers: { 'Authorization': `Bearer ${token}` }
      })
      const alertsData = await alertsRes.json()
      setAlerts(alertsData.alerts || [])

      // Fetch event mappings
      const mappingsRes = await fetch('/api/alerts/event-mappings', {
        headers: { 'Authorization': `Bearer ${token}` }
      })
      const mappingsData = await mappingsRes.json()
      setEventMappings(mappingsData.mappings || {})
      setDefaultMappings(mappingsData.defaultMappings || {})
      setAvailableEvents(mappingsData.availableEvents || [])

    } catch (error) {
      console.error('Error fetching alert data:', error)
    } finally {
      setLoading(false)
    }
  }

  const handleMappingChange = (eventType, alertId) => {
    setEventMappings(prev => ({
      ...prev,
      [eventType]: alertId
    }))
  }

  const saveEventMappings = async () => {
    try {
      setSaving(true)
      const token = localStorage.getItem('token')

      const res = await fetch('/api/alerts/event-mappings', {
        method: 'PUT',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(eventMappings)
      })

      if (res.ok) {
        const data = await res.json()
        setEventMappings(data.mappings)
        alert('✅ Event mappings saved successfully!')
      } else {
        throw new Error('Failed to save mappings')
      }
    } catch (error) {
      console.error('Error saving mappings:', error)
      alert('❌ Failed to save event mappings')
    } finally {
      setSaving(false)
    }
  }

  const resetToDefaults = async () => {
    if (!confirm('Reset all event mappings to defaults?')) return

    try {
      setSaving(true)
      const token = localStorage.getItem('token')

      const res = await fetch('/api/alerts/event-mappings/reset', {
        method: 'POST',
        headers: { 'Authorization': `Bearer ${token}` }
      })

      if (res.ok) {
        const data = await res.json()
        setEventMappings(data.mappings)
        alert('✅ Reset to default mappings!')
      }
    } catch (error) {
      console.error('Error resetting mappings:', error)
      alert('❌ Failed to reset mappings')
    } finally {
      setSaving(false)
    }
  }

  const testAlert = (eventType) => {
    const alertId = eventMappings[eventType]
    const alert = alerts.find(a => a.id === alertId)

    if (!alert) {
      window.alert('No alert configured for this event')
      return
    }

    const eventInfo = eventDescriptions[eventType]

    // Create sample data for preview
    const sampleData = {
      'channel.follow': { follower: username || 'TestUser' },
      'channel.subscribe': { subscriber: username || 'TestUser', tier: '1000' },
      'channel.subscription.gift': { gifter: username || 'TestUser', amount: 5, tier: '1000' },
      'channel.subscription.message': { subscriber: username || 'TestUser', months: 12, streakMonths: 6, message: 'Love this stream!', tier: '1000' },
      'channel.cheer': { cheerer: username || 'TestUser', bits: 100, message: 'Great content!' },
      'channel.raid': { raider: username || 'TestUser', viewers: 50 }
    }

    setPreviewData({
      eventType,
      eventInfo,
      alert,
      sampleData: sampleData[eventType]
    })
    setShowPreview(true)
    setTestingEvent(eventType)

    // Auto-hide preview after alert duration
    setTimeout(() => {
      setShowPreview(false)
      setTestingEvent(null)
    }, alert.duration || 4000)
  }

  const processTextWithVariables = (text, data) => {
    if (!text || !data) return text

    let processed = text
    processed = processed.replace(/\[User\]/g, data.follower || data.subscriber || data.gifter || data.cheerer || data.raider || 'TestUser')
    processed = processed.replace(/\[Tier\]/g, data.tier === '1000' ? 'Tier 1' : data.tier === '2000' ? 'Tier 2' : data.tier === '3000' ? 'Tier 3' : 'Prime')
    processed = processed.replace(/\[Amount\]/g, data.amount || '')
    processed = processed.replace(/\[Months\]/g, data.months || '')
    processed = processed.replace(/\[Streak\]/g, data.streakMonths || '')
    processed = processed.replace(/\[Message\]/g, data.message || '')
    processed = processed.replace(/\[Bits\]/g, data.bits || '')
    processed = processed.replace(/\[Viewers\]/g, data.viewers || '')
    processed = processed.replace(/\[X\]/g, data.viewers || data.amount || data.bits || data.months || '')

    return processed
  }

  if (loading) {
    return (
      <div className="alert-event-manager">
        <div className="manager-header">
          <h2>Loading...</h2>
        </div>
      </div>
    )
  }

  return (
    <div className="alert-event-manager">
      <div className="manager-header">
        <h2>🎯 Event Alert Mappings for {username}</h2>
        <button onClick={onClose} className="btn-close">✕</button>
      </div>

      <div className="manager-description">
        <p>Assign custom alerts to Twitch events. When these events occur, the configured alert will display on your stream overlay.</p>
      </div>

      <div className="event-mappings-grid">
        {availableEvents.map(eventType => {
          const eventInfo = eventDescriptions[eventType] || { name: eventType, icon: '📢' }
          const currentAlertId = eventMappings[eventType]
          const currentAlert = alerts.find(a => a.id === currentAlertId)
          const defaultAlertId = defaultMappings[eventType]

          return (
            <div key={eventType} className="event-mapping-card">
              <div className="event-header">
                <span className="event-icon">{eventInfo?.icon || '📅'}</span>
                <div className="event-info">
                  <h3>{eventInfo?.name || 'Unknown Event'}</h3>
                  <p className="event-description">{eventInfo?.description || 'Event description unavailable'}</p>
                  {eventInfo.variables && (
                    <div className="event-variables">
                      <small>Available variables: {eventInfo.variables.join(', ')}</small>
                    </div>
                  )}
                </div>
              </div>

              <div className="alert-selector">
                <label>Alert to Display:</label>
                <select
                  value={currentAlertId || ''}
                  onChange={(e) => handleMappingChange(eventType, e.target.value)}
                  className="alert-select"
                >
                  <option value="">No Alert (Disabled)</option>
                  {alerts.filter(a => a.enabled).map(alert => (
                    <option key={alert.id} value={alert.id}>
                      {alert.name} ({alert.type})
                    </option>
                  ))}
                </select>

                {currentAlertId !== defaultAlertId && (
                  <span className="custom-badge">Custom</span>
                )}
              </div>

              {currentAlert && (
                <div className="current-alert-preview">
                  <div className="preview-label">Preview:</div>
                  <div
                    className="mini-preview"
                    style={{
                      backgroundColor: currentAlert.backgroundColor || '#1a0d0d',
                      color: currentAlert.textColor || '#ffffff',
                      border: `2px solid ${currentAlert.borderColor || '#666666'}`,
                      padding: '10px',
                      borderRadius: '5px',
                      fontSize: '12px'
                    }}
                  >
                    {currentAlert.textPrompt || 'No text prompt'}
                  </div>
                  <button
                    onClick={() => testAlert(eventType)}
                    className="btn-test"
                    disabled={testingEvent === eventType}
                  >
                    {testingEvent === eventType ? '⏳ Testing...' : '🎬 Test Alert'}
                  </button>
                </div>
              )}
            </div>
          )
        })}
      </div>

      <div className="manager-actions">
        <button
          onClick={saveEventMappings}
          className="btn-save"
          disabled={saving}
        >
          {saving ? '💾 Saving...' : '💾 Save Event Mappings'}
        </button>
        <button
          onClick={resetToDefaults}
          className="btn-reset"
          disabled={saving}
        >
          🔄 Reset to Defaults
        </button>
      </div>

      {/* Full-screen alert preview */}
      {showPreview && previewData.alert && (
        <div className="alert-preview-overlay">
          <div
            className="alert-preview-content"
            style={{
              backgroundColor: previewData.alert.backgroundColor || '#1a0d0d',
              color: previewData.alert.textColor || '#ffffff',
              border: `3px solid ${previewData.alert.borderColor || '#666666'}`,
              padding: '20px 30px',
              borderRadius: '10px',
              minWidth: '400px',
              maxWidth: '600px',
              textAlign: 'center',
              animation: 'asylumPulse 1s ease-in-out'
            }}
          >
            {previewData.alert.visualCue && (
              <div style={{ fontSize: '14px', opacity: 0.8, marginBottom: '10px', fontStyle: 'italic' }}>
                {previewData.alert.visualCue}
              </div>
            )}
            <div style={{ fontSize: '24px', fontWeight: 'bold' }}>
              {processTextWithVariables(previewData.alert.textPrompt, previewData.sampleData)}
            </div>
            {previewData.alert.soundDescription && (
              <div style={{ fontSize: '12px', opacity: 0.6, marginTop: '10px', fontStyle: 'italic' }}>
                ♪ {previewData.alert.soundDescription}
              </div>
            )}
          </div>
          <div className="preview-info">
            Testing: {previewData.eventInfo?.name || 'Unknown'} {previewData.eventInfo?.icon || '📅'}
          </div>
        </div>
      )}
    </div>
  )
}

export default AlertEventManager

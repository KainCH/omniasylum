import { useState, useEffect } from 'react'
import './UserAlertManager.css'

function UserAlertManager({ onClose }) {
  const [alerts, setAlerts] = useState([])
  const [eventMappings, setEventMappings] = useState({})
  const [defaultMappings, setDefaultMappings] = useState({})
  const [availableEvents, setAvailableEvents] = useState([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [activeTab, setActiveTab] = useState('mappings') // 'mappings' or 'create'
  const [testingEvent, setTestingEvent] = useState(null)
  const [showPreview, setShowPreview] = useState(false)
  const [previewData, setPreviewData] = useState({})

  const [newAlert, setNewAlert] = useState({
    type: 'custom',
    name: '',
    textPrompt: '',
    visualCue: '',
    sound: '',
    soundDescription: '',
    duration: 4000,
    backgroundColor: '#1a0d0d',
    textColor: '#ffffff',
    borderColor: '#666666'
  })

  const eventDescriptions = {
    'channel.follow': {
      name: 'Follow',
      icon: '👥',
      description: 'User follows the channel',
      variables: ['[User]'],
      example: 'Welcome to the asylum, [User]!'
    },
    'channel.subscribe': {
      name: 'Subscription',
      icon: '⭐',
      description: 'New subscription or gift sub',
      variables: ['[User]', '[Tier]'],
      example: 'Thanks for the [Tier] sub, [User]!'
    },
    'channel.subscription.gift': {
      name: 'Gift Subscription',
      icon: '🎁',
      description: 'Community gift subs',
      variables: ['[User]', '[Amount]', '[Tier]'],
      example: '[User] gifted [Amount] subs!'
    },
    'channel.subscription.message': {
      name: 'Resub Message',
      icon: '🔄',
      description: 'Resub with message',
      variables: ['[User]', '[Months]', '[Streak]', '[Message]', '[Tier]'],
      example: '[User] resubbed for [Months] months!'
    },
    'channel.cheer': {
      name: 'Cheer/Bits',
      icon: '💎',
      description: 'Bits/cheers from viewers',
      variables: ['[User]', '[Bits]', '[Message]'],
      example: '[User] cheered [Bits] bits!'
    },
    'channel.raid': {
      name: 'Raid',
      icon: '🚨',
      description: 'Channel receives raid',
      variables: ['[User]', '[Viewers]'],
      example: '[User] raided with [Viewers] viewers!'
    }
  }

  useEffect(() => {
    fetchData()
  }, [])

  const fetchData = async () => {
    try {
      setLoading(true)
      const token = localStorage.getItem('authToken')

      // Fetch user's alerts
      const alertsRes = await fetch('/api/alerts', {
        headers: { 'Authorization': `Bearer ${token}` }
      })
      const alertsData = await alertsRes.json()
      setAlerts(alertsData?.alerts || [])

      // Fetch event mappings
      const mappingsRes = await fetch('/api/alerts/event-mappings', {
        headers: { 'Authorization': `Bearer ${token}` }
      })
      const mappingsData = await mappingsRes.json()
      setEventMappings(mappingsData?.mappings || {})
      setDefaultMappings(mappingsData?.defaultMappings || {})
      setAvailableEvents(mappingsData?.availableEvents || [])

    } catch (error) {
      console.error('Error fetching alert data:', error)
    } finally {
      setLoading(false)
    }
  }

  const createAlert = async () => {
    if (!newAlert?.name || !newAlert?.textPrompt) {
      alert('Please fill in alert name and text prompt')
      return
    }

    try {
      setSaving(true)
      const token = localStorage.getItem('authToken')

      const res = await fetch('/api/alerts', {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(newAlert)
      })

      if (res.ok) {
        await fetchData() // Refresh alerts list
        setNewAlert({
          type: 'custom',
          name: '',
          textPrompt: '',
          visualCue: '',
          sound: '',
          soundDescription: '',
          duration: 4000,
          backgroundColor: '#1a0d0d',
          textColor: '#ffffff',
          borderColor: '#666666'
        })
        setActiveTab('mappings')
        alert('✅ Alert created successfully!')
      } else {
        throw new Error('Failed to create alert')
      }
    } catch (error) {
      console.error('Error creating alert:', error)
      alert('❌ Failed to create alert')
    } finally {
      setSaving(false)
    }
  }

  const deleteAlert = async (alertId, alertName) => {
    if (!confirm(`Delete alert "${alertName}"?`)) return

    try {
      const token = localStorage.getItem('authToken')
      const res = await fetch(`/api/alerts/${alertId}`, {
        method: 'DELETE',
        headers: { 'Authorization': `Bearer ${token}` }
      })

      if (res.ok) {
        await fetchData()
        alert('✅ Alert deleted')
      }
    } catch (error) {
      console.error('Error deleting alert:', error)
      alert('❌ Failed to delete alert')
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
      const token = localStorage.getItem('authToken')

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
        alert('✅ Event mappings saved!')
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

  const testAlert = (eventType) => {
    const alertId = eventMappings[eventType]
    const alert = alerts.find(a => a?.id === alertId)

    if (!alert) {
      window.alert('No alert configured for this event')
      return
    }

    const eventInfo = eventDescriptions[eventType]
    const sampleData = {
      'channel.follow': { follower: 'TestUser' },
      'channel.subscribe': { subscriber: 'TestUser', tier: '1000' },
      'channel.subscription.gift': { gifter: 'TestUser', amount: 5, tier: '1000' },
      'channel.subscription.message': { subscriber: 'TestUser', months: 12, streakMonths: 6, message: 'Love this stream!', tier: '1000' },
      'channel.cheer': { cheerer: 'TestUser', bits: 100, message: 'Great content!' },
      'channel.raid': { raider: 'TestUser', viewers: 50 }
    }

    setPreviewData({ eventType, eventInfo, alert, sampleData: sampleData[eventType] })
    setShowPreview(true)
    setTestingEvent(eventType)

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
      <div className="user-alert-manager">
        <div className="manager-header">
          <h2>Loading...</h2>
          <button onClick={onClose} className="btn-close">✕</button>
        </div>
      </div>
    )
  }

  return (
    <div className="user-alert-manager">
      <div className="manager-header">
        <h2>🎯 Alert & Event Manager</h2>
        <button onClick={onClose} className="btn-close">✕</button>
      </div>

      <div className="tab-navigation">
        <button
          className={`tab-button ${activeTab === 'mappings' ? 'active' : ''}`}
          onClick={() => setActiveTab('mappings')}
        >
          🎬 Event Mappings
        </button>
        <button
          className={`tab-button ${activeTab === 'create' ? 'active' : ''}`}
          onClick={() => setActiveTab('create')}
        >
          ➕ Create Alert
        </button>
      </div>

      {activeTab === 'mappings' && (
        <div className="mappings-tab">
          <div className="tab-description">
            <p>Assign your custom alerts to Twitch events. When these events occur, the configured alert will display on your stream overlay.</p>
          </div>

          <div className="event-mappings-grid">
            {availableEvents.map(eventType => {
              const eventInfo = eventDescriptions[eventType] || { name: eventType, icon: '📢' }
              const currentAlertId = eventMappings[eventType]
              const currentAlert = alerts.find(a => a?.id === currentAlertId)
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
                          <small>Variables: {eventInfo.variables.join(', ')}</small>
                        </div>
                      )}
                      {eventInfo.example && (
                        <div className="event-example">
                          <small>Example: "{eventInfo.example}"</small>
                        </div>
                      )}
                    </div>
                  </div>

                  <div className="alert-selector">
                    <select
                      id={`event-${eventType}`}
                      name={`event-${eventType}`}
                      value={currentAlertId || ''}
                      onChange={(e) => handleMappingChange(eventType, e.target.value)}
                      className="alert-select"
                    >
                      <option value="">No Alert (Disabled)</option>
                      {alerts.filter(a => a?.isEnabled !== false).map(alert => (
                        <option key={alert?.id} value={alert?.id}>
                          {alert?.name}
                        </option>
                      ))}
                    </select>

                    {currentAlertId !== defaultAlertId && currentAlertId && (
                      <span className="custom-badge">Custom</span>
                    )}
                  </div>

                  {currentAlert && (
                    <div className="current-alert-preview">
                      <div
                        className="mini-preview"
                        style={{
                          backgroundColor: currentAlert.backgroundColor || '#1a0d0d',
                          color: currentAlert.textColor || '#ffffff',
                          border: `2px solid ${currentAlert.borderColor || '#666666'}`,
                          padding: '8px',
                          borderRadius: '5px',
                          fontSize: '11px',
                          textAlign: 'center'
                        }}
                      >
                        {currentAlert.textPrompt || 'No text'}
                      </div>
                      <button
                        onClick={() => testAlert(eventType)}
                        className="btn-test-small"
                        disabled={testingEvent === eventType}
                      >
                        {testingEvent === eventType ? '⏳' : '🎬 Test'}
                      </button>
                    </div>
                  )}
                </div>
              )
            })}
          </div>

          <div className="mappings-actions">
            <button
              onClick={saveEventMappings}
              className="btn-save"
              disabled={saving}
            >
              {saving ? '💾 Saving...' : '💾 Save Event Mappings'}
            </button>
          </div>
        </div>
      )}

      {activeTab === 'create' && (
        <div className="create-tab">
          <div className="tab-description">
            <p>Create custom alerts with your own text, colors, and styling. Use variables like [User], [Bits], [Months] to personalize messages.</p>
          </div>

          <div className="alert-form">
            <div className="form-row">
              <div className="form-group">
                <label htmlFor="alert-name">Alert Name *</label>
                <input
                  id="alert-name"
                  name="alertName"
                  type="text"
                  placeholder="e.g., Custom Follow Alert"
                  value={newAlert?.name}
                  onChange={(e) => setNewAlert({...newAlert, name: e.target.value})}
                />
              </div>

              <div className="form-group">
                <label htmlFor="alert-type">Alert Type</label>
                <select
                  id="alert-type"
                  name="alertType"
                  value={newAlert.type}
                  onChange={(e) => setNewAlert({...newAlert, type: e.target.value})}
                >
                  <option value="custom">Custom Event</option>
                  <option value="follow">Follow</option>
                  <option value="subscription">Subscription</option>
                  <option value="resub">Resub</option>
                  <option value="bits">Bits</option>
                  <option value="raid">Raid</option>
                  <option value="giftsub">Gift Sub</option>
                </select>
              </div>
            </div>

            <div className="form-group full-width">
              <label htmlFor="text-prompt">Text Prompt * <span className="hint">Use [User], [Bits], [Months], [Tier], etc.</span></label>
              <input
                id="text-prompt"
                name="textPrompt"
                type="text"
                placeholder="e.g., Welcome to the asylum, [User]!"
                value={newAlert.textPrompt}
                onChange={(e) => setNewAlert({...newAlert, textPrompt: e.target.value})}
              />
            </div>

            <div className="form-row">
              <div className="form-group">
                <label htmlFor="visual-cue">Visual Cue <span className="hint">(optional)</span></label>
                <input
                  id="visual-cue"
                  name="visualCue"
                  type="text"
                  placeholder="Describe the visual effect"
                  value={newAlert.visualCue}
                  onChange={(e) => setNewAlert({...newAlert, visualCue: e.target.value})}
                />
              </div>

              <div className="form-group">
                <label htmlFor="sound-description">Sound Description <span className="hint">(optional)</span></label>
                <input
                  id="sound-description"
                  name="soundDescription"
                  type="text"
                  placeholder="Describe the sound"
                  value={newAlert.soundDescription}
                  onChange={(e) => setNewAlert({...newAlert, soundDescription: e.target.value})}
                />
              </div>
            </div>

            <div className="form-row">
              <div className="form-group">
                <label htmlFor="alert-duration">Duration (ms)</label>
                <input
                  id="alert-duration"
                  name="duration"
                  type="number"
                  min="1000"
                  max="30000"
                  value={newAlert.duration}
                  onChange={(e) => setNewAlert({...newAlert, duration: parseInt(e.target.value)})}
                />
              </div>

              <div className="form-group">
                <label htmlFor="bg-color">Background Color</label>
                <input
                  id="bg-color"
                  name="backgroundColor"
                  type="color"
                  value={newAlert.backgroundColor}
                  onChange={(e) => setNewAlert({...newAlert, backgroundColor: e.target.value})}
                />
              </div>

              <div className="form-group">
                <label htmlFor="text-color">Text Color</label>
                <input
                  id="text-color"
                  name="textColor"
                  type="color"
                  value={newAlert.textColor}
                  onChange={(e) => setNewAlert({...newAlert, textColor: e.target.value})}
                />
              </div>

              <div className="form-group">
                <label htmlFor="border-color">Border Color</label>
                <input
                  id="border-color"
                  name="borderColor"
                  type="color"
                  value={newAlert.borderColor}
                  onChange={(e) => setNewAlert({...newAlert, borderColor: e.target.value})}
                />
              </div>
            </div>

            <div className="alert-preview-box">
              <h4>Preview:</h4>
              <div
                className="preview-content"
                style={{
                  backgroundColor: newAlert.backgroundColor,
                  color: newAlert.textColor,
                  border: `3px solid ${newAlert.borderColor}`,
                  padding: '15px',
                  borderRadius: '8px',
                  textAlign: 'center'
                }}
              >
                {newAlert.visualCue && (
                  <div style={{ fontSize: '12px', opacity: 0.8, marginBottom: '8px' }}>
                    {newAlert.visualCue}
                  </div>
                )}
                <div style={{ fontSize: '16px', fontWeight: 'bold' }}>
                  {newAlert.textPrompt || 'Enter text prompt...'}
                </div>
                {newAlert.soundDescription && (
                  <div style={{ fontSize: '10px', opacity: 0.6, marginTop: '8px' }}>
                    ♪ {newAlert.soundDescription}
                  </div>
                )}
              </div>
            </div>

            <button
              onClick={createAlert}
              className="btn-create"
              disabled={saving || !newAlert?.name || !newAlert?.textPrompt}
            >
              {saving ? '⏳ Creating...' : '✨ Create Alert'}
            </button>
          </div>

          {alerts.length > 0 && (
            <div className="my-alerts-list">
              <h3>My Alerts ({alerts.length})</h3>
              <div className="alerts-grid">
                {alerts.map(alert => (
                  <div key={alert?.id} className="alert-item">
                    <div className="alert-item-header">
                      <strong>{alert?.name}</strong>
                      <span className="alert-type-badge">{alert?.type}</span>
                    </div>
                    <div className="alert-item-text">"{alert?.textPrompt}"</div>
                    <div className="alert-item-actions">
                      <button
                        onClick={() => deleteAlert(alert?.id, alert?.name)}
                        className="btn-delete-small"
                      >
                        🗑️ Delete
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

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

export default UserAlertManager

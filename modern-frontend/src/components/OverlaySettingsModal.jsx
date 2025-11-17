import { useState, useEffect } from 'react'
import './AdminDashboard.css'

function OverlaySettingsModal({
  isOpen,
  onClose,
  user,
  isAdminMode = false,
  onUpdate
}) {
  const [overlaySettings, setOverlaySettings] = useState(null)
  const [isLoading, setIsLoading] = useState(false)
  const [hasLoaded, setHasLoaded] = useState(false)

  // Load overlay settings when modal opens
  useEffect(() => {
    if (isOpen && user?.userId && !hasLoaded) {
      loadOverlaySettings()
    }
  }, [isOpen, user?.userId, hasLoaded])

  // Reset loaded state when modal closes
  useEffect(() => {
    if (!isOpen) {
      setHasLoaded(false)
    }
  }, [isOpen])

  const loadOverlaySettings = async () => {
    if (!user?.userId) return

    setIsLoading(true)
    try {
      const token = localStorage.getItem('authToken')

      // Use different API endpoints based on context
      const endpoint = isAdminMode
        ? `/api/admin/users/${user.userId}/overlay`
        : '/api/user/overlay-settings'

      const response = await fetch(endpoint, {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      if (response.ok) {
        const data = await response.json()
        console.log('📥 Loaded overlay settings:', data)

        // Set default settings if none exist
        const settings = data.overlaySettings || data || {
          enabled: false,
          position: 'top-right',
          size: 'medium',
          counters: {
            deaths: true,
            swears: true,
            screams: true,
            bits: false
          },
          bitsGoal: {
            target: 1000,
            current: 0
          },
          theme: {
            backgroundColor: 'rgba(0,0,0,0.8)',
            borderColor: '#9146ff',
            textColor: '#ffffff'
          },
          animations: {
            enabled: true,
            showAlerts: true,
            celebrationEffects: true,
            bounceOnUpdate: true,
            fadeTransitions: true
          }
        }

        setOverlaySettings(settings)
        setHasLoaded(true)
      } else {
        console.error('❌ Failed to load overlay settings:', response.status)
        // Set default settings on error
        setOverlaySettings({
          enabled: false,
          position: 'top-right',
          size: 'medium',
          counters: { deaths: true, swears: true, screams: true, bits: false },
          bitsGoal: { target: 1000, current: 0 },
          theme: { backgroundColor: 'rgba(0,0,0,0.8)', borderColor: '#9146ff', textColor: '#ffffff' },
          animations: { enabled: true, showAlerts: true, celebrationEffects: true, bounceOnUpdate: true, fadeTransitions: true }
        })
        setHasLoaded(true)
      }
    } catch (error) {
      console.error('❌ Error loading overlay settings:', error)
      setOverlaySettings({
        enabled: false,
        position: 'top-right',
        size: 'medium',
        counters: { deaths: true, swears: true, screams: true, bits: false },
        bitsGoal: { target: 1000, current: 0 },
        theme: { backgroundColor: 'rgba(0,0,0,0.8)', borderColor: '#9146ff', textColor: '#ffffff' },
        animations: { enabled: true, showAlerts: true, celebrationEffects: true, bounceOnUpdate: true, fadeTransitions: true }
      })
      setHasLoaded(true)
    } finally {
      setIsLoading(false)
    }
  }

  const handleSettingChange = async (path, value) => {
    if (!overlaySettings || isLoading) return

    // Update local state immediately
    const keys = path.split('.')
    const newSettings = { ...overlaySettings }
    let current = newSettings

    for (let i = 0; i < keys.length - 1; i++) {
      current[keys[i]] = { ...current[keys[i]] }
      current = current[keys[i]]
    }
    current[keys[keys.length - 1]] = value

    setOverlaySettings(newSettings)

    // Save to server
    try {
      const token = localStorage.getItem('authToken')

      // Use different API endpoints based on context
      const endpoint = isAdminMode
        ? `/api/admin/users/${user.userId}/overlay`
        : '/api/user/overlay-settings'

      const response = await fetch(endpoint, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({ overlaySettings: newSettings })
      })

      if (response.ok) {
        console.log('✅ Overlay settings saved')
        if (onUpdate) {
          onUpdate(newSettings)
        }
      } else {
        console.error('❌ Failed to save overlay settings:', response.status)
      }
    } catch (error) {
      console.error('❌ Error saving overlay settings:', error)
    }
  }

  if (!isOpen) return null

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '1000px', width: '90vw' }}>
        <div className="modal-header">
          <h2>⚙️ Stream Overlay Settings</h2>
          <button onClick={onClose} className="close-btn">
            ×
          </button>
        </div>

        <div className="modal-body">
          {isLoading ? (
            <div style={{ textAlign: 'center', padding: '40px', color: '#fff' }}>
              <div style={{ fontSize: '24px', marginBottom: '10px' }}>⏳</div>
              <p>Loading overlay settings...</p>
            </div>
          ) : overlaySettings ? (
            <>
              <div style={{ marginBottom: '25px', textAlign: 'center' }}>
                <h3 style={{ color: '#9146ff', marginBottom: '10px' }}>
                  🎨 Configure Your Stream Overlay
                </h3>
                <p style={{ color: '#aaa', margin: 0 }}>
                  Customize your overlay appearance and behavior
                </p>
              </div>

              {/* Enable/Disable Overlay */}
              <div style={{ marginBottom: '25px', padding: '15px', background: '#2a2a2a', borderRadius: '8px', border: '2px solid #9146ff' }}>
                <label style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', cursor: 'pointer' }}>
                  <div>
                    <h4 style={{ color: '#fff', margin: 0 }}>🎬 Enable Overlay</h4>
                    <p style={{ color: '#aaa', fontSize: '12px', margin: '5px 0 0 0' }}>Show overlay when stream is live</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={overlaySettings.enabled}
                    onChange={(e) => handleSettingChange('enabled', e.target.checked)}
                    style={{ width: '24px', height: '24px', cursor: 'pointer' }}
                    disabled={isLoading}
                  />
                </label>
              </div>

              {/* Position Selector */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>🎯 Overlay Position</h4>
                <select
                  value={overlaySettings.position}
                  onChange={(e) => handleSettingChange('position', e.target.value)}
                  disabled={isLoading}
                  style={{
                    width: '100%',
                    padding: '10px',
                    borderRadius: '6px',
                    background: '#2a2a2a',
                    color: '#fff',
                    border: '1px solid #444',
                    fontSize: '14px'
                  }}
                >
                  <option value="top-left">↖️ Top Left</option>
                  <option value="top-right">↗️ Top Right</option>
                  <option value="bottom-left">↙️ Bottom Left</option>
                  <option value="bottom-right">↘️ Bottom Right</option>
                </select>
              </div>

              {/* Size Selector */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>📏 Overlay Size</h4>
                <select
                  value={overlaySettings.size || 'medium'}
                  onChange={(e) => handleSettingChange('size', e.target.value)}
                  disabled={isLoading}
                  style={{
                    width: '100%',
                    padding: '10px',
                    borderRadius: '6px',
                    background: '#2a2a2a',
                    color: '#fff',
                    border: '1px solid #444',
                    fontSize: '14px'
                  }}
                >
                  <option value="small">📱 Small</option>
                  <option value="medium">📺 Medium</option>
                  <option value="large">🖥️ Large</option>
                </select>
              </div>

              {/* Counter Selection */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>🔢 Counters to Display</h4>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '10px' }}>
                  {[
                    { key: 'deaths', label: '💀 Deaths', enabled: true },
                    { key: 'swears', label: '🤬 Swears', enabled: true },
                    { key: 'screams', label: '😱 Screams', enabled: true },
                    { key: 'bits', label: '💎 Bits', enabled: false }
                  ].map(counter => (
                    <label key={counter.key} style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      cursor: 'pointer',
                      padding: '8px',
                      background: '#1a1a1a',
                      borderRadius: '6px',
                      color: '#fff'
                    }}>
                      <input
                        type="checkbox"
                        checked={overlaySettings.counters?.[counter.key] || false}
                        onChange={(e) => handleSettingChange(`counters.${counter.key}`, e.target.checked)}
                        disabled={isLoading}
                        style={{ width: '18px', height: '18px' }}
                      />
                      <span>{counter.label}</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Bits Goal Settings */}
              {overlaySettings.counters?.bits && (
                <div style={{ marginBottom: '25px' }}>
                  <h4 style={{ color: '#fff', marginBottom: '10px' }}>🎯 Bits Goal</h4>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '15px', alignItems: 'end' }}>
                    <div>
                      <label style={{ color: '#fff', display: 'block', marginBottom: '5px' }}>
                        Goal Amount:
                      </label>
                      <input
                        type="number"
                        min="0"
                        step="100"
                        value={overlaySettings.bitsGoal?.target || 1000}
                        onChange={(e) => handleSettingChange('bitsGoal.target', parseInt(e.target.value) || 0)}
                        disabled={isLoading}
                        placeholder="1000"
                        style={{
                          width: '100%',
                          padding: '10px',
                          borderRadius: '6px',
                          background: '#2a2a2a',
                          color: '#fff',
                          border: '1px solid #444',
                          fontSize: '14px'
                        }}
                      />
                    </div>
                    <div>
                      <label style={{ color: '#fff', display: 'block', marginBottom: '5px' }}>
                        Progress: {overlaySettings.bitsGoal?.current || 0} / {overlaySettings.bitsGoal?.target || 1000}
                      </label>
                      <button
                        onClick={() => handleSettingChange('bitsGoal.current', 0)}
                        disabled={isLoading}
                        style={{
                          width: '100%',
                          padding: '10px',
                          borderRadius: '6px',
                          background: '#dc3545',
                          color: '#fff',
                          border: 'none',
                          fontSize: '14px',
                          cursor: 'pointer'
                        }}
                      >
                        🔄 Reset Progress
                      </button>
                    </div>
                  </div>
                  <div style={{ marginTop: '10px', background: '#1a1a1a', padding: '10px', borderRadius: '6px' }}>
                    <div style={{ color: '#aaa', fontSize: '12px', marginBottom: '5px' }}>
                      Progress: {Math.round(((overlaySettings.bitsGoal?.current || 0) / (overlaySettings.bitsGoal?.target || 1000)) * 100)}%
                    </div>
                    <div style={{
                      width: '100%',
                      height: '8px',
                      background: '#333',
                      borderRadius: '4px',
                      overflow: 'hidden'
                    }}>
                      <div style={{
                        width: `${Math.min(100, ((overlaySettings.bitsGoal?.current || 0) / (overlaySettings.bitsGoal?.target || 1000)) * 100)}%`,
                        height: '100%',
                        background: 'linear-gradient(90deg, #9146ff, #772ce8)',
                        transition: 'width 0.3s ease'
                      }} />
                    </div>
                  </div>
                </div>
              )}

              {/* Animations */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>✨ Animations & Effects</h4>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '10px' }}>
                  {[
                    { key: 'enabled', label: '🎬 Basic Animations' },
                    { key: 'showAlerts', label: '🚨 Counter Alerts' },
                    { key: 'celebrationEffects', label: '🎉 Celebrations' },
                    { key: 'bounceOnUpdate', label: '🎈 Bounce Effect' },
                    { key: 'fadeTransitions', label: '🌊 Fade Transitions' }
                  ].map(animation => (
                    <label key={animation.key} style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      cursor: 'pointer',
                      padding: '8px',
                      background: '#1a1a1a',
                      borderRadius: '6px',
                      color: '#fff'
                    }}>
                      <input
                        type="checkbox"
                        checked={overlaySettings.animations?.[animation.key] || false}
                        onChange={(e) => handleSettingChange(`animations.${animation.key}`, e.target.checked)}
                        disabled={isLoading}
                        style={{ width: '18px', height: '18px' }}
                      />
                      <span>{animation.label}</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Theme Colors */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>🎨 Theme Colors</h4>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '15px' }}>
                  <label style={{ color: '#fff' }}>
                    <span style={{ display: 'block', marginBottom: '5px' }}>Border Color:</span>
                    <input
                      type="color"
                      value={overlaySettings.theme?.borderColor || '#9146ff'}
                      onChange={(e) => handleSettingChange('theme.borderColor', e.target.value)}
                      disabled={isLoading}
                      style={{ width: '100%', height: '40px', cursor: 'pointer' }}
                    />
                  </label>
                  <label style={{ color: '#fff' }}>
                    <span style={{ display: 'block', marginBottom: '5px' }}>Text Color:</span>
                    <input
                      type="color"
                      value={overlaySettings.theme?.textColor || '#ffffff'}
                      onChange={(e) => handleSettingChange('theme.textColor', e.target.value)}
                      disabled={isLoading}
                      style={{ width: '100%', height: '40px', cursor: 'pointer' }}
                    />
                  </label>
                </div>
              </div>
            </>
          ) : (
            <div style={{ textAlign: 'center', padding: '40px', color: '#fff' }}>
              <div style={{ fontSize: '24px', marginBottom: '10px' }}>❌</div>
              <p>Failed to load overlay settings. Please try again.</p>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

export default OverlaySettingsModal

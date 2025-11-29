/**
 * Debug Dashboard Component
 * Real-time diagnostics UI for troubleshooting Discord notifications and other workflows
 */

import { useState, useEffect } from 'react'
import './DebugDashboard.css'
import { ActionButton, StatusBadge } from './ui/CommonControls'
import { userAPI, adminAPI } from '../utils/authUtils'
import { useToast, useLoading } from '../hooks'

function DebugDashboard({ user }) {
  const [debugData, setDebugData] = useState(null)
  const [systemHealth, setSystemHealth] = useState(null)
  const [testResults, setTestResults] = useState({})
  const [autoRefresh, setAutoRefresh] = useState(false)
  const [refreshInterval, setRefreshInterval] = useState(null)

  // Restore Series State
  const [showRestoreForm, setShowRestoreForm] = useState(false)
  const [restoreData, setRestoreData] = useState({
    twitchUserId: '',
    seriesName: '',
    description: 'Restored via Admin Debug',
    counters: {
      deaths: 0,
      swears: 0,
      screams: 0,
      bits: 0
    }
  })

  const { showMessage } = useToast()
  const { isLoading, withLoading } = useLoading()

  useEffect(() => {
    if (user) {
      loadDebugData()
      loadSystemHealth()
    }
  }, [user])

  useEffect(() => {
    if (autoRefresh) {
      const interval = setInterval(() => {
        loadDebugData()
        loadSystemHealth()
      }, 10000) // Refresh every 10 seconds
      setRefreshInterval(interval)
    } else {
      if (refreshInterval) {
        clearInterval(refreshInterval)
        setRefreshInterval(null)
      }
    }

    return () => {
      if (refreshInterval) clearInterval(refreshInterval)
    }
  }, [autoRefresh])

  const loadDebugData = async () => {
    if (!user?.twitchUserId) {
      console.warn('Cannot load debug data: user or twitchUserId not available')
      return
    }

    try {
      const response = await debugAPI.getDiscordDiagnostics(user.twitchUserId)
      setDebugData(response)
    } catch (error) {
      console.error('Failed to load debug data:', error)
      if (!debugData) { // Only show error if we don't have any data
        showMessage(`Failed to load debug data: ${error.message}`, 'error')
      }
    }
  }

  const loadSystemHealth = async () => {
    try {
      const response = await debugAPI.getSystemHealth()
      setSystemHealth(response)
    } catch (error) {
      console.error('Failed to load system health:', error)
    }
  }

  const runDiscordTest = async () => {
    if (!user?.twitchUserId) {
      showMessage('Cannot test: user data not available', 'error')
      return
    }

    try {
      await withLoading(async () => {
        const result = await debugAPI.testDiscordWebhook(user.twitchUserId)
        setTestResults(prev => ({
          ...prev,
          discordTest: {
            ...result,
            timestamp: new Date().toISOString()
          }
        }))

        if (result.success) {
          showMessage('Discord test successful! Check your Discord channel.', 'success')
        } else {
          showMessage(`Discord test failed: ${result.error}`, 'error')
        }

        // Refresh diagnostics after test
        setTimeout(() => loadDebugData(), 1000)
      })
    } catch (error) {
      setTestResults(prev => ({
        ...prev,
        discordTest: {
          success: false,
          error: error.message,
          timestamp: new Date().toISOString()
        }
      }))
      showMessage(`Discord test failed: ${error.message}`, 'error')
    }
  }

  const testEventSubAPI = async () => {
    try {
      await withLoading(async () => {
        const result = await debugAPI.testEventSubAPI()
        setTestResults(prev => ({
          ...prev,
          eventSubAPI: {
            success: result.success,
            message: result.message,
            details: result.results,
            timestamp: new Date().toISOString()
          }
        }))
        showMessage(`EventSub API test ${result.success ? 'passed' : 'failed'}`, result.success ? 'success' : 'error')
      })
    } catch (error) {
      setTestResults(prev => ({
        ...prev,
        eventSubAPI: {
          success: false,
          error: error.message,
          timestamp: new Date().toISOString()
        }
      }))
      showMessage(`EventSub API test failed: ${error.message}`, 'error')
    }
  }

  const startMonitoring = async () => {
    try {
      await withLoading(async () => {
        const result = await debugAPI.startMonitoring()
        showMessage('Stream monitoring started successfully', 'success')
        setTimeout(() => loadDebugData(), 1000)
      })
    } catch (error) {
      showMessage(`Failed to start monitoring: ${error.message}`, 'error')
    }
  }

  const testStreamStatus = async (status) => {
    if (!user?.twitchUserId) return

    try {
      await withLoading(async () => {
        const result = await debugAPI.testStreamStatus(user.twitchUserId, status)
        setTestResults(prev => ({
          ...prev,
          [`streamStatus_${status}`]: {
            success: result.success,
            message: result.message,
            timestamp: new Date().toISOString()
          }
        }))
        showMessage(`Stream ${status} test completed`, 'success')
      })
    } catch (error) {
      showMessage(`Stream status test failed: ${error.message}`, 'error')
    }
  }

  const testNotification = async (eventType) => {
    if (!user?.twitchUserId) return

    try {
      await withLoading(async () => {
        const result = await debugAPI.testNotification(user.twitchUserId, eventType)
        setTestResults(prev => ({
          ...prev,
          [`notification_${eventType}`]: {
            success: result.success,
            message: result.message,
            timestamp: new Date().toISOString()
          }
        }))
        showMessage(`${eventType} notification test completed`, 'success')
      })
    } catch (error) {
      showMessage(`Notification test failed: ${error.message}`, 'error')
    }
  }

  const testAllNotifications = async () => {
    if (!user?.twitchUserId) return

    try {
      await withLoading(async () => {
        const result = await debugAPI.testAllNotifications(user.twitchUserId)
        setTestResults(prev => ({
          ...prev,
          allNotifications: {
            success: result.success,
            message: result.message,
            details: result.results,
            timestamp: new Date().toISOString()
          }
        }))
        showMessage('All notifications test completed', result.success ? 'success' : 'warning')
      })
    } catch (error) {
      showMessage(`All notifications test failed: ${error.message}`, 'error')
    }
  }

  const cleanWebhookData = async () => {
    try {
      await withLoading(async () => {
        const result = await debugAPI.cleanDiscordWebhook()
        showMessage('Webhook data cleaned successfully', 'success')
        setTimeout(() => loadDebugData(), 1000)
      })
    } catch (error) {
      showMessage(`Failed to clean webhook data: ${error.message}`, 'error')
    }
  }

  const handleRestoreSeries = async (e) => {
    e.preventDefault()
    try {
      await withLoading(async () => {
        await adminAPI.restoreSeriesSave(restoreData)
        showMessage('Series save restored successfully', 'success')
        setShowRestoreForm(false)
        setRestoreData({
          twitchUserId: '',
          seriesName: '',
          description: 'Restored via Admin Debug',
          counters: { deaths: 0, swears: 0, screams: 0, bits: 0 }
        })
      })
    } catch (error) {
      showMessage(`Failed to restore series: ${error.message}`, 'error')
    }
  }

  const getStatusIcon = (status) => {
    switch (status) {
      case 'success': return '✅'
      case 'warning': return '⚠️'
      case 'error': return '❌'
      default: return '❓'
    }
  }

  const getStatusColor = (status) => {
    switch (status) {
      case 'success': return '#28a745'
      case 'warning': return '#ffc107'
      case 'error': return '#dc3545'
      default: return '#6c757d'
    }
  }

  if (!user || !user.twitchUserId) {
    return (
      <div className="debug-dashboard">
        <div className="debug-header">
          <h3>🔧 Debug Dashboard</h3>
          <p>
            {!user ? 'Please log in to access debug information.' :
             'Loading user data...'}
          </p>
          {user && (
            <div style={{ marginTop: '12px', fontSize: '13px', color: '#888' }}>
              Debug: User data partially loaded. TwitchUserId: {user.twitchUserId || 'missing'}
            </div>
          )}
        </div>
      </div>
    )
  }

  return (
    <div className="debug-dashboard">
      <div className="debug-header">
        <div>
          <h3>🔧 Debug Dashboard</h3>
          <p>Real-time diagnostics for {user.displayName}</p>
        </div>

        <div className="debug-controls">
          <label className="auto-refresh-control">
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
            />
            Auto Refresh (10s)
          </label>

          <ActionButton
            onClick={() => {
              loadDebugData()
              loadSystemHealth()
            }}
            disabled={isLoading}
            size="small"
          >
            🔄 Refresh
          </ActionButton>
        </div>
      </div>

      {/* System Health Overview */}
      {systemHealth && (
        <div className="debug-section">
          <h4>🏥 System Health</h4>
          <div className="health-grid">
            {Object.entries(systemHealth.checks).map(([key, check]) => (
              <div key={key} className="health-card">
                <div className="health-header">
                  <span className="health-icon">{getStatusIcon(check.status)}</span>
                  <span className="health-name">{key}</span>
                </div>
                <div className="health-message">{check.message}</div>
                {check.data && (
                  <div className="health-data">
                    {typeof check.data === 'object' ?
                      Object.entries(check.data).map(([k, v]) => (
                        <div key={k} className="health-stat">
                          <span className="stat-key">{k}:</span>
                          <span className="stat-value">{String(v)}</span>
                        </div>
                      )) :
                      <span>{String(check.data)}</span>
                    }
                  </div>
                )}
              </div>
            ))}
          </div>

          <div className="system-stats">
            <div className="stat-item">
              <strong>Uptime:</strong> {Math.floor(systemHealth.uptime / 3600)}h {Math.floor((systemHealth.uptime % 3600) / 60)}m
            </div>
            <div className="stat-item">
              <strong>Memory:</strong> {Math.round(systemHealth.memory.used / 1024 / 1024)}MB
            </div>
            <div className="stat-item">
              <strong>Node:</strong> {systemHealth.nodeVersion}
            </div>
            <div className="stat-item">
              <strong>Environment:</strong> {systemHealth.environment}
            </div>
          </div>
        </div>
      )}

      {/* Discord Diagnostics */}
      {debugData && (
        <div className="debug-section">
          <div className="section-header">
            <h4>🔔 Discord Notifications Diagnostics</h4>
            <StatusBadge
              status={debugData.summary?.overallStatus === 'healthy' ? 'success' :
                     debugData.summary?.overallStatus === 'warning' ? 'warning' : 'error'}
            >
              {debugData.summary?.successRate || 0}% Health
            </StatusBadge>
          </div>

          <div className="checks-grid">
            {Object.entries(debugData.checks).map(([key, check]) => (
              <div key={key} className="check-card">
                <div className="check-header">
                  <span
                    className="check-icon"
                    style={{ color: getStatusColor(check.status) }}
                  >
                    {getStatusIcon(check.status)}
                  </span>
                  <h5>{key.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase())}</h5>
                </div>

                <div className="check-message">{check.message}</div>

                {check.data && (
                  <details className="check-details">
                    <summary>View Details</summary>
                    <pre>{JSON.stringify(check.data, null, 2)}</pre>
                  </details>
                )}
              </div>
            ))}
          </div>

          {/* Recommendations */}
          {debugData.summary?.recommendations && (
            <div className="recommendations">
              <h5>💡 Recommendations</h5>
              <ul>
                {debugData.summary.recommendations.map((rec, index) => (
                  <li key={index}>{rec}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {/* Test Actions */}
      <div className="debug-section">
        <h4>🧪 Test Actions</h4>

        {/* Basic Tests */}
        <div className="test-group">
          <h5>Basic Tests</h5>
          <div className="test-actions">
            <ActionButton
              onClick={runDiscordTest}
              disabled={isLoading}
              className="test-button"
            >
              🔔 Test Discord Webhook
            </ActionButton>

            <ActionButton
              onClick={testEventSubAPI}
              disabled={isLoading}
              className="test-button"
            >
              🎯 Test EventSub API
            </ActionButton>

            <ActionButton
              onClick={loadDebugData}
              disabled={isLoading}
              className="test-button"
            >
              🔍 Refresh Diagnostics
            </ActionButton>
          </div>
        </div>

        {/* Stream Monitoring Tests */}
        <div className="test-group">
          <h5>Stream Monitoring</h5>
          <div className="test-actions">
            <ActionButton
              onClick={startMonitoring}
              disabled={isLoading}
              className="test-button"
            >
              🎬 Start Monitoring
            </ActionButton>

            <ActionButton
              onClick={() => testStreamStatus('online')}
              disabled={isLoading}
              className="test-button"
            >
              📺 Test Stream Online
            </ActionButton>

            <ActionButton
              onClick={() => testStreamStatus('offline')}
              disabled={isLoading}
              className="test-button"
            >
              📴 Test Stream Offline
            </ActionButton>
          </div>
        </div>

        {/* Notification Tests */}
        <div className="test-group">
          <h5>Notification Tests</h5>
          <div className="test-actions">
            <ActionButton
              onClick={() => testNotification('follow')}
              disabled={isLoading}
              className="test-button"
            >
              � Test Follow Alert
            </ActionButton>

            <ActionButton
              onClick={() => testNotification('subscription')}
              disabled={isLoading}
              className="test-button"
            >
              ⭐ Test Sub Alert
            </ActionButton>

            <ActionButton
              onClick={() => testNotification('cheer')}
              disabled={isLoading}
              className="test-button"
            >
              💎 Test Cheer Alert
            </ActionButton>

            <ActionButton
              onClick={testAllNotifications}
              disabled={isLoading}
              className="test-button warning"
            >
              🚀 Test All Notifications
            </ActionButton>
          </div>
        </div>

        {/* Cleanup Actions */}
        <div className="test-group">
          <h5>Cleanup Actions</h5>
          <div className="test-actions">
            <ActionButton
              onClick={cleanWebhookData}
              disabled={isLoading}
              className="test-button warning"
            >
              🧹 Clean Webhook Data
            </ActionButton>
          </div>
        </div>

        {/* Restore Tools */}
        <div className="test-group">
          <h5>Restore Tools</h5>
          <div className="test-actions">
            <ActionButton
              onClick={() => setShowRestoreForm(!showRestoreForm)}
              disabled={isLoading}
              className="test-button"
            >
              💾 Restore Series Save
            </ActionButton>
          </div>

          {showRestoreForm && (
            <div className="restore-form-container" style={{ marginTop: '15px', padding: '15px', background: 'rgba(0,0,0,0.2)', borderRadius: '8px' }}>
              <form onSubmit={handleRestoreSeries}>
                <div style={{ display: 'grid', gap: '10px', marginBottom: '15px' }}>
                  <div>
                    <label style={{ display: 'block', marginBottom: '5px' }}>Twitch User ID:</label>
                    <input
                      type="text"
                      value={restoreData.twitchUserId}
                      onChange={e => setRestoreData({...restoreData, twitchUserId: e.target.value})}
                      required
                      style={{ width: '100%', padding: '8px', borderRadius: '4px', border: '1px solid #444', background: '#222', color: '#fff' }}
                      placeholder="e.g. 12345678"
                    />
                  </div>
                  <div>
                    <label style={{ display: 'block', marginBottom: '5px' }}>Series Name:</label>
                    <input
                      type="text"
                      value={restoreData.seriesName}
                      onChange={e => setRestoreData({...restoreData, seriesName: e.target.value})}
                      required
                      style={{ width: '100%', padding: '8px', borderRadius: '4px', border: '1px solid #444', background: '#222', color: '#fff' }}
                      placeholder="e.g. Elden Ring Ep 1"
                    />
                  </div>
                  <div>
                    <label style={{ display: 'block', marginBottom: '5px' }}>Description:</label>
                    <input
                      type="text"
                      value={restoreData.description}
                      onChange={e => setRestoreData({...restoreData, description: e.target.value})}
                      style={{ width: '100%', padding: '8px', borderRadius: '4px', border: '1px solid #444', background: '#222', color: '#fff' }}
                    />
                  </div>

                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px' }}>
                    <div>
                      <label style={{ display: 'block', marginBottom: '5px' }}>Deaths:</label>
                      <input
                        type="number"
                        value={restoreData.counters.deaths}
                        onChange={e => setRestoreData({...restoreData, counters: {...restoreData.counters, deaths: parseInt(e.target.value) || 0}})}
                        style={{ width: '100%', padding: '8px', borderRadius: '4px', border: '1px solid #444', background: '#222', color: '#fff' }}
                      />
                    </div>
                    <div>
                      <label style={{ display: 'block', marginBottom: '5px' }}>Swears:</label>
                      <input
                        type="number"
                        value={restoreData.counters.swears}
                        onChange={e => setRestoreData({...restoreData, counters: {...restoreData.counters, swears: parseInt(e.target.value) || 0}})}
                        style={{ width: '100%', padding: '8px', borderRadius: '4px', border: '1px solid #444', background: '#222', color: '#fff' }}
                      />
                    </div>
                    <div>
                      <label style={{ display: 'block', marginBottom: '5px' }}>Screams:</label>
                      <input
                        type="number"
                        value={restoreData.counters.screams}
                        onChange={e => setRestoreData({...restoreData, counters: {...restoreData.counters, screams: parseInt(e.target.value) || 0}})}
                        style={{ width: '100%', padding: '8px', borderRadius: '4px', border: '1px solid #444', background: '#222', color: '#fff' }}
                      />
                    </div>
                    <div>
                      <label style={{ display: 'block', marginBottom: '5px' }}>Bits:</label>
                      <input
                        type="number"
                        value={restoreData.counters.bits}
                        onChange={e => setRestoreData({...restoreData, counters: {...restoreData.counters, bits: parseInt(e.target.value) || 0}})}
                        style={{ width: '100%', padding: '8px', borderRadius: '4px', border: '1px solid #444', background: '#222', color: '#fff' }}
                      />
                    </div>
                  </div>
                </div>

                <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
                  <ActionButton
                    type="button"
                    onClick={() => setShowRestoreForm(false)}
                    className="secondary"
                    size="small"
                  >
                    Cancel
                  </ActionButton>
                  <ActionButton
                    type="submit"
                    disabled={isLoading}
                    className="primary"
                    size="small"
                  >
                    Restore Save
                  </ActionButton>
                </div>
              </form>
            </div>
          )}
        </div>
      </div>

      {/* Test Results */}
      {Object.keys(testResults).length > 0 && (
        <div className="debug-section">
          <div className="test-results">
            <h5>📋 Recent Test Results</h5>
            {Object.entries(testResults).map(([test, result]) => (
              <div key={test} className="test-result">
                <div className="result-header">
                  <span className={`result-status ${result.success ? 'success' : 'error'}`}>
                    {result.success ? '✅' : '❌'}
                  </span>
                  <span className="result-name">{test}</span>
                  <span className="result-time">
                    {new Date(result.timestamp).toLocaleTimeString()}
                  </span>
                </div>

                {result.message && (
                  <div className="result-message">{result.message}</div>
                )}

                {result.details && (
                  <details className="result-details">
                    <summary>Details</summary>
                    <pre>{JSON.stringify(result.details, null, 2)}</pre>
                  </details>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Raw Data (for development) */}
      {process.env.NODE_ENV === 'development' && debugData && (
        <div className="debug-section">
          <details className="raw-data">
            <summary>🔧 Raw Debug Data (Development Only)</summary>
            <pre>{JSON.stringify(debugData, null, 2)}</pre>
          </details>
        </div>
      )}
    </div>
  )
}

export default DebugDashboard

/**
 * Debug Dashboard Component
 * Real-time diagnostics UI for troubleshooting Discord notifications and other workflows
 */

import { useState, useEffect } from 'react'
import './DebugDashboard.css'
import { ActionButton, StatusBadge } from './ui/CommonControls'
import { userAPI, debugAPI } from '../utils/apiHelpers'
import { useToast, useLoading } from '../hooks'

function DebugDashboard({ user }) {
  const [debugData, setDebugData] = useState(null)
  const [systemHealth, setSystemHealth] = useState(null)
  const [testResults, setTestResults] = useState({})
  const [loggingInfo, setLoggingInfo] = useState(null)
  const [autoRefresh, setAutoRefresh] = useState(false)
  const [refreshInterval, setRefreshInterval] = useState(null)

  const { showMessage } = useToast()
  const { isLoading, withLoading } = useLoading()

  useEffect(() => {
    if (user) {
      loadDebugData()
      loadSystemHealth()
      loadLoggingInfo()
    }
  }, [user])

  useEffect(() => {
    if (autoRefresh) {
      const interval = setInterval(() => {
        loadDebugData()
        loadSystemHealth()
        loadLoggingInfo()
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

  const loadLoggingInfo = async () => {
    try {
      const [info, insights] = await Promise.all([
        debugAPI.getLoggingInfo(),
        debugAPI.getLoggingInsights()
      ])
      setLoggingInfo({ info, insights })
    } catch (error) {
      console.error('Failed to load logging info:', error)
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

      {/* Logging Information */}
      {loggingInfo && (
        <div className="debug-section">
          <div className="section-header">
            <h4>📊 Application Logging</h4>
            <StatusBadge status="success">Azure Log Analytics</StatusBadge>
          </div>

          <div className="logging-info">
            <div className="info-card">
              <h5>📍 Log Location</h5>
              <p>{loggingInfo.info.message}</p>
              <div className="log-details">
                <strong>Service:</strong> {loggingInfo.insights.service}<br/>
                <strong>Format:</strong> {loggingInfo.insights.logFormat}
              </div>
            </div>

            <div className="info-card">
              <h5>🔍 KQL Query Example</h5>
              <pre className="kusto-query">
                {loggingInfo.info.instructions.exampleQuery}
              </pre>
              <ActionButton
                onClick={() => window.open(loggingInfo.info.azurePortalUrl, '_blank')}
                size="small"
              >
                🚀 Open Azure Portal
              </ActionButton>
            </div>

            <div className="info-card">
              <h5>📝 Log Categories</h5>
              <ul className="category-list">
                {loggingInfo.insights.categories.map((category, index) => (
                  <li key={index}>{category}</li>
                ))}
              </ul>
            </div>

            <div className="info-card">
              <h5>💡 Query Tips</h5>
              <ul className="tips-list">
                {loggingInfo.insights.queryTips.map((tip, index) => (
                  <li key={index}><code>{tip}</code></li>
                ))}
              </ul>
            </div>
          </div>
        </div>
      )}

      {/* Test Actions */}
      <div className="debug-section">
        <h4>🧪 Test Actions</h4>
        <div className="test-actions">
          <ActionButton
            onClick={runDiscordTest}
            disabled={isLoading}
            className="test-button"
          >
            🔔 Test Discord Webhook
          </ActionButton>

          <ActionButton
            onClick={cleanWebhookData}
            disabled={isLoading}
            className="test-button warning"
          >
            🧹 Clean Webhook Data
          </ActionButton>

          <ActionButton
            onClick={loadDebugData}
            disabled={isLoading}
            className="test-button"
          >
            🔍 Refresh Diagnostics
          </ActionButton>
        </div>

        {/* Test Results */}
        {Object.keys(testResults).length > 0 && (
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
        )}
      </div>

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

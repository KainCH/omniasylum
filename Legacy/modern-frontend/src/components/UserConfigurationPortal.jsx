import React, { useState, useEffect } from 'react'
import { ActionButton, FormSection, StatusBadge, InputGroup } from './ui/CommonControls'
import { useLoading, useToast } from '../hooks'
import './UserConfigurationPortal.css'

function UserConfigurationPortal({ user, onClose }) {
  const [userData, setUserData] = useState(null)
  const [userCounters, setUserCounters] = useState(null)
  const [userAlerts, setUserAlerts] = useState([])
  const [userDiscordSettings, setUserDiscordSettings] = useState(null)
  const [userOverlaySettings, setUserOverlaySettings] = useState(null)
  const [activeTab, setActiveTab] = useState('overview')

  const { loading, withLoading } = useLoading()
  const { addToast } = useToast()

  useEffect(() => {
    if (user?.userId) {
      loadUserConfiguration()
    }
  }, [user])

  const loadUserConfiguration = async () => {
    try {
      await withLoading(async () => {
        const token = localStorage.getItem('authToken')

        // Load user basic data + counters
        const userResponse = await fetch(`/api/admin/users/${user.userId}`, {
          headers: { 'Authorization': `Bearer ${token}` }
        })

        if (userResponse.ok) {
          const userData = await userResponse.json()
          setUserData(userData.user)
          setUserCounters(userData.counters)
        }

        // Load user alerts
        try {
          const alertsResponse = await fetch(`/api/alerts/user/${user.userId}`, {
            headers: { 'Authorization': `Bearer ${token}` }
          })
          if (alertsResponse.ok) {
            const alertsData = await alertsResponse.json()
            setUserAlerts(alertsData.alerts || [])
          }
        } catch (error) {
          console.log('Could not load alerts:', error)
        }

        // Load Discord settings
        try {
          const discordResponse = await fetch(`/api/admin/users/${user.userId}/discord-settings`, {
            headers: { 'Authorization': `Bearer ${token}` }
          })
          if (discordResponse.ok) {
            const discordData = await discordResponse.json()
            setUserDiscordSettings(discordData.settings)
          }
        } catch (error) {
          console.log('Could not load Discord settings:', error)
        }

        // Load overlay settings
        try {
          const overlayResponse = await fetch(`/api/admin/users/${user.userId}/overlay-settings`, {
            headers: { 'Authorization': `Bearer ${token}` }
          })
          if (overlayResponse.ok) {
            const overlayData = await overlayResponse.json()
            setUserOverlaySettings(overlayData.settings)
          }
        } catch (error) {
          console.log('Could not load overlay settings:', error)
        }
      })
    } catch (error) {
      console.error('Error loading user configuration:', error)
      addToast('Failed to load user configuration', 'error')
    }
  }

  const renderOverviewTab = () => (
    <div className="config-overview">
      <FormSection title="📊 Counter Statistics" collapsible={true}>
        {userCounters ? (
          <div className="counters-grid">
            <div className="counter-stat">
              <h3>💀 Deaths</h3>
              <div className="counter-value">{userCounters.deaths || 0}</div>
            </div>
            <div className="counter-stat">
              <h3>🤬 Swears</h3>
              <div className="counter-value">{userCounters.swears || 0}</div>
            </div>
            <div className="counter-stat">
              <h3>😱 Screams</h3>
              <div className="counter-value">{userCounters.screams || 0}</div>
            </div>
            <div className="counter-stat">
              <h3>💎 Bits</h3>
              <div className="counter-value">{userCounters.bits || 0}</div>
            </div>
          </div>
        ) : (
          <p>Loading counter data...</p>
        )}
      </FormSection>

      <FormSection title="🎨 Alerts & Notifications" collapsible={true}>
        <div className="alerts-summary">
          <p><strong>Total Alerts:</strong> {userAlerts.length}</p>
          {userAlerts.length > 0 && (
            <div className="alert-types">
              {userAlerts.map(alert => (
                <div key={alert.id} className="alert-item">
                  <span className="alert-name">{alert.name}</span>
                  <StatusBadge variant="info">{alert.type}</StatusBadge>
                </div>
              ))}
            </div>
          )}
        </div>
      </FormSection>

      <FormSection title="🎮 Stream Integration" collapsible={true}>
        <div className="integration-status">
          <div className="status-item">
            <span>Discord Notifications:</span>
            <StatusBadge variant={userDiscordSettings ? 'success' : 'warning'}>
              {userDiscordSettings ? 'Configured' : 'Not Set'}
            </StatusBadge>
          </div>
          <div className="status-item">
            <span>Overlay Settings:</span>
            <StatusBadge variant={userOverlaySettings ? 'success' : 'warning'}>
              {userOverlaySettings ? 'Configured' : 'Default'}
            </StatusBadge>
          </div>
        </div>
      </FormSection>
    </div>
  )

  const renderCountersTab = () => (
    <FormSection title="📊 Counter Management">
      {userCounters ? (
        <div className="counter-management">
          <div className="counter-edit-grid">
            <InputGroup label="💀 Deaths">
              <input
                type="number"
                value={userCounters.deaths || 0}
                readOnly
                className="counter-input"
              />
            </InputGroup>
            <InputGroup label="🤬 Swears">
              <input
                type="number"
                value={userCounters.swears || 0}
                readOnly
                className="counter-input"
              />
            </InputGroup>
            <InputGroup label="😱 Screams">
              <input
                type="number"
                value={userCounters.screams || 0}
                readOnly
                className="counter-input"
              />
            </InputGroup>
            <InputGroup label="💎 Bits">
              <input
                type="number"
                value={userCounters.bits || 0}
                readOnly
                className="counter-input"
              />
            </InputGroup>
          </div>
          <p className="note">💡 Counter editing will be available in a future update</p>
        </div>
      ) : (
        <p>Loading counter data...</p>
      )}
    </FormSection>
  )

  const renderAlertsTab = () => (
    <FormSection title="🎨 Alert Configuration">
      <div className="alerts-config">
        <p><strong>Total Alerts:</strong> {userAlerts.length}</p>
        {userAlerts.length > 0 ? (
          <div className="alerts-list">
            {userAlerts.map(alert => (
              <div key={alert.id} className="alert-config-item">
                <div className="alert-header">
                  <h4>{alert.name}</h4>
                  <StatusBadge variant="info">{alert.type}</StatusBadge>
                </div>
                <div className="alert-details">
                  <p><strong>Text:</strong> {alert.textPrompt || 'None'}</p>
                  <p><strong>Duration:</strong> {alert.duration || 4000}ms</p>
                  <p><strong>Sound:</strong> {alert.sound || 'None'}</p>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <p>No alerts configured</p>
        )}
      </div>
    </FormSection>
  )

  const renderDiscordTab = () => (
    <FormSection title="💬 Discord Integration">
      {userDiscordSettings ? (
        <div className="discord-config">
          <p><strong>Webhook URL:</strong> {userDiscordSettings.webhookUrl ? '✅ Configured' : '❌ Not Set'}</p>
          <div className="notification-settings">
            <h4>Notification Types:</h4>
            {Object.entries(userDiscordSettings.notifications || {}).map(([key, enabled]) => (
              <div key={key} className="notification-item">
                <span>{key.replace(/_/g, ' ').toUpperCase()}:</span>
                <StatusBadge variant={enabled ? 'success' : 'warning'}>
                  {enabled ? 'Enabled' : 'Disabled'}
                </StatusBadge>
              </div>
            ))}
          </div>
        </div>
      ) : (
        <p>Discord integration not configured</p>
      )}
    </FormSection>
  )

  if (!user) {
    return null
  }

  return (
    <div className="user-config-portal">
      <div className="portal-header">
        <div className="user-info">
          <h2>🔧 {user.displayName || user.username}'s Configuration</h2>
          <p>@{user.username}</p>
          <StatusBadge variant={user.isActive ? 'success' : 'warning'}>
            {user.isActive ? 'Active' : 'Inactive'}
          </StatusBadge>
        </div>
        <ActionButton
          variant="secondary"
          onClick={onClose}
          size="small"
        >
          ✕ Close
        </ActionButton>
      </div>

      <div className="portal-tabs">
        <button
          className={`tab-button ${activeTab === 'overview' ? 'active' : ''}`}
          onClick={() => setActiveTab('overview')}
        >
          📋 Overview
        </button>
        <button
          className={`tab-button ${activeTab === 'counters' ? 'active' : ''}`}
          onClick={() => setActiveTab('counters')}
        >
          📊 Counters
        </button>
        <button
          className={`tab-button ${activeTab === 'alerts' ? 'active' : ''}`}
          onClick={() => setActiveTab('alerts')}
        >
          🎨 Alerts
        </button>
        <button
          className={`tab-button ${activeTab === 'discord' ? 'active' : ''}`}
          onClick={() => setActiveTab('discord')}
        >
          💬 Discord
        </button>
      </div>

      <div className="portal-content">
        {loading ? (
          <div className="loading-state">
            <p>Loading user configuration...</p>
          </div>
        ) : (
          <>
            {activeTab === 'overview' && renderOverviewTab()}
            {activeTab === 'counters' && renderCountersTab()}
            {activeTab === 'alerts' && renderAlertsTab()}
            {activeTab === 'discord' && renderDiscordTab()}
          </>
        )}
      </div>
    </div>
  )
}

export default UserConfigurationPortal

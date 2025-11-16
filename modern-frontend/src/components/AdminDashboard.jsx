import { useState, useEffect } from 'react'
import { io } from 'socket.io-client'
import AlertEventManager from './AlertEventManager'
import AlertEffectsModal from './AlertEffectsModal'
import DiscordWebhookSettings from './DiscordWebhookSettings'
import SeriesSaveManager from './SeriesSaveManager'
import UserManagementModal from './UserManagementModal'
import UserConfigurationPortal from './UserConfigurationPortal'
import PermissionManager from './PermissionManager'
import OverlaySettingsModal from './OverlaySettingsModal'
import { ActionButton, FormSection, StatusBadge, ToggleSwitch, InputGroup } from './ui/CommonControls'
import { useUserData, useLoading, useToast } from '../hooks'
import { userAPI, counterAPI, streamAPI, APIError } from '../utils/apiHelpers'
import './AdminDashboard.css'

function AdminDashboard({ onNavigateToDebug }) {
  const [users, setUsers] = useState([])
  const [stats, setStats] = useState({})
  const [features, setFeatures] = useState([])
  const [roles, setRoles] = useState([])
  const [permissions, setPermissions] = useState([])
  const [streams, setStreams] = useState([])
  const [refreshing, setRefreshing] = useState(false)
  const [selectedUser, setSelectedUser] = useState(null)
  const [showUserModal, setShowUserModal] = useState(false)
  const [showUserConfigPortal, setShowUserConfigPortal] = useState(false)
  const [showPermissionManager, setShowPermissionManager] = useState(false)
  const [showOverlayModal, setShowOverlayModal] = useState(false)
  const [selectedOverlayUser, setSelectedOverlayUser] = useState(null)
  const [showAlertModal, setShowAlertModal] = useState(false)
  const [selectedAlertUser, setSelectedAlertUser] = useState(null)
  const [showDiscordModal, setShowDiscordModal] = useState(false)
  const [selectedDiscordUser, setSelectedDiscordUser] = useState(null)
  const [showSeriesModal, setShowSeriesModal] = useState(false)
  const [selectedSeriesUser, setSelectedSeriesUser] = useState(null)
  const [socket, setSocket] = useState(null)

  useEffect(() => {
    // Initialize socket connection
    const newSocket = io({
      auth: {
        token: localStorage.getItem('authToken')
      }
    })

    setSocket(newSocket)

    // Listen for real-time updates
    newSocket.on('adminDataUpdate', (data) => {
      console.log('📡 Real-time admin data update:', data)
      if (data.users) setUsers(data.users)
      if (data.stats) setStats(data.stats)
    })

    // Cleanup on unmount
    return () => {
      newSocket.disconnect()
    }
  }, [])

  useEffect(() => {
    fetchAdminData()
  }, [])

  useEffect(() => {
    // Initialize socket connection
    const socketConnection = io({
      auth: {
        token: localStorage.getItem('authToken')
      }
    })

    setSocket(socketConnection)

    // Listen for real-time updates
    socketConnection.on('adminDataUpdate', (data) => {
      console.log('📡 Real-time admin data update:', data)
      if (data.users) setUsers(data.users)
      if (data.stats) setStats(data.stats)
    })

    // Listen for stream status changes
    socketConnection.on('streamStatusChanged', (data) => {
      console.log('📡 Stream status changed:', data)
      fetchAdminData()
      if (!data.isActive && data.reason === 'Stream ended') {
        alert(`📴 ${data.username} was automatically deactivated because their stream ended`)
      }
    })

    return () => {
      socketConnection.disconnect()
    }
  }, [])

  // Helper function to get auth headers
  const getAuthHeaders = () => {
    const token = localStorage.getItem('authToken')
    const headers = {
      'Content-Type': 'application/json'
    }

    if (token) {
      headers['Authorization'] = `Bearer ${token}`
    }

    return headers
  }

  const fetchAdminData = async () => {
    try {
      const headers = getAuthHeaders()

      // Fetch users
      const usersResponse = await fetch('/api/admin/users', { headers })
      if (usersResponse.ok) {
        const usersData = await usersResponse.json()
        setUsers(usersData)
      }

      // Fetch stats
      const statsResponse = await fetch('/api/admin/stats', { headers })
      if (statsResponse.ok) {
        const statsData = await statsResponse.json()
        setStats(statsData)
      }

      // Fetch features
      const featuresResponse = await fetch('/api/admin/features', { headers })
      if (featuresResponse.ok) {
        const featuresData = await featuresResponse.json()
        setFeatures(featuresData)
      }

      // Fetch roles
      const rolesResponse = await fetch('/api/admin/roles', { headers })
      if (rolesResponse.ok) {
        const rolesData = await rolesResponse.json()
        setRoles(rolesData)
      }

      // Fetch permissions
      const permissionsResponse = await fetch('/api/admin/permissions', { headers })
      if (permissionsResponse.ok) {
        const permissionsData = await permissionsResponse.json()
        setPermissions(permissionsData)
      }

      // Fetch streams
      const streamsResponse = await fetch('/api/admin/streams', { headers })
      if (streamsResponse.ok) {
        const streamsData = await streamsResponse.json()
        setStreams(streamsData)
      }

    } catch (error) {
      console.error('❌ Error fetching admin data:', error)
    } finally {
      setRefreshing(false)
    }
  }

  const handleRefresh = async () => {
    setRefreshing(true)
    await fetchAdminData()
  }

  const toggleUserStatus = async (userId, currentStatus) => {
    try {
      const headers = getAuthHeaders()
      const response = await fetch(`/api/admin/users/${userId}/toggle`, {
        method: 'PUT',
        headers,
        body: JSON.stringify({ isActive: !currentStatus })
      })

      if (response.ok) {
        // Refresh data to show changes
        await fetchAdminData()
      } else {
        const errorData = await response.json()
        alert(`Failed to toggle user status: ${errorData.error}`)
      }
    } catch (error) {
      console.error('❌ Error toggling user status:', error)
      alert('Network error: Failed to toggle user status')
    }
  }

  const toggleFeature = async (userId, featureName, currentValue) => {
    try {
      const headers = getAuthHeaders()
      const response = await fetch(`/api/admin/users/${userId}/features`, {
        method: 'PUT',
        headers,
        body: JSON.stringify({
          feature: featureName,
          enabled: !currentValue
        })
      })

      if (response.ok) {
        // Refresh data to show changes
        await fetchAdminData()
      } else {
        const errorData = await response.json()
        alert(`Failed to toggle feature: ${errorData.error}`)
      }
    } catch (error) {
      console.error('❌ Error toggling feature:', error)
      alert('Network error: Failed to toggle feature')
    }
  }

  return (
    <div className="admin-dashboard">
      {/* Header */}
      <div className="admin-header">
        <h1>🛠️ Admin Dashboard</h1>
        <button
          onClick={handleRefresh}
          disabled={refreshing}
          className="refresh-btn"
        >
          {refreshing ? '🔄' : '♻️'} Refresh
        </button>
      </div>

      {/* Navigation Tabs */}
      <div className="admin-tabs">
        <button
          className={`tab-btn ${showUserModal ? 'active' : ''}`}
          onClick={() => setShowUserModal(true)}
        >
          👥 Users
        </button>
        <button
          className={`tab-btn ${showPermissionManager ? 'active' : ''}`}
          onClick={() => setShowPermissionManager(true)}
        >
          🔐 Permissions
        </button>
        <button
          className="tab-btn"
          onClick={() => {
            if (onNavigateToDebug) {
              onNavigateToDebug()
            }
          }}
        >
          🐛 Debug Tools
        </button>
      </div>

      {/* Stats Overview */}
      <div className="stats-grid">
        <div className="stat-card">
          <h3>📊 Total Users</h3>
          <div className="stat-value">{stats.totalUsers || 0}</div>
        </div>
        <div className="stat-card">
          <h3>✅ Active Users</h3>
          <div className="stat-value">{stats.activeUsers || 0}</div>
        </div>
        <div className="stat-card">
          <h3>🎮 Live Streams</h3>
          <div className="stat-value">{streams?.length || 0}</div>
        </div>
        <div className="stat-card">
          <h3>💀 Total Deaths</h3>
          <div className="stat-value">{stats.totalDeaths || 0}</div>
        </div>
      </div>

      {/* Modals */}
      {showUserModal && (
        <UserManagementModal
          isOpen={showUserModal}
          onClose={() => setShowUserModal(false)}
          users={users}
          onRefresh={fetchAdminData}
          onEditUser={(user) => {
            setSelectedUser(user)
            setShowUserConfigPortal(true)
          }}
          onToggleUser={toggleUserStatus}
          onToggleFeature={toggleFeature}
          onShowOverlay={(user) => {
            setSelectedOverlayUser(user)
            setShowOverlayModal(true)
          }}
          onShowAlerts={(user) => {
            setSelectedAlertUser(user)
            setShowAlertModal(true)
          }}
          onShowDiscord={(user) => {
            setSelectedDiscordUser(user)
            setShowDiscordModal(true)
          }}
          onShowSeries={(user) => {
            setSelectedSeriesUser(user)
            setShowSeriesModal(true)
          }}
        />
      )}

      {showPermissionManager && (
        <PermissionManager
          isOpen={showPermissionManager}
          onClose={() => setShowPermissionManager(false)}
          users={users}
          roles={roles}
          permissions={permissions}
          onRefresh={fetchAdminData}
        />
      )}

      {showUserConfigPortal && selectedUser && (
        <UserConfigurationPortal
          user={selectedUser}
          onClose={() => {
            setShowUserConfigPortal(false)
            setSelectedUser(null)
          }}
          onUpdate={fetchAdminData}
        />
      )}

      {/* Overlay Settings Modal */}
      <OverlaySettingsModal
        isOpen={showOverlayModal && !!selectedOverlayUser}
        onClose={() => {
          setShowOverlayModal(false)
          setSelectedOverlayUser(null)
        }}
        user={selectedOverlayUser}
        isAdminMode={true}
        onUpdate={() => {
          console.log('🔄 Overlay settings updated, refreshing data...')
          fetchAdminData()
        }}
      />

      {/* Alert Settings Modal */}
      {(showAlertModal && selectedAlertUser) && (
        <div className="modal-overlay" onClick={() => setShowAlertModal(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '1000px', width: '90vw' }}>
            <div className="modal-header">
              <h2>🎬 Alert Management - {selectedAlertUser.displayName || selectedAlertUser.username}</h2>
              <button
                onClick={() => {
                  setShowAlertModal(false)
                  setSelectedAlertUser(null)
                }}
                className="close-btn"
                aria-label="Close"
              >
                ×
              </button>
            </div>
            <div className="modal-body">
              <AlertEventManager
                user={selectedAlertUser}
                onUpdate={() => {
                  console.log('🔄 Alert settings updated, refreshing data...')
                  fetchAdminData()
                }}
              />
            </div>
          </div>
        </div>
      )}

      {/* Discord Settings Modal */}
      {(showDiscordModal && selectedDiscordUser) && (
        <div className="modal-overlay" onClick={() => setShowDiscordModal(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '800px', width: '90vw' }}>
            <div className="modal-header">
              <h2>🎮 Discord Integration - {selectedDiscordUser.displayName || selectedDiscordUser.username}</h2>
              <button
                onClick={() => {
                  setShowDiscordModal(false)
                  setSelectedDiscordUser(null)
                }}
                className="close-btn"
                aria-label="Close"
              >
                ×
              </button>
            </div>
            <div className="modal-body">
              <DiscordWebhookSettings
                user={selectedDiscordUser}
                onUpdate={() => {
                  console.log('🔄 Discord settings updated, refreshing data...')
                  fetchAdminData()
                }}
              />
            </div>
          </div>
        </div>
      )}

      {/* Series Save Modal */}
      {(showSeriesModal && selectedSeriesUser) && (
        <div className="modal-overlay" onClick={() => setShowSeriesModal(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '1000px', width: '90vw' }}>
            <div className="modal-header">
              <h2>💾 Series Save Management - {selectedSeriesUser.displayName || selectedSeriesUser.username}</h2>
              <button
                onClick={() => {
                  setShowSeriesModal(false)
                  setSelectedSeriesUser(null)
                }}
                className="close-btn"
                aria-label="Close"
              >
                ×
              </button>
            </div>
            <div className="modal-body">
              <SeriesSaveManager
                user={selectedSeriesUser}
                onUpdate={() => {
                  console.log('🔄 Series settings updated, refreshing data...')
                  fetchAdminData()
                }}
              />
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export default AdminDashboard

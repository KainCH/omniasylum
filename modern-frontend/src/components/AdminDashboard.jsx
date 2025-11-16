import { useState, useEffect } from 'react'
import { io } from 'socket.io-client'
import AlertEffectsModal from './AlertEffectsModal'
import AlertEventManagerModal from './AlertEventManagerModal'
import DiscordWebhookSettingsModal from './DiscordWebhookSettingsModal'
import SeriesSaveManagerModal from './SeriesSaveManagerModal'
import UserManagementModal from './UserManagementModal'
import UserRequestsModal from './UserRequestsModal'
import BrokenUserManagerModal from './BrokenUserManagerModal'
import UserConfigurationPortal from './UserConfigurationPortal'
import PermissionManager from './PermissionManager'
import OverlaySettingsModal from './OverlaySettingsModal'
import { ActionButton, FormSection, StatusBadge, ToggleSwitch, InputGroup } from './ui/CommonControls'
import { useUserData, useLoading, useToast } from '../hooks'
import { userAPI, counterAPI, adminAPI, APIError } from '../utils/authUtils'
import { getAuthHeaders, makeAuthenticatedJsonRequest, handleAuthError, getAuthToken } from '../utils/authUtils'
import './AdminDashboard.css'

// Helper function to get user-friendly feature names
const getFeatureDisplayName = (featureKey) => {
  const featureNames = {
    chatCommands: '💬 Chat Commands',
    channelPoints: '🏆 Channel Points',
    autoClip: '🎬 Auto Clip',
    customCommands: '⚡ Custom Commands',
    analytics: '📊 Analytics',
    webhooks: '🔗 Webhooks',
    bitsIntegration: '💎 Bits Integration',
    streamOverlay: '🖼️ Stream Overlay',
    alertAnimations: '✨ Alert Animations',
    streamAlerts: '🚨 Stream Alerts',
    discordNotifications: '🎮 Discord Notifications',
    seriesSaves: '💾 Series Saves',
    nightMode: '🌙 Night Mode',
    advancedStats: '📈 Advanced Stats'
  }
  return featureNames[featureKey] || featureKey
}

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
  const [showUserRequestsModal, setShowUserRequestsModal] = useState(false)
  const [showBrokenUserModal, setShowBrokenUserModal] = useState(false)
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
        token: getAuthToken()
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
        token: getAuthToken()
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

  const fetchAdminData = async () => {
    try {
      // Fetch users using centralized auth utilities
      const usersResponse = await fetch('/api/admin/users', {
        headers: getAuthHeaders(),
        credentials: 'include'
      })
      if (usersResponse.ok) {
        const usersData = await usersResponse.json()
        // Backend returns { users: [...], total: ... }
        const usersList = usersData.users || usersData
        setUsers(Array.isArray(usersList) ? usersList : [])
      } else {
        console.error('Failed to fetch users:', usersResponse.status)
        setUsers([])
      }

      // Fetch stats
      const statsResponse = await fetch('/api/admin/stats', {
        headers: getAuthHeaders(),
        credentials: 'include'
      })
      if (statsResponse.ok) {
        const statsData = await statsResponse.json()
        setStats(statsData)
      }

      // Fetch features
      const featuresResponse = await fetch('/api/admin/features', {
        headers: getAuthHeaders(),
        credentials: 'include'
      })
      if (featuresResponse.ok) {
        const featuresData = await featuresResponse.json()
        setFeatures(featuresData)
      }

      // Fetch roles
      const rolesResponse = await fetch('/api/admin/roles', {
        headers: getAuthHeaders(),
        credentials: 'include'
      })
      if (rolesResponse.ok) {
        const rolesData = await rolesResponse.json()
        setRoles(rolesData)
      }

      // Fetch permissions
      const permissionsResponse = await fetch('/api/admin/permissions', {
        headers: getAuthHeaders(),
        credentials: 'include'
      })
      if (permissionsResponse.ok) {
        const permissionsData = await permissionsResponse.json()
        setPermissions(permissionsData)
      }

      // Fetch streams
      const streamsResponse = await fetch('/api/admin/streams', {
        headers: getAuthHeaders(),
        credentials: 'include'
      })
      if (streamsResponse.ok) {
        const streamsData = await streamsResponse.json()
        setStreams(streamsData)
      }

    } catch (error) {
      console.error('❌ Error fetching admin data:', error)
      // Ensure state is set to safe defaults on error
      setUsers([])
      setStats({})
      setFeatures([])
      setRoles([])
      setPermissions([])
      setStreams([])
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
      const response = await fetch(`/api/admin/users/${userId}/toggle`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        credentials: 'include',
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
      const response = await fetch(`/api/admin/users/${userId}/features`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        credentials: 'include',
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

  const deleteUser = async (user) => {
    const userName = user.displayName || user.username || 'Unknown User'

    if (!confirm(`⚠️ Are you sure you want to permanently delete user "${userName}"?\n\nThis will remove:\n• User account and profile\n• All counter data\n• All settings and features\n• Discord webhooks\n• All associated data\n\nThis action CANNOT be undone!`)) {
      return
    }

    try {
      const userId = user.twitchUserId || user.userId
      if (!userId) {
        alert('❌ Cannot delete user: Invalid user ID')
        return
      }

      const response = await fetch(`/api/admin/users/${userId}`, {
        method: 'DELETE',
        headers: getAuthHeaders(),
        credentials: 'include'
      })

      if (response.ok) {
        alert(`✅ User "${userName}" has been successfully deleted.`)
        // Refresh data to show changes
        await fetchAdminData()
      } else {
        const errorData = await response.json()
        alert(`❌ Failed to delete user: ${errorData.error}`)
      }
    } catch (error) {
      console.error('❌ Error deleting user:', error)
      alert('❌ Network error: Failed to delete user')
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
          className={`tab-btn ${showUserRequestsModal ? 'active' : ''}`}
          onClick={() => setShowUserRequestsModal(true)}
        >
          📋 User Requests
        </button>
        <button
          className={`tab-btn ${showBrokenUserModal ? 'active' : ''}`}
          onClick={() => setShowBrokenUserModal(true)}
        >
          🛠️ Broken Users
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

      {/* Users Overview Section */}
      <div className="users-overview-section">
        <div className="section-header">
          <h2>👥 Users & Features Overview</h2>
          <div className="section-actions">
            <span className="user-count">{users.length} Total Users</span>
            <button
              onClick={() => setShowUserModal(true)}
              className="manage-users-btn"
            >
              ⚙️ Manage Users
            </button>
          </div>
        </div>

        <div className="users-grid">
          {!Array.isArray(users) || users.length === 0 ? (
            <div className="no-users-message">
              <div className="no-users-icon">👥</div>
              <h3>No Users Found</h3>
              <p>No registered users in the system yet.</p>
            </div>
          ) : (
            users.map(user => (
              <div key={user.twitchUserId || user.userId} className="user-tile">
                <div className="user-header">
                  <div className="user-info">
                    <img
                      src={user.profileImageUrl || '/default-avatar.png'}
                      alt={user.displayName || user.username}
                      className="user-avatar"
                    />
                    <div className="user-details">
                      <h4 className="user-name">{user.displayName || user.username}</h4>
                      <div className="user-meta">
                        <span className={`status-badge ${user.isActive ? 'active' : 'inactive'}`}>
                          {user.isActive ? '✅ Active' : '❌ Inactive'}
                        </span>
                        <span className="user-role">{user.role || 'streamer'}</span>
                      </div>
                    </div>
                  </div>
                  <div className="user-actions">
                    <button
                      onClick={() => {
                        setSelectedUser(user)
                        setShowUserConfigPortal(true)
                      }}
                      className="action-btn edit-btn"
                      title="Edit User"
                    >
                      ✏️
                    </button>
                  </div>
                </div>

                <div className="user-features">
                  <h5>Enabled Features:</h5>
                  <div className="features-list">
                    {user.features && Object.keys(user.features).length > 0 ? (
                      Object.entries(user.features)
                        .filter(([key, value]) => value === true)
                        .map(([featureKey, enabled]) => (
                          <span key={featureKey} className="feature-tag enabled">
                            {getFeatureDisplayName(featureKey)}
                          </span>
                        ))
                    ) : (
                      <span className="no-features">No features enabled</span>
                    )}
                  </div>

                  {user.features && Object.entries(user.features).filter(([key, value]) => value === false).length > 0 && (
                    <>
                      <h5>Disabled Features:</h5>
                      <div className="features-list">
                        {Object.entries(user.features)
                          .filter(([key, value]) => value === false)
                          .slice(0, 3)
                          .map(([featureKey, enabled]) => (
                            <span key={featureKey} className="feature-tag disabled">
                              {getFeatureDisplayName(featureKey)}
                            </span>
                          ))}
                        {Object.entries(user.features).filter(([key, value]) => value === false).length > 3 && (
                          <span className="feature-tag more">
                            +{Object.entries(user.features).filter(([key, value]) => value === false).length - 3} more
                          </span>
                        )}
                      </div>
                    </>
                  )}
                </div>

                <div className="user-quick-actions">
                  <button
                    onClick={() => {
                      setSelectedOverlayUser(user)
                      setShowOverlayModal(true)
                    }}
                    className="quick-action-btn"
                    title="Overlay Settings"
                  >
                    ⚙️ Overlay
                  </button>
                  <button
                    onClick={() => {
                      setSelectedAlertUser(user)
                      setShowAlertModal(true)
                    }}
                    className="quick-action-btn"
                    title="Alert Settings"
                  >
                    🎬 Alerts
                  </button>
                  <button
                    onClick={() => {
                      setSelectedDiscordUser(user)
                      setShowDiscordModal(true)
                    }}
                    className="quick-action-btn"
                    title="Discord Settings"
                  >
                    🎮 Discord
                  </button>
                </div>
              </div>
            ))
          )}
        </div>
      </div>

      {/* Modals */}
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
          onDeleteUser={deleteUser}
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

      <PermissionManager
        isOpen={showPermissionManager}
        onClose={() => setShowPermissionManager(false)}
        users={users}
          roles={roles}
        permissions={permissions}
        onRefresh={fetchAdminData}
      />      {showUserConfigPortal && selectedUser && (
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
      <AlertEventManagerModal
        isOpen={showAlertModal && selectedAlertUser}
        onClose={() => {
          setShowAlertModal(false)
          setSelectedAlertUser(null)
        }}
        user={selectedAlertUser}
        isAdminMode={true}
        onUpdate={() => {
          console.log('🔄 Alert settings updated, refreshing data...')
          fetchAdminData()
        }}
      />

      {/* Discord Settings Modal */}
      <DiscordWebhookSettingsModal
        isOpen={showDiscordModal && selectedDiscordUser}
        onClose={() => {
          setShowDiscordModal(false)
          setSelectedDiscordUser(null)
        }}
        user={selectedDiscordUser}
        isAdminMode={true}
        onUpdate={() => {
          console.log('🔄 Discord settings updated, refreshing data...')
          fetchAdminData()
        }}
      />

      {/* Series Save Modal */}
      <SeriesSaveManagerModal
        isOpen={showSeriesModal}
        onClose={() => {
          setShowSeriesModal(false)
          setSelectedSeriesUser(null)
        }}
        user={selectedSeriesUser}
        isAdminMode={true}
        onUpdate={() => {
          console.log('🔄 Series settings updated, refreshing data...')
          fetchAdminData()
        }}
      />

      <UserRequestsModal
        isOpen={showUserRequestsModal}
        onClose={() => setShowUserRequestsModal(false)}
      />

      <BrokenUserManagerModal
        isOpen={showBrokenUserModal}
        onClose={() => setShowBrokenUserModal(false)}
      />
    </div>
  )
}

export default AdminDashboard

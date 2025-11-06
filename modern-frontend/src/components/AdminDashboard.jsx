import { useState, useEffect } from 'react'
import { io } from 'socket.io-client'

function AdminDashboard() {
  const [users, setUsers] = useState([])
  const [stats, setStats] = useState({})
  const [features, setFeatures] = useState([])
  const [roles, setRoles] = useState([])
  const [permissions, setPermissions] = useState([])
  const [streams, setStreams] = useState([])
  const [loading, setLoading] = useState(true)
  const [addingUser, setAddingUser] = useState(false)
  const [showAddUser, setShowAddUser] = useState(false)
  const [showRoleManager, setShowRoleManager] = useState(false)
  const [showRewardsManager, setShowRewardsManager] = useState(false)
  const [showAlertsManager, setShowAlertsManager] = useState(false)
  const [rewards, setRewards] = useState([])
  const [alerts, setAlerts] = useState([])
  const [alertTemplates, setAlertTemplates] = useState([])
  const [newReward, setNewReward] = useState({
    title: '',
    cost: 100,
    action: 'increment_deaths',
    prompt: '',
    backgroundColor: '#9147FF'
  })
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
  const [newUser, setNewUser] = useState({
    username: '',
    displayName: '',
    email: '',
    twitchUserId: ''
  })
  const [currentUserRole, setCurrentUserRole] = useState('streamer')
  const [socket, setSocket] = useState(null)

  useEffect(() => {
    fetchAdminData()
  }, [])

  // Socket.io connection for real-time updates
  useEffect(() => {
    const socketConnection = io()
    setSocket(socketConnection)

    // Listen for user status changes (auto-deactivation when stream ends)
    socketConnection.on('userStatusChanged', (data) => {
      console.log(`🔄 User status changed: ${data.username} is now ${data.isActive ? 'active' : 'inactive'} (${data.reason})`)

      // Update user in the list
      setUsers(prevUsers =>
        prevUsers.map(user =>
          user.twitchUserId === data.userId
            ? { ...user, isActive: data.isActive }
            : user
        )
      )

      // Show notification to admin
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

  // Logout function
  const handleLogout = () => {
    localStorage.removeItem('authToken')
    window.location.href = '/auth/twitch'
  }

  const fetchAdminData = async () => {
    try {
      const headers = getAuthHeaders()

      // Fetch users
      const usersResponse = await fetch('/api/admin/users', {
        headers
      })
      if (usersResponse.ok) {
        const usersData = await usersResponse.json()
        setUsers(usersData.users || usersData) // Handle both response formats
      } else {
        console.error('Failed to fetch users:', usersResponse.status, usersResponse.statusText)
      }

      // Fetch system stats
      const statsResponse = await fetch('/api/admin/stats', {
        headers
      })
      if (statsResponse.ok) {
        const statsData = await statsResponse.json()
        setStats(statsData)
      } else {
        console.error('Failed to fetch stats:', statsResponse.status)
      }

      // Fetch available features
      const featuresResponse = await fetch('/api/admin/features', {
        headers
      })
      if (featuresResponse.ok) {
        const featuresData = await featuresResponse.json()
        setFeatures(featuresData.features)
      } else {
        console.error('Failed to fetch features:', featuresResponse.status)
      }

      // Fetch available roles
      const rolesResponse = await fetch('/api/admin/roles', {
        headers
      })
      if (rolesResponse.ok) {
        const rolesData = await rolesResponse.json()
        setRoles(rolesData.roles)
      } else {
        console.error('Failed to fetch roles:', rolesResponse.status)
      }

      // Fetch available permissions
      const permissionsResponse = await fetch('/api/admin/permissions', {
        headers
      })
      if (permissionsResponse.ok) {
        const permissionsData = await permissionsResponse.json()
        setPermissions(permissionsData.permissions)
      } else {
        console.error('Failed to fetch permissions:', permissionsResponse.status)
      }

      // Fetch stream sessions
      const streamsResponse = await fetch('/api/admin/streams', {
        headers
      })
      if (streamsResponse.ok) {
        const streamsData = await streamsResponse.json()
        setStreams(streamsData.sessions || [])
      } else {
        console.error('Failed to fetch streams:', streamsResponse.status)
      }

      // Fetch channel point rewards (admin view)
      const rewardsResponse = await fetch('/api/rewards/admin/all', {
        headers
      })
      if (rewardsResponse.ok) {
        const rewardsData = await rewardsResponse.json()
        setRewards(rewardsData.users || [])
      } else {
        console.error('Failed to fetch rewards:', rewardsResponse.status)
      }

      // Fetch alert configurations (admin view)
      const alertsResponse = await fetch('/api/alerts/admin/all', {
        headers
      })
      if (alertsResponse.ok) {
        const alertsData = await alertsResponse.json()
        setAlerts(alertsData.users || [])
      } else {
        console.error('Failed to fetch alerts:', alertsResponse.status)
      }

      // Fetch alert templates
      const templatesResponse = await fetch('/api/alerts/templates', {
        headers
      })
      if (templatesResponse.ok) {
        const templatesData = await templatesResponse.json()
        setAlertTemplates(templatesData.templates || [])
      } else {
        console.error('Failed to fetch alert templates:', templatesResponse.status)
      }
    } catch (error) {
      console.error('Failed to fetch admin data:', error)
    } finally {
      setLoading(false)
    }
  }

  const toggleUserActive = async (userId, isActive) => {
    try {
      const response = await fetch(`/api/admin/users/${userId}/status`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        body: JSON.stringify({ isActive: !isActive })
      })

      if (response.ok) {
        fetchAdminData() // Refresh data
      }
    } catch (error) {
      console.error('❌ Failed to update user:', error)
    }
  }

  const toggleFeature = async (userId, feature, enabled) => {
    try {
      // Get current features first
      const user = users.find(u => u.twitchUserId === userId)
      const currentFeatures = typeof user.features === 'string'
        ? JSON.parse(user.features)
        : user.features || {}

      // Update the specific feature
      const updatedFeatures = {
        ...currentFeatures,
        [feature]: !enabled
      }

      const response = await fetch(`/api/admin/users/${userId}/features`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        body: JSON.stringify({ features: updatedFeatures })
      })

      if (response.ok) {
        fetchAdminData() // Refresh data
      }
    } catch (error) {
      console.error('❌ Failed to update feature:', error)
    }
  }

  const updateUserRole = async (userId, newRole) => {
    try {
      const response = await fetch(`/api/admin/users/${userId}/role`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        body: JSON.stringify({ role: newRole })
      })

      if (response.ok) {
        // Refresh users data
        fetchAdminData()
      } else {
        const errorData = await response.json()
        console.error('❌ Failed to update role:', errorData.error)
        alert(errorData.error)
      }
    } catch (error) {
      console.error('❌ Failed to update role:', error)
      alert('Failed to update user role')
    }
  }

  const updateUserOverlaySettings = async (userId, settings) => {
    try {
      // Ensure we have a valid token before making the request
      const hasValidToken = await ensureValidToken()
      if (!hasValidToken) {
        console.log('❌ Cannot update overlay settings: invalid token')
        return
      }

      const authHeaders = getAuthHeaders()
      const token = localStorage.getItem('authToken')

      console.log('📤 Sending overlay settings update:', {
        userId,
        settings,
        hasToken: !!token,
        tokenPreview: token ? token.substring(0, 20) + '...' : 'no token',
        headers: Object.keys(authHeaders)
      })

      const url = `/api/admin/users/${userId}/overlay-settings`
      console.log('🌐 Request URL:', url)
      console.log('🔑 Request headers:', authHeaders)

      const response = await fetch(url, {
        method: 'PUT',
        headers: authHeaders,
        body: JSON.stringify(settings)
      })

      console.log('📊 Response status:', response.status, response.statusText)
      console.log('📊 Response headers:', Object.fromEntries(response.headers.entries()))

      if (response.ok) {
        const result = await response.json()
        console.log('📥 Overlay settings update response:', result)

        // Check token status
        const tokenStatus = checkTokenExpiry()
        console.log('🕒 Token status during update:', tokenStatus)

        console.log('⏳ Waiting 1 second before refreshing data to ensure backend persistence...')
        // Wait longer before refreshing to ensure backend has fully processed
        setTimeout(async () => {
          console.log('🔄 Refreshing admin data after overlay update...')
          await fetchAdminData()
          console.log('🔄 Data refresh complete')
        }, 1000)
        console.log('✅ Overlay settings updated successfully')
      } else {
        let errorMessage = 'Unknown error'
        try {
          const errorData = await response.json()
          errorMessage = errorData.error || errorMessage
          console.error('❌ Failed to update overlay settings:', errorData)
        } catch (parseError) {
          console.error('❌ Failed to parse error response:', parseError)
          errorMessage = `HTTP ${response.status}: ${response.statusText}`
        }
        console.error('❌ Request failed with status:', response.status)
        alert(errorMessage)
      }
    } catch (error) {
      console.error('❌ Failed to update overlay settings (network error):', error)
      alert('Network error: Failed to update overlay settings')
    }
  }

  // Get current user's role from token
  const getCurrentUserRole = () => {
    const token = localStorage.getItem('authToken')
    if (token) {
      try {
        const payload = JSON.parse(atob(token.split('.')[1]))
        return payload.role || 'streamer'
      } catch (e) {
        return 'streamer'
      }
    }
    return 'streamer'
  }

  // Check if token is expired or needs refresh
  const checkTokenExpiry = () => {
    const token = localStorage.getItem('authToken')
    if (token) {
      try {
        const payload = JSON.parse(atob(token.split('.')[1]))
        const now = Date.now() / 1000
        const exp = payload.exp || 0
        const timeUntilExpiry = exp - now

        console.log('🕒 Token info:', {
          userId: payload.userId,
          username: payload.username,
          role: payload.role,
          issuedAt: new Date(payload.iat * 1000).toISOString(),
          expiresAt: new Date(exp * 1000).toISOString(),
          timeUntilExpiry: Math.round(timeUntilExpiry),
          isExpired: timeUntilExpiry <= 0,
          needsRefresh: timeUntilExpiry <= 3600 // Less than 1 hour
        })

        return {
          isExpired: timeUntilExpiry <= 0,
          needsRefresh: timeUntilExpiry <= 3600,
          timeUntilExpiry
        }
      } catch (e) {
        console.error('❌ Failed to parse token:', e)
        return { isExpired: true, needsRefresh: true, timeUntilExpiry: 0 }
      }
    }
    return { isExpired: true, needsRefresh: true, timeUntilExpiry: 0 }
  }

  // Refresh JWT token using Twitch refresh token
  const refreshToken = async () => {
    try {
      console.log('🔄 Attempting to refresh token...')
      const response = await fetch('/auth/refresh', {
        method: 'POST',
        headers: getAuthHeaders()
      })

      if (response.ok) {
        const data = await response.json()
        localStorage.setItem('authToken', data.token)
        console.log('✅ Token refreshed successfully')
        return true
      } else {
        console.error('❌ Token refresh failed:', response.status)
        return false
      }
    } catch (error) {
      console.error('❌ Token refresh error:', error)
      return false
    }
  }

  // Automatically refresh token if needed before making requests
  const ensureValidToken = async () => {
    const tokenStatus = checkTokenExpiry()

    if (tokenStatus.isExpired) {
      console.log('🔄 Token expired, attempting refresh...')
      const refreshed = await refreshToken()
      if (!refreshed) {
        console.log('❌ Token refresh failed, redirecting to login...')
        localStorage.removeItem('authToken')
        window.location.href = '/auth/twitch'
        return false
      }
    } else if (tokenStatus.needsRefresh) {
      console.log('⚠️ Token expires soon, refreshing proactively...')
      await refreshToken() // Don't fail if this doesn't work
    }

    return true
  }

  // Get current user info from JWT token
  const getCurrentUser = () => {
    const token = localStorage.getItem('authToken')
    if (token) {
      try {
        const payload = JSON.parse(atob(token.split('.')[1]))
        return {
          twitchUserId: payload.sub,
          username: payload.username,
          role: payload.role || 'streamer'
        }
      } catch (e) {
        return null
      }
    }
    return null
  }

  // Check if current user can perform action based on role hierarchy
  const canManageRole = (targetRole) => {
    const currentRole = getCurrentUserRole()
    const roleHierarchy = { 'streamer': 0, 'moderator': 1, 'manager': 2, 'admin': 3 }
    const currentLevel = roleHierarchy[currentRole] || 0
    const targetLevel = roleHierarchy[targetRole] || 0
    return currentLevel >= 3 || currentLevel > targetLevel // Only admin can assign same/higher roles
  }

  // Self-activate function for streamers
  const selfActivate = async () => {
    const currentUser = getCurrentUser()
    if (!currentUser) {
      alert('❌ Unable to get user information. Please log in again.')
      return
    }

    try {
      const response = await fetch(`/api/admin/users/${currentUser.twitchUserId}/status`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        body: JSON.stringify({ isActive: true })
      })

      if (response.ok) {
        fetchAdminData() // Refresh data
        console.log('✅ Self-activated successfully')
        alert('✅ You are now ACTIVE and ready to stream!')
      } else {
        const errorData = await response.json()
        console.error('❌ Failed to self-activate:', errorData.error)
        alert(`❌ Failed to activate: ${errorData.error}`)
      }
    } catch (error) {
      console.error('❌ Failed to self-activate:', error)
      alert('❌ Failed to activate. Please try again.')
    }
  }

  // Update stream status function
  const updateStreamStatus = async (userId, action) => {
    try {
      const response = await fetch(`/api/stream/${action}`, {
        method: 'POST',
        headers: getAuthHeaders(),
        body: JSON.stringify({ userId })
      })

      if (response.ok) {
        const result = await response.json()
        console.log(`✅ Stream status updated: ${action}`, result)
        fetchAdminData() // Refresh data

        // Show user feedback
        const actionMessages = {
          'prep': '🎭 Stream prep started! Bots are warming up...',
          'go-live': '🚀 You are now LIVE! Overlay and bots active.',
          'end-stream': '🏁 Stream ended. Thanks for streaming!',
          'cancel-prep': '❌ Prep cancelled. Ready to start again.'
        }
        alert(actionMessages[action] || `Status updated: ${action}`)
      } else {
        const errorData = await response.json()
        console.error(`❌ Failed to update stream status (${action}):`, errorData.error)
        alert(`❌ Failed to ${action}: ${errorData.error}`)
      }
    } catch (error) {
      console.error(`❌ Failed to update stream status (${action}):`, error)
      alert(`❌ Failed to ${action}. Please try again.`)
    }
  }

  const addUser = async () => {
    try {
      // Validate required fields
      if (!newUser.username?.trim() || !newUser.twitchUserId?.trim()) {
        alert('Username and Twitch User ID are required')
        return
      }

      setAddingUser(true)

      // Clean the data before sending
      const cleanUserData = {
        username: newUser.username.trim(),
        displayName: newUser.displayName?.trim() || '',
        email: newUser.email?.trim() || '',
        twitchUserId: newUser.twitchUserId.trim()
      }

      const response = await fetch('/api/admin/users', {
        method: 'POST',
        headers: getAuthHeaders(),
        body: JSON.stringify(cleanUserData)
      })

      if (response.ok) {
        const result = await response.json()
        alert(`✅ ${result.message}`) // Show success message with avatar status
        setShowAddUser(false)
        setNewUser({ username: '', displayName: '', email: '', twitchUserId: '' })
        fetchAdminData() // Refresh data
      } else {
        const error = await response.json()
        alert(`Failed to add user: ${error.error}`)
      }
    } catch (error) {
      console.error('❌ Failed to add user:', error)
      alert('Failed to add user')
    } finally {
      setAddingUser(false)
    }
  }

  const deleteUser = async (userId, username) => {
    if (!confirm(`Are you sure you want to delete user ${username}? This cannot be undone.`)) {
      return
    }

    try {
      const response = await fetch(`/api/admin/users/${userId}`, {
        method: 'DELETE',
        headers: getAuthHeaders()
      })

      if (response.ok) {
        fetchAdminData() // Refresh data
      } else {
        const error = await response.json()
        alert(`Failed to delete user: ${error.error}`)
      }
    } catch (error) {
      console.error('❌ Failed to delete user:', error)
      alert('Failed to delete user')
    }
  }

  // Channel Point Reward Management Functions
  const createReward = async (userId) => {
    try {
      const response = await fetch('/api/rewards', {
        method: 'POST',
        headers: getAuthHeaders(),
        body: JSON.stringify({
          ...newReward,
          userId: userId
        })
      })

      if (response.ok) {
        const data = await response.json()
        alert(`Reward "${newReward.title}" created successfully!`)
        setNewReward({
          title: '',
          cost: 100,
          action: 'increment_deaths',
          prompt: '',
          backgroundColor: '#9147FF'
        })
        fetchAdminData() // Refresh data
      } else {
        const error = await response.json()
        alert(`Failed to create reward: ${error.error}`)
      }
    } catch (error) {
      console.error('❌ Failed to create reward:', error)
      alert('Failed to create reward')
    }
  }

  const deleteReward = async (userId, rewardId, rewardTitle) => {
    if (!confirm(`Are you sure you want to delete the reward "${rewardTitle}"? This action cannot be undone.`)) {
      return
    }

    try {
      const response = await fetch(`/api/rewards/${rewardId}`, {
        method: 'DELETE',
        headers: getAuthHeaders()
      })

      if (response.ok) {
        alert(`Reward "${rewardTitle}" deleted successfully!`)
        fetchAdminData() // Refresh data
      } else {
        const error = await response.json()
        alert(`Failed to delete reward: ${error.error}`)
      }
    } catch (error) {
      console.error('❌ Failed to delete reward:', error)
      alert('Failed to delete reward')
    }
  }

  // Alert Management Functions
  const createAlert = async (userId) => {
    try {
      const response = await fetch('/api/alerts', {
        method: 'POST',
        headers: getAuthHeaders(),
        body: JSON.stringify({
          ...newAlert,
          userId: userId
        })
      })

      if (response.ok) {
        const data = await response.json()
        alert(`Alert "${newAlert.name}" created successfully!`)
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
        fetchAdminData() // Refresh data
      } else {
        const error = await response.json()
        alert(`Failed to create alert: ${error.error}`)
      }
    } catch (error) {
      console.error('❌ Failed to create alert:', error)
      alert('Failed to create alert')
    }
  }

  const deleteAlert = async (userId, alertId, alertName) => {
    if (!confirm(`Are you sure you want to delete the alert "${alertName}"? This action cannot be undone.`)) {
      return
    }

    try {
      const response = await fetch(`/api/alerts/${alertId}`, {
        method: 'DELETE',
        headers: getAuthHeaders()
      })

      if (response.ok) {
        alert(`Alert "${alertName}" deleted successfully!`)
        fetchAdminData() // Refresh data
      } else {
        const error = await response.json()
        alert(`Failed to delete alert: ${error.error}`)
      }
    } catch (error) {
      console.error('❌ Failed to delete alert:', error)
      alert('Failed to delete alert')
    }
  }

  const updateAlertStatus = async (userId, alertId, isEnabled) => {
    try {
      const response = await fetch(`/api/alerts/${alertId}`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        body: JSON.stringify({ isEnabled: !isEnabled })
      })

      if (response.ok) {
        fetchAdminData() // Refresh data
      } else {
        const error = await response.json()
        alert(`Failed to update alert: ${error.error}`)
      }
    } catch (error) {
      console.error('❌ Failed to update alert:', error)
      alert('Failed to update alert')
    }
  }

  if (loading) {
    return (
      <div className="loading">
        <div className="loading-spinner"></div>
        <p>Loading admin dashboard...</p>
      </div>
    )
  }

  return (
    <div className="admin-dashboard">
      <div className="container">
        <header className="admin-header">
          <div className="admin-header-content">
            <div>
              <h1>🔧 Admin Dashboard - OmniForgeStream</h1>
              <p>Welcome, Administrator! Manage users and system settings.</p>
            </div>
            <div className="admin-header-actions">
              <button
                onClick={handleLogout}
                className="btn btn-secondary logout-btn"
                title="Clear auth token and force re-login"
              >
                🚪 Logout & Re-authenticate
              </button>
            </div>
          </div>
        </header>

        {(() => {
          const currentUser = getCurrentUser()
          const currentUserData = users.find(u => u.twitchUserId === currentUser?.twitchUserId)
          const isAdmin = getCurrentUserRole() === 'admin'

          // Show self-activation panel for non-admin users who are inactive
          if (!isAdmin && currentUser && currentUserData && !currentUserData.isActive) {
            return (
              <div className="self-activation-panel">
                <div className="activation-card">
                  <div className="activation-header">
                    <h2>🎮 Ready to Stream?</h2>
                    <p>Activate your account to enable all streaming features before going live!</p>
                  </div>
                  <div className="activation-status">
                    <span className="status-indicator inactive">❌ Currently Inactive</span>
                    <span className="activation-note">• Stream overlay disabled</span>
                    <span className="activation-note">• Chat commands disabled</span>
                    <span className="activation-note">• Event tracking disabled</span>
                  </div>
                  <button
                    onClick={selfActivate}
                    className="btn btn-primary activation-btn"
                  >
                    🚀 Activate Now - Ready to Stream!
                  </button>
                  <small className="activation-info">
                    💡 Your account will automatically deactivate when you stop streaming
                  </small>
                </div>
              </div>
            )
          }

          // Show status for active non-admin users
          if (!isAdmin && currentUser && currentUserData && currentUserData.isActive) {
            return (
              <div className="self-status-panel">
                <div className="status-card active">
                  <h3>✅ You're Active and Ready to Stream!</h3>
                  <p>All features are enabled. Your account will auto-deactivate when you go offline.</p>
                </div>
              </div>
            )
          }

          return null
        })()}

        <div className="admin-stats">
          <h2>📊 System Statistics</h2>
          <div className="stats-grid">
            <div className="stat-card">
              <h3>Total Users</h3>
              <p className="stat-number">{users.length}</p>
            </div>
            <div className="stat-card">
              <h3>Active Users</h3>
              <p className="stat-number">{users.filter(u => u.isActive).length}</p>
            </div>
            <div className="stat-card">
              <h3>Total Deaths</h3>
              <p className="stat-number">{stats.totalDeaths || 0}</p>
            </div>
            <div className="stat-card">
              <h3>Total Swears</h3>
              <p className="stat-number">{stats.totalSwears || 0}</p>
            </div>
            <div className="stat-card">
              <h3>Active Streams</h3>
              <p className="stat-number">{streams.filter(s => s.isStreaming).length}</p>
            </div>
            <div className="stat-card">
              <h3>Total Bits</h3>
              <p className="stat-number">{streams.reduce((sum, s) => sum + (s.sessionBits || 0), 0)}</p>
            </div>
          </div>
        </div>

        <div className="admin-roles">
          <h2>🛡️ Role Distribution</h2>
          <div className="stats-grid">
            {roles.map(role => {
              const roleCount = users.filter(u => u.role === role.id).length
              return (
                <div key={role.id} className="stat-card">
                  <h3>{role.icon} {role.name}</h3>
                  <p className="stat-number">{roleCount}</p>
                  <small className="role-description">{role.description}</small>
                </div>
              )
            })}
          </div>
        </div>

        <div className="admin-streams">
          <h2>🎮 Active Stream Sessions</h2>
          <div className="streams-grid">
            {streams.filter(s => s.isStreaming).map(stream => (
              <div key={stream.userId} className="stream-card active">
                <h3>🔴 {stream.displayName} (@{stream.username})</h3>
                <p><strong>Started:</strong> {new Date(stream.streamStartTime).toLocaleString()}</p>
                <div className="stream-stats">
                  <span className="stat">💎 {stream.sessionBits} bits</span>
                  <span className="stat">💀 {stream.sessionDeaths} deaths</span>
                  <span className="stat">🤬 {stream.sessionSwears} swears</span>
                </div>
                <div className="stream-settings">
                  <small>
                    <strong>Bit Thresholds:</strong>
                    Death: {stream.streamSettings?.bitThresholds?.death || 100} |
                    Swear: {stream.streamSettings?.bitThresholds?.swear || 50} |
                    Celebration: {stream.streamSettings?.bitThresholds?.celebration || 10}
                  </small>
                </div>
              </div>
            ))}
            {streams.filter(s => !s.isStreaming).slice(0, 3).map(stream => (
              <div key={stream.userId} className="stream-card offline">
                <h3>⚫ {stream.displayName} (@{stream.username})</h3>
                <p><strong>Status:</strong> Offline</p>
                <div className="stream-stats">
                  <span className="stat">💎 {stream.sessionBits} bits</span>
                  <span className="stat">💀 {stream.sessionDeaths} deaths</span>
                  <span className="stat">🤬 {stream.sessionSwears} swears</span>
                </div>
              </div>
            ))}
          </div>
          {streams.filter(s => s.isStreaming).length === 0 && (
            <div className="no-streams">
              <p>📴 No active stream sessions</p>
            </div>
          )}
        </div>

        <div className="admin-overlay-management">
          <h2>🎨 Overlay Management</h2>
          <div className="overlay-stats">
            <div className="overlay-stat-card">
              <h3>Users with Overlay</h3>
              <p className="stat-number">{users.filter(u => u.features?.streamOverlay).length}</p>
            </div>
            <div className="overlay-stat-card">
              <h3>Active Overlays</h3>
              <p className="stat-number">
                {users.filter(u => {
                  try {
                    const settings = typeof u.overlaySettings === 'string' ? JSON.parse(u.overlaySettings) : u.overlaySettings;
                    return u.features?.streamOverlay && settings?.enabled;
                  } catch {
                    return false;
                  }
                }).length}
              </p>
            </div>
            <div className="overlay-stat-card">
              <h3>Setup Instructions</h3>
              <select
                onChange={(e) => {
                  if (e.target.value) {
                    const baseUrl = window.location.origin;
                    window.open(`${baseUrl}/overlay/setup/${e.target.value}`, '_blank');
                  }
                }}
                className="user-select"
                defaultValue=""
              >
                <option value="">Select user for setup guide...</option>
                {(() => {
                  const currentRole = getCurrentUserRole();
                  const isAdmin = currentRole === 'admin';
                  const eligibleUsers = isAdmin ? users : users.filter(u => u.features?.streamOverlay);
                  return eligibleUsers.map(user => (
                    <option key={user.twitchUserId} value={user.twitchUserId}>
                      {user.displayName} (@{user.username})
                      {isAdmin && !user.features?.streamOverlay ? ' - No Overlay Permission' : ''}
                    </option>
                  ));
                })()}
              </select>
            </div>
          </div>

          <div className="overlay-users-grid">
            {users.map(user => {
              let overlaySettings;
              try {
                overlaySettings = typeof user.overlaySettings === 'string'
                  ? JSON.parse(user.overlaySettings)
                  : user.overlaySettings || {
                    enabled: false,
                    position: 'top-right',
                    counters: { deaths: true, swears: true, bits: false, channelPoints: false },
                    theme: {
                      backgroundColor: 'rgba(0,0,0,0.8)',
                      borderColor: '#9146ff',
                      textColor: '#ffffff',
                      accentColor: '#f0f0f0'
                    },
                    animations: {
                      enabled: true,
                      showAlerts: true,
                      celebrationEffects: true,
                      bounceOnUpdate: true,
                      fadeTransitions: true
                    },
                    display: {
                      showLabels: true,
                      showIcons: true,
                      compactMode: false,
                      hideWhenZero: false
                    }
                  };
              } catch {
                overlaySettings = {
                  enabled: false,
                  position: 'top-right',
                  counters: { deaths: true, swears: true, bits: false, channelPoints: false },
                  theme: {
                    backgroundColor: 'rgba(0,0,0,0.8)',
                    borderColor: '#9146ff',
                    textColor: '#ffffff',
                    accentColor: '#f0f0f0'
                  },
                  animations: {
                    enabled: true,
                    showAlerts: true,
                    celebrationEffects: true,
                    bounceOnUpdate: true,
                    fadeTransitions: true
                  },
                  display: {
                    showLabels: true,
                    showIcons: true,
                    compactMode: false,
                    hideWhenZero: false
                  }
                };
              }

              return (
                <div key={user.twitchUserId} className="overlay-user-card">
                  <div className="overlay-user-header">
                    <img src={user.profileImageUrl} alt="" className="user-avatar" />
                    <div className="user-info">
                      <h4>{user.displayName}</h4>
                      <p>@{user.username}</p>
                      <span className={`overlay-status-badge ${overlaySettings.enabled ? 'enabled' : 'disabled'}`}>
                        {overlaySettings.enabled ? '🟢 Active' : '🔴 Inactive'}
                      </span>
                    </div>
                    <div className="overlay-actions">
                      <button
                        onClick={() => {
                          const baseUrl = window.location.origin;
                          window.open(`${baseUrl}/overlay/setup/${user.twitchUserId}`, '_blank');
                        }}
                        className="btn btn-sm btn-secondary"
                        title="OBS Setup Instructions"
                      >
                        📺 Setup
                      </button>
                      <button
                        onClick={() => {
                          const baseUrl = window.location.origin;
                          window.open(`${baseUrl}/overlay/${user.twitchUserId}`, '_blank');
                        }}
                        className="btn btn-sm btn-primary"
                        title="Preview Overlay"
                      >
                        👁️ Preview
                      </button>
                    </div>
                  </div>

                  {(() => {
                    const currentRole = getCurrentUserRole();
                    const isAdmin = currentRole === 'admin';
                    const hasOverlayFeature = user.features?.streamOverlay;

                    // Overlay status display - automatic based on feature permission and stream activity
                    const currentUser = getCurrentUser();
                    const isOwnCard = currentUser?.twitchUserId === user.twitchUserId;

                    if (isAdmin || hasOverlayFeature || isOwnCard) {
                      const streamStatus = user.streamStatus || 'offline';
                      const isOverlayAvailable = hasOverlayFeature;
                      const isStreamActive = streamStatus === 'live' || streamStatus === 'prepping';
                      const isOverlayActive = isOverlayAvailable && streamStatus === 'live';

                      return (
                        <div className="overlay-feature-control">
                          <div className="feature-section">
                            <h5>� Stream Status</h5>
                            <div className="overlay-status-display">
                              <div className={`status-indicator ${isOverlayActive ? 'active' : 'inactive'}`}>
                                <span className="status-icon">
                                  {isOverlayActive ? '🟢' :
                                   isOverlayAvailable ? (isStreamActive ? '🟡' : '⚪') : '🔴'}
                                </span>
                                <span className="status-text">
                                  {!isOverlayAvailable ? (
                                    <>❌ No Overlay Permission
                                      {isAdmin && (
                                        <button
                                          className="grant-permission-btn"
                                          onClick={() => toggleFeature(user.twitchUserId, 'streamOverlay', false)}
                                        >
                                          Grant Permission
                                        </button>
                                      )}
                                    </>
                                  ) : isOverlayActive ? (
                                    '✅ Overlay Active (Stream Live)'
                                  ) : isStreamActive ? (
                                    '� Stream Active, Overlay Ready'
                                  ) : (
                                    '⚪ Overlay Available (Stream Offline)'
                                  )}
                                </span>
                              </div>
                              <div className="overlay-explanation">
                                <small>
                                  {isOverlayAvailable ?
                                    'Overlay automatically activates when stream goes live' :
                                    'Contact admin to enable overlay feature'
                                  }
                                </small>
                              </div>
                            </div>
                          </div>
                        </div>
                      );
                    }
                    return null;
                  })()}

                  {/* Phase 1: Stream Status Controls */}
                  {(() => {
                    const currentUser = getCurrentUser();
                    const isAdmin = getCurrentUserRole() === 'admin';
                    const isOwnCard = currentUser?.twitchUserId === user.twitchUserId;

                    if (isAdmin || isOwnCard) {
                      const streamStatus = user.streamStatus || 'offline';

                      return (
                        <div className="stream-status-control">
                          <div className="feature-section">
                            <h5>🎬 Stream Management</h5>
                            <p>Status: <strong>{streamStatus}</strong></p>
                            <div className="stream-controls">
                              {streamStatus === 'offline' && (
                                <button
                                  onClick={() => updateStreamStatus(user.twitchUserId, 'prep')}
                                  className="btn btn-warning btn-sm"
                                >
                                  🎭 Start Prepping
                                </button>
                              )}
                              {streamStatus === 'prepping' && (
                                <>
                                  <button
                                    onClick={() => updateStreamStatus(user.twitchUserId, 'go-live')}
                                    className="btn btn-success btn-sm"
                                  >
                                    🚀 Go Live
                                  </button>
                                  <button
                                    onClick={() => updateStreamStatus(user.twitchUserId, 'cancel-prep')}
                                    className="btn btn-secondary btn-sm"
                                  >
                                    ❌ Cancel
                                  </button>
                                </>
                              )}
                              {(streamStatus === 'live' || streamStatus === 'ending') && (
                                <button
                                  onClick={() => updateStreamStatus(user.twitchUserId, 'end-stream')}
                                  className="btn btn-danger btn-sm"
                                >
                                  🏁 End Stream
                                </button>
                              )}
                            </div>
                          </div>
                        </div>
                      );
                    }
                    return null;
                  })()}

                  {user.features?.streamOverlay && (
                    <div className="overlay-settings-panel">
                      <div className="settings-section">
                        <h5>🎯 Overlay Position</h5>
                      <div className="settings-row">
                        <select
                          value={overlaySettings.position}
                          onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                            ...overlaySettings,
                            position: e.target.value
                          })}
                          className="position-select"
                        >
                          <option value="top-left">↖️ Top Left</option>
                          <option value="top-right">↗️ Top Right</option>
                          <option value="bottom-left">↙️ Bottom Left</option>
                          <option value="bottom-right">↘️ Bottom Right</option>
                        </select>
                      </div>
                    </div>

                    <div className="settings-section">
                      <h5>📊 Counters</h5>
                      <div className="counter-grid">
                        {[
                          { key: 'deaths', label: '💀 Deaths', description: 'Track player deaths' },
                          { key: 'swears', label: '🤬 Swears', description: 'Count profanity' },
                          { key: 'bits', label: '💎 Bits', description: 'Show bit donations' },
                          { key: 'channelPoints', label: '⭐ Channel Points', description: 'Channel point redemptions' }
                        ].map(counter => (
                          <label key={counter.key} className="counter-checkbox">
                            <input
                              type="checkbox"
                              checked={overlaySettings.counters[counter.key] || false}
                              onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                ...overlaySettings,
                                counters: { ...overlaySettings.counters, [counter.key]: e.target.checked }
                              })}
                            />
                            <span className="counter-label">
                              <strong>{counter.label}</strong>
                              <small>{counter.description}</small>
                            </span>
                          </label>
                        ))}
                      </div>
                    </div>

                    <div className="settings-section">
                      <h5>✨ Animations & Effects</h5>
                      <div className="animation-grid">
                        {[
                          { key: 'enabled', label: 'Basic Animations', description: 'Smooth transitions' },
                          { key: 'showAlerts', label: 'Counter Alerts', description: 'Flash on updates' },
                          { key: 'celebrationEffects', label: 'Celebrations', description: 'Milestone effects' },
                          { key: 'bounceOnUpdate', label: 'Bounce Effect', description: 'Bounce when updated' },
                          { key: 'fadeTransitions', label: 'Fade Transitions', description: 'Smooth fade effects' }
                        ].map(animation => (
                          <label key={animation.key} className="animation-checkbox">
                            <input
                              type="checkbox"
                              checked={overlaySettings.animations[animation.key] || false}
                              onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                ...overlaySettings,
                                animations: { ...overlaySettings.animations, [animation.key]: e.target.checked }
                              })}
                            />
                            <span className="animation-label">
                              <strong>{animation.label}</strong>
                              <small>{animation.description}</small>
                            </span>
                          </label>
                        ))}
                      </div>
                    </div>

                    <div className="settings-section">
                      <h5>🎨 Theme & Display</h5>
                      <div className="theme-controls">
                        <div className="color-row">
                          <label>
                            <span>Background:</span>
                            <input
                              type="color"
                              value={overlaySettings.theme.borderColor || '#9146ff'}
                              onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                ...overlaySettings,
                                theme: { ...overlaySettings.theme, borderColor: e.target.value }
                              })}
                              className="color-input"
                            />
                          </label>
                          <label>
                            <span>Text Color:</span>
                            <input
                              type="color"
                              value={overlaySettings.theme.textColor || '#ffffff'}
                              onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                ...overlaySettings,
                                theme: { ...overlaySettings.theme, textColor: e.target.value }
                              })}
                              className="color-input"
                            />
                          </label>
                        </div>
                        <div className="display-options">
                          {[
                            { key: 'showLabels', label: 'Show Labels', description: 'Display counter names' },
                            { key: 'showIcons', label: 'Show Icons', description: 'Display emoji icons' },
                            { key: 'compactMode', label: 'Compact Mode', description: 'Smaller overlay size' },
                            { key: 'hideWhenZero', label: 'Hide Zero Values', description: 'Hide counters at 0' }
                          ].map(option => (
                            <label key={option.key} className="display-checkbox">
                              <input
                                type="checkbox"
                                checked={overlaySettings.display?.[option.key] || false}
                                onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                  ...overlaySettings,
                                  display: {
                                    ...overlaySettings.display,
                                    [option.key]: e.target.checked
                                  }
                                })}
                              />
                              <span className="display-label">
                                <strong>{option.label}</strong>
                                <small>{option.description}</small>
                              </span>
                            </label>
                          ))}
                        </div>
                      </div>
                    </div>
                  </div>
                  )}
                </div>
              );
            })}
          </div>

          {users.length === 0 && (
            <div className="no-overlay-users">
              <p>📺 No users found.</p>
              <p>Add users in the User Management section above.</p>
            </div>
          )}
        </div>

        <div className="admin-users">
          <div className="users-header">
            <h2>👥 User Management</h2>
            <button
              onClick={() => setShowAddUser(true)}
              className="btn btn-primary"
            >
              ➕ Add User
            </button>
          </div>

          {showAddUser && (
            <div className="add-user-form">
              <h3>Add New User</h3>
              <div className="form-grid">
                <input
                  type="text"
                  placeholder="Username (e.g., riress) *Required"
                  value={newUser.username || ''}
                  onChange={(e) => setNewUser({...newUser, username: e.target.value})}
                  required
                  style={{
                    color: 'white !important',
                    WebkitTextFillColor: 'white !important',
                    backgroundColor: 'rgba(255, 255, 255, 0.1) !important',
                    border: '2px solid rgba(139, 69, 19, 0.3) !important'
                  }}
                  className="force-white-text"
                />
                <input
                  type="text"
                  placeholder="Display Name (e.g., Riress)"
                  value={newUser.displayName || ''}
                  onChange={(e) => setNewUser({...newUser, displayName: e.target.value})}
                  style={{
                    color: 'white !important',
                    WebkitTextFillColor: 'white !important',
                    backgroundColor: 'rgba(255, 255, 255, 0.1) !important',
                    border: '2px solid rgba(139, 69, 19, 0.3) !important'
                  }}
                  className="force-white-text"
                />
                <input
                  type="email"
                  placeholder="Email (optional)"
                  value={newUser.email || ''}
                  onChange={(e) => setNewUser({...newUser, email: e.target.value})}
                  style={{
                    color: 'white !important',
                    WebkitTextFillColor: 'white !important',
                    backgroundColor: 'rgba(255, 255, 255, 0.1) !important',
                    border: '2px solid rgba(139, 69, 19, 0.3) !important'
                  }}
                  className="force-white-text"
                />
                <input
                  type="text"
                  placeholder="Twitch User ID *Required"
                  value={newUser.twitchUserId || ''}
                  onChange={(e) => setNewUser({...newUser, twitchUserId: e.target.value})}
                  required
                  style={{
                    color: 'white !important',
                    WebkitTextFillColor: 'white !important',
                    backgroundColor: 'rgba(255, 255, 255, 0.1) !important',
                    border: '2px solid rgba(139, 69, 19, 0.3) !important'
                  }}
                  className="force-white-text"
                />
              </div>
              <div className="form-actions">
                <button
                  onClick={addUser}
                  className="btn btn-success"
                  disabled={addingUser}
                >
                  {addingUser ? '🔄 Creating & Fetching Avatar...' : 'Create User'}
                </button>
                <button
                  onClick={() => setShowAddUser(false)}
                  className="btn btn-secondary"
                  disabled={addingUser}
                >
                  Cancel
                </button>
              </div>
              <p className="form-note">
                💡 System will automatically fetch Twitch avatar and profile data if the username exists on Twitch.
              </p>
            </div>
          )}

          <div className="users-table">
            {users.length === 0 ? (
              <div className="no-users" style={{ textAlign: 'center', padding: '40px', color: '#666' }}>
                <p>👤 No users found</p>
                <p>This could mean:</p>
                <ul style={{ textAlign: 'left', display: 'inline-block' }}>
                  <li>You're not authenticated as an admin</li>
                  <li>The admin API is not responding</li>
                  <li>No users have registered yet</li>
                </ul>
                <button onClick={fetchAdminData} className="btn btn-primary" style={{ marginTop: '10px' }}>
                  🔄 Retry
                </button>
              </div>
            ) : (
              users.map(user => {
                const userFeatures = typeof user.features === 'string'
                  ? JSON.parse(user.features)
                  : user.features || {}

                return (
                <div key={user.twitchUserId || user.userId || Math.random()} className="user-card">
                  <div className="user-info">
                    <img
                      src={user.profileImageUrl || `https://ui-avatars.com/api/?name=${encodeURIComponent(user.displayName || user.username || 'User')}&background=random`}
                      alt={user.displayName}
                      className="user-avatar"
                      onError={(e) => {
                        e.target.src = `https://ui-avatars.com/api/?name=${encodeURIComponent(user.displayName || user.username || 'User')}&background=random`;
                      }}
                    />
                    <div>
                      <h3>{user.displayName || 'Unknown User'}</h3>
                      <p>@{user.username || 'unknown'}</p>
                      <p className={`status stream-status-${user.streamStatus || 'offline'}`}>
                        {(() => {
                          const status = user.streamStatus || 'offline';
                          switch(status) {
                            case 'offline': return '⚪ Offline';
                            case 'prepping': return '🟡 Prepping';
                            case 'live': return '🟢 Live';
                            case 'ending': return '🔴 Ending';
                            default: return '❓ Unknown';
                          }
                        })()}
                      </p>
                    </div>
                  </div>

                  <div className="user-actions">
                    {(() => {
                      const currentUser = getCurrentUser()
                      const isAdmin = getCurrentUserRole() === 'admin'
                      const isOwnCard = currentUser?.twitchUserId === user.twitchUserId

                      if (isAdmin) {
                        // Admin sees admin controls
                        return (
                          <>
                            <button
                              onClick={() => toggleUserActive(user.twitchUserId, user.isActive)}
                              className={`btn ${user.isActive ? 'btn-danger' : 'btn-success'}`}
                            >
                              {user.isActive ? 'Deactivate' : 'Activate'}
                            </button>
                            {user.role !== 'admin' && (
                              <button
                                onClick={() => deleteUser(user.twitchUserId, user.username)}
                                className="btn btn-danger-outline"
                              >
                                🗑️ Delete
                              </button>
                            )}
                          </>
                        )
                      } else if (isOwnCard && !user.isActive) {
                        // Non-admin user sees self-activation button on their own inactive card
                        return (
                          <button
                            onClick={selfActivate}
                            className="btn btn-success self-activate-btn"
                            title="Activate your account before streaming"
                          >
                            🚀 Activate Myself
                          </button>
                        )
                      } else if (isOwnCard && user.isActive) {
                        // Non-admin user sees their active status
                        return (
                          <div className="self-status active-status">
                            <span className="status-badge active">✅ Active</span>
                            <small>Ready to stream!</small>
                          </div>
                        )
                      }

                      // Default: no actions for other users
                      return null
                    })()}
                  </div>

                  <div className="user-features">
                    <h4>Features</h4>
                    <div className="features-grid">
                      {Object.entries(userFeatures).map(([featureKey, enabled]) => {
                        const featureInfo = features.find(f => f.id === featureKey)
                        return (
                          <label key={featureKey} className="feature-toggle" title={featureInfo?.description}>
                            <input
                              type="checkbox"
                              checked={enabled}
                              onChange={() => toggleFeature(user.twitchUserId, featureKey, enabled)}
                            />
                            <span className="feature-name">
                              {featureInfo?.name || featureKey}
                            </span>
                            <span className={`feature-status ${enabled ? 'enabled' : 'disabled'}`}>
                              {enabled ? '✅' : '❌'}
                            </span>
                          </label>
                        )
                      })}
                    </div>
                  </div>

                  <div className="user-role-management">
                    <h4>Role & Permissions</h4>
                    <div className="role-selector">
                      <label>
                        <strong>Role:</strong>
                        <select
                          value={user.role || 'streamer'}
                          onChange={(e) => updateUserRole(user.twitchUserId || user.userId, e.target.value)}
                          disabled={!canManageRole(user.role) || !user.twitchUserId && !user.userId}
                          className="role-select"
                        >
                          {roles.map(role => (
                            <option key={role.id} value={role.id}>
                              {role.icon} {role.name}
                            </option>
                          ))}
                        </select>
                      </label>
                      <div className="role-info">
                        {roles.find(r => r.id === user.role) && (
                          <div className="role-badge" style={{ backgroundColor: roles.find(r => r.id === user.role).color }}>
                            {roles.find(r => r.id === user.role).icon} {roles.find(r => r.id === user.role).name}
                          </div>
                        )}
                        <p className="role-description">
                          {roles.find(r => r.id === user.role)?.description || 'No description available'}
                        </p>
                      </div>
                    </div>
                  </div>

                  {/* Overlay settings moved to dedicated Overlay Management section */}
                  {userFeatures.streamOverlay && (
                    <div className="overlay-feature-notice">
                      <p>🎨 <strong>Overlay settings</strong> are managed in the Overlay Management section above.</p>
                    </div>
                  )}

                  <div className="user-metadata">
                    <small>
                      <strong>Created:</strong> {new Date(user.createdAt).toLocaleDateString()} |
                      <strong>Last Login:</strong> {user.lastLogin ? new Date(user.lastLogin).toLocaleDateString() : 'Never'}
                    </small>
                  </div>
                </div>
              )
            })
          )}
          </div>
        </div>

        <div className="admin-rewards">
          <div className="rewards-header">
            <h2>🎯 Channel Points Rewards Management</h2>
            <button
              onClick={() => setShowRewardsManager(!showRewardsManager)}
              className="btn btn-primary"
            >
              {showRewardsManager ? '❌ Close Manager' : '⚙️ Manage Rewards'}
            </button>
          </div>

          {showRewardsManager && (
            <div className="rewards-manager">
              <div className="rewards-summary">
                <div className="stats-grid">
                  <div className="stat-card">
                    <h3>Total Users with Channel Points</h3>
                    <p className="stat-number">{rewards.filter(r => r.rewards.length > 0).length}</p>
                  </div>
                  <div className="stat-card">
                    <h3>Total Rewards</h3>
                    <p className="stat-number">{rewards.reduce((sum, r) => sum + r.rewards.length, 0)}</p>
                  </div>
                </div>
              </div>

              <div className="create-reward-form">
                <h3>Create New Reward</h3>
                <div className="form-grid">
                  <input
                    type="text"
                    placeholder="Reward Title *"
                    value={newReward.title}
                    onChange={(e) => setNewReward({...newReward, title: e.target.value})}
                    style={{
                      color: 'white !important',
                      WebkitTextFillColor: 'white !important',
                      backgroundColor: 'rgba(255, 255, 255, 0.1) !important',
                      border: '2px solid rgba(139, 69, 19, 0.3) !important'
                    }}
                  />
                  <input
                    type="number"
                    placeholder="Cost (Channel Points)"
                    value={newReward.cost}
                    min="1"
                    max="1000000"
                    onChange={(e) => setNewReward({...newReward, cost: parseInt(e.target.value)})}
                    style={{
                      color: 'white !important',
                      WebkitTextFillColor: 'white !important',
                      backgroundColor: 'rgba(255, 255, 255, 0.1) !important',
                      border: '2px solid rgba(139, 69, 19, 0.3) !important'
                    }}
                  />
                  <select
                    value={newReward.action}
                    onChange={(e) => setNewReward({...newReward, action: e.target.value})}
                    style={{
                      color: 'white !important',
                      WebkitTextFillColor: 'white !important',
                      backgroundColor: 'rgba(255, 255, 255, 0.1) !important',
                      border: '2px solid rgba(139, 69, 19, 0.3) !important'
                    }}
                  >
                    <option value="increment_deaths">💀 Add Death</option>
                    <option value="increment_swears">🤬 Add Swear</option>
                    <option value="decrement_deaths">💀 Remove Death</option>
                    <option value="decrement_swears">🤬 Remove Swear</option>
                  </select>
                  <input
                    type="text"
                    placeholder="Custom Prompt (optional)"
                    value={newReward.prompt}
                    onChange={(e) => setNewReward({...newReward, prompt: e.target.value})}
                    style={{
                      color: 'white !important',
                      WebkitTextFillColor: 'white !important',
                      backgroundColor: 'rgba(255, 255, 255, 0.1) !important',
                      border: '2px solid rgba(139, 69, 19, 0.3) !important'
                    }}
                  />
                  <input
                    type="color"
                    value={newReward.backgroundColor}
                    onChange={(e) => setNewReward({...newReward, backgroundColor: e.target.value})}
                    title="Background Color"
                  />
                </div>
              </div>

              <div className="users-rewards">
                <h3>Rewards by User</h3>
                {rewards.map(userRewards => (
                  <div key={userRewards.userId} className="user-rewards-section">
                    <div className="user-rewards-header">
                      <h4>
                        {userRewards.displayName} (@{userRewards.username})
                        <span className="rewards-count">
                          {userRewards.rewards.length} rewards
                        </span>
                      </h4>
                      <button
                        onClick={() => createReward(userRewards.userId)}
                        className="btn btn-primary btn-small"
                        disabled={!newReward.title || !newReward.cost}
                      >
                        ➕ Add Reward
                      </button>
                    </div>

                    {userRewards.rewards.length === 0 ? (
                      <p className="no-rewards">No channel point rewards configured</p>
                    ) : (
                      <div className="rewards-grid">
                        {userRewards.rewards.map(reward => (
                          <div key={reward.rewardId} className="reward-card">
                            <div className="reward-header">
                              <h5 style={{color: reward.backgroundColor || '#9147FF'}}>
                                {reward.rewardTitle}
                              </h5>
                              <span className="reward-cost">{reward.cost} points</span>
                            </div>
                            <div className="reward-details">
                              <p><strong>Action:</strong> {reward.action.replace('_', ' ')}</p>
                              <p><strong>Created:</strong> {new Date(reward.createdAt).toLocaleDateString()}</p>
                              <p><strong>Status:</strong>
                                <span className={`status ${reward.isEnabled ? 'enabled' : 'disabled'}`}>
                                  {reward.isEnabled ? '✅ Active' : '❌ Disabled'}
                                </span>
                              </p>
                            </div>
                            <div className="reward-actions">
                              <button
                                onClick={() => deleteReward(userRewards.userId, reward.rewardId, reward.rewardTitle)}
                                className="btn btn-danger btn-small"
                              >
                                🗑️ Delete
                              </button>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>

        <div className="admin-alerts">
          <div className="section-header">
            <h2>🚨 Alert Management</h2>
            <div className="section-actions">
              <button
                onClick={() => setShowAlertsManager(!showAlertsManager)}
                className="btn btn-secondary"
              >
                {showAlertsManager ? '🔼 Hide Alerts' : '🔽 Show Alerts'} ({alerts.reduce((sum, user) => sum + user.alerts.length, 0)} total)
              </button>
            </div>
          </div>

          <div className="alerts-stats">
            <div className="stat-card">
              <h3>Users with Alerts</h3>
              <p className="stat-number">{alerts.length}</p>
            </div>
            <div className="stat-card">
              <h3>Total Alerts</h3>
              <p className="stat-number">{alerts.reduce((sum, user) => sum + user.alerts.length, 0)}</p>
            </div>
            <div className="stat-card">
              <h3>Active Alerts</h3>
              <p className="stat-number">{alerts.reduce((sum, user) => sum + user.alerts.filter(a => a.isEnabled).length, 0)}</p>
            </div>
            <div className="stat-card">
              <h3>Default Templates</h3>
              <p className="stat-number">{alertTemplates.length}</p>
            </div>
          </div>

          {showAlertsManager && (
            <div className="alerts-manager">
              <div className="create-alert-form">
                <h3>Create Custom Alert</h3>
                <div className="alert-form-grid">
                  <div className="form-group">
                    <label>Alert Type</label>
                    <select
                      value={newAlert.type}
                      onChange={(e) => setNewAlert({...newAlert, type: e.target.value})}
                    >
                      <option value="custom">Custom Event</option>
                      <option value="follow">Follow Override</option>
                      <option value="subscription">Subscription Override</option>
                      <option value="resub">Resub Override</option>
                      <option value="bits">Bits Override</option>
                      <option value="raid">Raid Override</option>
                      <option value="giftsub">Gift Sub Override</option>
                    </select>
                  </div>

                  <div className="form-group">
                    <label>Alert Name</label>
                    <input
                      type="text"
                      placeholder="e.g., Custom Follow Alert"
                      value={newAlert.name}
                      onChange={(e) => setNewAlert({...newAlert, name: e.target.value})}
                    />
                  </div>

                  <div className="form-group full-width">
                    <label>Text Prompt</label>
                    <input
                      type="text"
                      placeholder="Use [User] for username, [X] for values"
                      value={newAlert.textPrompt}
                      onChange={(e) => setNewAlert({...newAlert, textPrompt: e.target.value})}
                    />
                  </div>

                  <div className="form-group full-width">
                    <label>Visual Cue (Optional)</label>
                    <input
                      type="text"
                      placeholder="Describe the visual effect"
                      value={newAlert.visualCue}
                      onChange={(e) => setNewAlert({...newAlert, visualCue: e.target.value})}
                    />
                  </div>

                  <div className="form-group">
                    <label>Sound Effect</label>
                    <input
                      type="text"
                      placeholder="Sound file name or ID"
                      value={newAlert.sound}
                      onChange={(e) => setNewAlert({...newAlert, sound: e.target.value})}
                    />
                  </div>

                  <div className="form-group">
                    <label>Sound Description</label>
                    <input
                      type="text"
                      placeholder="Describe the sound"
                      value={newAlert.soundDescription}
                      onChange={(e) => setNewAlert({...newAlert, soundDescription: e.target.value})}
                    />
                  </div>

                  <div className="form-group">
                    <label>Duration (ms)</label>
                    <input
                      type="number"
                      min="1000"
                      max="30000"
                      value={newAlert.duration}
                      onChange={(e) => setNewAlert({...newAlert, duration: parseInt(e.target.value)})}
                    />
                  </div>

                  <div className="form-group">
                    <label>Background Color</label>
                    <input
                      type="color"
                      value={newAlert.backgroundColor}
                      onChange={(e) => setNewAlert({...newAlert, backgroundColor: e.target.value})}
                    />
                  </div>

                  <div className="form-group">
                    <label>Text Color</label>
                    <input
                      type="color"
                      value={newAlert.textColor}
                      onChange={(e) => setNewAlert({...newAlert, textColor: e.target.value})}
                    />
                  </div>

                  <div className="form-group">
                    <label>Border Color</label>
                    <input
                      type="color"
                      value={newAlert.borderColor}
                      onChange={(e) => setNewAlert({...newAlert, borderColor: e.target.value})}
                    />
                  </div>
                </div>

                <div className="alert-preview">
                  <h4>Preview</h4>
                  <div
                    className="alert-preview-box"
                    style={{
                      backgroundColor: newAlert.backgroundColor,
                      color: newAlert.textColor,
                      border: `3px solid ${newAlert.borderColor}`,
                      padding: '15px',
                      borderRadius: '8px',
                      textAlign: 'center',
                      fontFamily: 'monospace'
                    }}
                  >
                    {newAlert.visualCue && (
                      <div style={{ fontSize: '12px', opacity: 0.8, fontStyle: 'italic', marginBottom: '8px' }}>
                        {newAlert.visualCue}
                      </div>
                    )}
                    <div style={{ fontSize: '16px', fontWeight: 'bold' }}>
                      {newAlert.textPrompt || 'Enter text prompt...'}
                    </div>
                    {newAlert.soundDescription && (
                      <div style={{ fontSize: '10px', opacity: 0.6, fontStyle: 'italic', marginTop: '8px' }}>
                        ♪ {newAlert.soundDescription}
                      </div>
                    )}
                  </div>
                </div>
              </div>

              <div className="users-alerts">
                <h3>Alerts by User</h3>
                {alerts.map(userAlerts => (
                  <div key={userAlerts.userId} className="user-alerts-section">
                    <div className="user-alerts-header">
                      <h4>
                        {userAlerts.displayName} (@{userAlerts.username})
                        <span className="alerts-count">
                          {userAlerts.alerts.length} alerts ({userAlerts.alerts.filter(a => a.isEnabled).length} active)
                        </span>
                      </h4>
                      <button
                        onClick={() => createAlert(userAlerts.userId)}
                        className="btn btn-primary btn-small"
                        disabled={!newAlert.name || !newAlert.textPrompt}
                      >
                        ➕ Add Alert
                      </button>
                    </div>

                    {userAlerts.alerts.length === 0 ? (
                      <p className="no-alerts">No custom alerts configured</p>
                    ) : (
                      <div className="alerts-grid">
                        {userAlerts.alerts.map(alert => (
                          <div key={alert.id} className={`alert-card ${alert.isDefault ? 'default-alert' : 'custom-alert'} ${alert.isEnabled ? 'enabled' : 'disabled'}`}>
                            <div className="alert-header">
                              <div className="alert-info">
                                <h5>
                                  {alert.isDefault ? '🔒' : '⚙️'} {alert.name}
                                  <span className="alert-type-badge">{alert.type}</span>
                                </h5>
                                <p className="alert-prompt">"{alert.textPrompt}"</p>
                              </div>
                              <div className="alert-status">
                                <label className="toggle-switch">
                                  <input
                                    type="checkbox"
                                    checked={alert.isEnabled}
                                    onChange={() => updateAlertStatus(userAlerts.userId, alert.id, alert.isEnabled)}
                                  />
                                  <span className="toggle-slider"></span>
                                </label>
                              </div>
                            </div>

                            <div className="alert-details">
                              {alert.visualCue && (
                                <div className="alert-detail">
                                  <strong>Visual:</strong> {alert.visualCue}
                                </div>
                              )}
                              {alert.soundDescription && (
                                <div className="alert-detail">
                                  <strong>Sound:</strong> {alert.soundDescription}
                                </div>
                              )}
                              <div className="alert-detail">
                                <strong>Duration:</strong> {alert.duration}ms
                              </div>
                              <div className="alert-colors">
                                <span
                                  className="color-preview"
                                  style={{ backgroundColor: alert.backgroundColor }}
                                  title="Background"
                                ></span>
                                <span
                                  className="color-preview"
                                  style={{ backgroundColor: alert.textColor }}
                                  title="Text"
                                ></span>
                                <span
                                  className="color-preview"
                                  style={{ backgroundColor: alert.borderColor }}
                                  title="Border"
                                ></span>
                              </div>
                            </div>

                            {!alert.isDefault && (
                              <div className="alert-actions">
                                <button
                                  onClick={() => deleteAlert(userAlerts.userId, alert.id, alert.name)}
                                  className="btn btn-danger btn-small"
                                >
                                  🗑️ Delete
                                </button>
                              </div>
                            )}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>

              <div className="alert-templates">
                <h3>Default Alert Templates</h3>
                <div className="templates-grid">
                  {alertTemplates.map(template => (
                    <div key={template.id} className="template-card">
                      <div className="template-header">
                        <h5>{template.name}</h5>
                        <span className="template-type">{template.type}</span>
                      </div>
                      <div className="template-content">
                        <p className="template-prompt">"{template.textPrompt}"</p>
                        <div className="template-details">
                          <div><strong>Visual:</strong> {template.visualCue}</div>
                          <div><strong>Sound:</strong> {template.soundDescription}</div>
                        </div>
                      </div>
                      <div
                        className="template-preview"
                        style={{
                          backgroundColor: template.backgroundColor,
                          color: template.textColor,
                          border: `2px solid ${template.borderColor}`,
                          padding: '10px',
                          borderRadius: '4px',
                          fontSize: '12px',
                          textAlign: 'center'
                        }}
                      >
                        Preview Style
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

export default AdminDashboard

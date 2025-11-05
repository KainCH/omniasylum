import { useState, useEffect } from 'react'

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
  const [newUser, setNewUser] = useState({
    username: '',
    displayName: '',
    email: '',
    twitchUserId: ''
  })
  const [currentUserRole, setCurrentUserRole] = useState('streamer')

  useEffect(() => {
    fetchAdminData()
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
      const response = await fetch(`/api/admin/users/${userId}/overlay-settings`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        body: JSON.stringify(settings)
      })

      if (response.ok) {
        fetchAdminData() // Refresh data
        console.log('✅ Overlay settings updated successfully')
      } else {
        const errorData = await response.json()
        console.error('❌ Failed to update overlay settings:', errorData.error)
        alert(errorData.error)
      }
    } catch (error) {
      console.error('❌ Failed to update overlay settings:', error)
      alert('Failed to update overlay settings')
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

  // Check if current user can perform action based on role hierarchy
  const canManageRole = (targetRole) => {
    const currentRole = getCurrentUserRole()
    const roleHierarchy = { 'streamer': 0, 'moderator': 1, 'manager': 2, 'admin': 3 }
    const currentLevel = roleHierarchy[currentRole] || 0
    const targetLevel = roleHierarchy[targetRole] || 0
    return currentLevel >= 3 || currentLevel > targetLevel // Only admin can assign same/higher roles
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
                      <p className={`status ${user.isActive ? 'active' : 'inactive'}`}>
                        {user.isActive ? '✅ Active' : '❌ Inactive'}
                      </p>
                    </div>
                  </div>

                  <div className="user-actions">
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

                  {/* Overlay Settings Management */}
                  {userFeatures.streamOverlay && (
                    <div className="user-overlay-settings">
                      <h4>Stream Overlay Settings</h4>
                      {(() => {
                        let overlaySettings;
                        try {
                          overlaySettings = typeof user.overlaySettings === 'string'
                            ? JSON.parse(user.overlaySettings)
                            : user.overlaySettings || {
                              enabled: false,
                              position: 'top-right',
                              counters: { deaths: true, swears: true, bits: false },
                              theme: { backgroundColor: 'rgba(0,0,0,0.7)', borderColor: '#d4af37', textColor: 'white' },
                              animations: { enabled: true, showAlerts: true, celebrationEffects: true }
                            };
                        } catch {
                          overlaySettings = {
                            enabled: false,
                            position: 'top-right',
                            counters: { deaths: true, swears: true, bits: false },
                            theme: { backgroundColor: 'rgba(0,0,0,0.7)', borderColor: '#d4af37', textColor: 'white' },
                            animations: { enabled: true, showAlerts: true, celebrationEffects: true }
                          };
                        }
                        return (
                          <div className="overlay-settings-grid">
                            <div className="overlay-setting-group">
                              <label className="overlay-toggle">
                                <input
                                  type="checkbox"
                                  checked={overlaySettings.enabled}
                                  onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                    ...overlaySettings,
                                    enabled: e.target.checked
                                  })}
                                />
                                <span className="overlay-setting-name">Overlay Enabled</span>
                                <span className={`overlay-status ${overlaySettings.enabled ? 'enabled' : 'disabled'}`}>
                                  {overlaySettings.enabled ? '✅' : '❌'}
                                </span>
                              </label>
                            </div>

                            <div className="overlay-setting-group">
                              <label>
                                <strong>Position:</strong>
                                <select
                                  value={overlaySettings.position}
                                  onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                    ...overlaySettings,
                                    position: e.target.value
                                  })}
                                  className="overlay-position-select"
                                >
                                  <option value="top-left">Top Left</option>
                                  <option value="top-right">Top Right</option>
                                  <option value="bottom-left">Bottom Left</option>
                                  <option value="bottom-right">Bottom Right</option>
                                </select>
                              </label>
                            </div>

                            <div className="overlay-setting-group">
                              <strong>Visible Counters:</strong>
                              <div className="counter-toggles">
                                <label className="counter-toggle">
                                  <input
                                    type="checkbox"
                                    checked={overlaySettings.counters.deaths}
                                    onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                      ...overlaySettings,
                                      counters: { ...overlaySettings.counters, deaths: e.target.checked }
                                    })}
                                  />
                                  <span>💀 Deaths</span>
                                </label>
                                <label className="counter-toggle">
                                  <input
                                    type="checkbox"
                                    checked={overlaySettings.counters.swears}
                                    onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                      ...overlaySettings,
                                      counters: { ...overlaySettings.counters, swears: e.target.checked }
                                    })}
                                  />
                                  <span>🤬 Swears</span>
                                </label>
                                <label className="counter-toggle">
                                  <input
                                    type="checkbox"
                                    checked={overlaySettings.counters.bits}
                                    onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                      ...overlaySettings,
                                      counters: { ...overlaySettings.counters, bits: e.target.checked }
                                    })}
                                  />
                                  <span>💎 Bits</span>
                                </label>
                              </div>
                            </div>

                            <div className="overlay-setting-group">
                              <strong>Animation Settings:</strong>
                              <div className="animation-toggles">
                                <label className="animation-toggle">
                                  <input
                                    type="checkbox"
                                    checked={overlaySettings.animations.enabled}
                                    onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                      ...overlaySettings,
                                      animations: { ...overlaySettings.animations, enabled: e.target.checked }
                                    })}
                                  />
                                  <span>Animations</span>
                                </label>
                                <label className="animation-toggle">
                                  <input
                                    type="checkbox"
                                    checked={overlaySettings.animations.showAlerts}
                                    onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                      ...overlaySettings,
                                      animations: { ...overlaySettings.animations, showAlerts: e.target.checked }
                                    })}
                                  />
                                  <span>Counter Alerts</span>
                                </label>
                                <label className="animation-toggle">
                                  <input
                                    type="checkbox"
                                    checked={overlaySettings.animations.celebrationEffects}
                                    onChange={(e) => updateUserOverlaySettings(user.twitchUserId, {
                                      ...overlaySettings,
                                      animations: { ...overlaySettings.animations, celebrationEffects: e.target.checked }
                                    })}
                                  />
                                  <span>Celebration Effects</span>
                                </label>
                              </div>
                            </div>

                            {overlaySettings.enabled && (
                              <div className="overlay-preview-link">
                                <a
                                  href={`/overlay/${user.twitchUserId}`}
                                  target="_blank"
                                  rel="noopener noreferrer"
                                  className="preview-button"
                                >
                                  🔗 Open Overlay Preview
                                </a>
                              </div>
                            )}
                          </div>
                        );
                      })()}
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
      </div>
    </div>
  )
}

export default AdminDashboard

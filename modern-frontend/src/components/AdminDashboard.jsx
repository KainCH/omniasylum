import { useState, useEffect } from 'react'

function AdminDashboard() {
  const [users, setUsers] = useState([])
  const [stats, setStats] = useState({})
  const [features, setFeatures] = useState([])
  const [loading, setLoading] = useState(true)
  const [showAddUser, setShowAddUser] = useState(false)
  const [newUser, setNewUser] = useState({
    username: '',
    displayName: '',
    email: '',
    twitchUserId: ''
  })

  useEffect(() => {
    fetchAdminData()
  }, [])

  const fetchAdminData = async () => {
    try {
      // Fetch users
      const usersResponse = await fetch('/api/admin/users', {
        credentials: 'include'
      })
      if (usersResponse.ok) {
        const usersData = await usersResponse.json()
        setUsers(usersData.users || usersData) // Handle both response formats
      }

      // Fetch system stats
      const statsResponse = await fetch('/api/admin/stats', {
        credentials: 'include'
      })
      if (statsResponse.ok) {
        const statsData = await statsResponse.json()
        setStats(statsData)
      }

      // Fetch available features
      const featuresResponse = await fetch('/api/admin/features', {
        credentials: 'include'
      })
      if (featuresResponse.ok) {
        const featuresData = await featuresResponse.json()
        setFeatures(featuresData.features)
      }
    } catch (error) {
      console.error('❌ Failed to fetch admin data:', error)
    } finally {
      setLoading(false)
    }
  }

  const toggleUserActive = async (userId, isActive) => {
    try {
      const response = await fetch(`/api/admin/users/${userId}/status`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json'
        },
        credentials: 'include',
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
        headers: {
          'Content-Type': 'application/json'
        },
        credentials: 'include',
        body: JSON.stringify({ features: updatedFeatures })
      })

      if (response.ok) {
        fetchAdminData() // Refresh data
      }
    } catch (error) {
      console.error('❌ Failed to update feature:', error)
    }
  }

  const addUser = async () => {
    try {
      const response = await fetch('/api/admin/users', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        credentials: 'include',
        body: JSON.stringify(newUser)
      })

      if (response.ok) {
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
    }
  }

  const deleteUser = async (userId, username) => {
    if (!confirm(`Are you sure you want to delete user ${username}? This cannot be undone.`)) {
      return
    }

    try {
      const response = await fetch(`/api/admin/users/${userId}`, {
        method: 'DELETE',
        credentials: 'include'
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
          <h1>🔧 Admin Dashboard - OmniForgeStream</h1>
          <p>Welcome, Administrator! Manage users and system settings.</p>
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
          </div>
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
                  placeholder="Username (e.g., riress)"
                  value={newUser.username}
                  onChange={(e) => setNewUser({...newUser, username: e.target.value})}
                />
                <input
                  type="text"
                  placeholder="Display Name (e.g., Riress)"
                  value={newUser.displayName}
                  onChange={(e) => setNewUser({...newUser, displayName: e.target.value})}
                />
                <input
                  type="email"
                  placeholder="Email (optional)"
                  value={newUser.email}
                  onChange={(e) => setNewUser({...newUser, email: e.target.value})}
                />
                <input
                  type="text"
                  placeholder="Twitch User ID"
                  value={newUser.twitchUserId}
                  onChange={(e) => setNewUser({...newUser, twitchUserId: e.target.value})}
                />
              </div>
              <div className="form-actions">
                <button onClick={addUser} className="btn btn-success">Create User</button>
                <button onClick={() => setShowAddUser(false)} className="btn btn-secondary">Cancel</button>
              </div>
              <p className="form-note">
                ⚠️ Note: User will need to authenticate via OAuth to get Twitch tokens for bot functionality.
              </p>
            </div>
          )}

          <div className="users-table">
            {users.map(user => {
              const features = typeof user.features === 'string'
                ? JSON.parse(user.features)
                : user.features || {}

              return (
                <div key={user.twitchUserId} className="user-card">
                  <div className="user-info">
                    <img
                      src={user.profileImageUrl || '/default-avatar.png'}
                      alt={user.displayName}
                      className="user-avatar"
                    />
                    <div>
                      <h3>{user.displayName}</h3>
                      <p>@{user.username}</p>
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
                      {Object.entries(features).map(([featureKey, enabled]) => {
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

                  <div className="user-metadata">
                    <small>
                      <strong>Role:</strong> {user.role} |
                      <strong>Created:</strong> {new Date(user.createdAt).toLocaleDateString()} |
                      <strong>Last Login:</strong> {user.lastLogin ? new Date(user.lastLogin).toLocaleDateString() : 'Never'}
                    </small>
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      </div>
    </div>
  )
}

export default AdminDashboard

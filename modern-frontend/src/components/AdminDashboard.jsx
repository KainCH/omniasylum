import { useState, useEffect } from 'react'

function AdminDashboard() {
  const [users, setUsers] = useState([])
  const [stats, setStats] = useState({})
  const [loading, setLoading] = useState(true)

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
        setUsers(usersData)
      }

      // Fetch system stats
      const statsResponse = await fetch('/api/admin/stats', {
        credentials: 'include'
      })
      if (statsResponse.ok) {
        const statsData = await statsResponse.json()
        setStats(statsData)
      }
    } catch (error) {
      console.error('❌ Failed to fetch admin data:', error)
    } finally {
      setLoading(false)
    }
  }

  const toggleUserActive = async (userId, isActive) => {
    try {
      const response = await fetch(`/api/admin/users/${userId}`, {
        method: 'PATCH',
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
      const response = await fetch(`/api/admin/users/${userId}/features`, {
        method: 'PATCH',
        headers: {
          'Content-Type': 'application/json'
        },
        credentials: 'include',
        body: JSON.stringify({ [feature]: !enabled })
      })

      if (response.ok) {
        fetchAdminData() // Refresh data
      }
    } catch (error) {
      console.error('❌ Failed to update feature:', error)
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
          <h2>👥 User Management</h2>
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
                  </div>

                  <div className="user-features">
                    <h4>Features</h4>
                    {Object.entries(features).map(([feature, enabled]) => (
                      <label key={feature} className="feature-toggle">
                        <input
                          type="checkbox"
                          checked={enabled}
                          onChange={() => toggleFeature(user.twitchUserId, feature, enabled)}
                        />
                        <span>{feature}</span>
                      </label>
                    ))}
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

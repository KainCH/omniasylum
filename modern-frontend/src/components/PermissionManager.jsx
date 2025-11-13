import { useState, useEffect } from 'react'
import { ActionButton, FormSection, StatusBadge, InputGroup } from './ui/CommonControls'
import { useLoading, useToast } from '../hooks'
import { userAPI, APIError } from '../utils/apiHelpers'
import './PermissionManager.css'

function PermissionManager({ userRole }) {
  const [users, setUsers] = useState([])
  const [managedUsers, setManagedUsers] = useState([])
  const [currentUser, setCurrentUser] = useState(null)
  const [selectedManager, setSelectedManager] = useState('')
  const [selectedBroadcaster, setSelectedBroadcaster] = useState('')
  const { loading, withLoading } = useLoading()
  const { addToast } = useToast()

  // Load data on component mount
  useEffect(() => {
    loadData()
  }, [])

  const loadData = async () => {
    try {
      await withLoading(async () => {
        // Get managed users (works for both super_admin and managers)
        const managedResponse = await fetch('/api/admin/permissions/managed-users', {
          credentials: 'include'
        })
        
        if (!managedResponse.ok) {
          throw new Error('Failed to load managed users')
        }
        
        const managedData = await managedResponse.json()
        setCurrentUser(managedData.currentUser)
        setManagedUsers(managedData.managedUsers)

        // If super admin, also get all users for permission management
        if (userRole === 'super_admin') {
          const allUsersResponse = await fetch('/api/admin/permissions/all-users-roles', {
            credentials: 'include'
          })
          
          if (allUsersResponse.ok) {
            const allUsersData = await allUsersResponse.json()
            setUsers(allUsersData.users)
          }
        }
      })
    } catch (error) {
      console.error('Error loading permission data:', error)
      addToast('Failed to load permission data', 'error')
    }
  }

  const grantManagerPermissions = async () => {
    if (!selectedManager || !selectedBroadcaster) {
      addToast('Please select both a manager and a broadcaster', 'error')
      return
    }

    try {
      await withLoading(async () => {
        const response = await fetch('/api/admin/permissions/grant-manager', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({
            managerUserId: selectedManager,
            broadcasterUserId: selectedBroadcaster
          })
        })

        if (!response.ok) {
          const error = await response.json()
          throw new Error(error.details || 'Failed to grant permissions')
        }

        const result = await response.json()
        addToast(`Manager permissions granted successfully to ${result.manager.displayName}`, 'success')
        
        // Reset form and reload data
        setSelectedManager('')
        setSelectedBroadcaster('')
        await loadData()
      })
    } catch (error) {
      console.error('Error granting manager permissions:', error)
      addToast(error.message, 'error')
    }
  }

  const revokeManagerPermissions = async (managerUserId, broadcasterUserId) => {
    try {
      await withLoading(async () => {
        const response = await fetch('/api/admin/permissions/revoke-manager', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({
            managerUserId,
            broadcasterUserId
          })
        })

        if (!response.ok) {
          const error = await response.json()
          throw new Error(error.details || 'Failed to revoke permissions')
        }

        const result = await response.json()
        addToast(`Manager permissions revoked successfully`, 'success')
        await loadData()
      })
    } catch (error) {
      console.error('Error revoking manager permissions:', error)
      addToast(error.message, 'error')
    }
  }

  const getRoleDisplayName = (role) => {
    const roleNames = {
      'super_admin': 'Super Admin',
      'manager': 'Manager',
      'streamer': 'Streamer'
    }
    return roleNames[role] || role
  }

  const getRoleBadgeVariant = (role) => {
    const variants = {
      'super_admin': 'error',
      'manager': 'warning',
      'streamer': 'info'
    }
    return variants[role] || 'default'
  }

  // Filter users for dropdowns
  const potentialManagers = users.filter(user => 
    user.role === 'streamer' || user.role === 'manager'
  )
  const potentialBroadcasters = users.filter(user => 
    user.role === 'streamer'
  )

  return (
    <div className="permission-manager">
      <div className="permission-header">
        <h2>🔑 Permission Management</h2>
        <StatusBadge variant={getRoleBadgeVariant(userRole)}>
          {getRoleDisplayName(userRole)}
        </StatusBadge>
      </div>

      {/* Current User Info */}
      {currentUser && (
        <FormSection title="👤 Your Account" collapsible={true}>
          <div className="user-info-card">
            <div className="user-details">
              <h4>{currentUser.displayName}</h4>
              <p>@{currentUser.username}</p>
              <StatusBadge variant={getRoleBadgeVariant(currentUser.role)}>
                {getRoleDisplayName(currentUser.role)}
              </StatusBadge>
            </div>
            {currentUser.managedStreamers?.length > 0 && (
              <div className="managed-count">
                <span className="count-badge">
                  Managing {currentUser.managedStreamers.length} streamer{currentUser.managedStreamers.length !== 1 ? 's' : ''}
                </span>
              </div>
            )}
          </div>
        </FormSection>
      )}

      {/* Grant Manager Permissions (Super Admin Only) */}
      {userRole === 'super_admin' && (
        <FormSection title="➕ Grant Manager Permissions">
          <div className="grant-permissions-form">
            <InputGroup label="Select Manager User">
              <select
                value={selectedManager}
                onChange={(e) => setSelectedManager(e.target.value)}
                disabled={loading}
              >
                <option value="">Choose a user to make manager...</option>
                {potentialManagers.map(user => (
                  <option key={user.userId} value={user.userId}>
                    {user.displayName} (@{user.username}) - {getRoleDisplayName(user.role)}
                  </option>
                ))}
              </select>
            </InputGroup>

            <InputGroup label="Select Broadcaster to Manage">
              <select
                value={selectedBroadcaster}
                onChange={(e) => setSelectedBroadcaster(e.target.value)}
                disabled={loading}
              >
                <option value="">Choose a broadcaster to manage...</option>
                {potentialBroadcasters.map(user => (
                  <option key={user.userId} value={user.userId}>
                    {user.displayName} (@{user.username})
                  </option>
                ))}
              </select>
            </InputGroup>

            <ActionButton
              variant="primary"
              onClick={grantManagerPermissions}
              loading={loading}
              disabled={!selectedManager || !selectedBroadcaster}
            >
              🔑 Grant Manager Permissions
            </ActionButton>
          </div>
        </FormSection>
      )}

      {/* Managed Users */}
      <FormSection title={`👥 ${userRole === 'super_admin' ? 'All Users & Permissions' : 'Users You Can Manage'}`}>
        <div className="managed-users-grid">
          {managedUsers.length === 0 ? (
            <div className="no-users">
              <p>No users found.</p>
              {userRole === 'manager' && (
                <p><small>Contact your super admin to get manager permissions for streamers.</small></p>
              )}
            </div>
          ) : (
            managedUsers.map(user => (
              <div key={user.userId} className="user-management-card">
                <div className="user-card-header">
                  <div className="user-info">
                    <h4>{user.displayName}</h4>
                    <p>@{user.username}</p>
                    <StatusBadge variant={getRoleBadgeVariant(user.role)}>
                      {getRoleDisplayName(user.role)}
                    </StatusBadge>
                  </div>
                  <StatusBadge variant={user.isActive ? 'success' : 'warning'}>
                    {user.isActive ? 'Active' : 'Inactive'}
                  </StatusBadge>
                </div>

                {/* Manager Info */}
                {user.role === 'manager' && user.managedStreamers?.length > 0 && (
                  <div className="manager-info">
                    <p><strong>Managing {user.managedStreamers.length} streamer(s):</strong></p>
                    <div className="managed-streamers">
                      {user.managedStreamers.map(streamerId => {
                        const streamer = users.find(u => u.userId === streamerId) || 
                                       managedUsers.find(u => u.userId === streamerId)
                        return (
                          <div key={streamerId} className="managed-streamer">
                            <span>{streamer ? `${streamer.displayName} (@${streamer.username})` : streamerId}</span>
                            {userRole === 'super_admin' && (
                              <ActionButton
                                variant="danger"
                                size="small"
                                onClick={() => revokeManagerPermissions(user.userId, streamerId)}
                                loading={loading}
                              >
                                ❌ Revoke
                              </ActionButton>
                            )}
                          </div>
                        )
                      })}
                    </div>
                  </div>
                )}

                {user.lastLogin && (
                  <div className="user-meta">
                    <small>Last login: {new Date(user.lastLogin).toLocaleDateString()}</small>
                  </div>
                )}
              </div>
            ))
          )}
        </div>
      </FormSection>

      {/* Refresh Button */}
      <div className="permission-actions">
        <ActionButton
          variant="secondary"
          onClick={loadData}
          loading={loading}
        >
          🔄 Refresh Data
        </ActionButton>
      </div>
    </div>
  )
}

export default PermissionManager
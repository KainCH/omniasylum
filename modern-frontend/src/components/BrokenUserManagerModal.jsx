import React, { useState, useEffect } from 'react';
import './AdminDashboard.css';

const BrokenUserManagerModal = ({ isOpen, onClose }) => {
  const [diagnostics, setDiagnostics] = useState(null);
  const [loading, setLoading] = useState(false);
  const [deleting, setDeleting] = useState(new Set());

  useEffect(() => {
    if (isOpen) {
      fetchDiagnostics();
    }
  }, [isOpen]);

  const fetchDiagnostics = async () => {
    setLoading(true);
    try {
      const response = await fetch('/api/admin/users/diagnostics', {
        credentials: 'include'
      });

      if (response.ok) {
        const data = await response.json();
        setDiagnostics(data);
      } else {
        console.error('Failed to fetch user diagnostics');
      }
    } catch (error) {
      console.error('Error fetching user diagnostics:', error);
    } finally {
      setLoading(false);
    }
  };

  const deleteBrokenUser = async (brokenUser) => {
    const { partitionKey, rowKey, tempDeleteId } = brokenUser;

    if (!partitionKey || !rowKey) {
      alert('Cannot delete user: missing partition or row key');
      return;
    }

    const confirmMessage = `Delete broken user record?\n\nTemp ID: ${tempDeleteId}\nPartition: ${partitionKey}\nRow: ${rowKey}\n\nThis action cannot be undone.`;

    if (!confirm(confirmMessage)) {
      return;
    }

    setDeleting(prev => new Set(prev).add(tempDeleteId));

    try {
      const response = await fetch(`/api/admin/users/broken/${encodeURIComponent(partitionKey)}/${encodeURIComponent(rowKey)}`, {
        method: 'DELETE',
        credentials: 'include'
      });

      if (response.ok) {
        console.log('✅ Broken user deleted successfully');
        await fetchDiagnostics(); // Refresh the diagnostics
      } else {
        const error = await response.json();
        console.error('Failed to delete broken user:', error);
        alert(`Failed to delete user: ${error.error}`);
      }
    } catch (error) {
      console.error('Error deleting broken user:', error);
      alert('Error deleting user');
    } finally {
      setDeleting(prev => {
        const newSet = new Set(prev);
        newSet.delete(tempDeleteId);
        return newSet;
      });
    }
  };

  if (!isOpen) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content broken-user-modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <h3>🛠️ Broken User Manager</h3>
          <button className="close-btn" onClick={onClose}>&times;</button>
        </div>

        <div className="modal-body">
          {loading ? (
            <div className="loading-spinner">
              <div className="spinner"></div>
              <p>Loading diagnostics...</p>
            </div>
          ) : !diagnostics ? (
            <div className="no-data">
              <p>No diagnostics data available</p>
            </div>
          ) : (
            <div className="diagnostics-content">
              {/* Summary */}
              <div className="diagnostics-summary">
                <div className="summary-card">
                  <h4>📊 Summary</h4>
                  <div className="summary-stats">
                    <div className="stat">
                      <span className="stat-label">Total Users:</span>
                      <span className="stat-value">{diagnostics.totalUsers}</span>
                    </div>
                    <div className="stat">
                      <span className="stat-label">Valid Users:</span>
                      <span className="stat-value valid">{diagnostics.validUsers.length}</span>
                    </div>
                    <div className="stat">
                      <span className="stat-label">Broken Users:</span>
                      <span className="stat-value broken">{diagnostics.brokenUsers.length}</span>
                    </div>
                    <div className="stat">
                      <span className="stat-label">Suspicious Users:</span>
                      <span className="stat-value suspicious">{diagnostics.suspiciousUsers.length}</span>
                    </div>
                  </div>
                </div>
              </div>

              {/* Broken Users Section */}
              {diagnostics.brokenUsers.length > 0 && (
                <div className="broken-users-section">
                  <h4>💥 Broken Users (Missing twitchUserId)</h4>
                  <p className="section-description">
                    These users are missing their twitchUserId and cannot function properly.
                    They can be safely deleted using the temporary IDs assigned below.
                  </p>

                  <div className="broken-users-list">
                    {diagnostics.brokenUsers.map(user => (
                      <div key={user.tempDeleteId} className="broken-user-item">
                        <div className="user-header">
                          <div className="user-info">
                            <h5>🔨 Broken Record #{user.tempDeleteId}</h5>
                            <div className="user-details">
                              <p><strong>Username:</strong> {user.username}</p>
                              <p><strong>Display Name:</strong> {user.displayName}</p>
                              <p><strong>Partition Key:</strong> {user.partitionKey}</p>
                              <p><strong>Row Key:</strong> {user.rowKey}</p>
                              <p><strong>Role:</strong> {user.role}</p>
                              <p><strong>Active:</strong> {user.isActive ? '✅' : '❌'}</p>
                            </div>
                          </div>

                          <div className="user-actions">
                            <button
                              className="delete-broken-btn"
                              onClick={() => deleteBrokenUser(user)}
                              disabled={deleting.has(user.tempDeleteId)}
                            >
                              {deleting.has(user.tempDeleteId) ? (
                                <>⏳ Deleting...</>
                              ) : (
                                <>🗑️ Delete #{user.tempDeleteId}</>
                              )}
                            </button>
                          </div>
                        </div>

                        <div className="user-metadata">
                          <p><strong>Available Fields:</strong> {user.allFields.join(', ')}</p>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Suspicious Users Section */}
              {diagnostics.suspiciousUsers.length > 0 && (
                <div className="suspicious-users-section">
                  <h4>⚠️ Suspicious Users</h4>
                  <p className="section-description">
                    These users have twitchUserId but are missing other important fields.
                  </p>

                  <div className="suspicious-users-list">
                    {diagnostics.suspiciousUsers.map((user, index) => (
                      <div key={index} className="suspicious-user-item">
                        <h5>⚠️ Suspicious Record</h5>
                        <div className="user-details">
                          <p><strong>Username:</strong> {user.username}</p>
                          <p><strong>Display Name:</strong> {user.displayName}</p>
                          <p><strong>Twitch User ID:</strong> {user.twitchUserId}</p>
                          <p><strong>Partition Key:</strong> {user.partitionKey}</p>
                          <p><strong>Row Key:</strong> {user.rowKey}</p>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Valid Users Summary */}
              <div className="valid-users-section">
                <h4>✅ Valid Users: {diagnostics.validUsers.length}</h4>
                <p className="section-description">
                  These users have all required fields and should function normally.
                </p>
              </div>
            </div>
          )}
        </div>

        <div className="modal-footer">
          <button className="secondary-btn" onClick={fetchDiagnostics} disabled={loading}>
            🔄 Refresh Diagnostics
          </button>
          <button className="primary-btn" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
};

export default BrokenUserManagerModal;

import React, { useState, useEffect } from 'react';
import { makeAuthenticatedJsonRequest, handleAuthError } from '../utils/authUtils';
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
      const data = await makeAuthenticatedJsonRequest('/api/admin/users/diagnostics');
      setDiagnostics(data);
    } catch (error) {
      handleAuthError(error, 'fetching user diagnostics');
    } finally {
      setLoading(false);
    }
  };

  const deleteBrokenUser = async (user) => {
    if (!window.confirm(`Are you sure you want to delete broken user with temp ID ${user.tempId}?`)) {
      return;
    }

    setDeleting(prev => new Set([...prev, user.tempId]));

    try {
      await makeAuthenticatedJsonRequest(
        `/api/admin/users/broken/${encodeURIComponent(user.partitionKey)}/${encodeURIComponent(user.rowKey)}`,
        { method: 'DELETE' }
      );

      // Refresh diagnostics to show updated state
      fetchDiagnostics();
    } catch (error) {
      handleAuthError(error, 'deleting broken user');
    } finally {
      setDeleting(prev => {
        const updated = new Set(prev);
        updated.delete(user.tempId);
        return updated;
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

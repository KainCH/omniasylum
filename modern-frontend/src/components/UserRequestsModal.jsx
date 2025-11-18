import React, { useState, useEffect } from 'react';
import './AdminDashboard.css';

const UserRequestsModal = ({ isOpen, onClose }) => {
  const [requests, setRequests] = useState([]);
  const [loading, setLoading] = useState(false);
  const [processingRequestId, setProcessingRequestId] = useState(null);

  useEffect(() => {
    if (isOpen) {
      fetchRequests();
    }
  }, [isOpen]);

  const fetchRequests = async () => {
    setLoading(true);
    try {
      const response = await fetch('/api/admin/user-requests', {
        credentials: 'include'
      });

      if (response.ok) {
        const data = await response.json();
        setRequests(data);
      } else {
        console.error('Failed to fetch user requests');
      }
    } catch (error) {
      console.error('Error fetching user requests:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleApprove = async (requestId) => {
    setProcessingRequestId(requestId);
    try {
      const response = await fetch(`/api/admin/user-requests/${requestId}/approve`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        credentials: 'include',
        body: JSON.stringify({
          adminNotes: 'Approved by admin'
        })
      });

      if (response.ok) {
        const result = await response.json();
        console.log('✅ User approved:', result);
        await fetchRequests(); // Refresh the list
      } else {
        const error = await response.json();
        console.error('Failed to approve user:', error);
        alert(`Failed to approve user: ${error.error}`);
      }
    } catch (error) {
      console.error('Error approving user:', error);
      alert('Error approving user');
    } finally {
      setProcessingRequestId(null);
    }
  };

  const handleReject = async (requestId) => {
    const reason = prompt('Enter reason for rejection (optional):');

    setProcessingRequestId(requestId);
    try {
      const response = await fetch(`/api/admin/user-requests/${requestId}/reject`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        credentials: 'include',
        body: JSON.stringify({
          adminNotes: reason || 'Request rejected'
        })
      });

      if (response.ok) {
        console.log('❌ User request rejected');
        await fetchRequests(); // Refresh the list
      } else {
        const error = await response.json();
        console.error('Failed to reject user:', error);
        alert(`Failed to reject user: ${error.error}`);
      }
    } catch (error) {
      console.error('Error rejecting user:', error);
      alert('Error rejecting user');
    } finally {
      setProcessingRequestId(null);
    }
  };

  const formatDate = (dateString) => {
    return new Date(dateString).toLocaleString();
  };

  const getStatusBadge = (status) => {
    const badges = {
      'pending': { text: 'Pending', class: 'status-pending' },
      'approved': { text: 'Approved', class: 'status-approved' },
      'rejected': { text: 'Rejected', class: 'status-rejected' }
    };

    const badge = badges[status] || { text: status, class: 'status-unknown' };

    return (
      <span className={`status-badge ${badge.class}`}>
        {badge.text}
      </span>
    );
  };

  if (!isOpen) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content user-requests-modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <h3>👥 User Access Requests</h3>
          <button className="close-btn" onClick={onClose}>&times;</button>
        </div>

        <div className="modal-body">
          {loading ? (
            <div className="loading-spinner">
              <div className="spinner"></div>
              <p>Loading requests...</p>
            </div>
          ) : requests.length === 0 ? (
            <div className="no-requests">
              <p>📋 No user access requests found</p>
            </div>
          ) : (
            <div className="requests-list">
              {requests.map(request => (
                <div key={request.requestId} className="request-item">
                  <div className="request-header">
                    <div className="user-info">
                      <img
                        src={request.profileImageUrl || '/default-avatar.png'}
                        alt={request.displayName}
                        className="user-avatar"
                        onError={(e) => { e.target.src = '/default-avatar.png'; }}
                      />
                      <div className="user-details">
                        <h4>{request.displayName}</h4>
                        <p className="username">@{request.username}</p>
                        <p className="twitch-id">ID: {request.twitchUserId}</p>
                      </div>
                    </div>
                    {getStatusBadge(request.status)}
                  </div>

                  <div className="request-metadata">
                    <div className="metadata-row">
                      <span className="label">📧 Email:</span>
                      <span className="value">{request.email || 'Not provided'}</span>
                    </div>
                    <div className="metadata-row">
                      <span className="label">📅 Requested:</span>
                      <span className="value">{formatDate(request.createdAt)}</span>
                    </div>
                    {request.message && (
                      <div className="metadata-row">
                        <span className="label">💬 Message:</span>
                        <span className="value message-text">{request.message}</span>
                      </div>
                    )}
                    {request.adminNotes && (
                      <div className="metadata-row">
                        <span className="label">📝 Admin Notes:</span>
                        <span className="value">{request.adminNotes}</span>
                      </div>
                    )}
                    {request.processedBy && (
                      <div className="metadata-row">
                        <span className="label">👤 Processed by:</span>
                        <span className="value">{request.processedBy}</span>
                      </div>
                    )}
                  </div>

                  {request.status === 'pending' && (
                    <div className="request-actions">
                      <button
                        className="approve-btn"
                        onClick={() => handleApprove(request.requestId)}
                        disabled={processingRequestId === request.requestId}
                      >
                        {processingRequestId === request.requestId ? '⏳ Processing...' : '✅ Approve'}
                      </button>
                      <button
                        className="reject-btn"
                        onClick={() => handleReject(request.requestId)}
                        disabled={processingRequestId === request.requestId}
                      >
                        {processingRequestId === request.requestId ? '⏳ Processing...' : '❌ Reject'}
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="modal-footer">
          <button className="secondary-btn" onClick={fetchRequests} disabled={loading}>
            🔄 Refresh
          </button>
          <button className="primary-btn" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
};

export default UserRequestsModal;

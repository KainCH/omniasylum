import { useState, useEffect } from 'react';
import './SeriesSaveManager.css';

function SeriesSaveManager({ isOpen, onClose }) {
  const [seriesSaves, setSeriesSaves] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);

  // Save form state
  const [seriesName, setSeriesName] = useState('');
  const [description, setDescription] = useState('');
  const [showSaveForm, setShowSaveForm] = useState(false);

  // Load series state
  const [selectedSeries, setSelectedSeries] = useState(null);
  const [showLoadConfirm, setShowLoadConfirm] = useState(false);

  // Delete series state
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [seriesToDelete, setSeriesToDelete] = useState(null);

  // Fetch series saves when modal opens
  useEffect(() => {
    if (isOpen) {
      fetchSeriesSaves();
    }
  }, [isOpen]);

  // Auto-clear messages after 5 seconds
  useEffect(() => {
    if (error || success) {
      const timer = setTimeout(() => {
        setError(null);
        setSuccess(null);
      }, 5000);
      return () => clearTimeout(timer);
    }
  }, [error, success]);

  const getApiUrl = () => {
    if (import.meta.env.PROD) {
      return window.location.origin;
    }
    return import.meta.env.VITE_API_URL || 'http://localhost:3000';
  };

  const getAuthHeaders = () => {
    const token = localStorage.getItem('authToken');
    return {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    };
  };

  const fetchSeriesSaves = async () => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await fetch(`${getApiUrl()}/api/counters/series/list`, {
        headers: getAuthHeaders()
      });

      if (!response.ok) {
        throw new Error('Failed to fetch series saves');
      }

      const data = await response.json();
      setSeriesSaves(data.saves || []);
    } catch (err) {
      console.error('Error fetching series:', err);
      setError('Failed to load series saves');
    } finally {
      setIsLoading(false);
    }
  };

  const handleSaveSeries = async (e) => {
    e.preventDefault();

    if (!seriesName.trim()) {
      setError('Series name is required');
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const response = await fetch(`${getApiUrl()}/api/counters/series/save`, {
        method: 'POST',
        headers: getAuthHeaders(),
        body: JSON.stringify({
          seriesName: seriesName.trim(),
          description: description.trim()
        })
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.error || 'Failed to save series');
      }

      const data = await response.json();
      setSuccess(`Series "${seriesName}" saved successfully!`);
      setSeriesName('');
      setDescription('');
      setShowSaveForm(false);

      // Refresh the list
      await fetchSeriesSaves();
    } catch (err) {
      console.error('Error saving series:', err);
      setError(err.message);
    } finally {
      setIsLoading(false);
    }
  };

  const handleLoadSeries = async (series) => {
    setIsLoading(true);
    setError(null);
    setShowLoadConfirm(false);

    try {
      const response = await fetch(`${getApiUrl()}/api/counters/series/load`, {
        method: 'POST',
        headers: getAuthHeaders(),
        body: JSON.stringify({ seriesId: series.seriesId })
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.error || 'Failed to load series');
      }

      const data = await response.json();
      setSuccess(`Series "${series.seriesName}" loaded! Deaths: ${data.counters.deaths}, Swears: ${data.counters.swears}`);
      setSelectedSeries(null);

      // Close modal after successful load
      setTimeout(() => {
        onClose();
      }, 2000);
    } catch (err) {
      console.error('Error loading series:', err);
      setError(err.message);
    } finally {
      setIsLoading(false);
    }
  };

  const handleDeleteSeries = async (series) => {
    setIsLoading(true);
    setError(null);
    setShowDeleteConfirm(false);

    try {
      const response = await fetch(`${getApiUrl()}/api/counters/series/${series.seriesId}`, {
        method: 'DELETE',
        headers: getAuthHeaders()
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.error || 'Failed to delete series');
      }

      setSuccess(`Series "${series.seriesName}" deleted successfully`);
      setSeriesToDelete(null);

      // Refresh the list
      await fetchSeriesSaves();
    } catch (err) {
      console.error('Error deleting series:', err);
      setError(err.message);
    } finally {
      setIsLoading(false);
    }
  };

  const formatDate = (dateString) => {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  if (!isOpen) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content series-save-manager" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>💾 Series Save States</h2>
          <button className="close-button" onClick={onClose}>×</button>
        </div>

        {error && (
          <div className="message error-message">
            ❌ {error}
          </div>
        )}

        {success && (
          <div className="message success-message">
            ✅ {success}
          </div>
        )}

        <div className="modal-body">
          {/* Save Current State Section */}
          <div className="section save-section">
            {!showSaveForm ? (
              <button
                className="action-button primary-button"
                onClick={() => setShowSaveForm(true)}
                disabled={isLoading}
              >
                💾 Save Current State
              </button>
            ) : (
              <form onSubmit={handleSaveSeries} className="save-form">
                <h3>Save Current Counter State</h3>
                <div className="form-group">
                  <label htmlFor="seriesName">Series Name *</label>
                  <input
                    type="text"
                    id="seriesName"
                    value={seriesName}
                    onChange={(e) => setSeriesName(e.target.value)}
                    placeholder="e.g., Elden Ring Episode 5"
                    maxLength={100}
                    required
                  />
                </div>
                <div className="form-group">
                  <label htmlFor="description">Description (optional)</label>
                  <input
                    type="text"
                    id="description"
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                    placeholder="e.g., Fighting Malenia"
                    maxLength={200}
                  />
                </div>
                <div className="form-actions">
                  <button
                    type="submit"
                    className="action-button primary-button"
                    disabled={isLoading}
                  >
                    {isLoading ? 'Saving...' : 'Save Series'}
                  </button>
                  <button
                    type="button"
                    className="action-button secondary-button"
                    onClick={() => {
                      setShowSaveForm(false);
                      setSeriesName('');
                      setDescription('');
                    }}
                    disabled={isLoading}
                  >
                    Cancel
                  </button>
                </div>
              </form>
            )}
          </div>

          {/* Saved Series List */}
          <div className="section list-section">
            <div className="section-header">
              <h3>📋 Saved Series ({seriesSaves.length})</h3>
              <button
                className="action-button refresh-button"
                onClick={fetchSeriesSaves}
                disabled={isLoading}
              >
                🔄 Refresh
              </button>
            </div>

            {isLoading && seriesSaves.length === 0 ? (
              <div className="loading-state">Loading series saves...</div>
            ) : seriesSaves.length === 0 ? (
              <div className="empty-state">
                <p>No series saves yet.</p>
                <p>Save your current counter state to create one!</p>
              </div>
            ) : (
              <div className="series-list">
                {seriesSaves.map((series) => (
                  <div key={series.seriesId} className="series-item">
                    <div className="series-info">
                      <h4>{series.seriesName}</h4>
                      {series.description && (
                        <p className="series-description">{series.description}</p>
                      )}
                      <div className="series-stats">
                        <span className="stat">💀 Deaths: {series.deaths}</span>
                        <span className="stat">🤬 Swears: {series.swears}</span>
                        {series.bits > 0 && (
                          <span className="stat">💎 Bits: {series.bits}</span>
                        )}
                      </div>
                      <div className="series-meta">
                        <span className="saved-date">📅 {formatDate(series.savedAt)}</span>
                        <span className="series-id" title={series.seriesId}>
                          ID: {series.seriesId.substring(0, 20)}...
                        </span>
                      </div>
                    </div>
                    <div className="series-actions">
                      <button
                        className="action-button load-button"
                        onClick={() => {
                          setSelectedSeries(series);
                          setShowLoadConfirm(true);
                        }}
                        disabled={isLoading}
                      >
                        📂 Load
                      </button>
                      <button
                        className="action-button delete-button"
                        onClick={() => {
                          setSeriesToDelete(series);
                          setShowDeleteConfirm(true);
                        }}
                        disabled={isLoading}
                      >
                        🗑️
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Load Confirmation Modal */}
        {showLoadConfirm && selectedSeries && (
          <div className="confirm-overlay">
            <div className="confirm-dialog">
              <h3>Load Series?</h3>
              <p>
                This will replace your current counters with values from:
              </p>
              <div className="confirm-series-info">
                <strong>{selectedSeries.seriesName}</strong>
                <div className="confirm-stats">
                  <span>💀 Deaths: {selectedSeries.deaths}</span>
                  <span>🤬 Swears: {selectedSeries.swears}</span>
                </div>
              </div>
              <p className="warning">Your current counter values will be overwritten!</p>
              <div className="confirm-actions">
                <button
                  className="action-button primary-button"
                  onClick={() => handleLoadSeries(selectedSeries)}
                  disabled={isLoading}
                >
                  {isLoading ? 'Loading...' : 'Load Series'}
                </button>
                <button
                  className="action-button secondary-button"
                  onClick={() => {
                    setShowLoadConfirm(false);
                    setSelectedSeries(null);
                  }}
                  disabled={isLoading}
                >
                  Cancel
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Delete Confirmation Modal */}
        {showDeleteConfirm && seriesToDelete && (
          <div className="confirm-overlay">
            <div className="confirm-dialog">
              <h3>Delete Series?</h3>
              <p>
                Are you sure you want to delete this series save?
              </p>
              <div className="confirm-series-info">
                <strong>{seriesToDelete.seriesName}</strong>
              </div>
              <p className="warning">This action cannot be undone!</p>
              <div className="confirm-actions">
                <button
                  className="action-button danger-button"
                  onClick={() => handleDeleteSeries(seriesToDelete)}
                  disabled={isLoading}
                >
                  {isLoading ? 'Deleting...' : 'Delete'}
                </button>
                <button
                  className="action-button secondary-button"
                  onClick={() => {
                    setShowDeleteConfirm(false);
                    setSeriesToDelete(null);
                  }}
                  disabled={isLoading}
                >
                  Cancel
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default SeriesSaveManager;

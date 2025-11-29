import { useState, useEffect } from 'react';
import './AdminDashboard.css';

function AlertEffectsModal({ isOpen, onClose, user, isAdminMode = false }) {
  const [settings, setSettings] = useState({
    enableSound: true,
    enableAnimations: true,
    enableParticles: true,
    enableScreenEffects: true,
    enableSVGFilters: true,
    enableTextEffects: true
  });

  const [volume, setVolume] = useState(70);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (isOpen && user) {
      loadSettings();
    }
  }, [isOpen, user, isAdminMode]);

  const loadSettings = async () => {
    try {
      setIsLoading(true);

      if (isAdminMode && user?.userId) {
        // Admin mode: Load settings for specific user via API
        const token = localStorage.getItem('authToken');
        const response = await fetch(`/api/admin/users/${user.userId}/alert-effects`, {
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
          }
        });

        if (response.ok) {
          const data = await response.json();
          setSettings(data.settings || settings);
          setVolume(data.volume || 70);
        }
      } else {
        // User mode: Load from localStorage (local browser settings)
        const saved = localStorage.getItem('asylumEffectsSettings');
        if (saved) {
          setSettings(JSON.parse(saved));
        }

        const savedVolume = localStorage.getItem('asylumEffectsVolume');
        if (savedVolume) {
          setVolume(parseInt(savedVolume));
        }
      }
    } catch (error) {
      console.error('Failed to load alert effects settings:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const saveSettings = async (newSettings) => {
    try {
      setIsLoading(true);

      if (isAdminMode && user?.userId) {
        // Admin mode: Save settings for specific user via API
        const token = localStorage.getItem('authToken');
        const response = await fetch(`/api/admin/users/${user.userId}/alert-effects`, {
          method: 'PUT',
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({ settings: newSettings, volume })
        });

        if (!response.ok) {
          throw new Error('Failed to save alert effects settings');
        }

        console.log('✅ Admin saved alert effects settings for user:', user.username);
      } else {
        // User mode: Save to localStorage
        localStorage.setItem('asylumEffectsSettings', JSON.stringify(newSettings));
        console.log('✅ Saved alert effect settings to localStorage');
      }

      setSettings(newSettings);
    } catch (error) {
      console.error('❌ Failed to save alert effects settings:', error);
      alert('Failed to save alert effects settings');
    } finally {
      setIsLoading(false);
    }
  };

  const saveVolume = async (newVolume) => {
    try {
      setIsLoading(true);

      if (isAdminMode && user?.userId) {
        // Admin mode: Save volume for specific user via API
        const token = localStorage.getItem('authToken');
        const response = await fetch(`/api/admin/users/${user.userId}/alert-effects`, {
          method: 'PUT',
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({ settings, volume: newVolume })
        });

        if (!response.ok) {
          throw new Error('Failed to save volume setting');
        }

        console.log('✅ Admin saved volume setting for user:', user.username);
      } else {
        // User mode: Save to localStorage
        localStorage.setItem('asylumEffectsVolume', newVolume.toString());
        console.log('✅ Saved volume setting to localStorage');
      }

      setVolume(newVolume);
    } catch (error) {
      console.error('❌ Failed to save volume setting:', error);
      alert('Failed to save volume setting');
    } finally {
      setIsLoading(false);
    }
  };

  const handleToggleSetting = (settingKey) => {
    const newSettings = {
      ...settings,
      [settingKey]: !settings[settingKey]
    };
    saveSettings(newSettings);
  };

  const handleVolumeChange = (e) => {
    const newVolume = parseInt(e.target.value);
    saveVolume(newVolume);
  };

  const resetToDefaults = () => {
    const defaultSettings = {
      enableSound: true,
      enableAnimations: true,
      enableParticles: true,
      enableScreenEffects: true,
      enableSVGFilters: true,
      enableTextEffects: true
    };
    saveSettings(defaultSettings);
    saveVolume(70);
  };

  const effectsOptions = [
    {
      key: 'enableSound',
      label: '🔊 Sound Effects',
      description: 'Play asylum-themed audio when alerts trigger'
    },
    {
      key: 'enableAnimations',
      label: '✨ Animations',
      description: 'Smooth transitions and movement effects'
    },
    {
      key: 'enableParticles',
      label: '🌟 Particle Effects',
      description: 'Floating particles and visual elements'
    },
    {
      key: 'enableScreenEffects',
      label: '📺 Screen Effects',
      description: 'Screen shake, flash, and distortion effects'
    },
    {
      key: 'enableSVGFilters',
      label: '🎨 SVG Filters',
      description: 'Advanced visual filters and distortions'
    },
    {
      key: 'enableTextEffects',
      label: '📝 Text Effects',
      description: 'Typography animations and text distortions'
    }
  ];

  if (!isOpen) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '900px', width: '90vw' }}>
        <div className="modal-header">
          <h2>
            🎬 Alert Effects Settings
            {isAdminMode && user && (
              <span style={{ fontSize: '14px', color: '#aaa', marginLeft: '10px' }}>
                - {user.displayName || user.username}
              </span>
            )}
          </h2>
          <button
            onClick={onClose}
            className="close-btn"
            aria-label="Close"
          >
            ×
          </button>
        </div>

        <div className="modal-body">
          <div style={{ padding: '20px' }}>
            {/* Effects Settings */}
            <div style={{ marginBottom: '30px' }}>
              <h3 style={{ color: '#fff', marginBottom: '15px' }}>🎭 Visual & Audio Effects</h3>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '15px' }}>
                {effectsOptions.map(option => (
                  <div key={option.key} style={{
                    background: '#2a2a2a',
                    padding: '15px',
                    borderRadius: '8px',
                    border: '2px solid #444'
                  }}>
                    <label style={{
                      display: 'flex',
                      alignItems: 'flex-start',
                      gap: '12px',
                      cursor: 'pointer'
                    }}>
                      <input
                        type="checkbox"
                        checked={settings[option.key]}
                        onChange={() => handleToggleSetting(option.key)}
                        disabled={isLoading}
                        style={{ width: '20px', height: '20px', marginTop: '2px' }}
                      />
                      <div>
                        <div style={{ color: '#fff', fontWeight: 'bold', marginBottom: '5px' }}>
                          {option.label}
                        </div>
                        <div style={{ color: '#aaa', fontSize: '12px' }}>
                          {option.description}
                        </div>
                      </div>
                    </label>
                  </div>
                ))}
              </div>
            </div>

            {/* Volume Control */}
            <div style={{ marginBottom: '30px' }}>
              <div style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                marginBottom: '15px'
              }}>
                <h3 style={{ color: '#fff', margin: 0 }}>🔊 Sound Volume</h3>
                <span style={{ color: '#9146ff', fontSize: '18px', fontWeight: 'bold' }}>
                  {volume}%
                </span>
              </div>

              <input
                type="range"
                min="0"
                max="100"
                value={volume}
                onChange={handleVolumeChange}
                disabled={!settings.enableSound || isLoading}
                style={{
                  width: '100%',
                  height: '8px',
                  borderRadius: '4px',
                  background: settings.enableSound ? '#444' : '#222',
                  outline: 'none',
                  cursor: settings.enableSound ? 'pointer' : 'not-allowed'
                }}
              />

              <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                marginTop: '8px',
                color: '#aaa',
                fontSize: '12px'
              }}>
                <span>🔇 Mute</span>
                <span>🔊 Max</span>
              </div>
            </div>

            {/* Info Section */}
            <div style={{
              background: '#1a1a2e',
              padding: '20px',
              borderRadius: '8px',
              border: '2px solid #9146ff',
              marginBottom: '20px'
            }}>
              <h4 style={{ color: '#9146ff', marginBottom: '15px' }}>💡 How It Works</h4>
              <ul style={{ color: '#ccc', paddingLeft: '20px', lineHeight: '1.6' }}>
                <li>Settings apply to your overlay alerts and visual effects</li>
                <li>Changes take effect immediately on new alerts</li>
                <li>Disable effects if you experience performance issues</li>
                <li>All effects are asylum-themed for maximum immersion</li>
                {isAdminMode && (
                  <li style={{ color: '#ff6b6b' }}>
                    <strong>Admin Mode:</strong> Changes affect the selected user's alert settings
                  </li>
                )}
              </ul>
            </div>

            {/* Action Buttons */}
            <div style={{ display: 'flex', gap: '15px', justifyContent: 'flex-end' }}>
              <button
                onClick={resetToDefaults}
                disabled={isLoading}
                style={{
                  padding: '10px 20px',
                  borderRadius: '6px',
                  border: '2px solid #ffc107',
                  background: '#ffc107',
                  color: '#000',
                  fontWeight: 'bold',
                  cursor: isLoading ? 'not-allowed' : 'pointer',
                  opacity: isLoading ? 0.6 : 1
                }}
              >
                🔄 Reset to Defaults
              </button>
              <button
                onClick={onClose}
                disabled={isLoading}
                style={{
                  padding: '10px 20px',
                  borderRadius: '6px',
                  border: '2px solid #28a745',
                  background: '#28a745',
                  color: '#fff',
                  fontWeight: 'bold',
                  cursor: isLoading ? 'not-allowed' : 'pointer',
                  opacity: isLoading ? 0.6 : 1
                }}
              >
                ✅ Save & Close
              </button>
            </div>
          </div>
        </div>

        {/* Loading overlay */}
        {isLoading && (
          <div style={{
            position: 'absolute',
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            background: 'rgba(0,0,0,0.5)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            borderRadius: '12px',
            zIndex: 10
          }}>
            <div style={{ color: '#fff', textAlign: 'center' }}>
              <div style={{ fontSize: '24px', marginBottom: '10px' }}>⏳</div>
              <p>Updating alert effects settings...</p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default AlertEffectsModal;

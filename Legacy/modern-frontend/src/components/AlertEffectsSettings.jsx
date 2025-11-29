import { useState, useEffect } from 'react';
import './AlertEffectsSettings.css';

function AlertEffectsSettings({ onClose }) {
  const [settings, setSettings] = useState({
    enableSound: true,
    enableAnimations: true,
    enableParticles: true,
    enableScreenEffects: true,
    enableSVGFilters: true,
    enableTextEffects: true
  });

  const [volume, setVolume] = useState(70);

  useEffect(() => {
    // Load settings from localStorage (synced with overlay)
    loadSettings();
  }, []);

  const loadSettings = () => {
    try {
      const saved = localStorage.getItem('asylumEffectsSettings');
      if (saved) {
        setSettings(JSON.parse(saved));
      }

      const savedVolume = localStorage.getItem('asylumEffectsVolume');
      if (savedVolume) {
        setVolume(parseInt(savedVolume));
      }
    } catch (error) {
      console.error('Failed to load settings:', error);
    }
  };

  const saveSettings = (newSettings) => {
    try {
      localStorage.setItem('asylumEffectsSettings', JSON.stringify(newSettings));
      setSettings(newSettings);
      console.log('Saved alert effect settings');
    } catch (error) {
      console.error('Failed to save settings:', error);
    }
  };

  const saveVolume = (newVolume) => {
    try {
      localStorage.setItem('asylumEffectsVolume', newVolume.toString());
      setVolume(newVolume);
      console.log(`Saved volume: ${newVolume}%`);
    } catch (error) {
      console.error('Failed to save volume:', error);
    }
  };

  const handleToggle = (settingName) => {
    const newSettings = {
      ...settings,
      [settingName]: !settings[settingName]
    };
    saveSettings(newSettings);
  };

  const handleVolumeChange = (e) => {
    const newVolume = parseInt(e.target.value);
    saveVolume(newVolume);
  };

  const resetToDefaults = () => {
    const defaults = {
      enableSound: true,
      enableAnimations: true,
      enableParticles: true,
      enableScreenEffects: true,
      enableSVGFilters: true,
      enableTextEffects: true
    };
    saveSettings(defaults);
    saveVolume(70);
  };

  const effectOptions = [
    {
      key: 'enableSound',
      icon: '🔊',
      title: 'Sound Effects',
      description: 'Play audio for alert events (door creaks, alarms, etc.)'
    },
    {
      key: 'enableAnimations',
      icon: '🎬',
      title: 'CSS Animations',
      description: 'Animate alerts with entrance effects (door creak, electric pulse, etc.)'
    },
    {
      key: 'enableParticles',
      icon: '✨',
      title: 'Particle Systems',
      description: 'Show particles like dust, sparks, pills, smoke, and hearts'
    },
    {
      key: 'enableScreenEffects',
      icon: '📺',
      title: 'Screen Effects',
      description: 'Shake, flicker, and red alert overlays'
    },
    {
      key: 'enableSVGFilters',
      icon: '🌫️',
      title: 'SVG Filters',
      description: 'Fog, glass distortion, and visual masks'
    },
    {
      key: 'enableTextEffects',
      icon: '🔤',
      title: 'Text Effects',
      description: 'Text scrambling and glitch effects'
    }
  ];

  return (
    <div className="alert-effects-settings">
      <div className="settings-header">
        <h2>🎭 Alert Effects Settings</h2>
        <p>Control which visual and audio effects are displayed in your stream overlay</p>
      </div>

      <div className="settings-content">
        {/* Effect Toggles */}
        <div className="effect-toggles">
          {effectOptions.map(option => (
            <div key={option.key} className="effect-option">
              <div className="option-header">
                <span className="option-icon">{option?.icon || '⚙️'}</span>
                <div className="option-info">
                  <h3>{option?.title || 'Unknown Effect'}</h3>
                  <p>{option?.description || 'Effect description unavailable'}</p>
                </div>
              </div>
              <label className="toggle-switch">
                <input
                  type="checkbox"
                  checked={settings[option.key]}
                  onChange={() => handleToggle(option.key)}
                />
                <span className="toggle-slider"></span>
              </label>
            </div>
          ))}
        </div>

        {/* Volume Control */}
        <div className="volume-control">
          <div className="volume-header">
            <span className="volume-icon">🔊</span>
            <h3>Sound Volume</h3>
            <span className="volume-value">{volume}%</span>
          </div>
          <input
            type="range"
            min="0"
            max="100"
            value={volume}
            onChange={handleVolumeChange}
            className="volume-slider"
            disabled={!settings.enableSound}
          />
          <div className="volume-labels">
            <span>🔇 Mute</span>
            <span>🔊 Max</span>
          </div>
        </div>

        {/* Info Section */}
        <div className="settings-info">
          <h4>💡 How It Works</h4>
          <ul>
            <li>Settings are saved to your browser and apply to your overlay</li>
            <li>Changes take effect immediately on new alerts</li>
            <li>Disable effects if you experience performance issues</li>
            <li>All effects are asylum-themed for maximum immersion</li>
          </ul>
        </div>

        {/* Action Buttons */}
        <div className="settings-actions">
          <button onClick={resetToDefaults} className="reset-button">
            🔄 Reset to Defaults
          </button>
          <button onClick={onClose} className="close-button">
            ✅ Save & Close
          </button>
        </div>
      </div>
    </div>
  );
}

export default AlertEffectsSettings;

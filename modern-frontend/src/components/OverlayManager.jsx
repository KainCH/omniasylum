import { useState, useEffect } from 'react';
import './OverlayManager.css';

function OverlayManager({ userId, username, overlaySettings, onUpdate, onClose }) {
  const [activeTab, setActiveTab] = useState('configuration');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  // Removed EventSub functionality - event mappings are handled in AlertEventManager





  const renderConfigurationTab = () => (
    <div className="configuration-tab">
      <div className="tab-description">
        <p>Configure how your stream overlay appears and behaves on your stream.</p>
      </div>

      <div className="config-section">
        <h3>🎬 Basic Settings</h3>
        <div className="setting-row">
          <label>
            <input
              type="checkbox"
              checked={overlaySettings?.enabled || false}
              onChange={(e) => onUpdate({ ...overlaySettings, enabled: e.target.checked })}
            />
            Enable Stream Overlay
          </label>
        </div>

        <div className="setting-row">
          <label>Position on Stream:</label>
          <select
            value={overlaySettings?.position || 'top-right'}
            onChange={(e) => onUpdate({ ...overlaySettings, position: e.target.value })}
          >
            <option value="top-left">Top Left</option>
            <option value="top-right">Top Right</option>
            <option value="bottom-left">Bottom Left</option>
            <option value="bottom-right">Bottom Right</option>
          </select>
        </div>
      </div>

      <div className="config-section">
        <h3>🎨 Visual Settings</h3>
        <div className="visual-settings-grid">
          <div className="color-settings-group">
            <h4>🎨 Color Palette</h4>
            <div className="color-settings">
              <div className="color-input">
                <label>Background Color</label>
                <input
                  type="color"
                  value={overlaySettings?.theme?.backgroundColor || '#1a1a1a'}
                  onChange={(e) => onUpdate({
                    ...overlaySettings,
                    theme: { ...overlaySettings?.theme, backgroundColor: e.target.value }
                  })}
                />
              </div>
              <div className="color-input">
                <label>Text Color</label>
                <input
                  type="color"
                  value={overlaySettings?.theme?.textColor || '#ffffff'}
                  onChange={(e) => onUpdate({
                    ...overlaySettings,
                    theme: { ...overlaySettings?.theme, textColor: e.target.value }
                  })}
                />
              </div>
              <div className="color-input">
                <label>Border Color</label>
                <input
                  type="color"
                  value={overlaySettings?.theme?.borderColor || '#9146ff'}
                  onChange={(e) => onUpdate({
                    ...overlaySettings,
                    theme: { ...overlaySettings?.theme, borderColor: e.target.value }
                  })}
                />
              </div>
            </div>
          </div>

          <div className="visual-options-group">
            <h4>🔧 Display Options</h4>
            <div className="setting-row">
              <label>
                Overlay Opacity
              </label>
              <input
                type="range"
                min="0.1"
                max="1"
                step="0.1"
                value={overlaySettings?.theme?.opacity || 0.9}
                onChange={(e) => onUpdate({
                  ...overlaySettings,
                  theme: { ...overlaySettings?.theme, opacity: parseFloat(e.target.value) }
                })}
                className="opacity-slider"
              />
              <span className="opacity-value">{Math.round((overlaySettings?.theme?.opacity || 0.9) * 100)}%</span>
            </div>

            <div className="setting-row">
              <label>
                Border Radius
              </label>
              <input
                type="range"
                min="0"
                max="20"
                step="2"
                value={overlaySettings?.theme?.borderRadius || 8}
                onChange={(e) => onUpdate({
                  ...overlaySettings,
                  theme: { ...overlaySettings?.theme, borderRadius: parseInt(e.target.value) }
                })}
                className="radius-slider"
              />
              <span className="radius-value">{overlaySettings?.theme?.borderRadius || 8}px</span>
            </div>
          </div>
        </div>
      </div>

      <div className="config-section">
        <h3>✨ Animation Settings</h3>
        <div className="setting-row">
          <label>
            <input
              type="checkbox"
              checked={overlaySettings?.animations?.enabled || false}
              onChange={(e) => onUpdate({
                ...overlaySettings,
                animations: {
                  ...overlaySettings?.animations,
                  enabled: e.target.checked
                }
              })}
            />
            Enable Animations
          </label>
        </div>
        <div className="setting-row">
          <label>
            <input
              type="checkbox"
              checked={overlaySettings?.animations?.celebrationEffects || false}
              onChange={(e) => onUpdate({
                ...overlaySettings,
                animations: {
                  ...overlaySettings?.animations,
                  celebrationEffects: e.target.checked
                }
              })}
            />
            Celebration Effects
          </label>
        </div>
      </div>
    </div>
  );



  return (
    <div className="overlay-manager-modal">
      <div className="modal-content">
        <div className="modal-header">
          <h2>📺 Stream Overlay Manager</h2>
          <p>Configuring overlay for <strong>{username}</strong></p>
          <button className="close-btn" onClick={onClose}>✕</button>
        </div>

        <div className="overlay-content">
          {renderConfigurationTab()}
        </div>

        <div className="modal-footer">
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" disabled={saving}>
            {saving ? 'Saving...' : '💾 Save Settings'}
          </button>
        </div>
      </div>
    </div>
  );
}

export default OverlayManager;

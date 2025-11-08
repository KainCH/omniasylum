import { useState, useEffect } from 'react';
import './OverlayManager.css';

function OverlayManager({ userId, username, overlaySettings, onUpdate, onClose }) {
  const [activeTab, setActiveTab] = useState('configuration');
  const [eventSubscriptions, setEventSubscriptions] = useState({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  // Available EventSub subscription types
  const availableEvents = {
    'channel.follow': {
      name: 'Follow Events',
      icon: '👥',
      description: 'User follows the channel',
      category: 'Engagement',
      cost: 'Low'
    },
    'channel.subscribe': {
      name: 'Subscription Events',
      icon: '⭐',
      description: 'New subscriptions (including gift subs)',
      category: 'Revenue',
      cost: 'Medium'
    },
    'channel.subscription.gift': {
      name: 'Gift Sub Events',
      icon: '🎁',
      description: 'Community gift subscriptions',
      category: 'Revenue',
      cost: 'Medium'
    },
    'channel.subscription.message': {
      name: 'Resub Events',
      icon: '🔄',
      description: 'Resubs with messages',
      category: 'Engagement',
      cost: 'Medium'
    },
    'channel.cheer': {
      name: 'Cheer/Bits Events',
      icon: '💎',
      description: 'Bits/cheers from viewers',
      category: 'Revenue',
      cost: 'Medium'
    },
    'channel.raid': {
      name: 'Raid Events',
      icon: '🚨',
      description: 'Channel receives raid',
      category: 'Engagement',
      cost: 'Low'
    },
    'channel.channel_points_custom_reward_redemption.add': {
      name: 'Channel Points',
      icon: '🏆',
      description: 'Channel point redemptions',
      category: 'Engagement',
      cost: 'High'
    },
    'channel.chat.message': {
      name: 'Chat Messages',
      icon: '💬',
      description: 'Chat messages (for !discord command) - Requires re-authentication',
      category: 'Chat',
      cost: 'High'
    },
    'stream.online': {
      name: 'Stream Online',
      icon: '🔴',
      description: 'Stream goes live',
      category: 'Stream Status',
      cost: 'Low'
    },
    'stream.offline': {
      name: 'Stream Offline',
      icon: '⚫',
      description: 'Stream goes offline',
      category: 'Stream Status',
      cost: 'Low'
    }
  };

  // Event categories for organization
  const eventCategories = {
    'Stream Status': { icon: '📡', color: '#00ff88' },
    'Engagement': { icon: '👥', color: '#9147ff' },
    'Revenue': { icon: '💰', color: '#ffaa00' },
    'Chat': { icon: '💬', color: '#17a2b8' }
  };

  useEffect(() => {
    if (userId && activeTab === 'subscriptions') {
      fetchEventSubscriptions();
    }
  }, [userId, activeTab]);

  const fetchEventSubscriptions = async () => {
    try {
      setLoading(true);
      const token = localStorage.getItem('authToken');

      const response = await fetch(`/api/eventsub/subscriptions/${userId}`, {
        headers: { 'Authorization': `Bearer ${token}` }
      });

      if (response.ok) {
        const data = await response.json();
        setEventSubscriptions(data.subscriptions || {});
      }
    } catch (error) {
      console.error('Error fetching event subscriptions:', error);
    } finally {
      setLoading(false);
    }
  };

  const updateEventSubscription = async (eventType, enabled) => {
    try {
      setSaving(true);
      const token = localStorage.getItem('authToken');

      const response = await fetch(`/api/eventsub/subscriptions/${userId}`, {
        method: 'PUT',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          eventType,
          enabled
        })
      });

      if (response.ok) {
        const data = await response.json();
        setEventSubscriptions(prev => ({
          ...prev,
          [eventType]: enabled
        }));

        // Show success message
        console.log(`✅ ${enabled ? 'Subscribed to' : 'Unsubscribed from'} ${eventType}`);
      } else {
        const errorData = await response.json();

        // Check if this is a re-authentication required error
        if (errorData.requiresReauth && errorData.missingScope) {
          const shouldReauth = confirm(
            `${errorData.error}\n\nWould you like to re-authenticate your account now to enable this feature?`
          );

          if (shouldReauth) {
            window.location.href = '/auth/twitch';
            return;
          }
        }

        throw new Error(errorData.error || `Failed to update subscription for ${eventType}`);
      }
    } catch (error) {
      console.error(`Error updating ${eventType} subscription:`, error);

      // Don't show alert if user cancelled re-authentication
      if (!error.message.includes('re-authenticate')) {
        alert(`Failed to update ${eventType} subscription: ${error.message}`);
      }
    } finally {
      setSaving(false);
    }
  };

  const subscribeToAllRecommended = async () => {
    const recommendedEvents = [
      'stream.online',
      'stream.offline',
      'channel.follow',
      'channel.subscribe',
      'channel.cheer'
    ];

    try {
      setSaving(true);
      for (const eventType of recommendedEvents) {
        await updateEventSubscription(eventType, true);
      }
      alert('✅ Subscribed to all recommended events!');
    } catch (error) {
      alert('❌ Error subscribing to recommended events');
    }
  };

  const unsubscribeFromAll = async () => {
    if (!confirm('Are you sure you want to unsubscribe from ALL EventSub events?')) return;

    try {
      setSaving(true);
      const token = localStorage.getItem('authToken');

      const response = await fetch(`/api/eventsub/subscriptions/${userId}/unsubscribe-all`, {
        method: 'POST',
        headers: { 'Authorization': `Bearer ${token}` }
      });

      if (response.ok) {
        setEventSubscriptions({});
        alert('✅ Unsubscribed from all events');
      }
    } catch (error) {
      alert('❌ Error unsubscribing from events');
    } finally {
      setSaving(false);
    }
  };

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

  const renderSubscriptionsTab = () => (
    <div className="subscriptions-tab">
      <div className="tab-description">
        <p>Control which Twitch events trigger alerts and effects on your stream. Only subscribe to events you want to receive.</p>
      </div>

      <div className="subscription-actions">
        <button
          className="btn-primary"
          onClick={subscribeToAllRecommended}
          disabled={saving}
        >
          ✅ Enable Recommended
        </button>
        <button
          className="btn-danger"
          onClick={unsubscribeFromAll}
          disabled={saving}
        >
          ❌ Disable All
        </button>
      </div>

      {loading ? (
        <div className="loading">Loading event subscriptions...</div>
      ) : (
        <div className="events-grid">
          {Object.entries(eventCategories).map(([category, config]) => (
            <div key={category} className="event-category">
              <h3 style={{ color: config.color }}>
                {config.icon} {category}
              </h3>

              <div className="events-list">
                {Object.entries(availableEvents)
                  .filter(([_, event]) => event.category === category)
                  .map(([eventType, event]) => {
                    const isSubscribed = eventSubscriptions[eventType] || false;

                    return (
                      <div key={eventType} className={`event-card ${isSubscribed ? 'subscribed' : ''}`}>
                        <div className="event-header">
                          <span className="event-icon">{event.icon}</span>
                          <div className="event-info">
                            <h4>{event.name}</h4>
                            <p>{event.description}</p>
                            <span className={`cost-badge cost-${event.cost.toLowerCase()}`}>
                              {event.cost} Cost
                            </span>
                          </div>
                        </div>

                        <div className="event-toggle">
                          <label className="toggle-switch">
                            <input
                              type="checkbox"
                              checked={isSubscribed}
                              onChange={(e) => updateEventSubscription(eventType, e.target.checked)}
                              disabled={saving}
                            />
                            <span className="toggle-slider"></span>
                          </label>
                        </div>
                      </div>
                    );
                  })}
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="subscription-info">
        <h4>💡 Tips:</h4>
        <ul>
          <li><strong>Recommended:</strong> Stream Online/Offline, Follows, Subs, and Bits</li>
          <li><strong>Performance:</strong> Fewer subscriptions = better performance</li>
          <li><strong>Cost:</strong> Some events have higher Twitch API costs</li>
          <li><strong>Testing:</strong> Enable events gradually to test your setup</li>
        </ul>
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

        <div className="tab-navigation">
          <button
            className={`tab-button ${activeTab === 'configuration' ? 'active' : ''}`}
            onClick={() => setActiveTab('configuration')}
          >
            ⚙️ Configuration
          </button>
          <button
            className={`tab-button ${activeTab === 'subscriptions' ? 'active' : ''}`}
            onClick={() => setActiveTab('subscriptions')}
          >
            📡 Event Subscriptions
          </button>
        </div>

        <div className="tab-content">
          {activeTab === 'configuration' && renderConfigurationTab()}
          {activeTab === 'subscriptions' && renderSubscriptionsTab()}
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

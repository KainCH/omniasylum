import DiscordWebhookSettings from './DiscordWebhookSettings';
import './AdminDashboard.css';

function DiscordWebhookSettingsModal({ isOpen, onClose, user, isAdminMode = false, onUpdate }) {
  if (!isOpen) return null;

  const { twitchUserId, userId, username, displayName } = user || {};

  // For admin mode, use the provided user data directly
  // For user mode, create the expected user object structure
  const userData = isAdminMode
    ? { twitchUserId: twitchUserId || userId, username, displayName }
    : { twitchUserId: userId || twitchUserId, username, displayName };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '800px', width: '90vw' }}>
        <div className="modal-header">
          <h2>🎮 Discord Integration{isAdminMode && userData ? ` - ${displayName || username}` : ''}</h2>
          <button onClick={onClose} className="close-btn">
            ×
          </button>
        </div>

        <div className="modal-body">
          <DiscordWebhookSettings
            user={userData}
            isAdminMode={isAdminMode}
            onUpdate={onUpdate}
          />
        </div>
      </div>
    </div>
  );
}

export default DiscordWebhookSettingsModal;

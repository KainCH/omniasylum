import SeriesSaveManager from './SeriesSaveManager';

function SeriesSaveManagerModal({ isOpen, onClose, user, isAdminMode = false, onUpdate }) {
  if (!isOpen) return null;

  const { displayName, username } = user || {};

  return (
    <div className="modal-overlay" onClick={() => onClose()}>
      <div
        className="modal-content"
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: '1000px', width: '90vw' }}
      >
        <div className="modal-header">
          <h2>💾 Series Save Management - {displayName || username}</h2>
          <button
            onClick={() => onClose()}
            className="close-btn"
            aria-label="Close"
          >
            ×
          </button>
        </div>
        <div className="modal-body">
          <SeriesSaveManager
            user={user}
            isAdminMode={isAdminMode}
            onUpdate={onUpdate}
            isOpen={true}
            onClose={() => {}}
          />
        </div>
      </div>
    </div>
  );
}

export default SeriesSaveManagerModal;

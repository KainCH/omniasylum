import UserAlertManager from './UserAlertManager';

function AlertManagerModal({ isOpen, onClose, user }) {
  if (!isOpen) return null;

  const { userId } = user || {};

  return (
    <div
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        background: 'rgba(0, 0, 0, 0.8)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 2000,
        padding: '20px'
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        style={{
          background: '#1a1a2e',
          borderRadius: '12px',
          width: '100%',
          maxWidth: '1200px',
          maxHeight: '90vh',
          overflow: 'auto',
          position: 'relative'
        }}
      >
        <button
          onClick={() => onClose()}
          style={{
            position: 'absolute',
            top: '20px',
            right: '20px',
            background: '#dc3545',
            color: '#fff',
            border: 'none',
            borderRadius: '6px',
            padding: '8px 16px',
            cursor: 'pointer',
            fontSize: '14px',
            fontWeight: 'bold',
            zIndex: 10
          }}
        >
          ✖ Close
        </button>
        <UserAlertManager userId={userId} />
      </div>
    </div>
  );
}

export default AlertManagerModal;

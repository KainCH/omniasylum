import React from 'react';

function ConnectionStatus({ status }) {
  const getStatusConfig = () => {
    switch (status) {
      case 'connected':
        return {
          icon: '🟢',
          text: 'Connected',
          className: 'status-connected'
        };
      case 'connecting':
        return {
          icon: '🟡',
          text: 'Connecting...',
          className: 'status-connecting'
        };
      case 'disconnected':
        return {
          icon: '🔴',
          text: 'Disconnected',
          className: 'status-disconnected'
        };
      default:
        return {
          icon: '⚫',
          text: 'Unknown',
          className: 'status-unknown'
        };
    }
  };

  const config = getStatusConfig();

  return (
    <div className={`connection-status ${config.className}`}>
      <span className="status-icon">{config.icon}</span>
      <span className="status-text">{config.text}</span>
    </div>
  );
}

export default ConnectionStatus;

import React from 'react';

function AuthPrompt() {
  const handleTwitchLogin = () => {
    window.location.href = '/auth/twitch';
  };

  return (
    <div className="auth-container">
      <div className="auth-card">
        <div className="auth-header">
          <h1>🎮 OmniForgeStream</h1>
          <p className="auth-subtitle">Multi-Tenant Stream Counter</p>
        </div>

        <div className="auth-content">
          <div className="auth-features">
            <h3>✨ Features</h3>
            <ul>
              <li>🔄 Real-time counter sync across devices</li>
              <li>💬 Twitch chat commands (!deaths, !swears)</li>
              <li>📱 Mobile-friendly interface</li>
              <li>🔐 Secure per-user data isolation</li>
              <li>☁️ Cloud-powered reliability</li>
            </ul>
          </div>

          <div className="auth-login">
            <p>Connect with your Twitch account to get started:</p>
            <button
              className="twitch-login-btn"
              onClick={handleTwitchLogin}
            >
              <span className="twitch-icon">🟣</span>
              Login with Twitch
            </button>
          </div>

          <div className="auth-info">
            <p className="privacy-note">
              🔒 Your Twitch credentials are securely stored and only used for authentication and chat integration.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}

export default AuthPrompt;

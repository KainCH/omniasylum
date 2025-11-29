import { useState, useEffect } from 'react';

function InstructionsModal({ isOpen, onClose, user }) {
  if (!isOpen) return null;

  const { userId, username } = user || {};

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
        zIndex: 2000
      }}
      onClick={() => onClose()}
    >
      <div
        style={{
          background: '#1a1a1a',
          padding: '30px',
          borderRadius: '12px',
          maxWidth: '600px',
          maxHeight: '80vh',
          overflow: 'auto',
          border: '2px solid #9146ff'
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <h2 style={{ color: '#9146ff', marginBottom: '20px' }}>📖 How to Use</h2>

        <div style={{ color: '#fff', lineHeight: '1.8' }}>
          <h3 style={{ color: '#fff', marginTop: '20px' }}>🎥 OBS Setup (Browser Source)</h3>
          <div style={{
            background: 'rgba(145, 70, 255, 0.2)',
            padding: '15px',
            borderRadius: '8px',
            marginBottom: '15px',
            border: '1px solid #9146ff'
          }}>
            <p style={{ marginBottom: '10px' }}><strong>1. Add Browser Source to OBS</strong></p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• Right-click in Sources → Add → Browser</p>

            <p style={{ marginTop: '15px', marginBottom: '10px' }}><strong>2. Configure Browser Source</strong></p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>
              • URL: <code style={{ background: '#000', padding: '2px 6px', borderRadius: '4px' }}>
                {`${window.location.origin}/overlay/${userId || 'YOUR_USER_ID'}`}
              </code>
            </p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>
              • Width: <code style={{ background: '#000', padding: '2px 6px', borderRadius: '4px' }}>1920</code>
            </p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>
              • Height: <code style={{ background: '#000', padding: '2px 6px', borderRadius: '4px' }}>1080</code>
            </p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• ✅ Check "Shutdown source when not visible"</p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• ✅ Check "Refresh browser when scene becomes active"</p>

            <p style={{ marginTop: '15px', marginBottom: '10px' }}><strong>3. Customize (Optional)</strong></p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• The overlay will automatically show when you go live!</p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• Go to ⚙️ Overlay Settings to customize position & theme</p>

            <p style={{ marginTop: '15px', marginBottom: '10px' }}><strong>4. Start Your Stream</strong></p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• Just go live on Twitch as normal!</p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• Overlay automatically activates when you go live</p>
            <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• No manual buttons needed - fully automated! 🤖</p>
          </div>

          <h3 style={{ color: '#fff', marginTop: '20px' }}>🎮 Counter Controls</h3>
          <p>• Use <strong>+ / -</strong> buttons to modify counters</p>
          <p>• <strong>Reset All</strong> button clears all counters to zero</p>
          <p>• <strong>Export Data</strong> saves counter data as JSON</p>

          <h3 style={{ color: '#fff', marginTop: '20px' }}>💬 Chat Commands (Broadcaster/Mods)</h3>
          <p>• <strong>!death+</strong> or <strong>!d+</strong> - Increment deaths</p>
          <p>• <strong>!death-</strong> or <strong>!d-</strong> - Decrement deaths</p>
          <p>• <strong>!swear+</strong> or <strong>!s+</strong> - Increment swears</p>
          <p>• <strong>!swear-</strong> or <strong>!s-</strong> - Decrement swears</p>
          <p>• <strong>!resetcounters</strong> - Reset all counters</p>

          <h3 style={{ color: '#fff', marginTop: '20px' }}>🤖 Auto Stream Detection</h3>
          <p>• Counters <strong>automatically activate</strong> when you go live on Twitch</p>
          <p>• Stream session <strong>automatically ends</strong> when you stop streaming</p>
          <p>• Discord notifications sent automatically (if webhook configured)</p>
          <p>• No manual buttons needed - everything is detected via EventSub!</p>

          <h3 style={{ color: '#fff', marginTop: '20px' }}>🔌 Real-time Sync</h3>
          <p>All devices connected to your account will update automatically in real-time!</p>
        </div>

        <button
          onClick={() => onClose()}
          style={{
            background: '#9146ff',
            color: '#fff',
            border: 'none',
            padding: '10px 30px',
            borderRadius: '6px',
            cursor: 'pointer',
            fontSize: '14px',
            marginTop: '20px',
            width: '100%'
          }}
        >
          ✅ Got it!
        </button>
      </div>
    </div>
  );
}

export default InstructionsModal;

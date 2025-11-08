import { useState, useEffect, useRef } from 'react'
import { io } from 'socket.io-client'
import Counter from './components/Counter'
import AuthPrompt from './components/AuthPrompt'
import ConnectionStatus from './components/ConnectionStatus'
import AdminDashboard from './components/AdminDashboard'
import UserAlertManager from './components/UserAlertManager'
import AlertEffectsSettings from './components/AlertEffectsSettings'
import SeriesSaveManager from './components/SeriesSaveManager'
import DiscordWebhookSettings from './components/DiscordWebhookSettings'
import './App.css'

function App() {
  // Helper function to get size-based styles
  const getSizeStyles = (size) => {
    const sizes = {
      small: {
        fontSize: '14px',
        counterFontSize: '20px',
        padding: '15px',
        minWidth: '250px',
        headingSize: '16px',
        itemPadding: '10px'
      },
      medium: {
        fontSize: '16px',
        counterFontSize: '24px',
        padding: '20px',
        minWidth: '300px',
        headingSize: '18px',
        itemPadding: '12px'
      },
      large: {
        fontSize: '20px',
        counterFontSize: '32px',
        padding: '25px',
        minWidth: '400px',
        headingSize: '24px',
        itemPadding: '15px'
      }
    }
    return sizes[size] || sizes.medium
  }

  const [isAuthenticated, setIsAuthenticated] = useState(false)
  const [isLoading, setIsLoading] = useState(true)
  const [socket, setSocket] = useState(null)
  const [connectionStatus, setConnectionStatus] = useState('disconnected')
  const [counters, setCounters] = useState({ deaths: 0, swears: 0 })
  const [userRole, setUserRole] = useState('streamer')
  const [username, setUsername] = useState('')
  const [userId, setUserId] = useState('')
  const [viewMode, setViewMode] = useState('user')
  const [showInstructionsModal, setShowInstructionsModal] = useState(false)
  const [showSettingsModal, setShowSettingsModal] = useState(false)
  const [showAlertManager, setShowAlertManager] = useState(false)
  const [showAlertEffectsSettings, setShowAlertEffectsSettings] = useState(false)
  const [showSeriesSaveManager, setShowSeriesSaveManager] = useState(false)
  const [showDiscordSettings, setShowDiscordSettings] = useState(false)
  const [userFeatures, setUserFeatures] = useState({})
  const [streamStatus, setStreamStatus] = useState('offline')
  const [overlaySettings, setOverlaySettings] = useState({
    enabled: true,
    position: 'top-right',
    size: 'medium',
    counters: {
      deaths: true,
      swears: true,
      bits: false,
      channelPoints: false
    },
    animations: {
      enabled: true,
      showAlerts: true,
      celebrationEffects: false,
      bounceOnUpdate: true,
      fadeTransitions: true
    },
    theme: {
      borderColor: '#9146ff',
      textColor: '#ffffff',
      backgroundColor: 'rgba(0, 0, 0, 0.8)'
    }
  }) // ALWAYS start in user mode

  // Refs for interval management
  const streamingHeartbeatRef = useRef(null)

  // Check authentication status
  useEffect(() => {
    checkAuth()
    checkUrlForToken()
  }, [])

  // Ensure admin users start in user mode
  useEffect(() => {
    if (isAuthenticated && userRole === 'admin') {
      console.log('🔧 Admin detected, forcing user mode')
      setViewMode('user')
    }
  }, [isAuthenticated, userRole])

  // Reset viewMode on authentication changes
  useEffect(() => {
    if (!isAuthenticated) {
      console.log('🔓 User logged out, resetting viewMode')
      setViewMode('user')
    }
  }, [isAuthenticated])

  // Fetch counters with a specific token
  const fetchCountersWithToken = async (token) => {
    try {
      const response = await fetch('/api/counters', {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      if (response.ok) {
        const data = await response.json()
        setCounters(data)
      }
    } catch (error) {
      console.error('❌ Failed to fetch counters:', error)
    }
  }

  // Check URL for token and role from OAuth redirect
  const checkUrlForToken = () => {
    const urlParams = new URLSearchParams(window.location.search)
    const token = urlParams.get('token')
    const role = urlParams.get('role')

    if (token) {
      // Store token in localStorage
      localStorage.setItem('authToken', token)

      // Decode JWT to get user info
      try {
        const payload = JSON.parse(atob(token.split('.')[1]))
        setUsername(payload.username)
        setUserId(payload.userId)
        setUserRole(payload.role || 'streamer')
        setIsAuthenticated(true) // Set authenticated status

        // Force admin users to start in user mode
        if (payload.role === 'admin' && payload.username.toLowerCase() === 'riress') {
          console.log('🔧 Admin user detected during token check, forcing user mode')
          setViewMode('user')
        }

        console.log(`✅ User ${payload.username} logged in as ${payload.role || 'streamer'}`)

        // Fetch initial counter data
        fetchCountersWithToken(token)
      } catch (error) {
        console.error('❌ Failed to decode token:', error)
      }

      // Clean URL
      window.history.replaceState({}, document.title, window.location.pathname)
    }
  }  // Initialize socket when authenticated
  useEffect(() => {
    console.log('🔌 Frontend: Socket useEffect triggered', { isAuthenticated, hasSocket: !!socket })
    if (isAuthenticated && !socket) {
      console.log('🔌 Frontend: Conditions met, initializing socket...')
      initializeSocket()
    } else if (!isAuthenticated) {
      console.log('🔌 Frontend: Not authenticated, skipping socket initialization')
    } else if (socket) {
      console.log('🔌 Frontend: Socket already exists, skipping initialization')
    }

    // Only cleanup on unmount, NOT on re-renders
    return () => {
      // Cleanup will only run when component unmounts
    }
  }, [isAuthenticated])

  // Cleanup socket and intervals when component unmounts
  useEffect(() => {
    return () => {
      if (socket) {
        console.log('🔌 Frontend: Component unmounting, disconnecting socket')
        socket.disconnect()
      }
      if (streamingHeartbeatRef.current) {
        clearInterval(streamingHeartbeatRef.current)
        streamingHeartbeatRef.current = null
        console.log('💓 Cleaned up streaming heartbeat on unmount')
      }
    }
  }, [])

  // Helper functions for streaming heartbeat management
  const startStreamingHeartbeat = (socket) => {
    if (!streamingHeartbeatRef.current && socket) {
      streamingHeartbeatRef.current = setInterval(() => {
        socket.emit('streamModeHeartbeat')
      }, 30000) // Send heartbeat every 30 seconds
      console.log('💓 Started streaming heartbeat (prep/live mode)')
    }
  }

  const stopStreamingHeartbeat = () => {
    if (streamingHeartbeatRef.current) {
      clearInterval(streamingHeartbeatRef.current)
      streamingHeartbeatRef.current = null
      console.log('💓 Stopped streaming heartbeat (went offline)')
    }
  }

  const checkAuth = async () => {
    try {
      const response = await fetch('/api/health')
      if (!response.ok) {
        throw new Error('Server unavailable')
      }

      // Get token from localStorage
      const token = localStorage.getItem('authToken')
      if (!token) {
        console.log('🔐 No auth token found')
        setIsAuthenticated(false)
        setIsLoading(false)
        return
      }

      // Try to get user data with token
      const userResponse = await fetch('/api/counters', {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      if (userResponse.ok) {
        const data = await userResponse.json()
        setIsAuthenticated(true)
        setCounters(data)

        // Fetch stream status and overlay settings
        fetchUserSettings(token)

        // Also decode token to get user info if we haven't already
        if (!username) {
          try {
            const payload = JSON.parse(atob(token.split('.')[1]))
            setUsername(payload.username)
            setUserId(payload.userId)
            setUserRole(payload.role || 'streamer')
            console.log(`✅ User ${payload.username} (ID: ${payload.userId}) authenticated as ${payload.role || 'streamer'}`)
          } catch (error) {
            console.error('❌ Failed to decode token:', error)
          }
        }
      } else {
        console.log('🔐 User not authenticated - invalid token')
        localStorage.removeItem('authToken') // Clear invalid token
        setIsAuthenticated(false)
      }
    } catch (error) {
      console.error('❌ Auth check failed:', error)
      setIsAuthenticated(false)
    } finally {
      setIsLoading(false)
    }
  }

  const initializeSocket = () => {
    console.log('🔌 Frontend: Initializing WebSocket connection...')
    const token = localStorage.getItem('authToken')
    console.log('🔌 Frontend: Token for WebSocket:', token ? 'EXISTS' : 'MISSING')

    const newSocket = io('/', {
      transports: ['websocket'], // Force WebSocket only, skip polling
      upgrade: false, // Don't try to upgrade from polling
      reconnection: true,
      reconnectionDelay: 1000,
      reconnectionAttempts: 5,
      auth: {
        token: token
      }
    })

    console.log('🔌 Frontend: Socket.io client created, attempting WebSocket-only connection...')

    newSocket.on('connect_error', (error) => {
      console.error('❌ Socket.io connect error:', error?.message || 'Unknown error')
      console.error('❌ Error details:', error)
    })

    newSocket.on('reconnect_attempt', (attemptNumber) => {
      console.log(`🔄 Socket.io reconnection attempt ${attemptNumber}...`)
    })

    newSocket.on('reconnect_failed', () => {
      console.error('❌ Socket.io reconnection failed after all attempts')
    })

    newSocket.on('connect', () => {
      console.log('✅ WebSocket connected')
      setConnectionStatus('connected')
    })

    newSocket.on('disconnect', () => {
      console.log('❌ WebSocket disconnected')
      setConnectionStatus('disconnected')
    })

    newSocket.on('counterUpdate', (data) => {
      console.log('📊 Counter update received:', data)
      setCounters(data)
    })

    newSocket.on('streamStatusUpdate', (data) => {
      console.log('🎬 Stream status update received:', data)
      setStreamStatus(data?.streamStatus)
    })

    newSocket.on('overlaySettingsUpdate', (data) => {
      console.log('🎨 Overlay settings update received:', data)
      const settings = typeof data?.overlaySettings === 'string'
        ? JSON.parse(data?.overlaySettings)
        : data?.overlaySettings
      setOverlaySettings(settings)
    })

    newSocket.on('streamOnline', (data) => {
      console.log('🔴 Stream ONLINE event received:', data)
      setStreamStatus('live')
      // Could add notification or UI indicator here
      console.log(`📺 ${data?.username || 'Unknown user'} went LIVE! "${data?.streamTitle || 'Untitled stream'}"`)
    })

    newSocket.on('streamOffline', (data) => {
      console.log('⚫ Stream OFFLINE event received:', data)
      setStreamStatus('offline')
      console.log(`📺 ${data?.username || 'Unknown user'} went offline`)
    })

    newSocket.on('error', (error) => {
      console.error('❌ Socket error:', error)
    })

    // Handle stream mode events
    newSocket.on('prepModeActive', (data) => {
      console.log('🎬 Prep mode ACTIVE event received:', data)
      setStreamStatus('prepping')
      startStreamingHeartbeat(newSocket)
    })

    newSocket.on('streamModeActive', (data) => {
      console.log('🔴 Live mode ACTIVE event received:', data)
      setStreamStatus('live')
      startStreamingHeartbeat(newSocket)
    })

    newSocket.on('streamModeStatus', (data) => {
      console.log('💓 Stream mode status received:', data)
      if (!data?.active || !data?.eventListenersConnected) {
        console.warn('⚠️ Stream connection issues detected:', data)
      }
    })

    newSocket.on('streamStatusChanged', (data) => {
      console.log('🔄 Stream status changed:', data)
      const newStatus = data?.streamStatus
      setStreamStatus(newStatus)

      // Start heartbeat when entering prep or live mode
      if ((newStatus === 'prepping' || newStatus === 'live') && !streamingHeartbeatRef.current) {
        startStreamingHeartbeat(newSocket)
      }

      // Stop heartbeat only when going offline
      if (newStatus === 'offline' && streamingHeartbeatRef.current) {
        stopStreamingHeartbeat()
      }
    })

    setSocket(newSocket)
  }

  const sendSocketEvent = (event) => {
    if (socket && socket.connected) {
      socket.emit(event)
    } else {
      console.error('❌ Not connected to server')
    }
  }

  const incrementDeaths = () => sendSocketEvent('incrementDeaths')
  const decrementDeaths = () => sendSocketEvent('decrementDeaths')
  const incrementSwears = () => sendSocketEvent('incrementSwears')
  const decrementSwears = () => sendSocketEvent('decrementSwears')

  const updateStreamStatus = async (action) => {
    const token = localStorage.getItem('authToken')
    if (!token) {
      alert('Not authenticated')
      return
    }

    console.log('🔄 Updating stream status with action:', action)

    try {
      const response = await fetch('/api/stream/status', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify({ action })
      })

      if (!response.ok) {
        throw new Error('Failed to update stream status')
      }

      const result = await response.json()
      console.log('✅ Stream status API response:', result)
      setStreamStatus(result?.streamStatus)
      console.log('✅ Stream status state updated to:', result?.streamStatus)
    } catch (error) {
      console.error('❌ Failed to update stream status:', error)
      alert('Failed to update stream status')
    }
  }

  const updateOverlaySettings = async (settings) => {
    const token = localStorage.getItem('authToken')
    if (!token) return

    try {
      const response = await fetch('/api/overlay-settings', {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify(settings)
      })

      if (!response.ok) {
        throw new Error('Failed to update overlay settings')
      }

      const result = await response.json()
      setOverlaySettings(result?.overlaySettings)
      console.log('✅ Overlay settings updated')
    } catch (error) {
      console.error('❌ Failed to update overlay settings:', error)
      alert('Failed to update overlay settings')
    }
  }

  const fetchUserSettings = async (token) => {
    try {
      const response = await fetch('/api/user/settings', {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      if (response.ok) {
        const data = await response.json()
        if (data?.streamStatus) setStreamStatus(data?.streamStatus)
        if (data?.features) setUserFeatures(data?.features)
        if (data?.overlaySettings) {
          const settings = typeof data?.overlaySettings === 'string'
            ? JSON.parse(data?.overlaySettings)
            : data?.overlaySettings
          setOverlaySettings(settings)
        }
      }
    } catch (error) {
      console.error('❌ Failed to fetch user settings:', error)
    }
  }

  const resetCounters = () => {
    if (window.confirm('Are you sure you want to reset all counters?')) {
      sendSocketEvent('resetCounters')
    }
  }

  const logout = () => {
    console.log('🔓 Logging out user')
    localStorage.removeItem('authToken')
    setIsAuthenticated(false)
    setUsername('')
    setUserRole('streamer')
    setViewMode('user') // Reset to user mode
    setCounters({ deaths: 0, swears: 0 })
    if (socket) {
      socket.disconnect()
      setSocket(null)
    }
    setConnectionStatus('disconnected')
  }

  const exportData = () => {
    const data = {
      deaths: counters?.deaths || 0,
      swears: counters?.swears || 0,
      total: (counters?.deaths || 0) + (counters?.swears || 0),
      timestamp: new Date().toLocaleString()
    }

    const json = JSON.stringify(data, null, 2)
    const blob = new Blob([json], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `stream-counter-${Date.now()}.json`
    a.click()
    URL.revokeObjectURL(url)
  }

  if (isLoading) {
    return (
      <div className="loading">
        <div className="loading-spinner"></div>
        <p>Connecting to OmniForgeStream...</p>
      </div>
    )
  }

  if (!isAuthenticated) {
    return <AuthPrompt />
  }

  // Check if user is admin (super admin riress)
  const isAdmin = userRole === 'admin' && username.toLowerCase() === 'riress'

  // FORCE admin users to user mode if viewMode isn't explicitly set to admin
  if (isAdmin && viewMode !== 'admin' && viewMode !== 'user') {
    console.log('🔧 FORCING admin to user mode in render')
    setViewMode('user')
  }

  // Debug logging
  console.log('🔍 Debug Info:', {
    isAdmin,
    userRole,
    username: username.toLowerCase(),
    viewMode,
    isAuthenticated
  })

  // More specific debugging
  console.log('🔍 Admin Check:', {
    userRoleCheck: userRole === 'admin',
    usernameCheck: username.toLowerCase() === 'riress',
    combinedIsAdmin: isAdmin,
    viewModeValue: viewMode,
    willShowAdminDashboard: isAdmin && viewMode === 'admin',
    willShowUserPortal: !(isAdmin && viewMode === 'admin')
  })

  // Show admin dashboard ONLY if explicitly in admin mode
  if (isAdmin && viewMode === 'admin') {
    console.log('🛠️ RENDERING ADMIN DASHBOARD - viewMode is admin')
    return (
      <div>
        <div style={{
          position: 'fixed',
          top: '10px',
          right: '10px',
          zIndex: 1000,
          background: 'rgba(0,0,0,0.8)',
          padding: '10px',
          borderRadius: '8px'
        }}>
          <button
            onClick={() => setViewMode('user')}
            style={{
              background: '#28a745',
              color: 'white',
              border: 'none',
              padding: '8px 16px',
              borderRadius: '4px',
              cursor: 'pointer',
              fontSize: '14px'
            }}
            title="Return to your stream dashboard"
          >
            🎬 Back to My Stream
          </button>
        </div>
        <AdminDashboard />
      </div>
    )
  }

  // EXPLICIT CHECK: Never show AdminDashboard for user mode
  if (isAdmin && viewMode === 'user') {
    console.log('🎬 RENDERING USER PORTAL - Admin in user mode')
  } else if (!isAdmin) {
    console.log('🎬 RENDERING USER PORTAL - Regular user')
  }

  return (
    <div className="app">
      <div className="container">
        <header className="app-header">
          <h1>🎮 OmniForgeStream Counter</h1>
          <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
            <ConnectionStatus status={connectionStatus} />
            {isAdmin && (
              <button
                onClick={() => setViewMode('admin')}
                style={{
                  background: 'linear-gradient(135deg, #dc3545, #fd7e14)',
                  color: 'white',
                  border: 'none',
                  padding: '10px 20px',
                  borderRadius: '6px',
                  cursor: 'pointer',
                  fontSize: '14px',
                  fontWeight: 'bold',
                  boxShadow: '0 2px 4px rgba(0,0,0,0.2)',
                  transition: 'all 0.3s ease'
                }}
                title="Access admin panel to manage all users"
                onMouseOver={(e) => {
                  e.target.style.transform = 'translateY(-2px)'
                  e.target.style.boxShadow = '0 4px 8px rgba(0,0,0,0.3)'
                }}
                onMouseOut={(e) => {
                  e.target.style.transform = 'translateY(0)'
                  e.target.style.boxShadow = '0 2px 4px rgba(0,0,0,0.2)'
                }}
              >
                🛠️ Admin Panel
              </button>
            )}
            <button
              onClick={logout}
              style={{
                background: '#6c757d',
                color: 'white',
                border: 'none',
                padding: '8px 16px',
                borderRadius: '4px',
                cursor: 'pointer',
                fontSize: '12px'
              }}
              title="Logout"
            >
              🚪 Logout
            </button>
          </div>
        </header>

        <Counter
          counters={counters}
          onIncrementDeaths={incrementDeaths}
          onDecrementDeaths={decrementDeaths}
          onIncrementSwears={incrementSwears}
          onDecrementSwears={decrementSwears}
          onReset={resetCounters}
          onExport={exportData}
        />

        {/* Auto-Detected Stream Status */}
        <div style={{
          background: 'rgba(0, 0, 0, 0.3)',
          padding: '20px',
          borderRadius: '12px',
          marginTop: '20px',
          border: '1px solid rgba(255, 255, 255, 0.1)'
        }}>
          <h3 style={{ marginBottom: '15px', color: '#fff' }}>🤖 Auto-Detected Stream Status</h3>
          <div style={{
            display: 'flex',
            alignItems: 'center',
            gap: '10px',
            marginBottom: '15px'
          }}>
            <div style={{
              width: '12px',
              height: '12px',
              borderRadius: '50%',
              backgroundColor: counters.streamStarted ? '#28a745' : '#6c757d'
            }}></div>
            <p style={{ margin: 0, color: '#ccc' }}>
              Status: <strong style={{
                color: counters.streamStarted ? '#28a745' : '#9146ff'
              }}>
                {counters.streamStarted ? '🔴 Live' : '⚫ Offline'}
              </strong>
            </p>
          </div>

          {counters.streamStarted ? (
            <div style={{
              background: 'rgba(40, 167, 69, 0.1)',
              border: '1px solid rgba(40, 167, 69, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#28a745'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                🎬 <strong>Stream automatically detected!</strong><br/>
                <small>Counter tracking started when you went live on Twitch</small>
              </p>
            </div>
          ) : (
            <div style={{
              background: 'rgba(108, 117, 125, 0.1)',
              border: '1px solid rgba(108, 117, 125, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#6c757d'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                📡 <strong>Waiting for Twitch stream...</strong><br/>
                <small>Counters will activate automatically when you go live</small>
              </p>
            </div>
          )}
        </div>

        {/* Action Buttons */}
        <div style={{
          display: 'flex',
          gap: '10px',
          marginTop: '20px',
          flexWrap: 'wrap'
        }}>
          <button
            onClick={() => setShowInstructionsModal(true)}
            style={{
              background: '#17a2b8',
              color: '#fff',
              border: 'none',
              padding: '10px 20px',
              borderRadius: '6px',
              cursor: 'pointer',
              fontSize: '14px',
              flex: 1
            }}
          >
            📖 Instructions
          </button>
          <button
            onClick={() => setShowSettingsModal(true)}
            style={{
              background: '#6f42c1',
              color: '#fff',
              border: 'none',
              padding: '10px 20px',
              borderRadius: '6px',
              cursor: 'pointer',
              fontSize: '14px',
              flex: 1
            }}
          >
            ⚙️ Overlay Settings
          </button>
          <button
            onClick={() => setShowAlertManager(true)}
            style={{
              background: '#fd7e14',
              color: '#fff',
              border: 'none',
              padding: '10px 20px',
              borderRadius: '6px',
              cursor: 'pointer',
              fontSize: '14px',
              flex: 1,
              fontWeight: 'bold'
            }}
          >
            🎯 Manage Alerts
          </button>
          <button
            onClick={() => setShowAlertEffectsSettings(true)}
            style={{
              background: '#9146ff',
              color: '#fff',
              border: 'none',
              padding: '10px 20px',
              borderRadius: '6px',
              cursor: 'pointer',
              fontSize: '14px',
              flex: 1,
              fontWeight: 'bold'
            }}
          >
            🎭 Alert Effects
          </button>
          <button
            onClick={() => setShowSeriesSaveManager(true)}
            style={{
              background: '#4CAF50',
              color: '#fff',
              border: 'none',
              padding: '10px 20px',
              borderRadius: '6px',
              cursor: 'pointer',
              fontSize: '14px',
              flex: 1,
              fontWeight: 'bold'
            }}
          >
            💾 Series Saves
          </button>
          <button
            onClick={() => setShowDiscordSettings(true)}
            style={{
              background: userFeatures.discordNotifications ? '#5865F2' : '#6c757d',
              color: '#fff',
              border: 'none',
              padding: '10px 20px',
              borderRadius: '6px',
              cursor: 'pointer',
              fontSize: '14px',
              flex: 1,
              fontWeight: 'bold'
            }}
            title={userFeatures.discordNotifications ? 'Configure Discord notifications' : 'Set up Discord notifications'}
          >
            🔔 Discord Notifications
          </button>
        </div>

        {/* Instructions Modal */}
        {showInstructionsModal && (
          <div style={{
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
          }} onClick={() => setShowInstructionsModal(false)}>
            <div style={{
              background: '#1a1a1a',
              padding: '30px',
              borderRadius: '12px',
              maxWidth: '600px',
              maxHeight: '80vh',
              overflow: 'auto',
              border: '2px solid #9146ff'
            }} onClick={(e) => e.stopPropagation()}>
              <h2 style={{ color: '#9146ff', marginBottom: '20px' }}>📖 How to Use</h2>

              <div style={{ color: '#fff', lineHeight: '1.8' }}>
                <h3 style={{ color: '#fff', marginTop: '20px' }}>� OBS Setup (Browser Source)</h3>
                <div style={{ background: 'rgba(145, 70, 255, 0.2)', padding: '15px', borderRadius: '8px', marginBottom: '15px', border: '1px solid #9146ff' }}>
                  <p style={{ marginBottom: '10px' }}><strong>1. Add Browser Source to OBS</strong></p>
                  <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• Right-click in Sources → Add → Browser</p>

                  <p style={{ marginTop: '15px', marginBottom: '10px' }}><strong>2. Configure Browser Source</strong></p>
                  <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• URL: <code style={{ background: '#000', padding: '2px 6px', borderRadius: '4px' }}>{`${window.location.origin}/overlay/${userId}`}</code></p>
                  <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• Width: <code style={{ background: '#000', padding: '2px 6px', borderRadius: '4px' }}>1920</code></p>
                  <p style={{ fontSize: '13px', color: '#ccc', marginLeft: '15px' }}>• Height: <code style={{ background: '#000', padding: '2px 6px', borderRadius: '4px' }}>1080</code></p>
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

                <h3 style={{ color: '#fff', marginTop: '20px' }}>�🎮 Counter Controls</h3>
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
                onClick={() => setShowInstructionsModal(false)}
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
        )}

        {/* Alert Manager Modal */}
        {showAlertManager && (
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
              if (e.target === e.currentTarget) setShowAlertManager(false)
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
                onClick={() => setShowAlertManager(false)}
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
        )}

        {/* Alert Effects Settings Modal */}
        {showAlertEffectsSettings && (
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
              if (e.target === e.currentTarget) setShowAlertEffectsSettings(false)
            }}
          >
            <div
              style={{
                background: '#1a1a2e',
                borderRadius: '12px',
                width: '100%',
                maxWidth: '900px',
                maxHeight: '90vh',
                overflow: 'auto',
                position: 'relative'
              }}
            >
              <AlertEffectsSettings onClose={() => setShowAlertEffectsSettings(false)} />
            </div>
          </div>
        )}

        {/* Series Save Manager Modal */}
        {showSeriesSaveManager && (
          <SeriesSaveManager
            isOpen={showSeriesSaveManager}
            onClose={() => setShowSeriesSaveManager(false)}
          />
        )}

        {/* Discord Notification Settings Modal */}
        {showDiscordSettings && (
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
              if (e.target === e.currentTarget) setShowDiscordSettings(false)
            }}
          >
            <div
              style={{
                background: '#1a1a2e',
                borderRadius: '12px',
                width: '100%',
                maxWidth: '600px',
                maxHeight: '90vh',
                overflow: 'auto',
                position: 'relative'
              }}
            >
              <button
                onClick={() => setShowDiscordSettings(false)}
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
              <DiscordWebhookSettings user={{ twitchUserId: userId, username }} />
            </div>
          </div>
        )}

        {/* Settings Modal */}
        {showSettingsModal && (
          <div style={{
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
          }} onClick={() => setShowSettingsModal(false)}>
            <div style={{
              background: '#1a1a1a',
              padding: '30px',
              borderRadius: '12px',
              maxWidth: '700px',
              maxHeight: '80vh',
              overflow: 'auto',
              border: '2px solid #9146ff'
            }} onClick={(e) => e.stopPropagation()}>
              <h2 style={{ color: '#9146ff', marginBottom: '20px' }}>⚙️ Overlay Settings</h2>

              {/* Enable/Disable Overlay */}
              <div style={{ marginBottom: '25px', padding: '15px', background: '#2a2a2a', borderRadius: '8px', border: '2px solid #9146ff' }}>
                <label style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', cursor: 'pointer' }}>
                  <div>
                    <h4 style={{ color: '#fff', margin: 0 }}>🎬 Enable Overlay</h4>
                    <p style={{ color: '#aaa', fontSize: '12px', margin: '5px 0 0 0' }}>Show overlay when stream is live</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={overlaySettings.enabled}
                    onChange={(e) => {
                      const newSettings = { ...overlaySettings, enabled: e.target.checked }
                      setOverlaySettings(newSettings)
                      updateOverlaySettings(newSettings)
                    }}
                    style={{ width: '24px', height: '24px', cursor: 'pointer' }}
                  />
                </label>
              </div>

              {/* Position Selector */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>🎯 Overlay Position</h4>
                <select
                  value={overlaySettings.position}
                  onChange={(e) => {
                    const newSettings = { ...overlaySettings, position: e.target.value }
                    setOverlaySettings(newSettings)
                    updateOverlaySettings(newSettings)
                  }}
                  style={{
                    width: '100%',
                    padding: '10px',
                    borderRadius: '6px',
                    background: '#2a2a2a',
                    color: '#fff',
                    border: '1px solid #444',
                    fontSize: '14px'
                  }}
                >
                  <option value="top-left">↖️ Top Left</option>
                  <option value="top-right">↗️ Top Right</option>
                  <option value="bottom-left">↙️ Bottom Left</option>
                  <option value="bottom-right">↘️ Bottom Right</option>
                </select>
              </div>

              {/* Size Selector */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>📏 Overlay Size</h4>
                <select
                  value={overlaySettings.size || 'medium'}
                  onChange={(e) => {
                    const newSettings = { ...overlaySettings, size: e.target.value }
                    setOverlaySettings(newSettings)
                    updateOverlaySettings(newSettings)
                  }}
                  style={{
                    width: '100%',
                    padding: '10px',
                    borderRadius: '6px',
                    background: '#2a2a2a',
                    color: '#fff',
                    border: '1px solid #444',
                    fontSize: '14px'
                  }}
                >
                  <option value="small">🔹 Small (Compact)</option>
                  <option value="medium">🔸 Medium (Default)</option>
                  <option value="large">🔶 Large (Bold)</option>
                </select>
              </div>

              {/* Counters */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>📊 Visible Counters</h4>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px' }}>
                  {[
                    { key: 'deaths', label: '💀 Deaths' },
                    { key: 'swears', label: '🤬 Swears' },
                    { key: 'bits', label: '💎 Bits' },
                    { key: 'channelPoints', label: '⭐ Channel Points' }
                  ].map(counter => (
                    <label key={counter.key} style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#fff', cursor: 'pointer' }}>
                      <input
                        type="checkbox"
                        checked={overlaySettings.counters[counter.key] || false}
                        onChange={(e) => {
                          const newSettings = {
                            ...overlaySettings,
                            counters: { ...overlaySettings.counters, [counter.key]: e.target.checked }
                          }
                          setOverlaySettings(newSettings)
                          updateOverlaySettings(newSettings)
                        }}
                        style={{ width: '18px', height: '18px' }}
                      />
                      <span>{counter.label}</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Animations */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>✨ Animations & Effects</h4>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px' }}>
                  {[
                    { key: 'enabled', label: 'Basic Animations' },
                    { key: 'showAlerts', label: 'Counter Alerts' },
                    { key: 'celebrationEffects', label: 'Celebrations' },
                    { key: 'bounceOnUpdate', label: 'Bounce Effect' },
                    { key: 'fadeTransitions', label: 'Fade Transitions' }
                  ].map(animation => (
                    <label key={animation.key} style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#fff', cursor: 'pointer' }}>
                      <input
                        type="checkbox"
                        checked={overlaySettings.animations[animation.key] || false}
                        onChange={(e) => {
                          const newSettings = {
                            ...overlaySettings,
                            animations: { ...overlaySettings.animations, [animation.key]: e.target.checked }
                          }
                          setOverlaySettings(newSettings)
                          updateOverlaySettings(newSettings)
                        }}
                        style={{ width: '18px', height: '18px' }}
                      />
                      <span>{animation.label}</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Theme Colors */}
              <div style={{ marginBottom: '25px' }}>
                <h4 style={{ color: '#fff', marginBottom: '10px' }}>🎨 Theme Colors</h4>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '15px' }}>
                  <label style={{ color: '#fff' }}>
                    <span style={{ display: 'block', marginBottom: '5px' }}>Border Color:</span>
                    <input
                      type="color"
                      value={overlaySettings.theme.borderColor}
                      onChange={(e) => {
                        const newSettings = {
                          ...overlaySettings,
                          theme: { ...overlaySettings.theme, borderColor: e.target.value }
                        }
                        setOverlaySettings(newSettings)
                        updateOverlaySettings(newSettings)
                      }}
                      style={{ width: '100%', height: '40px', cursor: 'pointer' }}
                    />
                  </label>
                  <label style={{ color: '#fff' }}>
                    <span style={{ display: 'block', marginBottom: '5px' }}>Text Color:</span>
                    <input
                      type="color"
                      value={overlaySettings.theme.textColor}
                      onChange={(e) => {
                        const newSettings = {
                          ...overlaySettings,
                          theme: { ...overlaySettings.theme, textColor: e.target.value }
                        }
                        setOverlaySettings(newSettings)
                        updateOverlaySettings(newSettings)
                      }}
                      style={{ width: '100%', height: '40px', cursor: 'pointer' }}
                    />
                  </label>
                </div>
              </div>

              <button
                onClick={() => setShowSettingsModal(false)}
                style={{
                  background: '#9146ff',
                  color: '#fff',
                  border: 'none',
                  padding: '10px 30px',
                  borderRadius: '6px',
                  cursor: 'pointer',
                  fontSize: '14px',
                  width: '100%'
                }}
              >
                ✅ Save & Close
              </button>
            </div>
          </div>
        )}

        {/* Stream Overlay - Rendered when stream is live */}
        {console.log('🎨 Overlay render check:', {
          streamStatus,
          overlayEnabled: overlaySettings.enabled,
          shouldShow: (streamStatus === 'live' || streamStatus === 'ending') && overlaySettings.enabled,
          overlayPosition: overlaySettings.position
        }) || ((streamStatus === 'live' || streamStatus === 'ending') && overlaySettings.enabled && (() => {
          const sizeStyles = getSizeStyles(overlaySettings.size || 'medium')
          return (
          <div style={{
            position: 'fixed',
            [overlaySettings.position.includes('top') ? 'top' : 'bottom']: '20px',
            [overlaySettings.position.includes('left') ? 'left' : 'right']: '20px',
            zIndex: 9999,
            background: overlaySettings.theme.backgroundColor,
            border: `3px solid ${overlaySettings.theme.borderColor}`,
            borderRadius: '12px',
            padding: sizeStyles.padding,
            minWidth: sizeStyles.minWidth,
            boxShadow: '0 8px 32px rgba(0, 0, 0, 0.5)',
            transition: overlaySettings.animations.fadeTransitions ? 'all 0.3s ease' : 'none',
            animation: overlaySettings.animations.bounceOnUpdate ? 'fadeIn 0.5s ease-in-out' : 'none'
          }}>
            <h3 style={{
              color: overlaySettings.theme.textColor,
              marginBottom: '15px',
              fontSize: sizeStyles.headingSize,
              fontWeight: 'bold',
              textAlign: 'center',
              textShadow: '2px 2px 4px rgba(0, 0, 0, 0.8)'
            }}>
              🎮 Live Counter
            </h3>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
              {overlaySettings.counters.deaths && (
                <div style={{
                  background: 'rgba(220, 53, 69, 0.2)',
                  padding: sizeStyles.itemPadding,
                  borderRadius: '8px',
                  border: '2px solid #dc3545',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center'
                }}>
                  <span style={{
                    color: overlaySettings.theme.textColor,
                    fontSize: sizeStyles.fontSize,
                    fontWeight: 'bold'
                  }}>💀 Deaths</span>
                  <span style={{
                    color: overlaySettings.theme.textColor,
                    fontSize: sizeStyles.counterFontSize,
                    fontWeight: 'bold'
                  }}>{counters?.deaths || 0}</span>
                </div>
              )}

              {overlaySettings.counters.swears && (
                <div style={{
                  background: 'rgba(255, 193, 7, 0.2)',
                  padding: sizeStyles.itemPadding,
                  borderRadius: '8px',
                  border: '2px solid #ffc107',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center'
                }}>
                  <span style={{
                    color: overlaySettings.theme.textColor,
                    fontSize: sizeStyles.fontSize,
                    fontWeight: 'bold'
                  }}>🤬 Swears</span>
                  <span style={{
                    color: overlaySettings.theme.textColor,
                    fontSize: sizeStyles.counterFontSize,
                    fontWeight: 'bold'
                  }}>{counters?.swears || 0}</span>
                </div>
              )}

              {overlaySettings.counters.bits && (counters?.bits || 0) > 0 && (
                <div style={{
                  background: 'rgba(145, 70, 255, 0.2)',
                  padding: sizeStyles.itemPadding,
                  borderRadius: '8px',
                  border: '2px solid #9146ff',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center'
                }}>
                  <span style={{
                    color: overlaySettings.theme.textColor,
                    fontSize: sizeStyles.fontSize,
                    fontWeight: 'bold'
                  }}>💎 Bits</span>
                  <span style={{
                    color: overlaySettings.theme.textColor,
                    fontSize: sizeStyles.counterFontSize,
                    fontWeight: 'bold'
                  }}>{counters.bits || 0}</span>
                </div>
              )}
            </div>
          </div>
          )
        })()
        )}
      </div>
    </div>
  )
}

export default App

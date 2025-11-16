import { useState, useEffect, useRef } from 'react'
import { io } from 'socket.io-client'
import Counter from './components/Counter'
import AuthPrompt from './components/AuthPrompt'
import ConnectionStatus from './components/ConnectionStatus'
import AdminDashboard from './components/AdminDashboard'
import DebugDashboard from './components/DebugDashboard'
import UserAlertManager from './components/UserAlertManager'
import AlertEffectsSettings from './components/AlertEffectsSettings'
import SeriesSaveManager from './components/SeriesSaveManager'
import DiscordWebhookSettings from './components/DiscordWebhookSettings'
import OverlaySettingsModal from './components/OverlaySettingsModal'
import { userAPI } from './utils/apiHelpers'
import './App.css'
import './components/AdminDashboard.css'

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

  // EventSub monitoring state
  const [eventSubStatus, setEventSubStatus] = useState({
    connected: false,
    monitoring: false,
    lastConnected: null,
    subscriptionsEnabled: false,
    lastStreamStart: null,
    lastStreamEnd: null,
    streamStatus: 'offline'  // Real-time stream status from EventSub
  })
  const [isStartingMonitoring, setIsStartingMonitoring] = useState(false)

  // Twitch bot connection state
  const [twitchBotStatus, setTwitchBotStatus] = useState({
    connected: false,
    userId: '',
    username: '',
    eligible: false,
    chatCommandsEnabled: false,
    hasTokens: false,
    reason: 'Not connected',
    lastUpdated: null
  })

  // Discord notification status tracking
  const [discordNotificationStatus, setDiscordNotificationStatus] = useState({
    status: 'not_configured', // not_configured, ready, pending, sent, error
    hasWebhook: false,
    pendingChannelInfo: false,
    pendingStreamInfo: false,
    lastNotificationSent: null,
    currentStreamId: null,
    setupSteps: []
  })

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

        // Check EventSub monitoring status
        checkEventSubStatus()
        checkTwitchBotStatus()

        // Check Discord notification status
        checkDiscordNotificationStatus()

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



    newSocket.on('streamOnline', (data) => {
      console.log('🔴 Stream ONLINE event received:', data)

      // Update main stream status to show overlay
      setStreamStatus('live')

      // Update EventSub status with real-time stream status
      setEventSubStatus(prevStatus => ({
        ...prevStatus,
        streamStatus: 'live',
        lastStreamStart: new Date().toISOString()
      }))

      console.log(`📺 ${data?.username || 'Unknown user'} went LIVE! "${data?.streamTitle || 'Untitled stream'}"`)
    })

    newSocket.on('streamOffline', (data) => {
      console.log('⚫ Stream OFFLINE event received:', data)

      // Update main stream status to hide overlay
      setStreamStatus('offline')

      // Update EventSub status with real-time stream status
      setEventSubStatus(prevStatus => ({
        ...prevStatus,
        streamStatus: 'offline',
        lastStreamEnd: new Date().toISOString()
      }))

      // Note: Monitoring stays active unless user manually stops it
      console.log(`📺 ${data?.username || 'Unknown user'} went offline - monitoring continues`)
    })

    // Real-time EventSub status updates
    newSocket.on('eventSubStatusChanged', (data) => {
      console.log('🔄 EventSub status changed:', data)
      setEventSubStatus(prevStatus => ({
        ...prevStatus,
        connected: data.connected || false,
        monitoring: data.monitoring || false,
        lastConnected: data.lastConnected || prevStatus.lastConnected,
        subscriptionsEnabled: data.subscriptionsEnabled || false,
        lastStreamStart: data.lastStreamStart || prevStatus.lastStreamStart,
        lastStreamEnd: data.lastStreamEnd || prevStatus.lastStreamEnd,
        streamStatus: data.streamStatus || prevStatus.streamStatus  // Real-time stream status
      }))

      // Discord notification status is now handled by real-time Socket.io events
      // No need to override with static API calls
    })

    // Real-time Twitch bot status updates
    newSocket.on('twitchBotStatusChanged', (data) => {
      console.log('🤖 Twitch bot status changed:', data)
      setTwitchBotStatus({
        connected: data.connected || false,
        userId: data.userId || '',
        username: data.username || '',
        eligible: data.eligible || false,
        chatCommandsEnabled: data.chatCommandsEnabled || false,
        hasTokens: data.hasTokens || false,
        reason: data.reason || (data.connected ? 'Connected' : 'Not connected'),
        lastUpdated: data.lastUpdated || new Date().toISOString()
      })
    })

    // Handle Discord notification status updates
    newSocket.on('discordNotificationPending', (data) => {
      console.log('📋 Discord notification pending:', data)
      setDiscordNotificationStatus(prevStatus => ({
        ...prevStatus,
        status: 'pending',
        pendingChannelInfo: data.waitingFor?.includes('channel'),
        pendingStreamInfo: data.waitingFor?.includes('stream'),
        currentStreamId: data.streamId
      }))
    })

    newSocket.on('discordNotificationSent', (data) => {
      console.log('✅ Discord notification sent:', data)
      setDiscordNotificationStatus(prevStatus => ({
        ...prevStatus,
        status: 'sent',
        lastNotificationSent: data.sentAt || new Date().toISOString(),
        currentStreamId: data.streamId,
        pendingChannelInfo: false,
        pendingStreamInfo: false,
        setupSteps: []
      }))
    })

    newSocket.on('discordNotificationFailed', (data) => {
      console.log('❌ Discord notification failed:', data)
      setDiscordNotificationStatus(prevStatus => ({
        ...prevStatus,
        status: 'error',
        setupSteps: [`❌ Notification failed: ${data.error || 'Unknown error'}`]
      }))
    })

    newSocket.on('discordNotificationReady', (data) => {
      console.log('🎯 Discord notification ready:', data)

      // Handle different ready states
      if (data.monitoringStopped) {
        // Monitoring was stopped, reset to ready state
        setDiscordNotificationStatus(prevStatus => ({
          ...prevStatus,
          status: 'ready',
          pendingChannelInfo: false,
          pendingStreamInfo: false,
          hasChannelInfo: false,
          channelInfo: null,
          setupSteps: []
        }))
      } else if (data.hasChannelInfo) {
        // Channel info received, ready to stream
        setDiscordNotificationStatus(prevStatus => ({
          ...prevStatus,
          status: 'channel_ready',
          hasChannelInfo: data.hasChannelInfo,
          channelInfo: data.channelInfo,
          pendingChannelInfo: false,
          pendingStreamInfo: data.waitingFor?.includes('stream')
        }))
      }
    })

    newSocket.on('discordNotificationReset', (data) => {
      console.log('🔄 Discord notification reset:', data)

      // Reset Discord notification status to ready for next stream
      setDiscordNotificationStatus(prevStatus => ({
        ...prevStatus,
        status: 'ready',
        pendingChannelInfo: false,
        pendingStreamInfo: false,
        hasChannelInfo: false,
        channelInfo: null,
        lastNotification: data,
        resetReason: data.reason || 'Stream ended'
      }))
    })

    // Request initial EventSub status
    newSocket.on('connect', () => {
      console.log('🔌 Connected to server, requesting EventSub status...')
      // Trigger EventSub status check which will broadcast current status
      const token = localStorage.getItem('authToken')
      if (token) {
        fetch('/api/stream/eventsub-status', {
          credentials: 'include',
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
          }
        }).catch(err => console.warn('❌ Failed to fetch EventSub status on connect:', err))
      }
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

  // EventSub monitoring functions
  const startEventSubMonitoring = async () => {
    setIsStartingMonitoring(true)
    try {
      const token = localStorage.getItem('authToken')
      const response = await fetch('/api/stream/monitor/start', {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      if (response.ok) {
        const result = await response.json()
        console.log('✅ EventSub monitoring started:', result)

        setEventSubStatus({
          connected: true,
          monitoring: true,
          lastConnected: new Date().toISOString()
        })

        alert('🎬 Stream monitoring started! We\'ll detect when you go live automatically.')
      } else {
        const error = await response.json()
        console.error('❌ Failed to start monitoring:', error)
        alert(`Failed to start monitoring: ${error.error || 'Unknown error'}`)
      }
    } catch (error) {
      console.error('❌ Error starting EventSub monitoring:', error)
      alert('Failed to start stream monitoring')
    } finally {
      setIsStartingMonitoring(false)
    }
  }

  const stopEventSubMonitoring = async () => {
    try {
      const token = localStorage.getItem('authToken')
      const response = await fetch('/api/stream/monitor/stop', {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      if (response.ok) {
        const result = await response.json()
        console.log('✅ EventSub monitoring stopped:', result)

        setEventSubStatus({
          connected: false,
          monitoring: false,
          lastConnected: null
        })

        // Clear pending Discord notifications and reset status
        setDiscordNotificationStatus(prev => ({
          ...prev,
          status: prev.hasWebhook ? 'ready' : 'not_configured',
          pendingChannelInfo: false,
          pendingStreamInfo: false,
          lastNotificationSent: null
        }))

        alert('⏹️ Stream monitoring stopped.')
      } else {
        const error = await response.json()
        console.error('❌ Failed to stop monitoring:', error)
        alert(`Failed to stop monitoring: ${error.error || 'Unknown error'}`)
      }
    } catch (error) {
      console.error('❌ Error stopping EventSub monitoring:', error)
      alert('Failed to stop stream monitoring')
    }
  }

  const checkEventSubStatus = async () => {
    try {
      const token = localStorage.getItem('authToken')
      const response = await fetch('/api/stream/monitor/status', {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      if (response.ok) {
        const status = await response.json()
        setEventSubStatus({
          connected: status.connected || false,
          monitoring: status.currentUserMonitored || false,
          lastConnected: status.lastConnected || null
        })

        // Update bot status if included
        if (status.twitchBot) {
          setTwitchBotStatus({
            connected: status.twitchBot.connected || false,
            userId: status.twitchBot.userId || '',
            username: username,
            eligible: status.twitchBot.eligible || false,
            chatCommandsEnabled: status.twitchBot.chatCommandsEnabled || false,
            hasTokens: status.twitchBot.hasTokens || false,
            reason: status.twitchBot.reason || 'Not connected',
            lastUpdated: status.twitchBot.lastConnected || null
          })
        }
      }
    } catch (error) {
      console.error('❌ Error checking EventSub status:', error)
    }
  }

  const checkTwitchBotStatus = async () => {
    try {
      const token = localStorage.getItem('authToken')
      const response = await fetch('/api/stream/bot/status', {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      if (response.ok) {
        const status = await response.json()
        setTwitchBotStatus({
          connected: status.bot?.connected || false,
          userId: status.userId || '',
          username: status.username || username,
          eligible: status.bot?.eligible || false,
          chatCommandsEnabled: status.bot?.chatCommandsEnabled || false,
          hasTokens: status.bot?.hasTokens || false,
          reason: status.bot?.reason || 'Not connected',
          lastUpdated: status.bot?.lastConnected || status.timestamp
        })
      }
    } catch (error) {
      console.error('❌ Error checking Twitch bot status:', error)
    }
  }

  const checkDiscordNotificationStatus = async () => {
    try {
      const token = localStorage.getItem('authToken')

      // Check if Discord webhook is configured
      const webhookResponse = await fetch('/api/user/discord-webhook', {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      let hasWebhook = false
      if (webhookResponse.ok) {
        const webhookData = await webhookResponse.json()
        hasWebhook = !!(webhookData.webhookUrl && webhookData.enabled)
      }

      // Check current stream status
      const countersResponse = await fetch('/api/counters', {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      })

      const isStreamActive = countersResponse.ok && (await countersResponse.json()).streamStarted

      // Determine notification status and setup steps
      let status = 'not_configured'
      let setupSteps = []

      if (!hasWebhook) {
        status = 'not_configured'
        setupSteps = [
          '1. Click "Discord Settings" below',
          '2. Add your Discord webhook URL',
          '3. Start monitoring to receive notifications'
        ]
      } else if (!eventSubStatus.monitoring) {
        status = 'ready'
        setupSteps = [
          '1. ✅ Discord webhook configured',
          '2. Click "Start Monitoring" to enable notifications',
          '3. Go live on Twitch to test'
        ]
      } else if (isStreamActive) {
        status = 'sent'
        setupSteps = []
      } else {
        status = 'ready'
        setupSteps = [
          '1. ✅ Discord webhook configured',
          '2. ✅ Monitoring active',
          '3. Go live on Twitch to trigger notification'
        ]
      }

      setDiscordNotificationStatus({
        status,
        hasWebhook: !!hasWebhook,
        pendingChannelInfo: false,
        pendingStreamInfo: false,
        lastNotificationSent: null,
        currentStreamId: null,
        setupSteps
      })

    } catch (error) {
      console.error('❌ Error checking Discord notification status:', error)
      setDiscordNotificationStatus(prev => ({
        ...prev,
        status: 'error',
        setupSteps: ['❌ Error checking configuration - please refresh']
      }))
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

  // FORCE admin users to user mode if viewMode isn't explicitly set to admin or debug
  if (isAdmin && !['admin', 'user', 'debug'].includes(viewMode)) {
    console.log('🔧 FORCING admin to user mode in render')
    setViewMode('user')
  }

  // Force non-admin users out of debug mode
  if (!isAdmin && viewMode === 'debug') {
    console.log('🔧 FORCING non-admin out of debug mode')
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

  // Show debug dashboard if in debug mode (admin only)
  if (isAdmin && viewMode === 'debug') {
    console.log('🔧 RENDERING DEBUG DASHBOARD - viewMode is debug')

    // Ensure user data is loaded before rendering debug dashboard
    if (!userId || !username || !userRole) {
      console.log('⏳ Waiting for user data to load before rendering debug dashboard...')
      return (
        <div style={{
          display: 'flex',
          justifyContent: 'center',
          alignItems: 'center',
          height: '100vh',
          background: '#1a1a1a',
          color: 'white'
        }}>
          <div>Loading debug dashboard...</div>
        </div>
      )
    }

    return (
      <div>
        <div style={{
          position: 'fixed',
          top: '10px',
          right: '10px',
          zIndex: 1000,
          background: 'rgba(0,0,0,0.8)',
          padding: '10px',
          borderRadius: '8px',
          display: 'flex',
          gap: '8px'
        }}>
          <button
            onClick={() => setViewMode('admin')}
            style={{
              background: '#dc3545',
              color: 'white',
              border: 'none',
              padding: '8px 16px',
              borderRadius: '4px',
              cursor: 'pointer',
              fontSize: '14px'
            }}
            title="Go to admin dashboard"
          >
            🛠️ Admin
          </button>
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
        <DebugDashboard user={{ twitchUserId: userId, username: username, displayName: username, role: userRole }} />
      </div>
    )
  }

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
          borderRadius: '8px',
          display: 'flex',
          gap: '8px'
        }}>
          <button
            onClick={() => setViewMode('debug')}
            style={{
              background: '#f59e0b',
              color: 'white',
              border: 'none',
              padding: '8px 16px',
              borderRadius: '4px',
              cursor: 'pointer',
              fontSize: '14px'
            }}
            title="Go to debug dashboard"
          >
            🔧 Debug
          </button>
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
        <AdminDashboard onNavigateToDebug={() => setViewMode('debug')} />
      </div>
    )
  }

  // EXPLICIT CHECK: Never show AdminDashboard for user mode
  if (isAdmin && viewMode === 'user') {
    console.log('🎬 RENDERING USER PORTAL - Admin in user mode')
  } else if (!isAdmin) {
    console.log('🎬 RENDERING USER PORTAL - Regular user')
  }

  // Helper function to render stream overlay
  const renderStreamOverlay = () => {
    // The overlay is now managed by the /overlay/{userId} route
    // This function is kept for backward compatibility but doesn't render anything
    // Users should add the overlay URL to their streaming software directly
    return null
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

          {/* EventSub Monitoring Status */}
          <div style={{ marginBottom: '15px' }}>
            <div style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: '10px',
              marginBottom: '10px'
            }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                <div style={{
                  width: '12px',
                  height: '12px',
                  borderRadius: '50%',
                  backgroundColor: eventSubStatus.monitoring ? '#28a745' : '#dc3545'
                }}></div>
                <p style={{ margin: 0, color: '#ccc' }}>
                  Monitoring: <strong style={{
                    color: eventSubStatus.monitoring ? '#28a745' : '#dc3545'
                  }}>
                    {eventSubStatus.monitoring ? '🟢 Active' : '🔴 Inactive'}
                  </strong>
                </p>
              </div>

              {eventSubStatus.monitoring ? (
                <button
                  onClick={stopEventSubMonitoring}
                  style={{
                    background: '#dc3545',
                    color: '#fff',
                    border: 'none',
                    padding: '6px 12px',
                    borderRadius: '4px',
                    cursor: 'pointer',
                    fontSize: '12px'
                  }}
                >
                  ⏹️ Stop
                </button>
              ) : (
                <button
                  onClick={startEventSubMonitoring}
                  disabled={isStartingMonitoring}
                  style={{
                    background: isStartingMonitoring ? '#6c757d' : '#28a745',
                    color: '#fff',
                    border: 'none',
                    padding: '6px 12px',
                    borderRadius: '4px',
                    cursor: isStartingMonitoring ? 'not-allowed' : 'pointer',
                    fontSize: '12px'
                  }}
                >
                  {isStartingMonitoring ? '⏳ Starting...' : '▶️ Start Monitoring'}
                </button>
              )}
            </div>
          </div>

          {/* Stream Status - Real-time from EventSub */}
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
              backgroundColor: eventSubStatus.streamStatus === 'live' ? '#28a745' : '#6c757d'
            }}></div>
            <p style={{ margin: 0, color: '#ccc' }}>
              Status: <strong style={{
                color: eventSubStatus.streamStatus === 'live' ? '#28a745' : '#9146ff'
              }}>
                {eventSubStatus.streamStatus === 'live' ? '💎 Live' : '⚫ Offline'}
              </strong>
            </p>
          </div>

          {/* Status Messages - Real-time Updates */}
          {!eventSubStatus.monitoring ? (
            <div style={{
              background: 'rgba(220, 53, 69, 0.1)',
              border: '1px solid rgba(220, 53, 69, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#dc3545'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                🚨 <strong>Stream monitoring not started!</strong><br/>
                <small>Click "Start Monitoring" to detect when you go live on Twitch</small>
              </p>
            </div>
          ) : eventSubStatus.streamStatus === 'live' ? (
            <div style={{
              background: 'rgba(40, 167, 69, 0.1)',
              border: '1px solid rgba(40, 167, 69, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#28a745'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                💎 <strong>LIVE on Twitch!</strong><br/>
                <small>Stream detected at {eventSubStatus.lastStreamStart ? new Date(eventSubStatus.lastStreamStart).toLocaleTimeString() : 'recently'} • Monitoring continues</small>
              </p>
            </div>
          ) : eventSubStatus.connected ? (
            <div style={{
              background: 'rgba(108, 117, 125, 0.1)',
              border: '1px solid rgba(108, 117, 125, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#6c757d'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                📡 <strong>Monitoring active - stream offline</strong><br/>
                <small>EventSub connected • Will update when you go live • Click "Stop" to end monitoring</small>
              </p>
            </div>
          ) : (
            <div style={{
              background: 'rgba(255, 193, 7, 0.1)',
              border: '1px solid rgba(255, 193, 7, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#ffc107'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                ⚠️ <strong>Connection issues detected</strong><br/>
                <small>EventSub disconnected • Last connected: {eventSubStatus.lastConnected ? new Date(eventSubStatus.lastConnected).toLocaleString() : 'Never'}</small>
              </p>
            </div>
          )}
        </div>

        {/* Twitch Bot Status */}
        <div style={{
          background: 'rgba(0, 0, 0, 0.3)',
          padding: '20px',
          borderRadius: '12px',
          marginTop: '20px',
          border: '1px solid rgba(255, 255, 255, 0.1)'
        }}>
          <h3 style={{ marginBottom: '15px', color: '#fff' }}>🤖 Twitch Chat Bot</h3>

          {/* Bot Connection Status */}
          <div style={{ marginBottom: '15px' }}>
            <div style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: '10px',
              marginBottom: '10px'
            }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                <div style={{
                  width: '12px',
                  height: '12px',
                  borderRadius: '50%',
                  backgroundColor: twitchBotStatus.connected ? '#28a745' : '#dc3545'
                }}></div>
                <p style={{ margin: 0, color: '#ccc' }}>
                  Bot Status: <strong style={{
                    color: twitchBotStatus.connected ? '#28a745' : '#dc3545'
                  }}>
                    {twitchBotStatus.connected ? '🟢 Connected' : '🔴 Disconnected'}
                  </strong>
                </p>
              </div>

              {eventSubStatus.monitoring && (
                <div style={{ fontSize: '12px', color: '#6c757d' }}>
                  {twitchBotStatus.connected ? 'Auto-started with monitoring' : 'Not eligible for auto-start'}
                </div>
              )}
            </div>

            {/* Bot Eligibility Status */}
            <div style={{ marginBottom: '10px' }}>
              <p style={{ margin: 0, color: '#ccc', fontSize: '14px' }}>
                Eligibility: <strong style={{
                  color: twitchBotStatus.eligible ? '#28a745' : '#ffc107'
                }}>
                  {twitchBotStatus.eligible ? '✅ Ready' : '⚠️ ' + twitchBotStatus.reason}
                </strong>
              </p>

              {!twitchBotStatus.eligible && (
                <div style={{
                  fontSize: '12px',
                  color: '#6c757d',
                  marginTop: '5px',
                  display: 'flex',
                  gap: '15px'
                }}>
                  <span>Chat Commands: {twitchBotStatus.chatCommandsEnabled ? '✅' : '❌'}</span>
                  <span>Auth Tokens: {twitchBotStatus.hasTokens ? '✅' : '❌'}</span>
                </div>
              )}
            </div>
          </div>

          {/* Bot Status Messages */}
          {!twitchBotStatus.eligible ? (
            <div style={{
              background: 'rgba(255, 193, 7, 0.1)',
              border: '1px solid rgba(255, 193, 7, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#ffc107'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                ⚠️ <strong>Bot not eligible for auto-start</strong><br/>
                <small>{twitchBotStatus.reason}</small>
                {!twitchBotStatus.chatCommandsEnabled && (
                  <><br/><small>Enable "Chat Commands" feature in settings to use the bot</small></>
                )}
                {!twitchBotStatus.hasTokens && (
                  <><br/><small>Re-authenticate with Twitch to refresh your tokens</small></>
                )}
              </p>
            </div>
          ) : twitchBotStatus.connected ? (
            <div style={{
              background: 'rgba(40, 167, 69, 0.1)',
              border: '1px solid rgba(40, 167, 69, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#28a745'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                🤖 <strong>Bot active in your chat!</strong><br/>
                <small>Listening for commands • Connected as @{twitchBotStatus.username || username} • Auto-manages with monitoring</small>
              </p>
            </div>
          ) : eventSubStatus.monitoring ? (
            <div style={{
              background: 'rgba(220, 53, 69, 0.1)',
              border: '1px solid rgba(220, 53, 69, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#dc3545'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                🔴 <strong>Bot failed to connect</strong><br/>
                <small>Monitoring started but bot connection failed • Check your Twitch authentication</small>
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
                📱 <strong>Bot ready for monitoring</strong><br/>
                <small>Will auto-connect when you start stream monitoring • Eligible for chat commands</small>
              </p>
            </div>
          )}
        </div>

        {/* Discord Notification Status */}
        <div style={{
          background: 'rgba(255, 255, 255, 0.05)',
          borderRadius: '8px',
          padding: '15px',
          marginTop: '15px',
          border: '1px solid rgba(255, 255, 255, 0.1)'
        }}>
          <h4 style={{ marginBottom: '15px', color: '#fff', fontSize: '16px' }}>📢 Discord Notifications</h4>

          {/* Discord Notification Status Indicator */}
          <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: '10px',
            marginBottom: '15px'
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              <div style={{
                width: '12px',
                height: '12px',
                borderRadius: '50%',
                backgroundColor:
                  discordNotificationStatus.status === 'sent' ? '#28a745' :
                  discordNotificationStatus.status === 'pending' ? '#ffc107' :
                  discordNotificationStatus.status === 'channel_ready' ? '#6f42c1' :
                  discordNotificationStatus.status === 'ready' ? (eventSubStatus.monitoring ? '#17a2b8' : '#fd7e14') :
                  discordNotificationStatus.status === 'error' ? '#dc3545' : '#6c757d'
              }}></div>
              <p style={{ margin: 0, color: '#ccc' }}>
                Status: <strong style={{
                  color:
                    discordNotificationStatus.status === 'sent' ? '#28a745' :
                    discordNotificationStatus.status === 'pending' ? '#ffc107' :
                    discordNotificationStatus.status === 'channel_ready' ? '#6f42c1' :
                    discordNotificationStatus.status === 'ready' ? (eventSubStatus.monitoring ? '#17a2b8' : '#fd7e14') :
                    discordNotificationStatus.status === 'error' ? '#dc3545' : '#6c757d'
                }}>
                  {discordNotificationStatus.status === 'sent' ? '✅ Notification Sent' :
                   discordNotificationStatus.status === 'pending' ? '⏳ Waiting for Stream Data' :
                   discordNotificationStatus.status === 'channel_ready' ? '🎬 Ready to Stream' :
                   discordNotificationStatus.status === 'ready' ? (eventSubStatus.monitoring ? '🎯 Ready' : '▶️ Start Monitoring') :
                   discordNotificationStatus.status === 'error' ? '❌ Error' : '⚙️ Not Configured'}
                </strong>
              </p>
            </div>

            <button
              onClick={() => setShowDiscordSettings(true)}
              style={{
                background: '#5865F2',
                color: '#fff',
                border: 'none',
                padding: '6px 12px',
                borderRadius: '4px',
                cursor: 'pointer',
                fontSize: '12px'
              }}
            >
              ⚙️ Discord Settings
            </button>
          </div>

          {/* Real-time Status Messages */}
          {discordNotificationStatus.status === 'sent' ? (
            <div style={{
              background: 'rgba(40, 167, 69, 0.1)',
              border: '1px solid rgba(40, 167, 69, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#28a745'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                🎉 <strong>Discord notification sent!</strong><br/>
                <small>
                  {discordNotificationStatus.lastNotificationSent ?
                    `Sent at ${new Date(discordNotificationStatus.lastNotificationSent).toLocaleTimeString()}` :
                    'Notification delivered to your Discord server'
                  } • No duplicate notifications will be sent while stream is active
                </small>
              </p>
            </div>
          ) : discordNotificationStatus.status === 'pending' ? (
            <div style={{
              background: 'rgba(255, 193, 7, 0.1)',
              border: '1px solid rgba(255, 193, 7, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#ffc107'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                ⏳ <strong>Waiting for complete stream information...</strong><br/>
                <small>
                  {discordNotificationStatus.pendingChannelInfo && discordNotificationStatus.pendingStreamInfo ?
                    'Waiting for stream title, category, and stream status' :
                    discordNotificationStatus.pendingChannelInfo ?
                    'Waiting for stream title and category information' :
                    'Waiting for stream to go live'
                  }
                </small>
              </p>
              <div style={{
                marginTop: '8px',
                padding: '8px',
                background: 'rgba(255, 255, 255, 0.05)',
                borderRadius: '4px',
                fontSize: '13px'
              }}>
                <strong>💡 Action Required:</strong><br/>
                Please update your channel settings in OBS or Twitch:
                <ul style={{ margin: '4px 0', paddingLeft: '18px' }}>
                  <li>Change your stream title or category</li>
                  <li>Click "Done" to save the changes</li>
                </ul>
                <small style={{ fontStyle: 'italic', opacity: 0.8 }}>
                  You must make actual changes - this triggers the channel update event needed for notifications
                </small>
              </div>
            </div>
          ) : discordNotificationStatus.status === 'channel_ready' ? (
            <div style={{
              background: 'rgba(111, 66, 193, 0.1)',
              border: '1px solid rgba(111, 66, 193, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#6f42c1'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                🎬 <strong>Channel information received - Ready to stream!</strong><br/>
                <small>
                  {discordNotificationStatus.channelInfo ?
                    `Title: "${discordNotificationStatus.channelInfo.title}" • Category: "${discordNotificationStatus.channelInfo.category}"` :
                    'Channel settings updated successfully'
                  }
                </small>
              </p>
              <div style={{
                marginTop: '8px',
                padding: '8px',
                background: 'rgba(255, 255, 255, 0.05)',
                borderRadius: '4px',
                fontSize: '13px'
              }}>
                <strong>🚀 Ready for Action:</strong><br/>
                Start streaming in OBS to automatically trigger your Discord notification!
                <br/><small style={{ fontStyle: 'italic', opacity: 0.8 }}>
                  Notification will be sent as soon as your stream goes live
                </small>
              </div>
            </div>
          ) : discordNotificationStatus.status === 'ready' ? (
            eventSubStatus.monitoring ? (
              <div style={{
                background: 'rgba(23, 162, 184, 0.1)',
                border: '1px solid rgba(23, 162, 184, 0.3)',
                borderRadius: '8px',
                padding: '12px',
                color: '#17a2b8'
              }}>
                <p style={{ margin: 0, fontSize: '14px' }}>
                  {discordNotificationStatus.resetReason ? (
                    <>
                      🔄 <strong>Ready for next stream notification!</strong><br/>
                      <small>Previous stream ended • Notification system reset • Go live to send notification</small>
                    </>
                  ) : (
                    <>
                      🎯 <strong>Ready for notifications!</strong><br/>
                      <small>Discord webhook configured and monitoring active • Go live to test</small>
                    </>
                  )}
                </p>
              </div>
            ) : (
              <div style={{
                background: 'rgba(253, 126, 20, 0.1)',
                border: '1px solid rgba(253, 126, 20, 0.3)',
                borderRadius: '8px',
                padding: '12px',
                color: '#fd7e14'
              }}>
                <p style={{ margin: 0, fontSize: '14px' }}>
                  ▶️ <strong>Start monitoring to enable notifications</strong><br/>
                  <small>Discord webhook is configured but EventSub monitoring is not active</small>
                </p>
                <div style={{
                  marginTop: '8px',
                  padding: '8px',
                  background: 'rgba(255, 255, 255, 0.05)',
                  borderRadius: '4px',
                  fontSize: '13px'
                }}>
                  <strong>👆 Action Required:</strong><br/>
                  Click "Start Monitoring" in the EventSub section above to begin automatic stream detection
                </div>
              </div>
            )
          ) : discordNotificationStatus.status === 'error' ? (
            <div style={{
              background: 'rgba(220, 53, 69, 0.1)',
              border: '1px solid rgba(220, 53, 69, 0.3)',
              borderRadius: '8px',
              padding: '12px',
              color: '#dc3545'
            }}>
              <p style={{ margin: 0, fontSize: '14px' }}>
                ❌ <strong>Notification failed</strong><br/>
                <small>Check Discord webhook settings and try again</small>
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
              <div>
                <p style={{ margin: '0 0 10px 0', fontSize: '14px' }}>
                  ⚙️ <strong>Setup required</strong>
                </p>
                {discordNotificationStatus.setupSteps.length > 0 && (
                  <div style={{ fontSize: '12px', lineHeight: '1.4' }}>
                    {discordNotificationStatus.setupSteps.map((step, index) => (
                      <div key={index} style={{ marginBottom: '2px' }}>
                        {step}
                      </div>
                    ))}
                  </div>
                )}
              </div>
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
                <h3 style={{ color: '#fff', marginTop: '20px' }}>� OBS ??? Setup (Browser Source)</h3>
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

                <h3 style={{ color: '#fff', marginTop: '20px' }}>🎮 Counter Controls</h3>
                <p>• Use <strong>+ {'/'} -</strong> buttons to modify counters</p>
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

        {/* Overlay Settings Modal */}
        <OverlaySettingsModal
          isOpen={showSettingsModal}
          onClose={() => setShowSettingsModal(false)}
          user={{ userId, username }}
          isAdminMode={false}
        />
        {/* Stream Overlay - Rendered when stream is live */}
        {renderStreamOverlay()}
      </div>
    </div>
  )
}

export default App

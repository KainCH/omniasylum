import { useState, useEffect } from 'react'
import { io } from 'socket.io-client'
import Counter from './components/Counter'
import AuthPrompt from './components/AuthPrompt'
import ConnectionStatus from './components/ConnectionStatus'
import AdminDashboard from './components/AdminDashboard'
import './App.css'

function App() {
  const [isAuthenticated, setIsAuthenticated] = useState(false)
  const [isLoading, setIsLoading] = useState(true)
  const [socket, setSocket] = useState(null)
  const [connectionStatus, setConnectionStatus] = useState('disconnected')
  const [counters, setCounters] = useState({ deaths: 0, swears: 0 })
  const [userRole, setUserRole] = useState('streamer')
  const [username, setUsername] = useState('')

  // Check authentication status
  useEffect(() => {
    checkAuth()
    checkUrlForToken()
  }, [])

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
        setUserRole(payload.role || 'streamer')
        setIsAuthenticated(true) // Set authenticated status
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
    if (isAuthenticated && !socket) {
      initializeSocket()
    }

    return () => {
      if (socket) {
        socket.disconnect()
      }
    }
  }, [isAuthenticated])

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

        // Also decode token to get user info if we haven't already
        if (!username) {
          try {
            const payload = JSON.parse(atob(token.split('.')[1]))
            setUsername(payload.username)
            setUserRole(payload.role || 'streamer')
            console.log(`✅ User ${payload.username} authenticated as ${payload.role || 'streamer'}`)
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
    const token = localStorage.getItem('authToken')

    const newSocket = io('/', {
      transports: ['websocket', 'polling'],
      auth: {
        token: token
      }
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

    newSocket.on('error', (error) => {
      console.error('❌ Socket error:', error)
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

  const resetCounters = () => {
    if (window.confirm('Are you sure you want to reset all counters?')) {
      sendSocketEvent('resetCounters')
    }
  }

  const exportData = () => {
    const data = {
      deaths: counters.deaths,
      swears: counters.swears,
      total: counters.deaths + counters.swears,
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

  // Handle keyboard shortcuts
  useEffect(() => {
    const handleKeyPress = (e) => {
      if (!isAuthenticated) return

      switch (e.key.toLowerCase()) {
        case 'd':
          incrementDeaths()
          break
        case 's':
          incrementSwears()
          break
        // Removed 'r' shortcut to prevent accidental counter resets
        default:
          break
      }
    }

    window.addEventListener('keydown', handleKeyPress)
    return () => window.removeEventListener('keydown', handleKeyPress)
  }, [isAuthenticated, socket])

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

  // Show admin dashboard for admin users
  if (userRole === 'admin' && username.toLowerCase() === 'riress') {
    return <AdminDashboard />
  }

  return (
    <div className="app">
      <div className="container">
        <header className="app-header">
          <h1>🎮 OmniForgeStream Counter</h1>
          <ConnectionStatus status={connectionStatus} />
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

        <div className="keyboard-hints">
          <p>💡 <strong>Keyboard shortcuts:</strong> D = Deaths, S = Swears</p>
        </div>
      </div>
    </div>
  )
}

export default App

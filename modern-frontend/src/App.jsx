import { useState, useEffect } from 'react'
import { io } from 'socket.io-client'
import Counter from './components/Counter'
import AuthPrompt from './components/AuthPrompt'
import ConnectionStatus from './components/ConnectionStatus'
import './App.css'

function App() {
  const [isAuthenticated, setIsAuthenticated] = useState(false)
  const [isLoading, setIsLoading] = useState(true)
  const [socket, setSocket] = useState(null)
  const [connectionStatus, setConnectionStatus] = useState('disconnected')
  const [counters, setCounters] = useState({ deaths: 0, swears: 0 })

  // Check authentication status
  useEffect(() => {
    checkAuth()
  }, [])

  // Initialize socket when authenticated
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

      // Try to get user data
      const userResponse = await fetch('/api/counters', {
        credentials: 'include'
      })

      if (userResponse.ok) {
        const data = await userResponse.json()
        setIsAuthenticated(true)
        setCounters(data)
        console.log('✅ User authenticated')
      } else {
        console.log('🔐 User not authenticated')
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
    const newSocket = io('/', {
      transports: ['websocket', 'polling']
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
        case 'r':
          resetCounters()
          break
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
          <p>💡 <strong>Keyboard shortcuts:</strong> D = Deaths, S = Swears, R = Reset</p>
        </div>
      </div>
    </div>
  )
}

export default App

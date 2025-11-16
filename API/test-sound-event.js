const io = require('socket.io-client');

// Connect to the local server
const socket = io('http://localhost:3000');

socket.on('connect', () => {
  console.log('✅ Connected to server');

  // Wait a moment, then emit a test newBitsUse event
  setTimeout(() => {
    console.log('🔊 Sending test newBitsUse event...');

    // This simulates the server emitting the event
    socket.emit('testEvent', 'newBitsUse', {
      userId: 'testuser123',
      username: 'testchannel',
      user: 'TestBitsUser',
      bits: 100,
      message: 'Test bits message!',
      eventType: 'cheer',
      isAnonymous: false,
      timestamp: new Date().toISOString(),
      alertConfig: {
        enabled: true,
        soundFile: 'pillRattle.mp3',
        textPrompt: '[User] used [X] bits!',
        backgroundColor: '#1a0d0d',
        textColor: '#ffffff',
        borderColor: '#d4af37'
      }
    });

    console.log('📡 Event sent');

    // Disconnect after a few seconds
    setTimeout(() => {
      socket.disconnect();
      console.log('👋 Disconnected');
      process.exit(0);
    }, 5000);
  }, 1000);
});

socket.on('connect_error', (error) => {
  console.error('❌ Connection failed:', error);
  process.exit(1);
});

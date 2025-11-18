# Copilot Instructions for Riress Streaming

## Project Overview
This is a Node.js/TypeScript application that integrates with Bambu Labs X1C 3D printers to provide real-time streaming overlays for Twitch streams using OBS Browser Sources. The application connects to the printer via MQTT, processes live data, and serves it through WebSockets to browser-based overlays.

## Architecture
- **Backend**: Node.js + TypeScript + Express
- **Communication**: MQTT (Bambu Labs printer) → Node.js App → WebSocket → OBS Browser Source
- **Frontend**: Vanilla HTML/CSS/JavaScript (no frameworks for OBS compatibility)
- **Real-time**: WebSocket for live updates

## Key Technologies
- **MQTT.js**: Printer communication protocol
- **WebSocket (ws)**: Real-time web communication  
- **Express.js**: Web server and API endpoints
- **TypeScript**: Type safety and better development experience

## Project Structure
```
src/
├── index.ts              # Main application entry point
├── config/               # Configuration management
├── services/            
│   ├── BambuMQTTService.ts    # MQTT communication with printer
│   └── WebSocketService.ts    # WebSocket server for overlays
└── types/               # TypeScript type definitions

public/
├── overlays/            # OBS Browser Source pages
├── css/                 # Styling for overlays
└── js/                  # Client-side JavaScript
```

## Development Guidelines

### Code Style
- Use TypeScript strict mode with comprehensive type definitions
- Follow async/await patterns for asynchronous operations
- Implement proper error handling and logging
- Use descriptive variable and function names
- Add JSDoc comments for public methods and complex logic

### MQTT Integration
- The printer communicates using JSON messages over MQTT
- Key topics: `device/{serial|ip}/report` for status updates
- Important data points: temperatures, print progress, system status, filament info
- Handle connection failures gracefully with reconnection logic
- Validate and sanitize incoming MQTT data

### WebSocket Communication
- Broadcast printer status updates to all connected overlay clients
- Implement message types for different data categories (progress, temperature, errors)
- Handle client disconnections and maintain connection health
- Support multiple overlay instances simultaneously

### OBS Overlay Design
- Keep overlays lightweight and performant for streaming
- Use CSS animations sparingly to avoid performance issues
- Ensure responsive design works across different overlay sizes
- Implement fallback states for connection issues
- Use readable fonts and high contrast for stream visibility

### Security Considerations
- Validate all input data from MQTT and WebSocket sources
- Use environment variables for sensitive configuration
- Implement proper CORS policies for web endpoints
- Add rate limiting for WebSocket connections if needed

### Error Handling
- Log errors with appropriate severity levels
- Provide meaningful error messages to overlay users
- Implement graceful degradation when printer is offline
- Use try-catch blocks around async operations

### Testing Approach
- Test MQTT connection reliability with network interruptions
- Verify WebSocket broadcasting to multiple clients
- Test overlay performance in OBS Browser Source
- Validate data parsing and type safety

## Common Patterns

### Service Integration
```typescript
// Always use dependency injection for services
class App {
  constructor(
    private mqttService: BambuMQTTService,
    private wsService: WebSocketService
  ) {}
}
```

### Data Flow
```typescript
// MQTT → Process → WebSocket → Overlay
mqttService.on('statusUpdate', (status) => {
  wsService.broadcastPrinterStatus(status);
});
```

### Configuration
```typescript
// Use centralized config with validation
Config.validate(); // Throws if required values missing
```

## Integration Points

### OBS Setup
1. Add Browser Source in OBS
2. Set URL to `http://localhost:3000/overlay`
3. Configure size (recommended: 320x400)
4. Enable "Shutdown source when not visible" for performance

### Twitch Features (Future)
- Chat commands for print status (`!print`, `!temp`)
- Channel point rewards integration
- Stream alerts for print completion
- Webhook integration for notifications

## Troubleshooting

### Common Issues
- **MQTT Connection Failed**: Check printer IP and access code
- **WebSocket Not Connecting**: Verify port 3001 is available
- **Overlay Not Updating**: Check browser console for errors
- **Performance Issues**: Reduce update frequency or animation complexity

### Debugging
- Enable detailed logging with `LOG_LEVEL=debug`
- Use browser dev tools for overlay debugging
- Monitor WebSocket connection health
- Check MQTT message parsing errors

## Development Commands
```bash
npm run dev      # Start development server with auto-reload
npm run build    # Compile TypeScript to JavaScript
npm run start    # Run production build
npm run lint     # Check code style and errors
```

## Environment Variables
Create `.env` file based on `.env.example`:
- `PRINTER_IP`: Your printer's local IP address
- `PRINTER_ACCESS_CODE`: Printer's access code for MQTT
- `PORT`: HTTP server port (default: 3000)
- `WS_PORT`: WebSocket server port (default: 3001)

## Future Enhancements
- Multi-printer support
- Advanced overlay themes and customization
- Integration with other streaming platforms
- Mobile dashboard for remote monitoring
- Print queue management
- Historical data logging and analytics
# Riress Streaming - Bambu Labs X1C Integration

🖨️ **Real-time 3D printer monitoring for Twitch streams using OBS Browser Sources**

A production-ready Node.js/TypeScript application that connects to your Bambu Labs X1C 3D printer via MQTT and provides beautiful, real-time overlays for your streaming setup. Perfect for makers, engineers, and content creators who want to share their 3D printing journey with their audience.

## ✨ Features

- **🔴 Live Print Monitoring**: Real-time print progress, temperatures, and status updates
- **📺 OBS Integration**: Browser Source compatible overlays that work seamlessly with OBS Studio
- **🎛️ Professional Design**: Clean, modern dark theme interface optimized for streaming
- **📡 MQTT Communication**: Secure TLS connection to your Bambu Labs X1C printer
- **🔌 WebSocket Updates**: Real-time data streaming with automatic reconnection
- **⚡ High Performance**: Optimized TypeScript codebase with gzip compression and minimal resource usage
- **🎨 Fully Customizable**: Comprehensive CSS theming and modular component design
- **🚨 Advanced Error Handling**: Robust connection management and graceful failure recovery
- **🔧 Developer Ready**: Full TypeScript support with ESLint, comprehensive logging
- **📊 REST API**: Complete API endpoints with optimized caching and compression
- **🏠 Local Development**: Optimized for local use with minimal security overhead for maximum performance

## 🎯 Perfect For

- **Twitch Streamers** showing 3D printing content
- **YouTube Creators** documenting maker projects  
- **Engineers/Designers** sharing prototyping processes
- **Educators** teaching 3D printing concepts
- **Hobbyists** wanting to monitor prints remotely

## 🚀 Quick Start

### Prerequisites

- **Node.js 18+** installed on your system
- **Bambu Labs X1C printer** with network access and LAN mode enabled
- **OBS Studio** (for streaming integration)
- Your printer's **IP address** and **access code**

### Installation

1. **Clone the repository**
   
   ```bash
   git clone https://github.com/KainCH/riress-streaming.git
   cd riress-streaming
   ```

2. **Install dependencies**
   
   ```bash
   npm install
   ```

3. **Configure environment**
   
   ```bash
   cp .env.example .env
   # Edit .env with your printer details (see Configuration section)
   ```

4. **Build the application**
   
   ```bash
   npm run build
   ```

5. **Start the application**
   
   ```bash
   # Development mode (with auto-reload)
   npm run dev
   
   # Or production mode
   npm start
   ```

6. **Add to OBS**
   - Add **Browser Source** in OBS
   - URL: `http://localhost:3000/overlay`
   - Width: **320px**, Height: **400px** (recommended)
   - Check **Shutdown source when not visible** for better performance

## ⚙️ Configuration

Edit your `.env` file with your printer information:

```env
# Environment
NODE_ENV=development

# Server Configuration
PORT=3000
WS_PORT=3001
LOG_LEVEL=info

# Bambu Labs X1C Printer Configuration
PRINTER_IP=192.168.1.100
PRINTER_ACCESS_CODE=your_access_code_here
PRINTER_SERIAL=your_printer_serial_here

# MQTT Configuration (Bambu Labs specific)
MQTT_PORT=8883
MQTT_USE_SSL=true
MQTT_USERNAME=bblp
```

### Finding Your Printer Information

1. **IP Address**: Check your router's admin panel or printer's network settings
2. **Access Code**: Found in Bambu Studio → Printer Settings → Network → LAN Only Mode
3. **Serial Number**: Located on the printer label or in Bambu Studio device info

### MQTT Settings for Bambu Labs X1C

The X1C uses Bambu Labs' proprietary MQTT implementation with the following **required** settings:

| Setting | Value | Notes |
|---------|-------|-------|
| **Port** | `8883` | MQTT over TLS (secure) |
| **Protocol** | `mqtts://` | TLS encryption is required |
| **Username** | `bblp` | Fixed username for all Bambu printers |
| **Password** | Your Access Code | Found in Bambu Studio |
| **Client ID** | Any unique string | App generates: `riress-streaming-{timestamp}` |
| **TLS** | Required | Uses self-signed certificates |
| **Certificate Validation** | Disabled | `rejectUnauthorized: false` |

#### Required Configuration Steps

1. **Enable LAN Mode (Optional but Recommended)**
   - Go to printer settings: Gear Icon → General → LAN Only Mode
   - You don't need to enable it, but it helps isolate local traffic

2. **Get the Access Code**
   - In the LAN Only section, you'll find a hexadecimal access code
   - This access code is used as the MQTT password

3. **Extract the Broker Certificate (Optional for Testing)**

   ```bash
   openssl s_client -showcerts -connect <printer_ip>:8883 </dev/null | sed -n -e '/-.BEGIN/,/-.END/ p' > blcert.pem
   ```

   Replace `<printer_ip>` with your printer's IP address.

4. **Network Requirements**:
   - Printer and computer must be on the same local network
   - Port 8883 must be accessible (not blocked by firewall)

#### Testing MQTT Connection

You can test the connection using mosquitto_sub:

```bash
mosquitto_sub -h <printer_ip> -p 8883 --cafile ./blcert.pem --insecure -u bblp -P <access_code> -t device/#
```

Replace:

- `<printer_ip>` with your printer's IP address
- `<access_code>` with the hex string from step 2#### MQTT Topics Used

The application subscribes to and publishes on these topics:

- **Subscribe**: `device/{serial_or_ip}/report` - Receives printer status updates
- **Publish**: `device/{serial_or_ip}/request` - Sends commands to printer

#### Troubleshooting MQTT Connection

##### MQTT Connection Failed

- Verify the access code is correct and recently generated
- Ensure LAN mode is enabled on the printer
- Check that port 8883 is not blocked by firewall
- Confirm printer and computer are on the same network subnet

##### Certificate Issues

- The X1C uses self-signed certificates - this is normal
- The app automatically sets `rejectUnauthorized: false`
- For enhanced security, extract the certificate using the OpenSSL command above

##### Advanced: Using Extracted Certificate

If you want to use the extracted certificate for better security:

1. Extract the certificate as shown above
2. Place `blcert.pem` in your project root
3. Modify the MQTT service to use the certificate file
4. Set `rejectUnauthorized: true` and provide the CA file path

**Note**: The default configuration works fine for most users.

## 📊 Data Display

The overlay provides real-time display of:

### 🖨️ Print Progress Section
- **Visual Progress Bar**: Animated progress bar with precise percentage
- **Print Stage**: Current status (Idle, Printing, Paused, Completed, etc.)
- **Time Remaining**: Estimated completion time
- **Print Job Name**: Currently loaded file name

### 🌡️ Temperature Monitoring
- **Nozzle Temperature**: Current and target temperatures
- **Bed Temperature**: Current and target temperatures  
- **Real-time Updates**: Live temperature monitoring with smooth transitions

### 💾 Filament Information
- **Active Filament**: Currently loaded filament details
- **Filament Type**: Material type (PLA, ABS, PETG, etc.)
- **Filament Color**: Color information from AMS system
- **AMS Support**: Full 4-tray AMS monitoring with real-time updates
  - See [AMS_MAPPING.md](AMS_MAPPING.md) for multi-color print configuration
  - Track filament type, color, and remaining percentage for all slots
  - Visual indication of active tray during prints

### 📡 System Status
- **Connection Status**: Real-time printer connectivity
- **System State**: Overall printer status
- **Error Alerts**: Connection issues and printer errors
- **Network Information**: Connection type and signal strength

### 🎨 Visual Features
- **Dark Theme**: Optimized for streaming with high contrast
- **Smooth Animations**: Professional transitions and hover effects
- **Status Icons**: Visual indicators for different states
- **Responsive Design**: Scales well with different overlay sizes

## 🎨 Customization

The overlay is designed to be easily customizable:

- **Colors**: Edit `public/css/overlay.css` variables
- **Layout**: Modify `public/overlays/main.html`
- **Data Points**: Adjust what's displayed in the services
- **Size**: Responsive design works with different overlay dimensions

### Color Themes

The default dark theme is optimized for streaming, but you can easily create custom themes by modifying the CSS variables in `overlay.css`.

## 🔧 Development

### Project Structure

```text
src/
├── index.ts                    # Main application entry point
├── config/
│   └── index.ts               # Configuration management and validation
├── services/
│   ├── BambuMQTTService.ts    # MQTT client for printer communication
│   ├── WebSocketService.ts    # Real-time WebSocket server
│   └── index.ts               # Service exports
└── types/
    ├── index.ts               # Type exports
    └── printer.ts             # Bambu Labs printer type definitions

public/
├── overlays/
│   └── main.html             # Primary OBS overlay template
├── css/
│   └── overlay.css           # Complete overlay styling with animations
└── js/
    └── overlay.js            # Client-side WebSocket and DOM management

Configuration Files:
├── package.json              # Dependencies and scripts
├── tsconfig.json            # TypeScript configuration
├── .eslintrc.js             # ESLint rules and configuration
├── .env.example             # Environment variable template
└── riress-streaming.code-workspace  # VS Code workspace with MCP
```

### Available Scripts

```bash
# Development
npm run dev              # Start with hot-reload using ts-node-dev
npm run build           # Compile TypeScript to dist/
npm run start           # Run compiled production build

# Code Quality
npm run lint            # Run ESLint on TypeScript files
npm run type-check      # TypeScript type checking without compilation
npm run clean           # Remove dist/ directory

# Development with debugging
LOG_LEVEL=debug npm run dev    # Enable verbose logging
```

### API Endpoints

| Endpoint | Method | Description | Response |
|----------|--------|-------------|----------|
| `/health` | GET | Server and printer health status | JSON status object |
| `/api/status` | GET | Current printer data and state | Complete printer status |
| `/api/config` | GET | Overlay and server configuration | Configuration object |
| `/overlay` | GET | OBS Browser Source overlay page | HTML page |
| `/*` | GET | Static file serving from `public/` | Static assets |

### WebSocket Events

The application uses WebSocket for real-time communication:

```typescript
// Message Types
enum WSMessageType {
  CONNECTION_STATUS = 'connection_status',
  PRINTER_STATUS = 'printer_status',
  ERROR = 'error',
  STATUS_REQUEST = 'status_request'
}

// Client can send:
{ type: 'status_request' }  // Request current printer status

// Server broadcasts:
{ 
  type: 'printer_status', 
  data: PrinterStatus, 
  timestamp: Date 
}
```

### TypeScript Integration

The application is built with comprehensive TypeScript support:

```typescript
// Core Printer Status Interface
interface PrinterStatus {
  printerId: string;
  printerType: string;
  printerName?: string;
  printJob?: PrintJob;
  temperatures: TemperatureData;
  systemStatus: SystemStatus;
  ams?: AMSStatus;  // Automatic Material System
  networkInfo: NetworkInfo;
  lastUpdate: Date;
}

// Print Job Information
interface PrintJob {
  id?: string;
  name?: string;
  fileName?: string;
  progress: number;  // 0-100
  stage: PrintStage;
  timeRemaining?: number;  // seconds
  timeElapsed?: number;
  layerCurrent?: number;
  layerTotal?: number;
}

// Temperature Monitoring
interface TemperatureData {
  bedTemp: number;
  bedTargetTemp: number;
  nozzleTemp: number;
  nozzleTargetTemp: number;
  chamberTemp?: number;
  frameTemp?: number;
}
```

All types are fully documented and provide IntelliSense support for development.

## 🎮 Twitch Integration (Future)

Planned features for Twitch streamers:

- **Chat Commands**: `!print`, `!temp`, `!eta` for viewer interaction
- **Channel Points**: Let viewers spend points to see detailed stats
- **Alerts**: Automatic notifications for print completion
- **Webhooks**: Integration with streaming tools

## 🛠️ Troubleshooting

### Common Issues

#### ❌ MQTT Connection Failed

**Symptoms**: `Failed to connect to printer` or `MQTT connection timeout`

**Solutions**:
- Verify printer IP address is correct and reachable: `ping <printer_ip>`
- Ensure access code is current (regenerate in Bambu Studio if needed)
- Check that LAN Mode is enabled on the printer
- Confirm port 8883 is not blocked by Windows Firewall
- Verify printer and computer are on the same network subnet

#### 🔌 Overlay Not Updating in OBS

**Symptoms**: Static display or "No Data" message in OBS

**Solutions**:
- Check browser console in OBS for JavaScript errors
- Verify WebSocket connection by visiting `/health` endpoint
- Refresh the browser source in OBS (right-click → Refresh)
- Ensure WebSocket port (3001) is not blocked
- Check server logs for WebSocket errors
- Enable "Shutdown source when not visible" for better performance
- Verify browser source dimensions are set to 320x400px

#### 🛡️ Browser Security/CSP Warnings

**Symptoms**: CSP errors about blocked resources or inline styles in browser console

**Solutions**:
- The overlay includes a permissive CSP meta tag for local development
- All resources (CSS, JS, fonts, images) are allowed from any source
- All inline styles have been moved to external CSS files for better performance
- If you still see CSP warnings, they can be safely ignored for local use
- For production deployment, remove the permissive CSP and add proper security headers

#### 📡 No Data Showing

**Symptoms**: Empty overlay or default values

**Solutions**:
- Confirm printer is powered on and connected to network
- Validate `.env` configuration matches your printer settings
- Check server logs with `LOG_LEVEL=debug npm run dev`
- Test API endpoint directly: `http://localhost:3000/api/status`
- Verify printer is not in Cloud-only mode

#### 🖥️ Application Won't Start

**Symptoms**: Server fails to start or crashes immediately

**Solutions**:
- Check Node.js version: `node --version` (requires 18+)
- Verify all dependencies are installed: `npm install`
- Check for port conflicts (3000/3001 already in use)
- Review environment variables in `.env` file
- Run with debugging: `LOG_LEVEL=debug npm run dev`

### Debugging Tools

#### Health Check Endpoint

Visit `http://localhost:3000/health` for real-time status:

```json
{
  "status": "healthy",
  "timestamp": "2025-01-01T12:00:00.000Z",
  "printer": {
    "connected": true,
    "lastUpdate": "2025-01-01T11:59:30.000Z"
  },
  "websocket": {
    "clients": 2
  }
}
```

#### Enable Debug Logging

Run with verbose logging to see detailed MQTT and WebSocket activity:

```bash
LOG_LEVEL=debug npm run dev
```

#### Test MQTT Connection Manually

Use mosquitto_sub to test MQTT connectivity:

```bash
mosquitto_sub -h <printer_ip> -p 8883 --insecure -u bblp -P <access_code> -t device/#
```

### Getting Help

1. **Check the logs**: Always run with `LOG_LEVEL=debug` first
2. **Test endpoints**: Use `/health` and `/api/status` to verify functionality  
3. **Review issues**: Check [GitHub Issues](https://github.com/KainCH/riress-streaming/issues)
4. **Network diagnostics**: Verify connectivity with `ping` and `telnet`
5. **OBS troubleshooting**: Test overlay in regular browser first

## 🤝 Contributing

Contributions are welcome! Please read our contributing guidelines and feel free to submit pull requests or open issues.

### Development Setup

1. Fork the repository
2. Create a feature branch
3. Install dependencies: `npm install`
4. Make your changes
5. Test thoroughly
6. Submit a pull request

## 🛠️ Development Environment

This project includes a comprehensive VS Code workspace with Model Context Protocol (MCP) servers for enhanced development experience with GitHub Copilot.

### MCP Servers Configured

- **Memory MCP**: Persistent conversation context across coding sessions  
- **Filesystem MCP**: Enhanced file operations and project navigation
- **GitHub MCP**: Direct repository integration and issue management
- **Microsoft Docs MCP**: Access to official documentation and best practices

### VS Code Setup

1. **Open the workspace**:
   
   ```bash
   code riress-streaming.code-workspace
   ```

2. **Install MCP dependencies** using the included task:
   - Open Command Palette (`Ctrl+Shift+P`)
   - Run: `Tasks: Run Task` → `Install MCP Dependencies`
   
   Or manually:
   
   ```bash
   npm install -g @modelcontextprotocol/server-memory @modelcontextprotocol/server-filesystem @modelcontextprotocol/server-github @modelcontextprotocol/server-docs
   ```

3. **Configure GitHub integration** (optional):
   - Set `GITHUB_TOKEN` environment variable for enhanced GitHub features

### Recommended Extensions

The workspace automatically suggests these extensions:

- **GitHub Copilot & Copilot Chat**: AI-powered coding assistance
- **TypeScript & JavaScript Language Features**: Enhanced TypeScript support
- **PowerShell**: Windows terminal integration
- **JSON**: Configuration file support

### Development Features

- **Hot Reload**: Automatic restart on file changes with `ts-node-dev`
- **Type Safety**: Full TypeScript configuration with strict mode
- **Code Quality**: ESLint with TypeScript rules
- **Debugging**: Source maps and debugging configuration
- **Local Optimization**: No security overhead for maximum local performance

## 📚 Documentation

Additional documentation is available for specific features:

- **[AMS_MAPPING.md](AMS_MAPPING.md)** - Comprehensive guide for AMS (Automatic Material System) mapping configuration
  - Multi-color print setup with MQTT commands
  - Detailed examples for 1-4 color prints
  - Troubleshooting and best practices
  - Type-safe helper functions and validation
- **[OVERLAYS.md](OVERLAYS.md)** - Detailed information about available overlays
  - Progress & AMS overlay features
  - System Monitor overlay capabilities
  - Banner overlay configuration
  - OBS integration setup for each overlay

## ⚡ Performance Optimizations

This application is optimized for streaming performance:

### Server-Side Optimizations

- **Gzip Compression**: All responses are compressed for faster delivery
- **Smart Caching**: Static assets cached for 24 hours, API config cached for 5 minutes
- **No Security Headers**: Zero security middleware overhead for maximum local performance
- **Static File Header Removal**: Custom middleware strips all security headers from CSS/JS files
- **Efficient Static Serving**: Optimized Express static middleware with ETags
- **Connection Pooling**: WebSocket connections managed efficiently
- **Minimal Middleware Stack**: Only essential middleware for local development
- **Express Optimization**: X-Powered-By header disabled globally

### Client-Side Optimizations

- **Minimal DOM Updates**: Only updates changed elements
- **CSS Animations**: Hardware-accelerated transitions
- **Efficient WebSocket**: Automatic reconnection with exponential backoff
- **Resource Preloading**: Critical CSS and JavaScript loaded first

### OBS Integration Best Practices

- **Browser Source Settings**: Recommended 320x400px size for optimal performance
- **Shutdown When Hidden**: Enable "Shutdown source when not visible" in OBS
- **Hardware Acceleration**: Enable GPU acceleration in OBS browser source
- **Minimal Redraws**: Overlay only updates when printer data changes
- **Zero Header Overhead**: No security headers for fastest possible loading
- **Local Network Only**: Designed for localhost streaming performance

### Performance Testing Results

The optimizations eliminate ALL security and performance warnings:

**Server-Side (HTTP Headers):**
- ✅ **Zero security headers** on static assets (CSS, JS files)
- ✅ **No XSS protection headers** on any endpoints  
- ✅ **No CSP headers** from server
- ✅ **Compressed responses** for faster load times

**Client-Side (Browser CSP):**
- ✅ **Permissive CSP meta tag** allows all resources
- ✅ **No blocked font sources** (data URIs allowed)
- ✅ **No blocked script sources** (inline scripts allowed)
- ✅ **No blocked style sources** (inline styles allowed)
- ✅ **No inline CSS styles** (all styles moved to external CSS files)
- ✅ **Optimized purely for local streaming performance**

## 📜 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- **Bambu Lab** for creating amazing 3D printers with open APIs
- **OBS Studio** for the powerful streaming platform
- **Node.js & TypeScript** communities for excellent tooling
- **3D Printing Community** for inspiration and feedback

---

## Made with ❤️ for the 3D printing and streaming communities

**⭐ Star this repository if you find it useful!**

Copyright (c) 2025 Ryan Hardy - MSFT

# OmniForge Sync Agent

Background application that connects your streaming software (OBS Studio or Streamlabs Desktop) to OmniForge, enabling scene-based counter actions and overlay automation.

## Prerequisites

- **OBS Studio** with obs-websocket v5 (included in OBS 28+), OR
- **Streamlabs Desktop** with Remote Control enabled
- An OmniForge account with Scene Sync enabled

## Quick Start

1. Download and run the Sync Agent
2. Right-click the tray icon and choose **Sign In**
3. Approve the pairing in your browser
4. The agent detects your streaming software automatically
5. A setup instructions page opens with steps for your detected software

## How Detection Works

The agent polls every 5 seconds for supported streaming software:

- **OBS Studio** — detected via WebSocket on port 4455
- **Streamlabs Desktop** — detected via named pipe

When software is found, a toast notification announces which application was detected. No manual configuration is needed — the agent connects automatically.

## OBS Studio Setup

1. Open OBS Studio
2. Go to **Tools > WebSocket Server Settings**
3. Check **Enable WebSocket Server** (default port: 4455)
4. If you set a password, enter it in the agent tray menu:
   Right-click > **Settings > OBS WebSocket Password**

> OBS 28+ includes WebSocket v5 by default. Older versions require the [obs-websocket plugin](https://github.com/obsproject/obs-websocket/releases).

## Streamlabs Desktop Setup

1. Open Streamlabs Desktop
2. Go to **Settings > Remote Control**
3. Enable **Remote Control**
4. The agent connects automatically via named pipe — no token or password needed

## Tray Icon

The system tray icon shows connection status:

| Color  | Meaning                                         |
| ------ | ----------------------------------------------- |
| Green  | Connected to both server and streaming software |
| Yellow | Partially connected (one side disconnected)     |
| Orange | Scanning for streaming software                 |
| Red    | Disconnected from both                          |
| Gray   | Not signed in                                   |

Right-click the tray icon for:
- **Status** — current connection details
- **Open Pre-Flight** — pre-flight checklist in your browser
- **Settings** — auto-start, OBS password
- **Setup Instructions** — opens software-specific configuration guide
- **Sign Out / Sign In** — manage your OmniForge connection

## Auto-Start with Windows

Right-click the tray icon > **Settings > Start with Windows**, or set `"StartWithWindows": true` in the agent config.

## How Scene Sync Works

1. Agent connects to your streaming software and discovers all scenes
2. Agent connects to OmniForge server via SignalR
3. Scene list is reported to the server and persisted
4. When you switch scenes, the agent reports the change
5. The server applies configured scene actions (counter visibility, timers, overtime alerts)

## Code Signing Setup

Windows SmartScreen blocks unsigned executables downloaded from the internet. Signing the agent with an Azure Key Vault certificate removes that warning permanently.

### One-time setup

**1. Create a self-signed code-signing certificate in Key Vault**

```powershell
az keyvault certificate create `
  --vault-name forge-steel-vault `
  --name OmniForgeAgentSigning `
  --policy (az keyvault certificate get-default-policy --out json | ConvertFrom-Json | `
      ForEach-Object { $_.keyProperties.keyType = "RSA"; $_.keyProperties.keySize = 4096; `
                       $_.keyProperties.reuseKey = $true; `
                       $_.x509CertificateProperties.ekus = @("1.3.6.1.5.5.7.3.3"); `
                       $_ } | ConvertTo-Json -Depth 10)
```

> **Full SmartScreen trust requires a purchased EV or OV code-signing certificate** from a CA such as DigiCert or Sectigo. An EV cert bypasses SmartScreen instantly; an OV cert builds reputation over time. Import the purchased PFX into Key Vault with `az keyvault certificate import` using the same name `OmniForgeAgentSigning`.

**2. Grant your identity access to the certificate**

```powershell
az keyvault set-policy `
  --name forge-steel-vault `
  --upn (az account show --query user.name -o tsv) `
  --certificate-permissions get list `
  --key-permissions sign
```

**3. Install AzureSignTool** (one-time, handled automatically by the publish script)

```powershell
dotnet tool install --global AzureSignTool
```

### Publish a signed release

```powershell
# VS Code task: "Publish Sync Agent"
.\.publish-agent.ps1
```

Signing is mandatory — the script always signs before uploading. The certificate and private key stay in Key Vault; nothing sensitive touches disk.

### Why EV over self-signed?

| Certificate type                  | SmartScreen result                                   |
| --------------------------------- | ---------------------------------------------------- |
| None (unsigned)                   | Blocked — "Windows protected your PC"                |
| Self-signed (Key Vault generated) | Warning — users can click through                    |
| OV code-signing (purchased)       | Warning until reputation builds (~100s of downloads) |
| EV code-signing (purchased)       | No warning — immediate trust                         |

## Advanced Configuration

Edit `appsettings.json` to override defaults:

```json
{
  "OmniForge": {
    "ServerUrl": "https://your-omniforge-instance.com"
  },
  "StreamingSoftware": "auto"
}
```

- `StreamingSoftware`: `"auto"` (detect), `"obs"`, or `"streamlabs"`

OBS-specific overrides:

```json
{
  "Obs": {
    "Url": "ws://localhost:4455",
    "Password": "your-obs-password"
  }
}
```

## Troubleshooting

- **No software detected**: Make sure OBS or Streamlabs is running. The agent polls every 5 seconds.
- **Can't connect to OBS**: Ensure WebSocket Server is enabled (Tools > WebSocket Server Settings).
- **OBS authentication failed**: Set your password via tray menu > Settings > OBS WebSocket Password.
- **Can't connect to Streamlabs**: Ensure Remote Control is enabled in Streamlabs Desktop settings.
- **Server connection fails**: Sign in again via the tray icon. Check your network connection.
- **Scenes not appearing**: Switch to each scene once, or restart the agent after adding new scenes.

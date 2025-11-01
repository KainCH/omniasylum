# MCP Server Setup for OmniAsylum

## What are MCP Servers?

Model Context Protocol (MCP) servers provide enhanced context to AI assistants like GitHub Copilot. They allow Copilot to access file systems, databases, APIs, and other resources to provide better suggestions and assistance.

## Configured MCP Servers

This workspace has three MCP servers configured in `.vscode/settings.json`:

### 1. Filesystem Server
**Purpose**: Provides read/write access to project files  
**Configuration**:
```json
{
  "command": "npx",
  "args": ["-y", "@modelcontextprotocol/server-filesystem", "c:\\Game Data\\Coding Projects\\doc-omni"]
}
```
**What it does**: Allows Copilot to understand your entire project structure and file contents.

### 2. GitHub Server
**Purpose**: Integrates with GitHub repositories  
**Configuration**:
```json
{
  "command": "npx",
  "args": ["-y", "@modelcontextprotocol/server-github"],
  "env": {
    "GITHUB_PERSONAL_ACCESS_TOKEN": "${env:GITHUB_TOKEN}"
  }
}
```
**What it does**: Provides access to GitHub issues, PRs, and repository information.

**Setup Required**:
1. Create a GitHub Personal Access Token at https://github.com/settings/tokens
2. Grant scopes: `repo`, `read:org`, `read:user`
3. Set environment variable:
   ```powershell
   [System.Environment]::SetEnvironmentVariable('GITHUB_TOKEN', 'your_token_here', 'User')
   ```
4. Restart VS Code

### 3. Azure Server
**Purpose**: Integrates with Azure resources  
**Configuration**:
```json
{
  "command": "npx",
  "args": ["-y", "@azure/mcp-server-azure"]
}
```
**What it does**: Provides context about Azure resources, services, and deployment information.

**Setup Required**:
1. Install Azure CLI: `winget install Microsoft.AzureCLI`
2. Login: `az login`
3. The MCP server will use your Azure CLI credentials

## How to Use MCP Servers

Once configured, MCP servers work automatically with GitHub Copilot Chat. You can:

1. **Ask about project structure**:
   - "Show me all API routes in this project"
   - "What files handle authentication?"

2. **Query GitHub**:
   - "What are the open issues in this repo?"
   - "Show me recent pull requests"

3. **Check Azure resources**:
   - "What Container Apps are deployed?"
   - "Show my Key Vault secrets"

## Verifying MCP Server Status

1. Open GitHub Copilot Chat
2. Type `@workspace` to see available context
3. MCP servers appear as context sources

## Troubleshooting

### Filesystem Server Issues
- **Error**: Path not found
- **Fix**: Update path in `.vscode/settings.json` to match your workspace location

### GitHub Server Issues
- **Error**: Authentication failed
- **Fix**: Check that `GITHUB_TOKEN` environment variable is set correctly
- **Fix**: Ensure token has correct scopes

### Azure Server Issues
- **Error**: Not authenticated
- **Fix**: Run `az login` in terminal
- **Fix**: Check that Azure CLI is installed: `az --version`

### General Issues
- **Error**: MCP server not starting
- **Fix**: Ensure Node.js is installed: `node --version`
- **Fix**: Clear npx cache: `npx clear-npx-cache`
- **Fix**: Restart VS Code

## Advanced Configuration

### Custom MCP Servers

You can add more MCP servers to `.vscode/settings.json`:

```json
"mcpServers": {
  "database": {
    "command": "npx",
    "args": ["-y", "@modelcontextprotocol/server-postgres"],
    "env": {
      "DATABASE_URL": "${env:DATABASE_URL}"
    }
  }
}
```

### Available MCP Servers

- `@modelcontextprotocol/server-filesystem` - File system access
- `@modelcontextprotocol/server-github` - GitHub integration
- `@azure/mcp-server-azure` - Azure resources
- `@modelcontextprotocol/server-postgres` - PostgreSQL database
- `@modelcontextprotocol/server-sqlite` - SQLite database
- `@modelcontextprotocol/server-puppeteer` - Web browser automation
- `@modelcontextprotocol/server-brave-search` - Web search

## Environment Variables

Create a `.env.local` file in the workspace root for MCP server credentials:

```env
# GitHub
GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxxx

# Azure (optional - uses Azure CLI by default)
AZURE_SUBSCRIPTION_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
AZURE_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx

# Database (if using database MCP servers)
DATABASE_URL=postgres://user:pass@localhost:5432/dbname
```

**Important**: Add `.env.local` to `.gitignore`!

## Security Notes

- MCP servers run locally and only access what you configure
- Never commit credentials to version control
- Use environment variables for sensitive data
- Review MCP server permissions before installation
- Keep MCP server packages updated

## Resources

- [MCP Documentation](https://modelcontextprotocol.io/)
- [Available MCP Servers](https://github.com/modelcontextprotocol/servers)
- [Creating Custom MCP Servers](https://modelcontextprotocol.io/docs/concepts/servers)

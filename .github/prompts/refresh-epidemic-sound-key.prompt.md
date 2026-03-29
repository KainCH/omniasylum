---
description: "Navigate to Epidemic Sound account and generate a new API key for the epidemic-sound MCP server. Use when: the API key has expired (keys last 30 days), the MCP server is returning auth errors, or a key rotation is needed."
tools:
  - mcp_microsoft_pla_browser_navigate
  - mcp_microsoft_pla_browser_take_screenshot
  - mcp_microsoft_pla_browser_snapshot
  - mcp_microsoft_pla_browser_click
  - mcp_microsoft_pla_browser_wait_for
  - mcp_microsoft_pla_browser_fill_form
  - mcp_microsoft_pla_browser_tabs
---

You are fetching a fresh Epidemic Sound API key to restore the `epidemic-sound` MCP server connection in VS Code.

## Step 1 — Open the API keys page

Navigate to `https://www.epidemicsound.com/account/api-keys` in the browser.
Take a screenshot so the user can see the current page state.

## Step 2 — Handle authentication

If redirected to a login page:

- Take a screenshot and tell the user they need to log in first
- Wait for them to confirm before proceeding
- After confirmation, take another screenshot to verify the API keys page loaded

## Step 3 — Generate a new key

On the API keys page:

- Look for a "Generate" or "Regenerate" or "Create API key" button
- Take a snapshot to identify the correct element
- Click it to generate a new key
- Wait for the page to update
- Take a screenshot to capture the result

## Step 4 — Present the new key

- Display the new API key clearly in the chat (copy it from the page snapshot/screenshot)
- Remind the user that:
  - Keys expire after **30 days** — set a reminder
  - To use it: in VS Code, open the MCP panel (Command Palette → `MCP: List Servers`), find `epidemic-sound`, and click **Restart**
  - VS Code will prompt for `EpidemicSoundApiKey` — paste the new key there
  - Never commit the key to version control

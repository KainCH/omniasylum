# How to Restore a Series Save

This guide explains how to use the `restore-series-save.js` script to manually insert or restore a series save into Azure Table Storage. This is useful if data was accidentally wiped or if you need to manually create a save state for testing.

## Prerequisites

1.  **Node.js**: Ensure Node.js is installed.
2.  **Azure CLI**: You must be logged in to Azure CLI to authenticate.
    ```powershell
    az login
    ```
3.  **Dependencies**: Ensure the project dependencies are installed.
    ```powershell
    cd Legacy/API
    npm install
    ```

## Configuration

1.  Open the script file: `Legacy/API/restore-series-save.js`
2.  Locate the `CONFIG` object at the top of the file.
3.  Edit the values to match the data you want to restore.

### Configuration Options

| Option               | Description                                                           | Example                                |
| :------------------- | :-------------------------------------------------------------------- | :------------------------------------- |
| `targetSystem`       | Choose which system to target: `'DOTNET'` (New) or `'NODE'` (Legacy). | `'DOTNET'`                             |
| `storageAccountName` | The name of your Azure Storage Account.                               | `'omni46jismtjodyuc'`                  |
| `twitchUserId`       | The Twitch User ID (Partition Key) of the user who owns the save.     | `'12345678'`                           |
| `seriesName`         | The display name of the series.                                       | `'Elden Ring Episode 5'`               |
| `description`        | A short description for the save.                                     | `'Restored manually'`                  |
| `counters`           | An object containing the counter values to restore.                   | `{ deaths: 10, swears: 5, bits: 100 }` |
| `savedAt`            | (Optional) The timestamp of the save. Defaults to current time.       | `'2023-10-27T10:00:00.000Z'`           |

### Example Configuration

```javascript
const CONFIG = {
  targetSystem: 'DOTNET', // Targeting the new .NET backend
  storageAccountName: 'omni46jismtjodyuc',
  twitchUserId: '44322889', // Example User ID
  seriesName: 'Dark Souls 3 - Boss Rush',
  description: 'Restored from backup',
  counters: {
    deaths: 42,
    swears: 15,
    bits: 500
  }
};
```

## Running the Script

1.  Open a terminal in VS Code.
2.  Navigate to the `Legacy/API` directory:
    ```powershell
    cd Legacy/API
    ```
3.  Run the script using Node.js:
    ```powershell
    node restore-series-save.js
    ```

## Output

If successful, you will see output similar to this:

```text
🔄 Starting Series Save Restoration for [DOTNET]...
📡 Connecting to: https://omni46jismtjodyuc.table.core.windows.net
📂 Target Table: series
📝 Preparing to insert entity: { ... }
✅ Successfully restored series save!
🔑 Series ID: 1701234567890_Dark_Souls_3___Boss_Rush
👤 User ID: 44322889
```

## Verification

### For .NET (Production)
You can verify the restoration by:
1.  Logging into the application.
2.  Opening the Series Save Manager.
3.  Checking if the restored series appears in the list.

Alternatively, you can use the Azure Portal or Azure Storage Explorer to view the `series` table and confirm the row exists.

### Troubleshooting

*   **Error: Authentication Failed**: Make sure you ran `az login` and have permissions to access the storage account.
*   **Error: Table Not Found**: Check if `storageAccountName` is correct and if the table (`series` for .NET, `seriessaves` for Node) exists.
*   **Wrong Data Format**: Ensure `twitchUserId` is a string (e.g., `'12345'`, not `12345`).

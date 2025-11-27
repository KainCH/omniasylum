<#
.SYNOPSIS
    Copies data from production Azure Tables to development tables.

.DESCRIPTION
    This script copies all entities from production tables (users, counters, series, alerts)
    to their corresponding dev tables (usersdev, countersdev, seriesdev, alertsdev).

.PARAMETER StorageAccountName
    The name of the Azure Storage Account.

.PARAMETER ResourceGroup
    The resource group containing the storage account.

.EXAMPLE
    .\copy-tables-to-dev.ps1 -StorageAccountName "omnistorage" -ResourceGroup "Streamer-Tools-RG"
#>

param(
    [string]$StorageAccountName = "",
    [string]$ResourceGroup = "Streamer-Tools-RG"
)

$ErrorActionPreference = "Stop"

Write-Host "🔄 Azure Table Data Copy: Prod -> Dev" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan

# Get storage account name if not provided
if ([string]::IsNullOrEmpty($StorageAccountName)) {
    Write-Host "📦 Retrieving Storage Account..." -ForegroundColor Yellow
    $StorageAccountName = az storage account list --resource-group $ResourceGroup --query "[0].name" -o tsv
    if ([string]::IsNullOrEmpty($StorageAccountName)) {
        Write-Error "❌ No storage account found in resource group $ResourceGroup"
        exit 1
    }
}

Write-Host "📦 Storage Account: $StorageAccountName" -ForegroundColor Gray

# Get storage account key
Write-Host "🔑 Getting storage account key..." -ForegroundColor Yellow
$storageKey = az storage account keys list --account-name $StorageAccountName --resource-group $ResourceGroup --query "[0].value" -o tsv

if ([string]::IsNullOrEmpty($storageKey)) {
    Write-Error "❌ Failed to get storage account key"
    exit 1
}

# Define table mappings (source -> destination)
$tableMappings = @{
    "users"    = "usersdev"
    "counters" = "countersdev"
    "series"   = "seriesdev"
    "alerts"   = "alertsdev"
}

# Function to copy table data
function Copy-TableData {
    param(
        [string]$SourceTable,
        [string]$DestTable,
        [string]$AccountName,
        [string]$AccountKey
    )

    Write-Host ""
    Write-Host "📋 Copying: $SourceTable -> $DestTable" -ForegroundColor Cyan

    # Check if source table exists
    $sourceExists = az storage table exists --name $SourceTable --account-name $AccountName --account-key $AccountKey --query "exists" -o tsv
    if ($sourceExists -ne "true") {
        Write-Host "   ⚠️  Source table '$SourceTable' does not exist, skipping..." -ForegroundColor Yellow
        return
    }

    # Create destination table if it doesn't exist
    Write-Host "   Creating destination table if needed..." -ForegroundColor Gray
    az storage table create --name $DestTable --account-name $AccountName --account-key $AccountKey --output none 2>$null

    # Get all entities from source table
    Write-Host "   Reading entities from source..." -ForegroundColor Gray
    $entities = az storage entity query --table-name $SourceTable --account-name $AccountName --account-key $AccountKey --output json 2>$null | ConvertFrom-Json

    if ($null -eq $entities -or $null -eq $entities.items -or $entities.items.Count -eq 0) {
        Write-Host "   ⚠️  No entities found in source table" -ForegroundColor Yellow
        return
    }

    $entityCount = $entities.items.Count
    Write-Host "   Found $entityCount entities to copy" -ForegroundColor Gray

    $copied = 0
    $errors = 0

    foreach ($entity in $entities.items) {
        try {
            $partitionKey = $entity.PartitionKey
            $rowKey = $entity.RowKey

            # Build entity properties (excluding system properties)
            $props = @{}
            foreach ($prop in $entity.PSObject.Properties) {
                $name = $prop.Name
                # Skip system/metadata properties
                if ($name -notin @("PartitionKey", "RowKey", "Timestamp", "etag", "odata.etag", "odata.type")) {
                    $props[$name] = $prop.Value
                }
            }

            # Convert properties to JSON for the entity
            $entityJson = $props | ConvertTo-Json -Compress -Depth 10

            # Insert or replace entity in destination
            # Using az storage entity insert with --if-exists replace
            $tempFile = [System.IO.Path]::GetTempFileName()
            $entityJson | Out-File -FilePath $tempFile -Encoding utf8

            az storage entity insert --table-name $DestTable `
                --account-name $AccountName `
                --account-key $AccountKey `
                --entity PartitionKey=$partitionKey RowKey=$rowKey `
                --if-exists replace `
                --output none 2>$null

            # For complex entities, we need to merge properties
            if ($props.Count -gt 0) {
                $propsArgs = @()
                foreach ($key in $props.Keys) {
                    $value = $props[$key]
                    if ($null -ne $value) {
                        # Handle different types
                        if ($value -is [bool]) {
                            $propsArgs += "$key=$($value.ToString().ToLower())@Edm.Boolean"
                        }
                        elseif ($value -is [int] -or $value -is [long]) {
                            $propsArgs += "$key=$value@Edm.Int32"
                        }
                        elseif ($value -is [double]) {
                            $propsArgs += "$key=$value@Edm.Double"
                        }
                        else {
                            # String - escape quotes
                            $strValue = $value.ToString().Replace('"', '\"')
                            $propsArgs += "$key=$strValue"
                        }
                    }
                }

                if ($propsArgs.Count -gt 0) {
                    $entityArgs = @("PartitionKey=$partitionKey", "RowKey=$rowKey") + $propsArgs
                    az storage entity replace --table-name $DestTable `
                        --account-name $AccountName `
                        --account-key $AccountKey `
                        --entity @entityArgs `
                        --output none 2>$null
                }
            }

            Remove-Item $tempFile -ErrorAction SilentlyContinue
            $copied++
        }
        catch {
            $errors++
            Write-Host "   ⚠️  Error copying entity $partitionKey/$rowKey : $_" -ForegroundColor Yellow
        }
    }

    Write-Host "   ✅ Copied: $copied, Errors: $errors" -ForegroundColor $(if ($errors -eq 0) { "Green" } else { "Yellow" })
}

# Copy each table
foreach ($mapping in $tableMappings.GetEnumerator()) {
    Copy-TableData -SourceTable $mapping.Key -DestTable $mapping.Value -AccountName $StorageAccountName -AccountKey $storageKey
}

Write-Host ""
Write-Host "✅ Table copy complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Dev tables created:" -ForegroundColor Cyan
foreach ($mapping in $tableMappings.GetEnumerator()) {
    Write-Host "   - $($mapping.Value)" -ForegroundColor Gray
}

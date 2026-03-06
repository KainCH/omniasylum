<#
.SYNOPSIS
    One-time setup: grants Key Vault signing permissions to the current az login user
    and creates the OmniForgeAgentSigning code-signing certificate if it does not exist.

.DESCRIPTION
    Run this once before using 'Publish Sync Agent'. Requires:
      - az CLI logged in: az login
      - Sufficient Azure permissions to create role assignments on the Key Vault
        (typically Owner or User Access Administrator on the vault or subscription)

.EXAMPLE
    .\setup-agent-signing.ps1
#>

param(
    [string]$SubscriptionId  = "b8a36f4a-bde2-446f-81b5-7a48d5522724",
    [string]$ResourceGroup   = "Streamer-Tools-RG",
    [string]$KeyVaultName    = "forge-steel-vault",
    [string]$CertificateName = "OmniForgeAgentSigning"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Verify az login ───────────────────────────────────────────────────────────
Write-Host "Verifying Azure CLI login..." -ForegroundColor Cyan
$account = az account show --subscription $SubscriptionId 2>&1 | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) {
    Write-Error "Not logged in. Run: az login"
    exit 1
}
Write-Host "  Subscription : $($account.name) ($SubscriptionId)" -ForegroundColor Gray

# ── Get current user object ID ────────────────────────────────────────────────
$userOid = az ad signed-in-user show --query id -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrEmpty($userOid)) {
    # Fallback: extract from the JWT in the current account token
    $userOid = az account show --query "id" -o tsv
    $jwt     = az account get-access-token --query accessToken -o tsv
    $payload = ($jwt -split '\.')[1]
    # base64url-decode (pad to 4-byte boundary)
    $padded  = $payload.PadRight($payload.Length + (4 - $payload.Length % 4) % 4, '=')
    $userOid = ([System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($padded)) |
                ConvertFrom-Json).oid
}
Write-Host "  Current user OID: $userOid" -ForegroundColor Gray

# ── Resolve Key Vault resource ID ─────────────────────────────────────────────
$vaultId = az keyvault show --name $KeyVaultName --resource-group $ResourceGroup --query id -o tsv
Write-Host "  Key Vault: $vaultId" -ForegroundColor Gray

# ── Grant roles ───────────────────────────────────────────────────────────────
$roles = @(
    @{ Name = "Key Vault Certificate User"; Id = "a4417e6f-fecd-4de8-b567-7b0420556985" },
    @{ Name = "Key Vault Crypto User";      Id = "12338af0-0e69-4776-bea7-57ae8d297424" }
)

foreach ($role in $roles) {
    Write-Host "Checking role '$($role.Name)'..." -ForegroundColor Cyan
    $existing = az role assignment list `
        --assignee $userOid `
        --role $role.Id `
        --scope $vaultId `
        --query "[0].id" -o tsv 2>$null

    if (-not [string]::IsNullOrEmpty($existing)) {
        Write-Host "  Already assigned." -ForegroundColor Green
    } else {
        Write-Host "  Assigning..." -ForegroundColor Yellow
        az role assignment create `
            --assignee-object-id $userOid `
            --assignee-principal-type User `
            --role $role.Id `
            --scope $vaultId | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to assign '$($role.Name)'. You may need Owner/UAA permissions."
            exit 1
        }
        Write-Host "  Assigned." -ForegroundColor Green
    }
}

# ── Create certificate if missing ─────────────────────────────────────────────
Write-Host "Checking certificate '$CertificateName'..." -ForegroundColor Cyan
$certStatus = az keyvault certificate show `
    --vault-name $KeyVaultName `
    --name $CertificateName `
    --query "attributes.enabled" -o tsv 2>$null

if ($certStatus -eq "true") {
    Write-Host "  Certificate already exists and is enabled." -ForegroundColor Green
} else {
    Write-Host "  Creating self-signed code-signing certificate (valid 2 years)..." -ForegroundColor Yellow

    $policy = @{
        issuerParameters  = @{ name = "Self" }
        keyProperties     = @{ exportable = $false; keyType = "RSA"; keySize = 4096; reuseKey = $false }
        lifetimeActions   = @(@{ action = @{ actionType = "AutoRenew" }; trigger = @{ daysBeforeExpiry = 30 } })
        secretProperties  = @{ contentType = "application/x-pkcs12" }
        x509CertificateProperties = @{
            subject       = "CN=OmniForge Sync Agent,O=OmniForge,C=US"
            validityInMonths = 24
            keyUsage      = @("digitalSignature")
            enhancedKeyUsage = @("1.3.6.1.5.5.7.3.3")   # Code Signing OID
        }
    } | ConvertTo-Json -Depth 10 -Compress

    # Write policy to a temp file (az CLI can't take inline JSON on Windows reliably)
    $policyFile = [System.IO.Path]::GetTempFileName() + ".json"
    Set-Content -Path $policyFile -Value $policy -Encoding UTF8

    az keyvault certificate create `
        --vault-name $KeyVaultName `
        --name $CertificateName `
        --policy "@$policyFile" | Out-Null

    Remove-Item $policyFile -ErrorAction SilentlyContinue

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Certificate creation failed."
        exit 1
    }

    # Poll until the cert is in a ready state (creation is async)
    Write-Host "  Waiting for certificate to become ready..." -ForegroundColor Gray
    $maxWait = 60   # seconds
    $waited  = 0
    do {
        Start-Sleep -Seconds 3
        $waited += 3
        $state = az keyvault certificate show `
            --vault-name $KeyVaultName `
            --name $CertificateName `
            --query "policy.attributes.created" -o tsv 2>$null
    } while ([string]::IsNullOrEmpty($state) -and $waited -lt $maxWait)

    Write-Host "  Certificate created successfully." -ForegroundColor Green
}

Write-Host ""
Write-Host "Setup complete. You can now run 'Publish Sync Agent' to build and sign the agent." -ForegroundColor Green
Write-Host ""
Write-Host "NOTE: Role assignment propagation can take 1-2 minutes in Azure." -ForegroundColor Yellow
Write-Host "If the publish task still fails with 403, wait a minute and try again." -ForegroundColor Yellow

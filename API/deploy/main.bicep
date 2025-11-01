// Infrastructure parameters
@description('The Azure region where resources will be deployed')
param location string = resourceGroup().location

@description('Base name for all resources')
param baseName string = 'omniasylum'

@description('Environment name (dev, staging, prod)')
param environment string = 'prod'

@description('Container image to deploy')
param containerImage string

@description('Twitch OAuth redirect URI')
param twitchRedirectUri string

@description('Frontend URL for CORS')
param frontendUrl string

// Variables
var uniqueSuffix = uniqueString(resourceGroup().id)
var containerAppName = '${baseName}-api-${environment}'
var containerEnvName = '${baseName}-env-${environment}'
var keyVaultName = '${baseName}-kv-${uniqueSuffix}'
var storageAccountName = '${baseName}${uniqueSuffix}'
var logAnalyticsName = '${baseName}-logs-${environment}'
var appInsightsName = '${baseName}-insights-${environment}'

// Log Analytics Workspace
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Application Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// Storage Account for Table Storage
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }
}

// Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Container Apps Environment
resource containerAppEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: containerEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// Container App with Managed Identity
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 3000
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
        transport: 'auto' // Supports both HTTP and WebSocket
      }
      secrets: []
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'NODE_ENV'
              value: 'production'
            }
            {
              name: 'PORT'
              value: '3000'
            }
            {
              name: 'AZURE_KEYVAULT_NAME'
              value: keyVaultName
            }
            {
              name: 'AZURE_STORAGE_ACCOUNT'
              value: storageAccountName
            }
            {
              name: 'DB_MODE'
              value: 'azure'
            }
            {
              name: 'TWITCH_REDIRECT_URI'
              value: twitchRedirectUri
            }
            {
              name: 'FRONTEND_URL'
              value: frontendUrl
            }
            {
              name: 'CORS_ORIGIN'
              value: frontendUrl
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsights.properties.ConnectionString
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0 // Scale to zero when idle for cost savings
        maxReplicas: 5 // Increase slightly for approved streamer growth
        rules: [
          {
            name: 'http-rule'
            http: {
              metadata: {
                concurrentRequests: '20' // Higher concurrency per instance
              }
            }
          }
          {
            name: 'websocket-rule'
            tcp: {
              metadata: {
                concurrentConnections: '50' // Handle WebSocket connections
              }
            }
          }
        ]
      }
    }
  }
}

// Role Assignment: Give Container App access to Key Vault
resource keyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, containerApp.id, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role Assignment: Give Container App access to Storage Account
resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, containerApp.id, 'Storage Table Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3') // Storage Table Data Contributor
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Outputs
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output keyVaultName string = keyVault.name
output storageAccountName string = storageAccount.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString

// Infrastructure parameters
@description('The Azure region where resources will be deployed')
param location string = resourceGroup().location

@description('Base name for all resources')
param baseName string = 'omniforgestream'

@description('Environment name (dev, staging, prod)')
param environment string = 'dev'

@description('Container image to deploy')
param containerImage string

@description('Frontend URL for CORS')
param frontendUrl string

@description('Name of the existing storage account')
param storageAccountName string = 'omni${uniqueString(resourceGroup().id)}'

@description('Name of the existing Key Vault')
param keyVaultName string = 'forge-steel-vault'

// Variables
var containerAppName = '${baseName}-api-${environment}'
var containerEnvName = '${baseName}-env-${environment}'
// var keyVaultName = 'forge-steel-vault' // Replaced by param
var logAnalyticsName = '${baseName}-logs-${environment}'
var appInsightsName = '${baseName}-insights-${environment}'
var acrName = 'omniforgeacr'
// Map environment param to ASPNETCORE_ENVIRONMENT value
var aspnetEnvironment = environment == 'prod' ? 'Production' : 'Development'

// Reference existing Azure Container Registry
resource acrRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

// Log Analytics Workspace - creates if not exists, updates if exists
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

// Application Insights - creates if not exists, updates if exists
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// Container Apps Environment - creates if not exists, updates if exists
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
    // Custom domain configuration is managed manually in the Azure Portal.
    // This empty object is required to preserve existing domain bindings during deployment.
    // Do not remove - see: https://learn.microsoft.com/en-us/azure/container-apps/custom-domains-managed-certificates
    customDomainConfiguration: {}
  }
}

// Reference existing User Assigned Managed Identity
resource userAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'bears-stream-UAMI'
  scope: resourceGroup()
}

// Container App with User Assigned Managed Identity
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        corsPolicy: {
          allowedOrigins: [
            frontendUrl
            'http://localhost:3000'
            'http://localhost:5173'
          ]
          allowCredentials: true
        }
      }
      registries: [
        {
          server: acrRegistry.properties.loginServer
          identity: userAssignedIdentity.id
        }
      ]
      secrets: []
    }
    template: {
      containers: [
        {
          name: 'omniforge-dotnet'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1.0Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: aspnetEnvironment
            }
            {
              name: 'KeyVaultName'
              value: keyVaultName
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: userAssignedIdentity.properties.clientId
            }
            {
              name: 'AzureStorage__AccountName'
              value: storageAccountName
            }
            {
              name: 'Authentication__Twitch__CallbackPath'
              value: '/auth/twitch/callback'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsights.properties.ConnectionString
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output containerAppUrl string = containerApp.properties.configuration.ingress.fqdn

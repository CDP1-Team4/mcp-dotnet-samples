param name string
@description('Primary location for all resources & Flex Consumption Function App')
param location string = resourceGroup().location
param tags object = {}
param applicationInsightsName string = ''
param appServicePlanId string
param appSettings object = {}
param runtimeName string 
param runtimeVersion string 
param serviceName string = 'mcp'
param storageAccountName string
param deploymentStorageContainerName string
param virtualNetworkSubnetId string = ''
param instanceMemoryMB int = 2048
param maximumInstanceCount int = 100
param identityId string = ''
param identityClientId string = ''
param enableBlob bool = true
param enableQueue bool = false
param enableTable bool = false
param enableFile bool = false
param azdServiceName string

@allowed(['SystemAssigned', 'UserAssigned'])
param identityType string = 'UserAssigned'

var applicationInsightsIdentity = 'ClientId=${identityClientId};Authorization=AAD'
var kind = 'functionapp,linux'

// Create base application settings
var baseAppSettings = {
  FUNCTIONS_EXTENSION_VERSION: '~4'
//   FUNCTIONS_WORKER_RUNTIME: runtimeName
  WEBSITE_RUN_FROM_PACKAGE: '1'

  // Only include required credential settings unconditionally
  AzureWebJobsStorage: 'DefaultEndpointsProtocol=https;AccountName=${stg.name};AccountKey=${stg.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
  AzureWebJobsStorage__credential: 'managedidentity'
  AzureWebJobsStorage__clientId: identityClientId
  
  // Application Insights settings are always included
  APPLICATIONINSIGHTS_AUTHENTICATION_STRING: applicationInsightsIdentity
}

// Dynamically build storage endpoint settings based on feature flags
var blobSettings = enableBlob ? { AzureWebJobsStorage__blobServiceUri: stg.properties.primaryEndpoints.blob } : {}
var queueSettings = enableQueue ? { AzureWebJobsStorage__queueServiceUri: stg.properties.primaryEndpoints.queue } : {}
var tableSettings = enableTable ? { AzureWebJobsStorage__tableServiceUri: stg.properties.primaryEndpoints.table } : {}
var fileSettings = enableFile ? { AzureWebJobsStorage__fileServiceUri: stg.properties.primaryEndpoints.file } : {}

// Merge all app settings
var allAppSettings = union(
  appSettings,
//   blobSettings,
//   queueSettings,
//   tableSettings,
  fileSettings,
  baseAppSettings
)

resource stg 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
  name: storageAccountName
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = if (!empty(applicationInsightsName)) {
  name: applicationInsightsName
}

// Create a Flex Consumption Function App to host the MCP server
module mcp 'br/public:avm/res/web/site:0.21.0' = {
  name: '${serviceName}-flex-consumption'
  params: {
    kind: kind
    name: name
    location: location
    tags: union(tags, { 'azd-service-name': azdServiceName })
    serverFarmResourceId: appServicePlanId
    managedIdentities: {
      systemAssigned: identityType == 'SystemAssigned'
      userAssignedResourceIds: [
        '${identityId}'
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${stg.properties.primaryEndpoints.blob}${deploymentStorageContainerName}'
          authentication: {
            type: identityType == 'SystemAssigned' ? 'SystemAssignedIdentity' : 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identityType == 'UserAssigned' ? identityId : '' 
          }
        }
      }
      scaleAndConcurrency: {
        instanceMemoryMB: instanceMemoryMB
        maximumInstanceCount: maximumInstanceCount
      }
      runtime: {
        name: runtimeName
        version: runtimeVersion
      }
    }
    siteConfig: {
      alwaysOn: false
    }
    virtualNetworkSubnetResourceId: !empty(virtualNetworkSubnetId) ? virtualNetworkSubnetId : null
    configs: [
      {
        name: 'appsettings'
        applicationInsightResourceId: applicationInsights.id
        storageAccountResourceId: stg.id
        storageAccountUseIdentityAuthentication: true
        properties: union(allAppSettings, {
          UseHttp: 'true'
          UseAzureStorage: 'true'

          AZURE_CLIENT_ID: identityClientId

          AzureStorage__ConnectionString: 'DefaultEndpointsProtocol=https;AccountName=${stg.name};AccountKey=${stg.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

          EntraId__TenantId: 'common'
          EntraId__ClientId: identityClientId
          EntraId__UseManagedIdentity: 'true'

          STORAGE_DOWNLOAD_MOUNT: '/mounts/downloads'
        })
      }
      {
        name: 'azurestorageaccounts'
        properties: {
          'download-storage': {
            type: 'AzureFiles'
            protocol: 'Smb'
            accessKey: stg.listKeys().keys[0].value
            accountName: stg.name
            shareName: 'downloads'
            mountPath: '/mounts/downloads'
          }
        }
      }
    ]
  }
}

output resourceId string = mcp.outputs.resourceId
output name string = mcp.outputs.name
// Ensure output is always string, handle potential null from module output if SystemAssigned is not used
output principalId string = identityType == 'SystemAssigned' ? mcp.outputs.?systemAssignedMIPrincipalId ?? '' : ''
output fqdn string = mcp.outputs.defaultHostname

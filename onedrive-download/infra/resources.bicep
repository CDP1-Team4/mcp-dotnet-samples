@description('The location used for all deployed resources')
param location string = resourceGroup().location

@description('Tags that will be applied to all resources')
param tags object = {}

param mcpOneDriveDownloadExists bool

param vnetEnabled bool = true // Enable VNet by default

@description('Id of the user or app to assign application roles')
param principalId string

@description('The name of the service defined in azure.yaml.')
param azdServiceName string

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)
var functionAppName = '${abbrs.webSitesFunctions}${resourceToken}'
var deploymentStorageContainerName = 'app-package-${take(functionAppName, 32)}-${take(toLower(uniqueString(functionAppName, resourceToken)), 7)}'

// Monitor application with Azure Monitor
module monitoring 'br/public:avm/ptn/azd/monitoring:0.1.0' = {
  name: 'monitoring'
  params: {
    location: location
    tags: tags
    logAnalyticsName: '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    applicationInsightsName: '${abbrs.insightsComponents}${resourceToken}'
    applicationInsightsDashboardName: '${abbrs.portalDashboards}${resourceToken}'
  }
}

// User assigned identity
module mcpOneDriveDownloadIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.2.1' = {
  name: 'mcpOneDriveDownloadIdentity'
  params: {
    name: '${abbrs.managedIdentityUserAssignedIdentities}mcponedrivedownload-${resourceToken}'
    location: location
    tags: tags
  }
}

// // 3. User-assigned managed identity for the function app
// resource userAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
//   name: '${abbrs.managedIdentityUserAssignedIdentities}${azdServiceName}-${resourceToken}'
//   location: location
//   tags: tags
// }

// // API Management
// module apimService './modules/apim.bicep' = {
//   name: 'apimService'
//   params:{
//     apiManagementName: '${abbrs.apiManagementService}${resourceToken}'
//   }
// }

// MCP Entra App
module mcpEntraApp './modules/mcp-entra-app.bicep' = {
  name: 'mcpEntraApp'
  params: {
    mcpAppUniqueName: 'mcp-onedrive-download-${resourceToken}'
    mcpAppDisplayName: 'mcp-onedrive-download-${resourceToken}'
    userAssignedIdentityPrincipleId: mcpOneDriveDownloadIdentity.outputs.principalId
    tenantId: 'common'
    functionAppName: functionAppName
    appScopes: ['Files.Read.All', ' offline_access', 'email', 'openid', 'profile']
    appRoles: []
  }
}

// // MCP Entra App
// module entraApp './modules/mcp-entra-app.bicep' = {
//   name: 'mcpEntraApp'
//   params: {
//     mcpAppUniqueName: 'mcp-onedrive-download-${resourceToken}'
//     mcpAppDisplayName: 'mcp-onedrive-download-${resourceToken}'
//     userAssignedIdentityPrincipleId: userAssignedIdentity.properties.principalId
//     functionAppName: functionAppName
//     appScopes: [
//       'User.Read'
//       'Files.Read.All'
//       'offline_access'
//     ]
//     appRoles: []
//   }
// }

// // MCP server API endpoints
// module mcpApiModule './modules/mcp-api.bicep' = {
//   name: 'mcpApiModule'
//   params: {
//     apimServiceName: apimService.outputs.name
//     functionAppName: functionAppName
//     mcpAppId: mcpEntraApp.outputs.mcpAppId
//     mcpAppTenantId: mcpEntraApp.outputs.mcpAppTenantId
//   }
//   dependsOn: [
//     appServicePlan
//     fncapp
//     storagePrivateEndpoint
//   ]
// }

// // MCP server API endpoints
// module mcpApiModule './modules/mcp-api.bicep' = {
//   name: 'mcpApiModule'
//   params: {
//     apimServiceName: apimService.outputs.name
//     functionAppName: functionAppName
//     mcpAppId: entraApp.outputs.mcpAppId
//     mcpAppTenantId: entraApp.outputs.mcpAppTenantId
//   }
//   dependsOn: [
//     functionApp
//   ]
// }

// // Create an App Service Plan to group applications under the same payment plan and SKU
// module appServicePlan 'br/public:avm/res/web/serverfarm:0.1.1' = {
//   name: 'appServicePlan'
//   params: {
//     name: '${abbrs.webServerFarms}${resourceToken}'
//     location: location
//     tags: tags
//     sku: {
//       name: 'FC1'
//       tier: 'FlexConsumption'
//     }
//     reserved: true
//   }
// }

// // 4. App Service plan (Flex Consumption)
// resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
//   name: '${abbrs.webServerFarms}${resourceToken}'
//   location: location
//   tags: tags
//   sku: {
//     name: 'FC1'
//     tier: 'FlexConsumption'
//   }
//   properties: {
//     reserved: true // Required for Linux
//   }
// }

// // Function app
// module fncapp './modules/functionapp.bicep' = {
//   name: 'functionapp'
//   params: {
//     name: functionAppName
//     location: location
//     tags: tags
//     azdServiceName: azdServiceName
//     applicationInsightsName: monitoring.outputs.applicationInsightsName
//     appServicePlanId: appServicePlan.outputs.resourceId
//     runtimeName: 'dotnet-isolated'
//     runtimeVersion: '9.0'
//     storageAccountName: storage.outputs.name
//     enableBlob: storageEndpointConfig.enableBlob
//     enableQueue: storageEndpointConfig.enableQueue
//     enableTable: storageEndpointConfig.enableTable
//     enableFile: storageEndpointConfig.enableFile
//     deploymentStorageContainerName: deploymentStorageContainerName
//     identityId: mcpOneDriveDownloadIdentity.outputs.resourceId
//     identityClientId: mcpOneDriveDownloadIdentity.outputs.clientId
//     appSettings: {}
//     virtualNetworkSubnetId: vnetEnabled ? serviceVirtualNetwork!.outputs.appSubnetID : ''
//   }
// }

// // 5. The Function App (Flex Consumption용)
// resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
//   name: functionAppName
//   location: location
//   kind: 'functionapp,linux'
//   tags: union(tags, { 'azd-service-name': azdServiceName })
//   identity: {
//     type: 'UserAssigned'
//     userAssignedIdentities: {
//       '${userAssignedIdentity.id}': {}
//     }
//   }
//   properties: {
//     serverFarmId: appServicePlan.id
//     httpsOnly: true

//     // ★ Flex Consumption 필수: functionAppConfig
//     functionAppConfig: {
//       deployment: {
//         storage: {
//           type: 'blobContainer'
//           value: '${storageAccount.properties.primaryEndpoints.blob}${deploymentStorageContainerName}'
//           authentication: {
//             type: 'UserAssignedIdentity'
//             userAssignedIdentityResourceId: userAssignedIdentity.id
//           }
//         }
//       }
//       scaleAndConcurrency: {
//         instanceMemoryMB: 2048
//         maximumInstanceCount: 100
//       }
//       runtime: {
//         name: 'dotnet-isolated'
//         version: '9.0'
//       }
//     }

//     siteConfig: {
//       alwaysOn: false

//       // ★★★ CORS 설정 (VS Code 접속 허용) ★★★
//       cors: {
//         allowedOrigins: ['*']
//       }

//       // ★★★ 핵심: 스토리지 마운트 설정 ★★★
//       azureStorageAccounts: {
//         'downloads-mount': {
//           type: 'AzureFiles'
//           accountName: storageAccount.name
//           shareName: 'downloads'
//           mountPath: '/mount/downloads'
//           accessKey: storageAccount.listKeys().keys[0].value
//         }
//       }
//       appSettings: [
//         // ★ Flex Consumption 필수 설정
//         {
//           name: 'WEBSITE_FUNCTIONS_MESSAGING_EXTENSION_VERSION'
//           value: '~4'
//         }
//         // ★ AzureWebJobsStorage는 Full Connection String으로 제공
//         {
//           name: 'AzureWebJobsStorage'
//           value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
//         }
//         {
//           name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
//           value: monitoring.outputs.applicationInsightsConnectionString
//         }
//         {
//           name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
//           value: '~3'
//         }
//         {
//           name: 'XDT_MicrosoftApplicationInsights_Mode'
//           value: 'recommended'
//         }
//         // ★ 다운로드 경로 설정 (마운트된 경로와 일치)
//         {
//           name: 'DOWNLOAD_DIR'
//           value: '/mount/downloads'
//         }
//         // ★ RFC 9728 OAuth Protected Resource Metadata를 위한 인증 설정
//         {
//           name: 'OneDriveDownload__Auth__TenantId'
//           value: tenant().tenantId
//         }
//         {
//           name: 'OneDriveDownload__Auth__ClientId'
//           value: entraApp.outputs.mcpAppId
//         }
//         {
//           name: 'AZURE_STORAGE_CONNECTION_STRING'
//           value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
//         }
//         // ★ APIM 프록시 URL을 위한 FQDN
//         {
//           name: 'APIM_FQDN'
//           value: replace(apimService.outputs.gatewayUrl, 'https://', '')
//         }
//       ]
//     }
//   }
// }

// Define the configuration object locally to pass to the modules
var storageEndpointConfig = {
  enableBlob: true  // Required for AzureWebJobsStorage, .zip deployment, Event Hubs trigger and Timer trigger checkpointing
  enableQueue: true  // Required for Durable Functions and MCP trigger
  enableTable: true  // Required for Durable Functions and OpenAI triggers and bindings
  enableFile: true   // Not required, used in legacy scenarios
  allowUserIdentityPrincipal: true   // Allow interactive user identity to access for testing and debugging
}

// Backing storage for Azure Functions app
module storage 'br/public:avm/res/storage/storage-account:0.8.3' = {
  name: 'storage'
  params: {
    name: '${abbrs.storageStorageAccounts}${resourceToken}'
    location: location
    tags: tags
    minimumTlsVersion: 'TLS1_2'  // Enforcing TLS 1.2 for better security
    supportsHttpsTrafficOnly: true
    dnsEndpointType: 'Standard'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // Disable local authentication methods as per policy

    publicNetworkAccess: vnetEnabled ? 'Disabled' : 'Enabled'
    networkAcls: vnetEnabled ? {
      defaultAction: 'Deny'
      bypass: 'None'
    } : {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }

    largeFileSharesState: 'Enabled'

    blobServices: {
      name: 'default'
      containers: [
        {
          name: deploymentStorageContainerName
          properties: {
            publicAccess: 'None'
          }
        }
      ]
    }

    fileServices: {
      name: 'default'
      shares: [
        {
          name: 'downloads'
          shareQuota: 1024
          enabledProtocols: 'SMB'
        }
      ]
    }
  }
}

resource stg 'Microsoft.Storage/storageAccounts@2025-06-01' existing = {
  name: '${abbrs.storageStorageAccounts}${resourceToken}'
  dependsOn: [
    storage
  ]
}

// // 2. Storage account for the function app
// resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
//   name: '${abbrs.storageStorageAccounts}${resourceToken}'
//   location: location
//   tags: tags
//   sku: {
//     name: 'Standard_LRS'
//   }
//   kind: 'StorageV2'
//   properties: {
//     minimumTlsVersion: 'TLS1_2'
//     supportsHttpsTrafficOnly: true
//     allowBlobPublicAccess: false
//     largeFileSharesState: 'Enabled'
//   }

//   resource blobServices 'blobServices' = {
//     name: 'default'
//     resource container 'containers' = {
//       name: deploymentStorageContainerName
//       properties: {
//         publicAccess: 'None'
//       }
//     }
//   }

//   // ★ File Share 추가: downloads 폴더를 마운트 가능하게 함
//   resource fileServices 'fileServices' = {
//     name: 'default'
//     resource share 'shares' = {
//       name: 'downloads'
//       properties: {
//         shareQuota: 1024
//         enabledProtocols: 'SMB'
//       }
//     }
//   }
// }

// Virtual Network & private endpoint to blob storage
module serviceVirtualNetwork 'modules/vnet.bicep' =  if (vnetEnabled) {
  name: 'serviceVirtualNetwork'
  params: {
    location: location
    tags: tags
    vNetName: '${abbrs.networkVirtualNetworks}${resourceToken}'
  }
}

module storagePrivateEndpoint './modules/storage-privateendpoint.bicep' = if (vnetEnabled) {
  name: 'servicePrivateEndpoint'
  params: {
    location: location
    tags: tags
    virtualNetworkName: '${abbrs.networkVirtualNetworks}${resourceToken}'
    subnetName: vnetEnabled ? serviceVirtualNetwork!.outputs.peSubnetName : '' // Keep conditional check for safety, though module won't run if !vnetEnabled
    resourceName: storage.outputs.name
    enableBlob: storageEndpointConfig.enableBlob
    enableQueue: storageEndpointConfig.enableQueue
    enableTable: storageEndpointConfig.enableTable
    enableFile: storageEndpointConfig.enableFile
  }
}

// // ★ Built-in Authentication 설정 (VSCode 팝업을 위해 필수)
// // authSettingsV2를 entraApp 이후에 정의 (dependency 순서)
// // ★★★ [토큰 패스스루 방식] Easy Auth 완전 해제 ★★★
// // 인증은 Program.cs의 미들웨어에서 처리하므로, Azure 서버 레벨의 문지기는 끕니다.
// // 이렇게 하면 VS Code가 받은 Graph용 토큰이 서버 코드까지 무사히 도착합니다.
// resource authSettingsV2 'Microsoft.Web/sites/config@2023-12-01' = {
//   parent: functionApp
//   name: 'authsettingsV2'
//   dependsOn: [
//     entraApp  // entraApp이 먼저 생성되어야 함
//   ]
//   properties: {
//     // 1. 인증 기능 비활성화 (서버 레벨 인증 끔)
//     platform: {
//       enabled: false
//     }
//   }
// }

// Consolidated Role Assignments
module rbac './modules/rbac.bicep' = {
  name: 'rbacAssignments'
  params: {
    storageAccountName: storage.outputs.name
    appInsightsName: monitoring.outputs.applicationInsightsName
    managedIdentityPrincipalId: mcpOneDriveDownloadIdentity.outputs.principalId
    userIdentityPrincipalId: principalId
    enableBlob: storageEndpointConfig.enableBlob
    enableQueue: storageEndpointConfig.enableQueue
    enableTable: storageEndpointConfig.enableTable
    enableFile: storageEndpointConfig.enableFile
    allowUserIdentityPrincipal: storageEndpointConfig.allowUserIdentityPrincipal
  }
}


// // Grant the function app's identity access to the storage account
// var storageBlobDataOwnerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')

// resource rbac 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
//   name: guid(storageAccount.id, userAssignedIdentity.id, storageBlobDataOwnerRole)
//   scope: storageAccount
//   properties: {
//     principalId: userAssignedIdentity.properties.principalId
//     principalType: 'ServicePrincipal'
//     roleDefinitionId: storageBlobDataOwnerRole
//   }
// }

// Container registry
module containerRegistry 'br/public:avm/res/container-registry/registry:0.1.1' = {
  name: 'registry'
  params: {
    name: '${abbrs.containerRegistryRegistries}${resourceToken}'
    location: location
    tags: tags
    publicNetworkAccess: 'Enabled'
    roleAssignments: [
      {
        principalId: mcpOneDriveDownloadIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        // ACR pull role
        roleDefinitionIdOrName: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
      }
    ]
  }
}

// Container apps environment
module containerAppsEnvironment 'br/public:avm/res/app/managed-environment:0.4.5' = {
  name: 'container-apps-environment'
  params: {
    logAnalyticsWorkspaceResourceId: monitoring.outputs.logAnalyticsWorkspaceResourceId
    name: '${abbrs.appManagedEnvironments}${resourceToken}'
    location: location
    zoneRedundant: false
  }
}

// Azure Container Apps
module mcpOneDriveDownloadFetchLatestImage './modules/fetch-container-image.bicep' = {
  name: 'mcp-onedrive-download-fetch-image'
  params: {
    exists: mcpOneDriveDownloadExists
    name: 'onedrive-download'
  }
}

module mcpOneDriveDownload 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'mcp-onedrive-download'
  params: {
    name: 'onedrive-download'
    location: location
    tags: union(tags, { 'azd-service-name': 'onedrive-download' })

    ingressTargetPort: 8080
    scaleMinReplicas: 1
    scaleMaxReplicas: 10

    secrets: {
      secureList: [
        {
          name: 'storage-connection-string'
          value: 'DefaultEndpointsProtocol=https;AccountName=${stg.name};AccountKey=${stg.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
      ]
    }

    containers: [
      {
        image: mcpOneDriveDownloadFetchLatestImage.outputs.?containers[?0].?image ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
        name: 'main'
        resources: {
          cpu: json('0.5')
          memory: '1.0Gi'
        }
        env: [
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: monitoring.outputs.applicationInsightsConnectionString
          }
          {
            name: 'AZURE_CLIENT_ID'
            value: mcpOneDriveDownloadIdentity.outputs.clientId
          }
          {
            name: 'PORT'
            value: '8080'
          }
          {
            name: 'AzureStorage__ConnectionString'
            secretRef: 'storage-connection-string'
          }
          {
            name: 'EntraId__TenantId'
            value: 'common'
          }
          {
            name: 'EntraId__ClientId'
            value: mcpEntraApp.outputs.mcpAppId
          }
          {
            name: 'EntraId__UseManagedIdentity'
            value: 'false'
          }
        ]
        args: [
          '--http'
          '--use-azure-storage'
        ]
      }
    ]
    managedIdentities: {
      systemAssigned: false
      userAssignedResourceIds: [
        mcpOneDriveDownloadIdentity.outputs.resourceId
      ]
    }
    registries: [
      {
        server: containerRegistry.outputs.loginServer
        identity: mcpOneDriveDownloadIdentity.outputs.resourceId
      }
    ]
    environmentResourceId: containerAppsEnvironment.outputs.resourceId
    corsPolicy: {
      allowedOrigins: [
        'https://make.preview.powerapps.com'
        'https://make.powerapps.com'
        'https://make.preview.powerautomate.com'
        'https://make.powerautomate.com'
        'https://copilotstudio.preview.microsoft.com'
        'https://copilotstudio.microsoft.com'
      ]
    }
  }
}

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.outputs.loginServer
output AZURE_RESOURCE_MCP_ONEDRIVE_DOWNLOAD_ID string = mcpOneDriveDownload.outputs.resourceId
output AZURE_RESOURCE_MCP_ONEDRIVE_DOWNLOAD_NAME string = mcpOneDriveDownload.outputs.name
output AZURE_RESOURCE_MCP_ONEDRIVE_DOWNLOAD_FQDN string = mcpOneDriveDownload.outputs.fqdn



// Outputs for azd
// output AZURE_RESOURCE_MCP_ONEDRIVE_DOWNLOAD_ID string = fncapp.outputs.resourceId
// output AZURE_RESOURCE_MCP_ONEDRIVE_DOWNLOAD_NAME string = fncapp.outputs.name
// output AZURE_RESOURCE_MCP_ONEDRIVE_DOWNLOAD_FQDN string = fncapp.outputs.fqdn

// output AZURE_RESOURCE_MCP_ONEDRIVE_DOWNLOAD_GATEWAY_ID string = apimService.outputs.id
// output AZURE_RESOURCE_MCP_ONEDRIVE_DOWNLOAD_GATEWAY_NAME string = apimService.outputs.name
// output AZURE_RESOURCE_MCP_ONEDRIVE_DOWNLOAD_GATEWAY_FQDN string = replace(apimService.outputs.gatewayUrl, 'https://', '')

// output AZURE_USER_ASSIGNED_IDENTITY_PRINCIPAL_ID string = userAssignedIdentity.properties.principalId
// output mcpAppId string = entraApp.outputs.mcpAppId
// // ★ postprovision 훅에서 mcp.json에 주입할 Client ID
// // 이 이름(AZURE_CLIENT_ID)이 스크립트에서 $env:AZURE_CLIENT_ID 가 됩니다.
// output AZURE_CLIENT_ID string = entraApp.outputs.mcpAppId
// // This output is no longer relevant, but keeping it to avoid breaking main.bicep for now. I will fix main.bicep next.
// output AZURE_CONTAINER_REGISTRY_ENDPOINT string = ''

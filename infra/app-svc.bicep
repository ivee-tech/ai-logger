param webAppName string
param webAppKind string
param serverFarmName string
param location string = resourceGroup().location
// param dnsZoneResourceGroupName string
// param vnetResourceGroupName string
// param vnetName string
// param subnetName string
// param hostingEnvironmentName string

var subscriptionId = subscription().subscriptionId
var resourceGroupName = resourceGroup().name
// var dnsZoneName = 'privatelink.azurewebsites.net'

var serverFarmResourceId = '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.Web/serverfarms/${serverFarmName}'
// var privateDnsZoneResourceId = '/subscriptions/${subscriptionId}/resourceGroups/${dnsZoneResourceGroupName}/providers/Microsoft.Network/privateDnsZones/${dnsZoneName}'
// var subnetResourceId = '/subscriptions/${subscriptionId}/resourceGroups/${vnetResourceGroupName}/providers/Microsoft.Network/virtualNetworks/${vnetName}/subnets/${subnetName}'
// var appServiceEnvironmentResourceId = '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.Web/hostingEnvironments/${hostingEnvironmentName}'

module webApp 'br/public:avm/res/web/site:0.15.0' = {
  name: 'webAppDeployment'
  params: {
    // Required parameters
    kind: webAppKind
    name: webAppName
    location: location
    serverFarmResourceId: serverFarmResourceId
    // privateEndpoints: [
    //   {
    //     privateDnsZoneGroup: {
    //       privateDnsZoneGroupConfigs: [
    //         {
    //           privateDnsZoneResourceId: privateDnsZoneResourceId
    //         }
    //       ]
    //     }
    //     subnetResourceId: subnetResourceId
    //   }
    // ]
    appSettingsKeyValuePairs: {
      AzureFunctionsJobHost__logging__logLevel__default: 'Trace'
      FUNCTIONS_EXTENSION_VERSION: '~4'
      FUNCTIONS_WORKER_RUNTIME: 'dotnet'
    }
    siteConfig: { alwaysOn: false }
    // virtualNetworkSubnetId: subnetResourceId
    // appServiceEnvironmentResourceId: appServiceEnvironmentResourceId
  }
}

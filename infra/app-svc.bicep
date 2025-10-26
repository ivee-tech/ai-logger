param webAppName string
param webAppKind string
param serverFarmName string
param location string = resourceGroup().location
param useLinux bool = false
param linuxFxVersion string = 'NODE|20-lts'
param netFrameworkVersion string = 'v9.0'
param alwaysOn bool = false
param appSettings object = {}
// param dnsZoneResourceGroupName string
// param vnetResourceGroupName string
// param vnetName string
// param subnetName string
// param hostingEnvironmentName string

var subscriptionId = subscription().subscriptionId
var resourceGroupName = resourceGroup().name
// var dnsZoneName = 'privatelink.azurewebsites.net'

var serverFarmResourceId = '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.Web/serverfarms/${serverFarmName}'
var siteConfigBase = {
  alwaysOn: alwaysOn
}
var siteConfigPlatform = useLinux ? {
  linuxFxVersion: linuxFxVersion
} : {
  netFrameworkVersion: netFrameworkVersion
}
var siteConfigSettings = union(siteConfigBase, siteConfigPlatform)
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
    appSettingsKeyValuePairs: appSettings
    siteConfig: siteConfigSettings
    // virtualNetworkSubnetId: subnetResourceId
    // appServiceEnvironmentResourceId: appServiceEnvironmentResourceId
  }
}

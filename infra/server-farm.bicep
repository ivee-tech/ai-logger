param name string = 'wsfamin001'
param location string = resourceGroup().location
param skuName string = 'S1'
param skuCapacity int = 1
param kind string = 'app'

module serverfarm 'br/public:avm/res/web/serverfarm:0.4.0' = {
  name: 'serverfarmDeployment'
  params: {
    // Required parameters
    name: name
    location: location
    // Non-required parameters
    kind: kind
    skuCapacity: skuCapacity
    skuName: skuName
  }
}

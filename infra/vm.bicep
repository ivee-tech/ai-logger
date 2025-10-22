param name string
param location string = resourceGroup().location
param adminUsername string
@secure()
param adminPassword string
param imageReference object
param vmSize string
param diskSizeGB int = 1024
param osType string
param zone int
param disablePasswordAuthentication bool
param tags object = {}
param encryptionAtHost bool = false
param vnetName string
param subnetName string


resource vnet 'Microsoft.Network/virtualNetworks@2020-06-01' existing = {
  name: vnetName
}

resource subnet 'Microsoft.Network/virtualNetworks/subnets@2020-06-01' existing = {
  parent: vnet
  name: subnetName
}

module vm 'br/public:avm/res/compute/virtual-machine:0.11.0' = {
  name: 'vmDeployment'
  params: {
    name: name
    location: location
    imageReference: imageReference
    adminUsername: adminUsername
    adminPassword: adminPassword
    nicConfigurations: [
      {
        ipConfigurations: [
          {
            name: 'ipconfig01'
            pipConfiguration: {
              name: 'pip-01'
            }
            subnetResourceId: subnet.id
          }
        ]
        nicSuffix: '-nic-01'
      }
    ]
    vmSize: vmSize
    osDisk: { diskSizeGB: diskSizeGB, caching: 'ReadWrite', managedDisk: { storageAccountType: 'StandardSSD_LRS' } } // Override only the disk size
    osType: osType
    zone: zone
    disablePasswordAuthentication: disablePasswordAuthentication
    tags: tags
    encryptionAtHost: encryptionAtHost
    managedIdentities: { systemAssigned: true }
  }
}

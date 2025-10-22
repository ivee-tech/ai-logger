param saName string
param saSkuName string = 'Standard_LRS'
param saKind string = 'StorageV2' //   'Storage', 'StorageV2', 'BlobStorage', 'FileStorage', 'BlockBlobStorage'

module storageAccount 'br/public:avm/res/storage/storage-account:0.27.0' = {
  name: 'storageAccountDeployment'
  params: {
    // Required parameters
    name: saName
    // Non-required parameters
    location: resourceGroup().location
    kind: saKind
    skuName: saSkuName
    accessTier: 'Hot'

  }
}

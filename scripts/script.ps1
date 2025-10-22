$rgName = 'rg-nasc-spoke-dev-001'
$vmName = 'vm-dev-aue'
$saName = ''
.\setup-vm-managed-identity.ps1 -ResourceGroup $rgName -VmName $vmName -StorageAccount $saName

# create RG
$rgName = 'rg-ailogger-001'
$location = 'australiaeast'
az group create --name $rgName --location $location

# deploy VM
$rgName = 'rg-ailogger-001'
$adminPassword = '***'
az deployment group create `
  --resource-group $rgName `
  --template-file ../infra/vm.bicep `
  --parameters ../infra/vm.ai-logger.parameters.jsonc `
  --parameters adminPassword=$adminPassword

# depploy SA
$rgName = 'rg-ailogger-001'
az deployment group create `
  --resource-group $rgName `
  --template-file ../infra/sa.bicep `
  --parameters ../infra/sa.ai-logger.parameters.jsonc

# assign Storage Blob Data Reader role to VM's managed identity
$rgName = 'rg-ailogger-001'
$vmName = 'vm-ailogger-001'
$saName = 'saailogger001'
$roleName = 'Storage Blob Data Reader'
$saId = az storage account show --name $saName --resource-group $rgName --query id --output tsv
$id = az vm identity show --resource-group $rgName --name $vmName --query principalId --output tsv
az role assignment create --assignee $id --scope $saId --role "$roleName"

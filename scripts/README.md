# Azure VM Managed Identity Setup Scripts

This directory contains scripts to set up Azure VM managed identity and role assignments for AI Logger deployment. These scripts should be run once before using the Azure DevOps deployment pipeline.

## Overview

The AI Logger deployment pipeline requires the target VM to have:
1. **System-assigned managed identity** enabled
2. **Storage Blob Data Reader** role assigned to access the deployment artifacts storage account

These scripts automate the setup process and should be executed once per VM before running the deployment pipeline.

## Available Scripts

### 1. setup-vm-managed-identity.sh (Linux/macOS/WSL)
Bash script for configuring VM managed identity using Azure CLI.

### 2. setup-vm-managed-identity.ps1 (Windows/PowerShell)
PowerShell script for configuring VM managed identity using Azure CLI.

## Prerequisites

### Common Requirements
- Target Azure VM must exist
- Storage account for deployment artifacts must exist
- Sufficient Azure permissions to:
  - Modify VM identity settings
  - Assign roles at storage account scope
  - Read VM and storage account information

### For Bash Script (setup-vm-managed-identity.sh)
- Azure CLI installed and authenticated (`az login`)
- Bash shell (Linux, macOS, WSL, or Git Bash on Windows)

### For PowerShell Script (setup-vm-managed-identity.ps1)
- Azure CLI installed and authenticated (`az login`)
- PowerShell 5.1 or later

## Usage

### Basic Usage - Bash Script

```bash
# Make script executable
chmod +x setup-vm-managed-identity.sh

# Run with required parameters
./setup-vm-managed-identity.sh \
  --resource-group "rg-ailogger-prod" \
  --vm-name "vm-ailogger-prod" \
  --storage-account "stailoggerdeployment"
```

### Basic Usage - PowerShell Script

```powershell
# Run with required parameters
.\setup-vm-managed-identity.ps1 `
  -ResourceGroup "rg-ailogger-prod" `
  -VmName "vm-ailogger-prod" `
  -StorageAccount "stailoggerdeployment"
```

### Advanced Usage Examples

#### With Different Storage Resource Group

```bash
# Bash
./setup-vm-managed-identity.sh \
  --resource-group "rg-vm" \
  --vm-name "vm-ailogger" \
  --storage-account "mystorage" \
  --storage-rg "rg-storage"
```

```powershell
# PowerShell
.\setup-vm-managed-identity.ps1 `
  -ResourceGroup "rg-vm" `
  -VmName "vm-ailogger" `
  -StorageAccount "mystorage" `
  -StorageResourceGroup "rg-storage"
```

#### Dry Run (See What Would Be Done)

```bash
# Bash
./setup-vm-managed-identity.sh \
  --resource-group "rg-ailogger-prod" \
  --vm-name "vm-ailogger-prod" \
  --storage-account "stailoggerdeployment" \
  --dry-run
```

```powershell
# PowerShell
.\setup-vm-managed-identity.ps1 `
  -ResourceGroup "rg-ailogger-prod" `
  -VmName "vm-ailogger-prod" `
  -StorageAccount "stailoggerdeployment" `
  -DryRun
```

#### Force Role Reassignment

```bash
# Bash
./setup-vm-managed-identity.sh \
  --resource-group "rg-ailogger-prod" \
  --vm-name "vm-ailogger-prod" \
  --storage-account "stailoggerdeployment" \
  --force
```

```powershell
# PowerShell
.\setup-vm-managed-identity.ps1 `
  -ResourceGroup "rg-ailogger-prod" `
  -VmName "vm-ailogger-prod" `
  -StorageAccount "stailoggerdeployment" `
  -Force
```

## Script Parameters

### Required Parameters
- **Resource Group**: Resource group containing the target VM
- **VM Name**: Name of the target virtual machine
- **Storage Account**: Storage account name for deployment artifacts

### Optional Parameters
- **Storage Resource Group**: Resource group of the storage account (defaults to VM resource group)
- **Subscription**: Azure subscription ID (uses current subscription if not specified)
- **Dry Run**: Preview changes without making modifications
- **Force**: Force role assignment even if it already exists

## Script Behavior

### What the Scripts Do

1. **Validate Prerequisites**
   - Check Azure CLI/PowerShell authentication
   - Verify VM exists
   - Verify storage account exists

2. **Configure Managed Identity**
   - Check if VM already has system-assigned managed identity
   - Enable system-assigned managed identity if not present
   - Retrieve the managed identity principal ID

3. **Assign Storage Permissions**
   - Check for existing "Storage Blob Data Reader" role assignment
   - Assign role if not present or if force flag is used
   - Implement retry logic for Azure AD propagation delays

4. **Verify Configuration**
   - Confirm managed identity is properly configured
   - Confirm role assignment is active
   - Provide status summary

### Output and Logging

Both scripts provide colored output with different message types:
- **[INFO]** - General information and success messages
- **[WARNING]** - Non-critical issues or confirmations
- **[ERROR]** - Critical errors that stop execution
- **[HEADER]** - Section headers for organization

## Error Handling

### Common Issues and Solutions

#### 1. Authentication Issues
```
Error: Azure CLI is not authenticated
```
**Solution**: Run `az login` for both bash and PowerShell scripts

#### 2. Permission Issues
```
Error: Insufficient privileges to complete the operation
```
**Solution**: Ensure your Azure account has:
- VM Contributor role (or equivalent)
- Role assignments permissions at storage account scope

#### 3. Resource Not Found
```
Error: VM 'vm-name' not found in resource group 'rg-name'
```
**Solution**: Verify VM name and resource group are correct

#### 4. Role Assignment Propagation Delays
```
Warning: Role assignment failed (attempt 1/5). Retrying in 30 seconds...
```
**Solution**: Scripts automatically retry with delays. Azure AD propagation can take time.

#### 5. Existing Role Assignment
```
Info: Storage Blob Data Reader role already assigned
```
**Solution**: This is normal. Use `--force` or `-Force` flag to reassign if needed.

## Integration with Deployment Pipeline

After running these scripts successfully, update your Azure DevOps pipeline variables:

```yaml
variables:
  vmResourceGroup: 'rg-ailogger-prod'        # From script parameters
  vmName: 'vm-ailogger-prod'                 # From script parameters  
  storageAccountName: 'stailoggerdeployment' # From script parameters
  containerName: 'deployments'               # Container for artifacts
```

## Security Considerations

### Principle of Least Privilege
The scripts assign only the minimum required permissions:
- **Storage Blob Data Reader**: Allows reading deployment artifacts from storage
- **Scope**: Limited to the specific storage account only

### Managed Identity Benefits
- No credentials stored on the VM
- Automatic token management by Azure
- Integration with Azure AD for auditing
- Automatic rotation and lifecycle management

### Network Security
Ensure your VM has proper network connectivity:
- Service endpoints for Azure Storage (recommended)
- Private endpoints for enhanced security
- NSG rules allowing outbound HTTPS to Azure Storage

## Troubleshooting

### Verification Commands

#### Check Managed Identity Status
```bash
# Azure CLI
az vm identity show --resource-group <rg> --name <vm>
```

```powershell
# PowerShell
(Get-AzVM -ResourceGroupName <rg> -Name <vm>).Identity
```

#### Check Role Assignments
```bash
# Azure CLI (replace <principal-id> and <storage-resource-id>)
az role assignment list --assignee <principal-id> --scope <storage-resource-id>
```

```powershell
# PowerShell
Get-AzRoleAssignment -ObjectId <principal-id> -Scope <storage-resource-id>
```

#### Test from VM (via Azure Bastion)
```bash
# On the VM, test managed identity authentication
curl -H "Metadata: true" "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https://storage.azure.com/"

# Test Azure CLI with managed identity
az login --identity
az storage blob list --container-name deployments --account-name <storage-account> --auth-mode login
```

### Re-running Scripts
Scripts are designed to be idempotent and can be safely re-run:
- Existing configurations are detected and preserved
- Use `--force` or `-Force` to override existing role assignments
- Use `--dry-run` or `-DryRun` to preview changes

## Support and Maintenance

### Script Maintenance
- Scripts are versioned (currently v1.0.0)
- Designed to work with current Azure CLI and PowerShell modules
- Error handling includes retry logic for transient failures

### Getting Help
```bash
# Bash script help
./setup-vm-managed-identity.sh --help
```

```powershell
# PowerShell script help
Get-Help .\setup-vm-managed-identity.ps1 -Full
```

### Updates and Improvements
These scripts are part of the AI Logger project and will be maintained alongside the deployment pipeline. Check for updates when updating the main project.
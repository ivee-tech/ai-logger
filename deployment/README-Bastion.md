# AI Logger CLI Deployment for Azure Bastion VMs

This guide explains how to deploy the AI Logger CLI tool to Azure VMs that are only accessible via Azure Bastion (no public IP).

## Overview

When deploying to VMs without public IPs, direct SSH/RDP access isn't possible. This pipeline uses:
- **Azure Storage Account** as an intermediate file transfer mechanism
- **VM Managed Identity** for secure authentication to Azure Storage
- **Azure CLI `az vm run-command`** to execute deployment commands remotely
- **Azure Bastion** (implied) for any manual access needed

## Architecture

```
Azure DevOps Pipeline → Azure Storage Account → VM (via run-command) → CLI Installation
                                ↑
                        VM Managed Identity
```

## Prerequisites

### Azure Resources Required

1. **Azure Virtual Machine**
   - Linux VM (Ubuntu 20.04+ recommended)
   - Must have **System-assigned Managed Identity** enabled
   - Network connectivity to Azure Storage (typically via Service Endpoints or Private Endpoints)

2. **Azure Storage Account**
   - Standard storage account for artifact transfer
   - Must be accessible from both Azure DevOps and the target VM
   - Container named `deployments` (created automatically by pipeline)

3. **Azure Bastion Host** (for manual access)
   - Configured in the same VNet as the target VM
   - Used for troubleshooting and manual operations

4. **Network Configuration**
   - VM subnet with route to Azure Storage
   - Service Endpoints or Private Endpoints for Azure Storage (recommended)
   - NSG rules allowing outbound HTTPS (443) to Azure Storage

## Setup Instructions

### 1. Configure VM Managed Identity

The pipeline automatically configures this, but you can set it up manually:

```bash
# Enable system-assigned managed identity
az vm identity assign --resource-group <resource-group> --name <vm-name>

# Get the principal ID
PRINCIPAL_ID=$(az vm identity show --resource-group <resource-group> --name <vm-name> --query principalId -o tsv)

# Assign Storage Blob Data Reader role
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Storage Blob Data Reader" \
  --scope "/subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.Storage/storageAccounts/<storage-account>"
```

### 2. Update Pipeline Variables

```yaml
variables:
  azureSubscription: 'YourAzureServiceConnection'
  vmResourceGroup: 'rg-ailogger-prod'
  vmName: 'vm-ailogger-prod'
  deploymentPath: '/usr/local/bin/ailogger'
  storageAccountName: 'stailoggerdeployment'  # Your storage account
  containerName: 'deployments'
```

### 3. Storage Account Configuration

Ensure your storage account allows access from:
- Azure DevOps (for uploading artifacts)
- Target VM (for downloading artifacts via managed identity)

#### Option A: Service Endpoints (Recommended)
```bash
# Enable service endpoint on VM subnet
az network vnet subnet update \
  --resource-group <vnet-resource-group> \
  --vnet-name <vnet-name> \
  --name <subnet-name> \
  --service-endpoints Microsoft.Storage

# Configure storage account network rules
az storage account network-rule add \
  --account-name <storage-account> \
  --subnet <subnet-resource-id>
```

#### Option B: Private Endpoints (Enterprise)
```bash
# Create private endpoint for storage account
az network private-endpoint create \
  --resource-group <resource-group> \
  --name <storage-account>-pe \
  --vnet-name <vnet-name> \
  --subnet <subnet-name> \
  --private-connection-resource-id <storage-account-resource-id> \
  --group-ids blob \
  --connection-name <storage-account>-connection
```

## Pipeline Process

### Stage 1: Build
Standard .NET build process:
1. Restore packages
2. Build solution
3. Run tests
4. Publish self-contained deployment
5. Create deployment artifacts

### Stage 2: Deploy to Private VM

#### Step 1: Upload to Storage
- Creates deployment archive (`ailogger-<BuildId>.tar.gz`)
- Uploads to Azure Storage using service connection credentials

#### Step 2: Configure VM Managed Identity
- Ensures VM has system-assigned managed identity
- Assigns Storage Blob Data Reader role
- Handles cases where identity already exists

#### Step 3: Deploy via VM Run-Command
Executes a comprehensive deployment script on the VM that:
- Authenticates using managed identity (`az login --identity`)
- Downloads deployment package from storage
- Stops any running processes
- Backs up existing installation
- Deploys new application files
- Creates CLI wrapper script
- Configures system PATH
- Sets up symlinks

#### Step 4: Verification
- Tests CLI functionality
- Verifies file permissions
- Checks PATH configuration
- Validates symlinks

#### Step 5: Cleanup
- Removes old deployment artifacts from storage
- Cleans up old backups on VM
- Provides usage instructions

## Deployment Script Details

The pipeline generates and executes this script on the target VM:

```bash
#!/bin/bash
set -e

# Parameters passed from pipeline
BUILD_ID="$1"
STORAGE_ACCOUNT="$2"
CONTAINER_NAME="$3"
DEPLOY_PATH="$4"

# Authenticate with managed identity
az login --identity

# Download and extract deployment package
az storage blob download \
  --container-name "$CONTAINER_NAME" \
  --name "ailogger-$BUILD_ID.tar.gz" \
  --file "/tmp/ailogger-$BUILD_ID.tar.gz" \
  --account-name "$STORAGE_ACCOUNT" \
  --auth-mode login

# Deploy application
tar -xzf "/tmp/ailogger-$BUILD_ID.tar.gz" -C /opt/ailogger/

# Configure CLI wrapper and permissions
# ... (see pipeline for full details)
```

## Manual Operations via Azure Bastion

### Accessing the VM
1. Navigate to Azure Portal → Virtual Machines → Your VM
2. Click "Connect" → "Bastion"
3. Enter credentials and connect

### Troubleshooting Commands
```bash
# Check deployment status
ls -la /opt/ailogger/
ls -la /usr/local/bin/ailogger

# Test CLI functionality
/opt/ailogger/AiLogger.Console --help
ailogger --help

# Check logs
journalctl --since "1 hour ago" | grep ailogger
ls -la /var/log/ailogger/

# Verify managed identity
az login --identity
az account show

# Check storage connectivity
az storage blob list --container-name deployments --account-name <storage-account> --auth-mode login
```

## Network Requirements

### Outbound Connectivity from VM
- **Azure Storage**: `*.blob.core.windows.net` (HTTPS/443)
- **Azure Login**: `login.microsoftonline.com` (HTTPS/443)
- **Package Repositories**: For downloading Azure CLI if needed

### Service Tags (for NSG rules)
- `Storage` - For Azure Storage access
- `AzureActiveDirectory` - For managed identity authentication

## Security Considerations

### Managed Identity Benefits
- No credentials stored on VM
- Automatic token management
- Scoped permissions (only Storage Blob Data Reader)
- Audit trail in Azure AD

### Network Security
- No public IP required on VM
- All traffic goes through Azure backbone
- Private endpoints for additional isolation
- Service endpoints for secure storage access

### Storage Security
- Time-limited artifacts (auto-cleanup after 5 deployments)
- Role-based access control
- Audit logging for storage access

## Troubleshooting

### Common Issues

#### 1. Managed Identity Not Working
```bash
# On VM, check identity status
curl -H "Metadata: true" "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https://storage.azure.com/"

# Should return access token
```

#### 2. Storage Access Denied
- Verify storage account network rules
- Check managed identity role assignments
- Ensure service endpoints are configured

#### 3. Download Failures
```bash
# Test storage connectivity from VM
az storage blob list --container-name deployments --account-name <storage> --auth-mode login
```

#### 4. Pipeline Run-Command Timeouts
- Large deployments may timeout
- Consider breaking into smaller steps
- Check VM performance and connectivity

### Diagnostic Commands

#### From Azure DevOps Pipeline
```bash
# Check VM extension status
az vm extension list --resource-group <rg> --vm-name <vm>

# Test run-command functionality
az vm run-command invoke \
  --resource-group <rg> \
  --name <vm> \
  --command-id RunShellScript \
  --scripts "echo 'Hello from VM'"
```

#### From VM (via Bastion)
```bash
# Check Azure CLI installation
which az
az --version

# Test managed identity
az login --identity
az account show

# Check deployment files
ls -la /opt/ailogger/
ls -la /usr/local/bin/ailogger

# Test CLI
source /etc/profile.d/ailogger.sh
ailogger --help
```

## Performance Considerations

### Deployment Time
- Typical deployment: 5-10 minutes
- Factors affecting time:
  - VM performance
  - Network connectivity to storage
  - Size of deployment package

### Optimization Tips
- Use smaller deployment packages when possible
- Consider VM size for better performance
- Use Premium Storage for VMs when available
- Implement parallel processing where safe

## Monitoring and Alerting

### Pipeline Monitoring
- Azure DevOps pipeline success/failure alerts
- Monitor deployment duration trends
- Track storage usage and costs

### VM Monitoring
- Azure Monitor for VM performance
- Application Insights for CLI usage (if configured)
- Storage access logs for troubleshooting

### Recommended Alerts
- Pipeline failure notifications
- VM disk space monitoring
- Storage account access anomalies
- Managed identity authentication failures

This approach provides a secure, scalable solution for deploying CLI tools to private Azure VMs without requiring public IP addresses or direct network access.
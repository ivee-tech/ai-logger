# AI Logger Azure Pipeline CLI Deployment Guide

This document explains how to set up and use the Azure DevOps pipeline to deploy the AI Logger as a command-line interface (CLI) tool to an Azure Virtual Machine.

> **📋 Note**: This guide covers deployment to VMs accessible via Azure Bastion (no public IP). For VMs with public IPs, see the original deployment approach. For detailed Bastion-specific instructions, see [README-Bastion.md](README-Bastion.md).

## Prerequisites

### Azure Resources
1. **Azure DevOps Project** with access to Azure Pipelines
2. **Azure Subscription** with appropriate permissions
3. **Azure Virtual Machine** (Linux recommended, Ubuntu 20.04+ or CentOS 8+)
4. **Azure Storage Account** for deployment artifacts (optional but recommended)
5. **Azure Service Connection** configured in Azure DevOps

### Azure VM Requirements
- **Operating System**: Linux (Ubuntu 20.04+) or Windows Server 2019+
- **RAM**: Minimum 2GB, recommended 4GB+
- **CPU**: 2+ cores recommended
- **Storage**: 20GB+ available space
- **Network**: Outbound access to Azure Storage and Azure AD endpoints
- **Managed Identity**: System-assigned managed identity enabled
- **Azure Bastion**: For manual access (no public IP required)

## Setup Instructions

### 1. Configure VM Managed Identity (One-time Setup)

Before running the deployment pipeline, configure the VM's managed identity and permissions:

#### Option A: Using Bash Script (Linux/macOS/WSL)
```bash
cd scripts
chmod +x setup-vm-managed-identity.sh
./setup-vm-managed-identity.sh \
  --resource-group "rg-ailogger-prod" \
  --vm-name "vm-ailogger-prod" \
  --storage-account "stailoggerdeployment"
```

#### Option B: Using PowerShell Script (Windows)
```powershell
cd scripts
.\setup-vm-managed-identity.ps1 `
  -ResourceGroup "rg-ailogger-prod" `
  -VmName "vm-ailogger-prod" `
  -StorageAccount "stailoggerdeployment"
```

> **📋 Note**: See [scripts/README.md](../scripts/README.md) for detailed setup script documentation.

### 2. Configure Azure Service Connection

1. In Azure DevOps, go to **Project Settings** > **Service connections**
2. Create a new **Azure Resource Manager** service connection
3. Select **Service principal (automatic)** or **Service principal (manual)**
4. Configure connection with appropriate subscription and resource group permissions
5. Name the connection (update `azureSubscription` variable in pipeline)

### 2. Update Pipeline Variables

Edit the variables section in `azure-pipelines.yml`:

```yaml
variables:
  # Azure Variables - UPDATE THESE
  azureSubscription: 'YourAzureServiceConnection'        # Your service connection name
  vmResourceGroup: 'rg-ailogger-prod'                    # Your VM resource group
  vmName: 'vm-ailogger-prod'                             # Your VM name
  deploymentPath: '/usr/local/bin/ailogger'              # Linux CLI path, Windows: C:\Program Files\AILogger
  storageAccountName: 'stailoggerdeployment'             # Storage account for artifact transfer
  containerName: 'deployments'                           # Container name for artifacts
```

### 3. Configure Azure VM

#### For Linux VMs:

1. **Enable Azure CLI authentication**:
   ```bash
   # Install Azure CLI
   curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
   
   # Login (this will be handled by the pipeline using service principal)
   az login --service-principal -u <client-id> -p <client-secret> --tenant <tenant-id>
   ```

2. **Configure VM for deployments**:
   ```bash
   # Run the setup script (included in deployment folder)
   chmod +x deployment/setup-linux.sh
   ./deployment/setup-linux.sh
   ```

#### For Windows VMs:

1. **Install PowerShell and Azure CLI** (if not present)
2. **Run setup script**:
   ```cmd
   deployment\setup-windows.bat
   ```

### 4. Environment Configuration

#### Production Configuration
Update the production configuration in `deployment/appsettings.Production.json`:

1. **AI Provider Settings**:
   - Configure Azure OpenAI endpoint and API key
   - Set up alternative providers (OpenAI, Ollama) as needed

2. **Security Settings**:
   - Configure Key Vault for secret management
   - Set encryption keys

3. **Storage Settings**:
   - Configure Azure Storage for data persistence
   - Set up backup retention policies

#### Environment Variables
Set these environment variables on your Azure VM or in Azure Key Vault:

```bash
# AI Provider Configuration
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_API_KEY="your-api-key"
export OPENAI_API_KEY="your-openai-key"

# Security Configuration
export ENCRYPTION_KEY="your-encryption-key"
export KEY_VAULT_ENDPOINT="https://your-keyvault.vault.azure.net/"
export KEY_VAULT_CLIENT_ID="your-client-id"
export KEY_VAULT_CLIENT_SECRET="your-client-secret"
export KEY_VAULT_TENANT_ID="your-tenant-id"

# Storage Configuration
export STORAGE_CONNECTION_STRING="DefaultEndpointsProtocol=https;AccountName=..."

# Monitoring Configuration
export APPINSIGHTS_CONNECTIONSTRING="InstrumentationKey=..."
```

## Pipeline Stages

### Stage 1: Build
- **Restore NuGet packages**
- **Build the solution**
- **Run unit tests**
- **Publish application** (self-contained deployment)
- **Create deployment artifacts**

### Stage 2: Deploy
- **Download artifacts**
- **Stop any running processes** (if any)
- **Create CLI installation directory**
- **Upload application files**
- **Configure CLI wrapper and PATH**
- **Test CLI installation**
- **Verify CLI deployment**
- **Cleanup old backups and finalize**

## Deployment Features

### Security
- **Service User**: Runs as dedicated `ailogger` user (Linux)
- **File Permissions**: Restricted access to application files
- **Firewall Configuration**: Basic firewall rules
- **Secrets Management**: Integration with Azure Key Vault

### CLI Features
- **Global Access**: Available system-wide via PATH configuration
- **Multiple Access Methods**: Direct execution, wrapper script, or symlink
- **User-Friendly**: Easy command-line interface with help and options
- **Backup Management**: Automatic backup of previous installations

### Scalability
- **Self-contained Deployment**: No external .NET runtime dependencies
- **Resource Management**: Memory and CPU limits configuration
- **Performance Tuning**: GC and thread pool optimization

## Usage

### Manual Pipeline Trigger
1. Go to Azure DevOps Pipelines
2. Select the AI Logger pipeline
3. Click **Run pipeline**
4. Select branch and verify variables
5. Run the pipeline

### Automatic Triggers
Pipeline automatically triggers on:
- **Push to main branch**
- **Push to develop branch**
- **Changes to src/ folder**

### Using the CLI Tool

#### Basic Usage (Linux):
```bash
# Reload PATH (first time after installation)
source /etc/profile.d/ailogger.sh

# Use the CLI tool
ailogger --help
ailogger --version
ailogger process-log --input /path/to/logfile.log --output /path/to/sanitized.log

# Alternative usage methods
/usr/local/bin/ailogger --help
/opt/ailogger/AiLogger.Console --help
```

#### Basic Usage (Windows):
```cmd
# Use the CLI tool (after PATH configuration)
ailogger --help
ailogger --version
ailogger process-log --input C:\logs\logfile.log --output C:\logs\sanitized.log

# Direct execution
C:\Program Files\AILogger\AiLogger.Console.exe --help
```

## Troubleshooting

### Common Issues

1. **Service Connection Issues**:
   - Verify Azure service connection permissions
   - Check subscription access and resource group permissions

2. **VM Access Issues**:
   - Ensure VM has public IP or proper network configuration
   - Verify Azure CLI is installed and authenticated on VM

3. **Deployment Failures**:
   - Check pipeline logs for detailed error messages
   - Verify storage account access permissions
   - Ensure VM has sufficient disk space

4. **CLI Access Issues**:
   - Run `source /etc/profile.d/ailogger.sh` to reload PATH
   - Check file permissions: `ls -la /usr/local/bin/ailogger`
   - Verify installation: `ls -la /opt/ailogger/`
   - Test direct execution: `/opt/ailogger/AiLogger.Console --help`

5. **Command Not Found**:
   - Ensure PATH is updated: `echo $PATH | grep ailogger`
   - Try full path: `/usr/local/bin/ailogger`
   - Check executable permissions: `chmod +x /usr/local/bin/ailogger`

### Pipeline Customization

#### Windows VM Deployment
To deploy to Windows VM, modify these sections:

1. **Change runtime** in publish task:
   ```yaml
   arguments: '--configuration $(buildConfiguration) --output $(Build.ArtifactStagingDirectory)/publish --no-build --self-contained true --runtime win-x64'
   ```

2. **Update deployment path**:
   ```yaml
   deploymentPath: 'C:\ailogger'
   ```

3. **Use PowerShell scripts** instead of bash scripts in deployment tasks

#### Multiple Environment Deployment
Add additional stages for different environments:

```yaml
- stage: DeployDev
  displayName: 'Deploy to Development'
  condition: eq(variables['Build.SourceBranch'], 'refs/heads/develop')
  variables:
    vmResourceGroup: 'rg-ailogger-dev'
    vmName: 'vm-ailogger-dev'
```

## Security Considerations

1. **Network Security**:
   - Use private VMs with bastion host access when possible
   - Implement network security groups (NSGs)
   - Consider VPN or ExpressRoute for production deployments

2. **Identity and Access**:
   - Use managed identity for Azure resource access
   - Implement least-privilege access principles
   - Regular rotation of API keys and secrets

3. **Data Protection**:
   - Encrypt sensitive data at rest and in transit
   - Use Azure Key Vault for secret management
   - Implement proper backup and recovery procedures

4. **Monitoring and Auditing**:
   - Enable Azure Monitor and Application Insights
   - Set up alerting for failures and anomalies
   - Regular security assessments and updates
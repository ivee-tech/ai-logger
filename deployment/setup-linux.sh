#!/bin/bash

# AI Logger CLI Setup Script for Linux VM
# This script sets up the AI Logger application as a CLI tool on a Linux VM

set -e

# Configuration
APP_NAME="ailogger"
DEPLOY_PATH="/opt/ailogger"
CLI_PATH="/usr/local/bin/ailogger"
LOG_PATH="/var/log/ailogger"

echo "Starting AI Logger CLI setup..."

# Create directories
echo "Creating application directories..."
sudo mkdir -p "$DEPLOY_PATH"
sudo mkdir -p "$LOG_PATH" 
sudo mkdir -p /etc/ailogger
sudo mkdir -p "$(dirname "$CLI_PATH")"

# Set permissions for CLI usage (accessible by all users)
sudo chmod 755 "$DEPLOY_PATH"
sudo chmod 777 "$LOG_PATH"  # Allow all users to write logs
sudo chmod 755 /etc/ailogger

# Install .NET runtime if not present
if ! command -v dotnet &> /dev/null; then
    echo "Installing .NET runtime..."
    
    # Detect OS and install accordingly
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        OS=$NAME
        
        case "$OS" in
            "Ubuntu"*)
                wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
                sudo dpkg -i packages-microsoft-prod.deb
                sudo apt-get update
                sudo apt-get install -y dotnet-runtime-9.0
                ;;
            "CentOS Linux"*|"Red Hat"*)
                sudo dnf install -y dotnet-runtime-9.0
                ;;
            *)
                echo "Unsupported OS for automatic .NET installation: $OS"
                echo "Please install .NET 9.0 runtime manually"
                exit 1
                ;;
        esac
    fi
fi

# Configure firewall (if UFW is available)
if command -v ufw &> /dev/null; then
    echo "Configuring firewall..."
    sudo ufw allow 22/tcp  # SSH
    # Add any additional ports your application needs
fi

# Create CLI wrapper script
echo "Creating CLI wrapper script..."
sudo tee "$CLI_PATH" > /dev/null <<EOF
#!/bin/bash
# AI Logger CLI Wrapper Script

# Set working directory to application directory
cd "$DEPLOY_PATH"

# Set environment variables
export ASPNETCORE_ENVIRONMENT=Production
export DOTNET_ENVIRONMENT=Production

# Execute the application with all passed arguments
exec ./AiLogger.Console "\$@"
EOF

# Make CLI wrapper executable
sudo chmod +x "$CLI_PATH"

# Add to PATH for all users
echo "Configuring system PATH..."
sudo tee /etc/profile.d/ailogger.sh > /dev/null <<EOF
# AI Logger CLI tool
export PATH=\$PATH:$(dirname "$CLI_PATH")
EOF

sudo chmod +x /etc/profile.d/ailogger.sh

echo "AI Logger CLI setup completed successfully!"
echo
echo "Next steps:"
echo "1. Copy application files to $DEPLOY_PATH"
echo "2. Update configuration in $DEPLOY_PATH/appsettings.json"
echo "3. Set executable permissions: chmod +x $DEPLOY_PATH/AiLogger.Console"
echo "4. Reload PATH: source /etc/profile.d/ailogger.sh"
echo "5. Test installation: ailogger --help"
echo
echo "Usage options:"
echo "- Direct execution: $DEPLOY_PATH/AiLogger.Console [options]"
echo "- Via CLI wrapper: $CLI_PATH [options]"
echo "- Via PATH (after reload): ailogger [options]"
echo
echo "Configuration: $DEPLOY_PATH/appsettings.json"
echo "Logs directory: $LOG_PATH"
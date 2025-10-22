#!/bin/bash

# AI Logger CLI Installation Validation Script
# This script validates that the AI Logger CLI tool is properly installed

set -e

# Configuration
CLI_NAME="ailogger"
DEPLOY_PATH="/opt/ailogger"
CLI_PATH="/usr/local/bin/ailogger"
LOG_PATH="/var/log/ailogger"
EXPECTED_FILES=("AiLogger.Console" "appsettings.json" "AiLogger.Core.dll" "AiLogger.Providers.dll")

echo "=== AI Logger CLI Installation Validation ==="
echo "Timestamp: $(date)"
echo

# Check if CLI wrapper exists
echo "1. Checking CLI wrapper..."
if [ -f "$CLI_PATH" ]; then
    echo "✓ CLI wrapper exists: $CLI_PATH"
    
    # Check if it's executable
    if [ -x "$CLI_PATH" ]; then
        echo "✓ CLI wrapper is executable"
    else
        echo "⚠ CLI wrapper is not executable"
        echo "Fixing permissions..."
        sudo chmod +x "$CLI_PATH"
    fi
else
    echo "✗ CLI wrapper not found: $CLI_PATH"
    exit 1
fi

# Check PATH configuration
echo
echo "2. Checking PATH configuration..."
if [ -f "/etc/profile.d/ailogger.sh" ]; then
    echo "✓ PATH configuration file exists"
    
    # Source the file to test
    source /etc/profile.d/ailogger.sh
    
    # Check if command is in PATH
    if command -v ailogger &> /dev/null; then
        echo "✓ ailogger command is in PATH"
    else
        echo "⚠ ailogger command not found in PATH (may need to reload shell)"
    fi
else
    echo "⚠ PATH configuration file not found"
fi

# Check if deployment directory exists and has correct permissions
echo
echo "3. Checking installation directory..."
if [ -d "$DEPLOY_PATH" ]; then
    echo "✓ Installation directory exists: $DEPLOY_PATH"
    
    # Check permissions
    PERMS=$(stat -c '%a' "$DEPLOY_PATH")
    if [ "$PERMS" = "755" ]; then
        echo "✓ Correct permissions: $PERMS"
    else
        echo "⚠ Unexpected permissions: $PERMS (expected: 755)"
    fi
else
    echo "✗ Installation directory not found: $DEPLOY_PATH"
    exit 1
fi

# Check for required files
echo
echo "4. Checking application files..."
for file in "${EXPECTED_FILES[@]}"; do
    if [ -f "$DEPLOY_PATH/$file" ]; then
        echo "✓ Found: $file"
    else
        echo "✗ Missing: $file"
    fi
done

# Check if main executable is executable
if [ -x "$DEPLOY_PATH/AiLogger.Console" ]; then
    echo "✓ Main executable has execute permissions"
else
    echo "⚠ Main executable is not executable"
    echo "Fixing permissions..."
    sudo chmod +x "$DEPLOY_PATH/AiLogger.Console"
fi

# Check logs directory
echo
echo "5. Checking logs directory..."
if [ -d "$LOG_PATH" ]; then
    echo "✓ Log directory exists: $LOG_PATH"
    
    # Check for log files
    LOG_COUNT=$(find "$LOG_PATH" -name "*.log" | wc -l)
    if [ $LOG_COUNT -gt 0 ]; then
        echo "✓ Found $LOG_COUNT log file(s)"
    else
        echo "⚠ No log files found (this might be normal for new deployment)"
    fi
else
    echo "⚠ Log directory not found: $LOG_PATH"
    echo "Creating log directory..."
    sudo mkdir -p "$LOG_PATH"
    sudo chown ailogger:ailogger "$LOG_PATH"
fi

# Check .NET runtime
echo
echo "6. Checking .NET runtime..."
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    echo "✓ .NET runtime is available: $DOTNET_VERSION"
else
    echo "⚠ .NET runtime not found in PATH (might be using self-contained deployment)"
fi

# Test CLI functionality
echo
echo "7. Testing CLI functionality..."

# Test help command
echo "Testing help command..."
if timeout 10s "$CLI_PATH" --help >/dev/null 2>&1; then
    echo "✓ Help command executed successfully"
elif timeout 10s "$DEPLOY_PATH/AiLogger.Console" --help >/dev/null 2>&1; then
    echo "✓ Direct execution works (help command)"
else
    echo "⚠ CLI help command failed or timed out"
fi

# Test version command (if available)
echo "Testing version command..."
if timeout 5s "$CLI_PATH" --version >/dev/null 2>&1; then
    echo "✓ Version command executed successfully"
elif timeout 5s "$DEPLOY_PATH/AiLogger.Console" --version >/dev/null 2>&1; then
    echo "✓ Direct execution works (version command)"
else
    echo "ℹ Version command not available or failed"
fi

# Check if service is listening (if applicable)
echo
echo "8. Checking network connectivity..."
# This assumes the application might expose a health endpoint
# Adjust port as needed for your application
HEALTH_PORT=8080
if netstat -tuln 2>/dev/null | grep -q ":$HEALTH_PORT "; then
    echo "✓ Application is listening on port $HEALTH_PORT"
else
    echo "ℹ Application is not listening on port $HEALTH_PORT (might be normal for console app)"
fi

# Check for running processes (should be none for CLI tool)
echo
echo "9. Checking for running processes..."
RUNNING_PROCESSES=$(ps aux | grep AiLogger.Console | grep -v grep | wc -l)
if [ $RUNNING_PROCESSES -eq 0 ]; then
    echo "✓ No persistent processes running (expected for CLI tool)"
else
    echo "⚠ Found $RUNNING_PROCESSES running AiLogger processes:"
    ps aux | grep AiLogger.Console | grep -v grep
fi

# Disk space check
echo
echo "10. Checking disk space..."
DISK_USAGE=$(df -h "$DEPLOY_PATH" | awk 'NR==2{print $5}' | sed 's/%//')
if [ $DISK_USAGE -lt 90 ]; then
    echo "✓ Disk usage is acceptable: ${DISK_USAGE}%"
else
    echo "⚠ High disk usage: ${DISK_USAGE}%"
fi

# Configuration validation
echo
echo "11. Validating configuration..."
CONFIG_FILE="$DEPLOY_PATH/appsettings.json"
if [ -f "$CONFIG_FILE" ]; then
    echo "✓ Configuration file exists"
    
    # Check if it's valid JSON
    if python3 -m json.tool "$CONFIG_FILE" >/dev/null 2>&1; then
        echo "✓ Configuration file is valid JSON"
    else
        echo "⚠ Configuration file has JSON syntax errors"
    fi
else
    echo "✗ Configuration file not found"
fi

echo
echo "=== Validation Summary ==="
echo "CLI Wrapper: $CLI_PATH"
echo "Installation Path: $DEPLOY_PATH"
echo "Log Path: $LOG_PATH"
echo "PATH Configuration: /etc/profile.d/ailogger.sh"
echo
echo "=== Usage Instructions ==="
echo "To use the AI Logger CLI:"
echo "1. Reload your shell: source /etc/profile.d/ailogger.sh"
echo "2. Run: ailogger --help"
echo "3. Or directly: $CLI_PATH --help"
echo "4. Or from install directory: cd $DEPLOY_PATH && ./AiLogger.Console --help"
echo
echo "Validation completed at $(date)"

# Return appropriate exit code
if [ -x "$CLI_PATH" ] && [ -x "$DEPLOY_PATH/AiLogger.Console" ]; then
    echo "✓ Overall status: INSTALLED"
    exit 0
else
    echo "✗ Overall status: INSTALLATION_INCOMPLETE"
    exit 1
fi
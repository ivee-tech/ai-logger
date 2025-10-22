@echo off
REM AI Logger Deployment Script for Windows VM
REM This script sets up the AI Logger application on a Windows VM

setlocal EnableDelayedExpansion

REM Configuration
set "APP_NAME=AiLogger"
set "DEPLOY_PATH=C:\ailogger"
set "SERVICE_NAME=AILoggerService"
set "LOG_PATH=C:\Logs\AILogger"

echo Starting AI Logger deployment setup for Windows...

REM Create directories
echo Creating application directories...
if not exist "%DEPLOY_PATH%" mkdir "%DEPLOY_PATH%"
if not exist "%LOG_PATH%" mkdir "%LOG_PATH%"

REM Check if .NET runtime is installed
echo Checking for .NET runtime...
dotnet --info >nul 2>&1
if errorlevel 1 (
    echo .NET runtime not found. Please install .NET 9.0 runtime from:
    echo https://dotnet.microsoft.com/download/dotnet/9.0
    echo Continuing with deployment setup...
)

REM Configure Windows Firewall (optional)
echo Configuring Windows Firewall...
netsh advfirewall firewall add rule name="AI Logger" dir=in action=allow protocol=TCP localport=3389

REM Create application configuration directory
if not exist "C:\ProgramData\AILogger" mkdir "C:\ProgramData\AILogger"

REM Set permissions
echo Setting directory permissions...
icacls "%DEPLOY_PATH%" /grant "Everyone:(OI)(CI)F" /T
icacls "%LOG_PATH%" /grant "Everyone:(OI)(CI)F" /T

echo AI Logger deployment setup completed successfully!
echo.
echo Next steps:
echo 1. Copy application files to %DEPLOY_PATH%
echo 2. Update configuration in %DEPLOY_PATH%\appsettings.json
echo 3. Install as Windows Service using: sc create "%SERVICE_NAME%" binPath="%DEPLOY_PATH%\AiLogger.Console.exe"
echo 4. Start the service with: sc start "%SERVICE_NAME%"
echo 5. Check service status with: sc query "%SERVICE_NAME%"
echo.
echo Alternative: Run as console application directly from %DEPLOY_PATH%\AiLogger.Console.exe

pause
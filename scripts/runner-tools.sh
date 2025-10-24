# install az CLI
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

# install Ollama
sudo apt update && sudo apt upgrade -y
curl -fsSL https://ollama.com/install.sh | sh
# verify
ollama version

# Install .NET 9.0 runtime if not present
sudo add-apt-repository ppa:dotnet/backports
sudo apt-get update && \
  sudo apt-get install -y aspnetcore-runtime-9.0

# verify
dotnet --list-runtimes | grep 'Microsoft.AspNetCore.App 9.0'

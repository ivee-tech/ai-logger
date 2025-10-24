# install az CLI
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

# install Ollama
sudo apt update && sudo apt upgrade -y
curl -fsSL https://ollama.com/install.sh | sh
# verify
ollama version

# Install .NET 9.0 runtime if not present
sudo add-apt-repository ppa:dotnet/backports
# install SDK
sudo apt-get update && \
  sudo apt-get install -y dotnet-sdk-9.0
# install runtime
sudo apt-get update && \
  sudo apt-get install -y aspnetcore-runtime-9.0
# verify
dotnet --list-runtimes | grep 'Microsoft.AspNetCore.App 9.0'


## execute the runner in background
nohup ./run.sh &
# to kill  the process triggered by nohup, identify the PID executing run.sh, then call kill <PID>
ps aux | grep run.sh
kill <PID> 
# if the runners are still showing in GH runners dashboard, you may need to restart the GH runner VMs

## run the GH runner as a service
# install
sudo ./svc.sh install
# start
sudo ./svc.sh start
# check status
sudo ./svc.sh status
# stop
sudo ./svc.sh stop
# uninstall
sudo ./svc.sh uninstall

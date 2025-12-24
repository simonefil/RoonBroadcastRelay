# Roon Relay - Linux Deployment Guide

## Prerequisites

- Linux server (Debian, Ubuntu, or similar)
- Root or sudo access
- Network connectivity to Roon server and clients

## Installation

### 1. Download the latest release

```bash
# Create installation directory
sudo mkdir -p /opt/roonrelay
cd /opt/roonrelay

# Download latest release
wget https://github.com/simonefil/RoonRelay/releases/download/1.0/RoonRelay

# Make executable
sudo chmod +x RoonRelay
```

### 2. Create configuration file

```bash
sudo nano /opt/roonrelay/appsettings.json
```

Example configuration:

```json
{
  "SiteName": "MainRelay",
  "TunnelPort": 9004,
  "RemoteRelayIp": "",
  "LocalInterfaces": [
    {
      "LocalIp": "192.168.1.100",
      "BroadcastAddress": "192.168.1.255",
      "SubnetMask": "255.255.255.0"
    }
  ],
  "UnicastTargets": []
}
```

See [EXAMPLES.md](EXAMPLES.md) for more configuration examples.

### 3. Create systemd service

```bash
sudo nano /etc/systemd/system/roonrelay.service
```

Contents:

```ini
[Unit]
Description=Roon Relay Service
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/roonrelay
ExecStart=/opt/roonrelay/RoonRelay /opt/roonrelay/appsettings.json
Restart=always
RestartSec=10
User=root

[Install]
WantedBy=multi-user.target
```

### 4. Enable and start the service

```bash
# Reload systemd
sudo systemctl daemon-reload

# Enable service to start on boot
sudo systemctl enable roonrelay

# Start the service
sudo systemctl start roonrelay

# Check status
sudo systemctl status roonrelay
```

## Management Commands

```bash
# View logs
journalctl -u roonrelay -f

# View logs from last hour
journalctl -u roonrelay --since "1 hour ago"

# Restart service
sudo systemctl restart roonrelay

# Stop service
sudo systemctl stop roonrelay

# Disable service
sudo systemctl disable roonrelay
```

## Building from source

```bash
# Clone repository
git clone https://github.com/simonefil/RoonRelay.git
cd roonrelay

# Build self-contained binary
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./publish

# Copy to installation directory
sudo cp -r ./publish/* /opt/roonrelay/
```

## Buy me a coffee!

[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/simonefil)

## FAQ

**- Will it work with uRPF (Reverse Path Forwarding)?**
No, because the relay injects packets with a preserved (spoofed) source IP, routers with Strict uRPF enabled (common on enterprise and UniFi gear) will drop this traffic. You may need to disable strict uRPF or switch to loose mode on relay/VPN interfaces.

**- Why there are no Windows build?**
Although this is a .NET project, modern Windows networking stacks prevent UDP source-IP spoofing via raw sockets. This is a Linux-only solution.

**- It does not work!**
Have you checked the examples?

**- How to deply on docker?**
I won't support Docker since I don't use it in my setup. If anyone is willing to test it and share instructions it would be great!

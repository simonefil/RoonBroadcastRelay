# RoonBroadcastRelay - Deployment Guide

## Platform Support

| Platform | Status | Notes |
|----------|--------|-------|
| Linux x64 | Tested | Primary development platform |
| Linux ARM64 | Tested | Raspberry Pi, etc. |
| macOS x64 | Untested | Intel Macs - binaries provided but not tested |
| macOS ARM64 | Untested | Apple Silicon - binaries provided but not tested |
| FreeBSD x64 | Untested | Binaries provided but not tested |
| Windows | Not supported | Kernel blocks raw socket UDP spoofing |

## Prerequisites

- Linux server (Debian, Ubuntu, or similar), macOS, or FreeBSD
- Root or sudo access
- Network connectivity to Roon server and clients

## Installation

### 1. Download the latest release

```bash
# Create installation directory
sudo mkdir -p /opt/roonrelay
cd /opt/roonrelay

# Download latest release (choose your platform)
# Linux x64:
wget https://github.com/simonefil/RoonBroadcastRelay/releases/latest/download/RoonBroadcastRelay-linux-x64
mv RoonBroadcastRelay-linux-x64 RoonBroadcastRelay

# Make executable
sudo chmod +x RoonBroadcastRelay
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
  "UnicastTargets": [],
  "Protocols": {
    "Raat": true,
    "AirPlay": false,
    "Ssdp": false,
    "Squeezebox": false
  }
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
git clone https://github.com/simonefil/RoonBroadcastRelay.git
cd RoonBroadcastRelay

# Build self-contained binary (choose your platform)
# Available targets: linux-x64, linux-arm64, osx-x64, osx-arm64, freebsd-x64
dotnet publish RoonBroadcastRelay/RoonBroadcastRelay.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o ./publish

# Copy to installation directory
sudo cp ./publish/RoonBroadcastRelay /opt/roonrelay/
```

## Buy me a coffee!

[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/simonefil)

## Protocol Configuration

RoonBroadcastRelay supports multiple discovery protocols. Enable only what you need:

| Protocol | Port | Multicast Address | Default | Description |
|----------|------|-------------------|---------|-------------|
| Raat | 9003 | 239.255.90.90 | true | Roon native discovery (RAAT) |
| AirPlay | 5353 | 224.0.0.251 | false | Apple AirPlay via mDNS |
| Ssdp | 1900 | 239.255.255.250 | false | Chromecast, Sonos, LINN (UPnP) |
| Squeezebox | 3483 | (broadcast only) | false | Logitech Media Server/SlimProto |

### Port Conflicts

Each protocol binds to its designated port at startup. If the port is already in use by another service, the protocol will be automatically disabled with a warning message.

Common conflicts:
- **Port 5353 (AirPlay/mDNS)**: avahi-daemon, mDNSResponder
- **Port 1900 (SSDP)**: miniupnpd, minidlna, Plex

### WireGuard AllowedIPs

Your WireGuard client configuration must include broadcast and multicast addresses:

```ini
AllowedIPs = ..., 255.255.255.255/32, 224.0.0.0/4
```

- `255.255.255.255/32` - Broadcast address
- `224.0.0.0/4` - All multicast addresses (Class D range)

### Firewall Rules

If you enable additional protocols beyond RAAT, duplicate your NAT port forward rules for the additional ports (5353, 1900, 3483).

## FAQ

**- Will it work with uRPF (Reverse Path Forwarding)?**
No, because the relay injects packets with a preserved (spoofed) source IP, routers with Strict uRPF enabled (common on enterprise and UniFi gear) will drop this traffic. You may need to disable strict uRPF or switch to loose mode on relay/VPN interfaces.

**- Why are there no Windows builds?**
Modern Windows networking stacks prevent UDP source-IP spoofing via raw sockets at kernel level. This limitation cannot be bypassed.

**- Does it work on macOS and FreeBSD?**
Binaries are provided for macOS (Intel and Apple Silicon) and FreeBSD x64, but they are currently untested. Feedback is welcome.

**- Which protocols should I enable?**
Enable only Raat unless you specifically need AirPlay, Chromecast/Sonos, or Squeezebox endpoints to be discovered across subnets. Each enabled protocol adds listener threads and network traffic.

**- It does not work!**
Have you checked the examples? Make sure your WireGuard AllowedIPs include `255.255.255.255/32` and `224.0.0.0/4`.

**- Why is Docker not supported?**
Docker is not supported nor recommended for this application because:
- Raw sockets require `network_mode: host`, which disables network isolation
- Additional capabilities (`NET_RAW`, `NET_ADMIN`) are needed for IP spoofing
- With these requirements, Docker provides no benefits over running the binary directly

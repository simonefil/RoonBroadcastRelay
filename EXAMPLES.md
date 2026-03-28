# Roon Relay - Configuration Examples

## Example 1: Single Site with 1 Relay + 1 Roadwarrior VPN

### Network Diagram

```
                                    INTERNET
                                        │
                                        │
                              ┌─────────┴─────────┐
                              │    OPNsense       │
                              │   172.16.0.1      │
                              │                   │
                              │  WG Roadwarrior   │
                              │  10.10.99.1/24    │
                              └─────────┬─────────┘
                                        │
                    ┌───────────────────┼───────────────────┐
                    │                   │                   │
                    │           LAN 172.16.0.0/24           │
                    │                   │                   │
           ┌────────┴────────┐ ┌────────┴────────┐ ┌────────┴────────┐
           │  Roon Server    │ │   VM Relay      │ │  Other clients  │
           │  172.16.0.106   │ │  172.16.0.108   │ │  172.16.0.x     │
           └─────────────────┘ └─────────────────┘ └─────────────────┘


                              ┌─────────────────┐
                              │ VPN RW Client   │
                              │ (smartphone)    │
                              │ 10.10.99.3      │
                              └─────────────────┘
```

### Components

| Component | IP | Role |
|-----------|-----|------|
| OPNsense | 172.16.0.1 / 10.10.99.1 | Firewall + WG server |
| Roon Server | 172.16.0.106 | Roon Server |
| VM Relay | 172.16.0.108 | Broadcast relay |
| RW Client | 10.10.99.3 | Smartphone via VPN |

### Firewall Rules

#### NAT Port Forward (Firewall → NAT → Port Forward)

| Interface | Protocol | Source | Destination | Dest Port | Redirect IP | Redirect Port | Description |
|-----------|----------|--------|-------------|-----------|-------------|---------------|-------------|
| WG_RW | UDP | 10.10.99.0/24 | 255.255.255.255 | 9003 | 172.16.0.108 | 9003 | Roon broadcast to relay |
| WG_RW | UDP | 10.10.99.0/24 | 239.255.90.90 | 9003 | 172.16.0.108 | 9003 | Roon multicast to relay |

### WireGuard Configuration

#### OPNsense - Instance (VPN → WireGuard → Instances)

- Name: `roadwarrior`
- Listen Port: `51820`
- Tunnel Address: `10.10.99.1/24`

#### OPNsense - Peer (VPN → WireGuard → Peers)

- Name: `smartphone`
- Public Key: `<CLIENT_PUBLIC_KEY>`
- Allowed IPs: `10.10.99.3/32`

#### Client Configuration

```ini
[Interface]
PrivateKey = <CLIENT_PRIVATE_KEY>
Address = 10.10.99.3/24
DNS = 172.16.0.1

[Peer]
PublicKey = <SERVER_PUBLIC_KEY>
Endpoint = <PUBLIC_IP>:51820
AllowedIPs = 10.10.99.0/24, 172.16.0.0/24, 255.255.255.255/32, 224.0.0.0/4
PersistentKeepalive = 25
```

### appsettings.json

```json
{
  "SiteName": "MainRelay",
  "TunnelPort": 9004,
  "RemoteRelayIp": "",
  "LocalInterfaces": [
    {
      "LocalIp": "172.16.0.108",
      "BroadcastAddress": "172.16.0.255",
      "SubnetMask": "255.255.255.0"
    }
  ],
  "UnicastTargets": [
    "10.10.99.3"
  ],
  "Protocols": {
    "Raat": true,
    "AirPlay": false,
    "Ssdp": false,
    "Squeezebox": false
  }
}
```

**Note:** Add all roadwarrior client IPs to `UnicastTargets` array.

---

## Example 2: Site-to-Site

### Network Diagram

```
        SITE A                                                    SITE B
        ══════                                                    ══════

      INTERNET                                                  INTERNET
          │                                                         │
          │                                                         │
┌─────────┴─────────┐                                   ┌───────────┴─────────┐
│    OPNsense A     │                                   │     OPNsense B      │
│   172.16.0.1      │                                   │    192.168.30.1     │
│                   │         WireGuard S2S             │                     │
│  WG S2S           │◄─────────────────────────────────►│  WG S2S             │
│  10.10.90.1/30    │         encrypted tunnel          │  10.10.90.2/30      │
│                   │                                   │                     │
│  WG RW            │                                   │  WG RW              │
│  10.10.99.1/24    │                                   │  10.10.98.1/24      │
└─────────┬─────────┘                                   └───────────┬─────────┘
          │                                                         │
          │ LAN 172.16.0.0/24                                       │ LAN 192.168.30.0/24
          │                                                         │
    ┌─────┴─────┐                                             ┌─────┴─────┐
    │           │                                             │           │
┌───┴───┐   ┌───┴───┐                                     ┌───┴───┐   ┌───┴───┐
│ Roon  │   │Relay A│                                     │Relay B│   │Client │
│Server │   │  VM   │◄ ─ ─ ─ ─ tunnel 9004 ─ ─ ─ ─ ─ ─ ─ ►│  VM   │   │  LAN  │
│.106   │   │.108   │                                     │.40    │   │.30    │
└───────┘   └───────┘                                     └───────┘   └───────┘


┌─────────────────┐                                       ┌─────────────────┐
│ VPN RW Client A │                                       │ VPN RW Client B │
│ 10.10.99.3      │                                       │ 10.10.98.3      │
└─────────────────┘                                       └─────────────────┘
```

### Components

| Component | IP | Site | Role |
|-----------|-----|------|------|
| OPNsense A | 172.16.0.1 / 10.10.90.1 / 10.10.99.1 | A | Firewall |
| Roon Server | 172.16.0.106 | A | Roon Server |
| VM Relay A | 172.16.0.108 | A | Relay |
| OPNsense B | 192.168.30.1 / 10.10.90.2 / 10.10.98.1 | B | Firewall |
| VM Relay B | 192.168.30.40 | B | Relay |
| LAN Client B | 192.168.30.30 | B | Roon endpoint |
| RW Client A | 10.10.99.3 | A | VPN client |
| RW Client B | 10.10.98.3 | B | VPN client |

### Firewall Rules

#### Site A - NAT Port Forward

| Interface | Protocol | Source | Destination | Dest Port | Redirect IP | Redirect Port | Description |
|-----------|----------|--------|-------------|-----------|-------------|---------------|-------------|
| WG_RW | UDP | 10.10.99.0/24 | 255.255.255.255 | 9003 | 172.16.0.108 | 9003 | Roon broadcast to relay |

#### Site A - WAN Rules

| # | Proto | Source | Destination | Port | Description |
|---|-------|--------|-------------|------|-------------|
| 1 | UDP | * | This firewall | 51820 | WireGuard S2S |

#### Site A - WG_S2S Rules

| # | Proto | Source | Destination | Port | Description |
|---|-------|--------|-------------|------|-------------|
| 1 | UDP | 192.168.30.40 | 172.16.0.108 | 9004 | Relay tunnel |
| 2 | TCP/UDP | 192.168.30.0/24 | 172.16.0.106 | * | Roon from LAN B |
| 3 | TCP/UDP | 10.10.98.0/24 | 172.16.0.106 | * | Roon from RW B |
| 4 | * | * | * | * | BLOCK all |

#### Site B - NAT Port Forward

| Interface | Protocol | Source | Destination | Dest Port | Redirect IP | Redirect Port | Description |
|-----------|----------|--------|-------------|-----------|-------------|---------------|-------------|
| WG_RW | UDP | 10.10.98.0/24 | 255.255.255.255 | 9003 | 192.168.30.40 | 9003 | Roon broadcast to relay |

#### Site B - WAN Rules

| # | Proto | Source | Destination | Port | Description |
|---|-------|--------|-------------|------|-------------|
| 1 | UDP | * | This firewall | 51820 | WireGuard S2S |

#### Site B - WG_S2S Rules

| # | Proto | Source | Destination | Port | Description |
|---|-------|--------|-------------|------|-------------|
| 1 | UDP | 172.16.0.108 | 192.168.30.40 | 9004 | Relay tunnel |
| 2 | TCP/UDP | 172.16.0.106 | 192.168.30.0/24 | * | Roon to LAN B |
| 3 | TCP/UDP | 172.16.0.106 | 10.10.98.0/24 | * | Roon to RW B |
| 4 | * | * | * | * | BLOCK all |

### WireGuard Configuration

#### Site A - S2S Instance (VPN → WireGuard → Instances)

- Name: `s2s_siteb`
- Listen Port: `51820`
- Tunnel Address: `10.10.90.1/30`

#### Site A - S2S Peer (VPN → WireGuard → Peers)

- Name: `SiteB`
- Public Key: `<SITE_B_PUBLIC_KEY>`
- Allowed IPs: `10.10.90.2/32, 192.168.30.0/24, 10.10.98.0/24`
- Endpoint Address: `<SITE_B_PUBLIC_IP>`
- Endpoint Port: `51820`
- Keepalive: `25`

#### Site B - S2S Instance (VPN → WireGuard → Instances)

- Name: `s2s_sitea`
- Listen Port: `51820`
- Tunnel Address: `10.10.90.2/30`

#### Site B - S2S Peer (VPN → WireGuard → Peers)

- Name: `SiteA`
- Public Key: `<SITE_A_PUBLIC_KEY>`
- Allowed IPs: `10.10.90.1/32, 172.16.0.0/24, 10.10.99.0/24`
- Endpoint Address: `<SITE_A_PUBLIC_IP>`
- Endpoint Port: `51820`
- Keepalive: `25`

#### RW Client A Configuration

```ini
[Interface]
PrivateKey = <CLIENT_PRIVATE_KEY>
Address = 10.10.99.3/24
DNS = 172.16.0.1

[Peer]
PublicKey = <SITE_A_RW_PUBLIC_KEY>
Endpoint = <SITE_A_PUBLIC_IP>:51821
AllowedIPs = 10.10.99.0/24, 172.16.0.0/24, 255.255.255.255/32, 224.0.0.0/4
PersistentKeepalive = 25
```

#### RW Client B Configuration

```ini
[Interface]
PrivateKey = <CLIENT_PRIVATE_KEY>
Address = 10.10.98.3/24
DNS = 192.168.30.1

[Peer]
PublicKey = <SITE_B_RW_PUBLIC_KEY>
Endpoint = <SITE_B_PUBLIC_IP>:51821
AllowedIPs = 10.10.98.0/24, 192.168.30.0/24, 172.16.0.0/24, 255.255.255.255/32, 224.0.0.0/4
PersistentKeepalive = 25
```

### appsettings.json - Relay A (Site A)

```json
{
  "SiteName": "RelayA",
  "TunnelPort": 9004,
  "RemoteRelayIp": "192.168.30.40",
  "LocalInterfaces": [
    {
      "LocalIp": "172.16.0.108",
      "BroadcastAddress": "172.16.0.255",
      "SubnetMask": "255.255.255.0"
    }
  ],
  "UnicastTargets": [
    "10.10.99.3"
  ],
  "Protocols": {
    "Raat": true,
    "AirPlay": false,
    "Ssdp": false,
    "Squeezebox": false
  }
}
```

### appsettings.json - Relay B (Site B)

```json
{
  "SiteName": "RelayB",
  "TunnelPort": 9004,
  "RemoteRelayIp": "172.16.0.108",
  "LocalInterfaces": [
    {
      "LocalIp": "192.168.30.40",
      "BroadcastAddress": "192.168.30.255",
      "SubnetMask": "255.255.255.0"
    }
  ],
  "UnicastTargets": [
    "10.10.98.3"
  ],
  "Protocols": {
    "Raat": true,
    "AirPlay": false,
    "Ssdp": false,
    "Squeezebox": false
  }
}
```

**Note:** Each relay should have its local roadwarrior clients in `UnicastTargets`. Both relays must have identical `Protocols` settings.

---

## Example 3: Complex Multi-VLAN Configuration

### Network Diagram

```
                    SITE A                                                          SITE B
                    ══════                                                          ══════

                  INTERNET                                                        INTERNET
                      │                                                               │
                      │                                                               │
          ┌───────────┴───────────┐                                     ┌─────────────┴───────────┐
          │      OPNsense A       │                                     │       OPNsense B        │
          │                       │                                     │                         │
          │  LAN:    172.16.0.1   │         WireGuard S2S               │  LAN:    192.168.30.1   │
          │  VLAN100:192.168.100.1│◄───────────────────────────────────►│  VLAN100:192.168.99.1   │
          │  WG_S2S: 10.10.90.1   │         10.10.90.0/30               │  WG_S2S: 10.10.90.2     │
          │  WG_RW:  10.10.99.1   │                                     │  WG_RW:  10.10.98.1     │
          └───────────┬───────────┘                                     └─────────────┬───────────┘
                      │                                                               │
      ┌───────────────┼───────────────┐                           ┌───────────────────┼───────────────┐
      │               │               │                           │                   │               │
      │ LAN           │ VLAN 100      │                           │ LAN               │ VLAN 100      │
      │ 172.16.0.0/24 │192.168.100/24 │                           │ 192.168.30.0/24   │192.168.99/24  │
      │               │               │                           │                   │               │
┌─────┴─────┐   ┌─────┴─────┐   ┌─────┴─────┐               ┌─────┴─────┐       ┌─────┴─────┐   ┌─────┴─────┐
│   Roon    │   │ Relay A   │   │  Client   │               │ Relay B   │       │  Client   │   │  Client   │
│  Server   │   │    VM     │   │ VLAN100   │               │    VM     │       │   LAN     │   │ VLAN100   │
│           │   │           │   │           │               │           │       │           │   │           │
│172.16.0   │   │172.16.0   │   │192.168    │               │192.168.30 │       │192.168.30 │   │192.168.99 │
│  .106     │   │  .108     │   │  .100.5   │               │   .40     │       │   .30     │   │   .50     │
│           │   │192.168    │   │ (EndP2)   │               │192.168.99 │       │ (EndP3)   │   │           │
│           │   │ .100.100  │   │           │               │  .100     │       │           │   │           │
└───────────┘   └───────────┘   └───────────┘               └───────────┘       └───────────┘   └───────────┘


        ┌─────────────────┐                                         ┌─────────────────┐
        │ VPN RW Client A │                                         │ VPN RW Client B │
        │    (EndP1)      │                                         │    (EndP5)      │
        │   10.10.99.5    │                                         │   10.10.98.5    │
        └─────────────────┘                                         └─────────────────┘


        ┌─────────────────┐
        │ LAN Client A    │
        │    (EndP4)      │
        │  172.16.0.16    │
        └─────────────────┘
```

### Components

| Component | IP | Site | Subnet | Role |
|-----------|-----|------|--------|------|
| OPNsense A | 172.16.0.1 | A | LAN | Firewall |
| OPNsense A | 192.168.100.1 | A | VLAN100 | Firewall |
| OPNsense A | 10.10.90.1 | A | WG_S2S | Tunnel endpoint |
| OPNsense A | 10.10.99.1 | A | WG_RW | VPN server |
| Roon Server | 172.16.0.106 | A | LAN | Roon Server |
| VM Relay A | 172.16.0.108, 192.168.100.100 | A | LAN + VLAN100 | Multi-homed relay |
| OPNsense B | 192.168.30.1 | B | LAN | Firewall |
| OPNsense B | 192.168.99.1 | B | VLAN100 | Firewall |
| OPNsense B | 10.10.90.2 | B | WG_S2S | Tunnel endpoint |
| OPNsense B | 10.10.98.1 | B | WG_RW | VPN server |
| VM Relay B | 192.168.30.40, 192.168.99.100 | B | LAN + VLAN100 | Multi-homed relay |
| EndP1 | 10.10.99.5 | A | WG_RW | VPN client |
| EndP2 | 192.168.100.5 | A | VLAN100 | LAN client |
| EndP3 | 192.168.30.30 | B | LAN | LAN client |
| EndP4 | 172.16.0.16 | A | LAN | LAN client |
| EndP5 | 10.10.98.5 | B | WG_RW | VPN client |

### Endpoints Summary

| Endpoint | IP | Location | Works | Notes |
|----------|-----|----------|-------|-------|
| EndP1 | 10.10.99.5 | WG_RW Site A | ✓ | NAT 255.255.255.255 → Relay A |
| EndP2 | 192.168.100.5 | VLAN100 Site A | ✓ | Relay A has interface on VLAN100 |
| EndP3 | 192.168.30.30 | LAN Site B | ✓ | Via S2S tunnel + Relay B |
| EndP4 | 172.16.0.16 | LAN Site A | ✓ | Same broadcast domain as server |
| EndP5 | 10.10.98.5 | WG_RW Site B | ✓ | NAT → Relay B → tunnel → Relay A |

### Firewall Rules

#### Site A - NAT Port Forward

| Interface | Protocol | Source | Destination | Dest Port | Redirect IP | Redirect Port | Description |
|-----------|----------|--------|-------------|-----------|-------------|---------------|-------------|
| WG_RW | UDP | 10.10.99.0/24 | 255.255.255.255 | 9003 | 172.16.0.108 | 9003 | Roon broadcast to relay |

#### Site A - WAN Rules

| # | Proto | Source | Destination | Port | Description |
|---|-------|--------|-------------|------|-------------|
| 1 | UDP | * | This firewall | 51820 | WireGuard S2S |

#### Site A - WG_S2S Rules

| # | Proto | Source | Destination | Port | Description |
|---|-------|--------|-------------|------|-------------|
| 1 | UDP | 192.168.30.40 | 172.16.0.108 | 9004 | Relay tunnel |
| 2 | TCP/UDP | 192.168.30.0/24 | 172.16.0.106 | * | Roon from LAN B |
| 3 | TCP/UDP | 192.168.99.0/24 | 172.16.0.106 | * | Roon from VLAN100 B |
| 4 | TCP/UDP | 10.10.98.0/24 | 172.16.0.106 | * | Roon from RW B |
| 5 | * | * | * | * | BLOCK all |

#### Site B - NAT Port Forward

| Interface | Protocol | Source | Destination | Dest Port | Redirect IP | Redirect Port | Description |
|-----------|----------|--------|-------------|-----------|-------------|---------------|-------------|
| WG_RW | UDP | 10.10.98.0/24 | 255.255.255.255 | 9003 | 192.168.30.40 | 9003 | Roon broadcast to relay |

#### Site B - WAN Rules

| # | Proto | Source | Destination | Port | Description |
|---|-------|--------|-------------|------|-------------|
| 1 | UDP | * | This firewall | 51820 | WireGuard S2S |

#### Site B - WG_S2S Rules

| # | Proto | Source | Destination | Port | Description |
|---|-------|--------|-------------|------|-------------|
| 1 | UDP | 172.16.0.108 | 192.168.30.40 | 9004 | Relay tunnel |
| 2 | TCP/UDP | 172.16.0.106 | 192.168.30.0/24 | * | Roon to LAN B |
| 3 | TCP/UDP | 172.16.0.106 | 192.168.99.0/24 | * | Roon to VLAN100 B |
| 4 | TCP/UDP | 172.16.0.106 | 10.10.98.0/24 | * | Roon to RW B |
| 5 | * | * | * | * | BLOCK all |

### WireGuard Configuration

#### Site A - S2S Instance (VPN → WireGuard → Instances)

- Name: `s2s_siteb`
- Listen Port: `51820`
- Tunnel Address: `10.10.90.1/30`

#### Site A - S2S Peer (VPN → WireGuard → Peers)

- Name: `SiteB`
- Public Key: `<SITE_B_PUBLIC_KEY>`
- Allowed IPs: `10.10.90.2/32, 192.168.30.0/24, 192.168.99.0/24, 10.10.98.0/24`
- Endpoint Address: `<SITE_B_PUBLIC_IP>`
- Endpoint Port: `51820`
- Keepalive: `25`

#### Site A - RW Instance (VPN → WireGuard → Instances)

- Name: `roadwarrior`
- Listen Port: `51821`
- Tunnel Address: `10.10.99.1/24`

#### Site B - S2S Instance (VPN → WireGuard → Instances)

- Name: `s2s_sitea`
- Listen Port: `51820`
- Tunnel Address: `10.10.90.2/30`

#### Site B - S2S Peer (VPN → WireGuard → Peers)

- Name: `SiteA`
- Public Key: `<SITE_A_PUBLIC_KEY>`
- Allowed IPs: `10.10.90.1/32, 172.16.0.0/24, 192.168.100.0/24, 10.10.99.0/24`
- Endpoint Address: `<SITE_A_PUBLIC_IP>`
- Endpoint Port: `51820`
- Keepalive: `25`

#### Site B - RW Instance (VPN → WireGuard → Instances)

- Name: `roadwarrior`
- Listen Port: `51821`
- Tunnel Address: `10.10.98.1/24`

#### RW Client A (EndP1) Configuration

```ini
[Interface]
PrivateKey = <CLIENT_PRIVATE_KEY>
Address = 10.10.99.5/24
DNS = 172.16.0.1

[Peer]
PublicKey = <SITE_A_RW_PUBLIC_KEY>
Endpoint = <SITE_A_PUBLIC_IP>:51821
AllowedIPs = 10.10.99.0/24, 172.16.0.0/24, 192.168.100.0/24, 255.255.255.255/32, 224.0.0.0/4
PersistentKeepalive = 25
```

#### RW Client B (EndP5) Configuration

```ini
[Interface]
PrivateKey = <CLIENT_PRIVATE_KEY>
Address = 10.10.98.5/24
DNS = 192.168.30.1

[Peer]
PublicKey = <SITE_B_RW_PUBLIC_KEY>
Endpoint = <SITE_B_PUBLIC_IP>:51821
AllowedIPs = 10.10.98.0/24, 192.168.30.0/24, 192.168.99.0/24, 172.16.0.0/24, 255.255.255.255/32, 224.0.0.0/4
PersistentKeepalive = 25
```

### appsettings.json - Relay A (Site A)

```json
{
  "SiteName": "RelayA",
  "TunnelPort": 9004,
  "RemoteRelayIp": "192.168.30.40",
  "LocalInterfaces": [
    {
      "LocalIp": "172.16.0.108",
      "BroadcastAddress": "172.16.0.255",
      "SubnetMask": "255.255.255.0"
    },
    {
      "LocalIp": "192.168.100.100",
      "BroadcastAddress": "192.168.100.255",
      "SubnetMask": "255.255.255.0"
    }
  ],
  "UnicastTargets": [
    "10.10.99.5"
  ],
  "Protocols": {
    "Raat": true,
    "AirPlay": false,
    "Ssdp": false,
    "Squeezebox": false
  }
}
```

### appsettings.json - Relay B (Site B)

```json
{
  "SiteName": "RelayB",
  "TunnelPort": 9004,
  "RemoteRelayIp": "172.16.0.108",
  "LocalInterfaces": [
    {
      "LocalIp": "192.168.30.40",
      "BroadcastAddress": "192.168.30.255",
      "SubnetMask": "255.255.255.0"
    },
    {
      "LocalIp": "192.168.99.100",
      "BroadcastAddress": "192.168.99.255",
      "SubnetMask": "255.255.255.0"
    }
  ],
  "UnicastTargets": [
    "10.10.98.5"
  ],
  "Protocols": {
    "Raat": true,
    "AirPlay": false,
    "Ssdp": false,
    "Squeezebox": false
  }
}
```

**Note:**
- Each relay lists all its local network interfaces in `LocalInterfaces`
- Each relay lists its local roadwarrior clients in `UnicastTargets`
- VLANs require the relay VM to have an IP on each VLAN
- Both relays must have identical `Protocols` settings

---

## Roon Ports Summary

| Port | Protocol | Purpose |
|------|----------|---------|
| 9003 | UDP | RAAT discovery broadcast/multicast |
| 9004 | UDP | Tunnel between relays |
| 9100-9200 | TCP | Control and audio streaming |
| 9330-9332 | TCP | HTTP/HTTPS API |

## Additional Protocol Ports

If you enable additional protocols in the `Protocols` configuration, these ports are also used:

| Port | Protocol | Purpose |
|------|----------|---------|
| 5353 | UDP | mDNS (AirPlay discovery) |
| 1900 | UDP | SSDP (Chromecast, Sonos, LINN) |
| 3483 | UDP | SlimProto (Squeezebox discovery) |

**Note:** If you enable additional protocols, you must also update your firewall NAT port forward rules to redirect these ports to the relay.

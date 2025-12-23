using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RoonRelay
{
    /// <summary>
    /// Main relay service for forwarding Roon Audio protocol packets between networks.
    /// Handles multicast/broadcast traffic and tunnel connections to remote relays.
    /// </summary>
    public class RoonRelay
    {
        // Roon Audio protocol uses UDP port 9003
        const int ROON_PORT = 9003;

        // Roon multicast group address
        static readonly IPAddress MULTICAST_GROUP = IPAddress.Parse("239.255.90.90");

        readonly RelayConfig _config;
        readonly List<LanInterface> _lanInterfaces = new List<LanInterface>();
        readonly List<IPAddress> _unicastTargets = new List<IPAddress>();
        readonly HashSet<IPAddress> _localIps = new HashSet<IPAddress>();

        Socket _lanSocket;
        Socket _tunnelSocket;
        IPEndPoint _remoteRelayEp;
        Socket _rawSocket;
        Dictionary<int, DateTime> _recentPorts = new Dictionary<int, DateTime>();

        /// <summary>
        /// Initializes a new instance of the RoonRelay with the specified configuration.
        /// </summary>
        /// <param name="config">Relay configuration settings.</param>
        public RoonRelay(RelayConfig config)
        {
            this._config = config;

            // Initialize local interfaces and track their IPs
            // This prevents packet loops by filtering out our own traffic
            foreach (InterfaceConfig iface in config.LocalInterfaces)
            {
                // Parse IP address from configuration
                IPAddress ip = IPAddress.Parse(iface.LocalIp);
                this._localIps.Add(ip);

                // Create LAN interface object with IP, broadcast, and subnet mask
                LanInterface lanIface = new LanInterface(
                    ip,
                    IPAddress.Parse(iface.BroadcastAddress),
                    IPAddress.Parse(iface.SubnetMask)
                );
                this._lanInterfaces.Add(lanIface);
            }

            // Parse and store unicast target addresses if configured
            // These are specific devices that will receive forwarded packets
            if (config.UnicastTargets != null)
            {
                foreach (string target in config.UnicastTargets)
                {
                    this._unicastTargets.Add(IPAddress.Parse(target));
                }
            }

            // Setup remote relay endpoint for tunnel communication if configured
            if (!string.IsNullOrEmpty(config.RemoteRelayIp))
                this._remoteRelayEp = new IPEndPoint(IPAddress.Parse(config.RemoteRelayIp), config.TunnelPort);
        }

        /// <summary>
        /// Starts the relay service and begins listening on all configured interfaces.
        /// Blocks until Ctrl+C is pressed.
        /// </summary>
        public void Start()
        {
            Console.WriteLine($"[{this._config.SiteName}] Starting Roon Relay");

            // Create and bind sockets
            // LAN socket for regular UDP traffic on port 9003
            this._lanSocket = this.CreateLanSocket();
            // Raw socket for IP spoofing (requires admin privileges)
            this._rawSocket = this.CreateRawSocket();

            // Log configured LAN interfaces
            foreach (LanInterface iface in this._lanInterfaces)
            {
                Console.WriteLine($"[{this._config.SiteName}] LAN interface: {iface.LocalIp} (broadcast: {iface.BroadcastAddress}, mask: {iface.SubnetMask})");
            }

            // Create tunnel socket if remote relay is configured
            // This allows communication with another relay instance
            if (this._remoteRelayEp != null)
            {
                this._tunnelSocket = this.CreateTunnelSocket();
                Console.WriteLine($"[{this._config.SiteName}] Remote relay: {this._config.RemoteRelayIp}:{this._config.TunnelPort}");
            }

            // Log configured unicast targets
            foreach (IPAddress target in this._unicastTargets)
            {
                Console.WriteLine($"[{this._config.SiteName}] Unicast target: {target}");
            }

            // Start LAN listener thread
            // This will continuously listen for packets on the LAN socket
            Thread lanThread = new Thread(this.ListenLan) { IsBackground = true };
            lanThread.Start();

            // Start tunnel listener thread if tunnel is configured
            if (this._tunnelSocket != null)
            {
                Thread tunnelThread = new Thread(this.ListenTunnel) { IsBackground = true };
                tunnelThread.Start();
            }

            Console.WriteLine($"[{this._config.SiteName}] Relay running. Press Ctrl+C to stop.");

            // Wait for Ctrl+C signal to shutdown gracefully
            ManualResetEvent exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; exitEvent.Set(); };
            exitEvent.WaitOne();

            Console.WriteLine($"[{this._config.SiteName}] Shutting down...");
        }

        /// <summary>
        /// Creates a raw socket for sending packets with custom IP headers (IP spoofing).
        /// </summary>
        /// <returns>Configured raw socket.</returns>
        private Socket CreateRawSocket()
        {
            // Create raw socket for sending custom IP packets
            // This requires administrator/root privileges
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Udp);

            // Enable manual IP header construction
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
            // Allow broadcasting to subnet addresses
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

            Console.WriteLine($"[{this._config.SiteName}] Raw socket created for IP spoofing");
            return socket;
        }

        /// <summary>
        /// Sends a UDP packet with spoofed source IP and port using raw socket.
        /// </summary>
        /// <param name="srcIp">Source IP address to spoof.</param>
        /// <param name="srcPort">Source port to spoof.</param>
        /// <param name="dstIp">Destination IP address.</param>
        /// <param name="dstPort">Destination port.</param>
        /// <param name="payload">Packet payload.</param>
        private void SendRawPacket(IPAddress srcIp, int srcPort, IPAddress dstIp, int dstPort, byte[] payload)
        {
            // Build complete IP/UDP packet with custom source address
            byte[] packet = this.BuildIpUdpPacket(srcIp, srcPort, dstIp, dstPort, payload);
            // Send raw packet (destination IP is in the packet header, port 0 is ignored for raw sockets)
            this._rawSocket.SendTo(packet, new IPEndPoint(dstIp, 0));
        }

        /// <summary>
        /// Builds a complete IP/UDP packet with custom headers.
        /// </summary>
        /// <param name="srcIp">Source IP address.</param>
        /// <param name="srcPort">Source port.</param>
        /// <param name="dstIp">Destination IP address.</param>
        /// <param name="dstPort">Destination port.</param>
        /// <param name="payload">UDP payload.</param>
        /// <returns>Complete IP/UDP packet as byte array.</returns>
        private byte[] BuildIpUdpPacket(IPAddress srcIp, int srcPort, IPAddress dstIp, int dstPort, byte[] payload)
        {
            // Calculate packet sizes
            int udpLength = 8 + payload.Length;  // UDP header (8) + data
            int ipLength = 20 + udpLength;       // IP header (20) + UDP packet

            byte[] packet = new byte[ipLength];

            // Build IP Header (20 bytes, RFC 791)
            packet[0] = 0x45; // Version (IPv4 = 4) + Header Length (5 * 4 bytes = 20)
            packet[1] = 0x00; // DSCP + ECN (no special handling)
            // Total packet length in bytes
            packet[2] = (byte)(ipLength >> 8);
            packet[3] = (byte)(ipLength & 0xFF);
            packet[4] = 0x00; // Identification (not used for fragmentation)
            packet[5] = 0x00;
            packet[6] = 0x40; // Flags: Don't Fragment bit set
            packet[7] = 0x00; // Fragment Offset = 0
            packet[8] = 0x40; // TTL = 64 hops
            packet[9] = 0x11; // Protocol = UDP (17)
            packet[10] = 0x00; // Header checksum (calculated below)
            packet[11] = 0x00;

            // Copy source and destination IP addresses into header
            byte[] srcBytes = srcIp.GetAddressBytes();
            byte[] dstBytes = dstIp.GetAddressBytes();
            Array.Copy(srcBytes, 0, packet, 12, 4);  // Source IP at offset 12
            Array.Copy(dstBytes, 0, packet, 16, 4);  // Destination IP at offset 16

            // Calculate and set IP header checksum
            ushort ipChecksum = this.CalculateChecksum(packet, 0, 20);
            packet[10] = (byte)(ipChecksum >> 8);
            packet[11] = (byte)(ipChecksum & 0xFF);

            // Build UDP Header (8 bytes, RFC 768)
            packet[20] = (byte)(srcPort >> 8);     // Source port (high byte)
            packet[21] = (byte)(srcPort & 0xFF);   // Source port (low byte)
            packet[22] = (byte)(dstPort >> 8);     // Destination port (high byte)
            packet[23] = (byte)(dstPort & 0xFF);   // Destination port (low byte)
            packet[24] = (byte)(udpLength >> 8);   // UDP length (high byte)
            packet[25] = (byte)(udpLength & 0xFF); // UDP length (low byte)
            packet[26] = 0x00; // UDP checksum = 0 (optional for IPv4)
            packet[27] = 0x00;

            // Copy payload data after headers
            Array.Copy(payload, 0, packet, 28, payload.Length);

            return packet;
        }

        /// <summary>
        /// Calculates IP header checksum using the standard algorithm.
        /// </summary>
        /// <param name="data">Data buffer containing the header.</param>
        /// <param name="offset">Offset in the buffer to start calculating.</param>
        /// <param name="length">Length of data to checksum.</param>
        /// <returns>Calculated checksum.</returns>
        private ushort CalculateChecksum(byte[] data, int offset, int length)
        {
            uint sum = 0;

            // Sum all 16-bit words in the header
            for (int i = 0; i < length; i += 2)
            {
                ushort word;
                // Combine two bytes into a 16-bit word (big-endian)
                if (i + 1 < length)
                    word = (ushort)(data[offset + i] << 8 | data[offset + i + 1]);
                else
                    word = (ushort)(data[offset + i] << 8);  // Last odd byte
                sum += word;
            }

            // Add carry bits until sum fits in 16 bits
            while (sum >> 16 != 0)
                sum = (sum & 0xFFFF) + (sum >> 16);

            // Return one's complement
            return (ushort)~sum;
        }

        /// <summary>
        /// Creates and configures a UDP socket for a LAN interface.
        /// Enables multicast membership and broadcast capability.
        /// </summary>
        /// <returns>Configured UDP socket bound to the interface.</returns>
        private Socket CreateLanSocket()
        {
            // Create UDP socket for LAN communication
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Allow multiple processes to bind to the same port
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            // Enable broadcast capability for subnet-wide communication
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

            // Bind to all interfaces on Roon port 9003
            socket.Bind(new IPEndPoint(IPAddress.Any, ROON_PORT));

            // Join Roon multicast group on each configured interface
            // This allows receiving multicast packets on all subnets
            foreach (LanInterface iface in this._lanInterfaces)
            {
                MulticastOption mcastOption = new MulticastOption(MULTICAST_GROUP, iface.LocalIp);
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, mcastOption);
            }

            Console.WriteLine($"[{this._config.SiteName}] LAN socket bound to 0.0.0.0:{ROON_PORT}");
            return socket;
        }

        /// <summary>
        /// Creates and configures a UDP socket for tunnel communication with remote relay.
        /// </summary>
        /// <returns>Configured UDP socket for tunnel traffic.</returns>
        private Socket CreateTunnelSocket()
        {
            // Create UDP socket for tunnel communication with remote relay
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Allow multiple processes to bind to the same port
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // Bind to all interfaces on configured tunnel port
            socket.Bind(new IPEndPoint(IPAddress.Any, this._config.TunnelPort));
            Console.WriteLine($"[{this._config.SiteName}] Tunnel socket bound to 0.0.0.0:{this._config.TunnelPort}");

            return socket;
        }

        /// <summary>
        /// Finds the LAN interface that matches the sender's subnet.
        /// </summary>
        /// <param name="sender">IP address of the sender.</param>
        /// <returns>Matching LAN interface, or null if not found.</returns>
        private LanInterface FindInterfaceForSender(IPAddress sender)
        {
            // Search through all configured LAN interfaces
            foreach (LanInterface iface in this._lanInterfaces)
            {
                // Check if sender's IP is in this interface's subnet
                if (iface.IsInSubnet(sender))
                    return iface;
            }
            // Sender is not in any of our subnets
            return null;
        }

        /// <summary>
        /// Listens for packets on LAN socket and forwards them to remote relay, unicast targets, and other interfaces.
        /// Uses IP spoofing for packets from unicast targets to preserve original source.
        /// </summary>
        private void ListenLan()
        {
            // Allocate receive buffer for incoming packets
            byte[] buffer = new byte[4096];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            // Main receive loop - runs continuously
            while (true)
            {
                try
                {
                    // Receive packet from LAN socket and get sender information
                    int received = this._lanSocket.ReceiveFrom(buffer, ref remoteEp);
                    IPEndPoint senderEp = (IPEndPoint)remoteEp;

                    // Ignore packets from our own interfaces to prevent loops
                    if (this._localIps.Contains(senderEp.Address))
                        continue;

                    // Determine if packet is from a configured unicast target
                    bool fromUnicastTarget = this._unicastTargets.Contains(senderEp.Address);
                    // Find which local interface the sender belongs to (by subnet)
                    LanInterface sourceIface = this.FindInterfaceForSender(senderEp.Address);

                    // Drop packet if sender is not in any of our subnets and not a unicast target
                    if (sourceIface == null && !fromUnicastTarget)
                        continue;

                    Console.WriteLine($"[{this._config.SiteName}] LAN <- {senderEp.Address}:{senderEp.Port} ({received} bytes){(fromUnicastTarget ? " [unicast target]" : "")}");

                    // Copy received data to new buffer for forwarding
                    byte[] packet = new byte[received];
                    Array.Copy(buffer, packet, received);

                    // Forward packet to remote relay via tunnel if configured
                    // This sends the packet to the other site with original source info preserved
                    if (this._tunnelSocket != null && this._remoteRelayEp != null)
                        this.SendToTunnel(packet, senderEp.Address, senderEp.Port);

                    // Forward to all configured unicast targets except the sender
                    foreach (IPAddress target in this._unicastTargets)
                    {
                        // Skip sending back to sender
                        if (target.Equals(senderEp.Address))
                            continue;

                        IPEndPoint targetEp = new IPEndPoint(target, ROON_PORT);
                        this._lanSocket.SendTo(packet, targetEp);
                        Console.WriteLine($"[{this._config.SiteName}] UNICAST -> {target}:{ROON_PORT}");
                    }

                    // Bridge packet to other local interfaces (interface-to-interface forwarding)
                    foreach (LanInterface otherIface in this._lanInterfaces)
                    {
                        // Don't send back to the source interface
                        if (sourceIface != null && otherIface.LocalIp.Equals(sourceIface.LocalIp))
                            continue;

                        // Special handling for packets from unicast targets
                        // Use IP spoofing to preserve original source address
                        if (fromUnicastTarget)
                        {
                            DateTime now = DateTime.Now;

                            // Clean up old ports (> 100ms)
                            // This prevents duplicate forwarding of the same packet
                            List<int> toRemove = new List<int>();
                            foreach (KeyValuePair<int, DateTime> kv in this._recentPorts)
                            {
                                if ((now - kv.Value).TotalMilliseconds > 100)
                                    toRemove.Add(kv.Key);
                            }
                            foreach (int port in toRemove)
                                this._recentPorts.Remove(port);

                            // Skip if we already forwarded a packet from this port recently
                            // This prevents packet duplication in multi-interface scenarios
                            if (this._recentPorts.ContainsKey(senderEp.Port))
                                continue;

                            // Mark this port as recently seen
                            this._recentPorts[senderEp.Port] = now;

                            // Send with spoofed source IP to preserve original sender
                            // This allows devices on other subnets to see the real source
                            this.SendRawPacket(senderEp.Address, senderEp.Port, otherIface.BroadcastAddress, ROON_PORT, packet);
                            Console.WriteLine($"[{this._config.SiteName}] RAW (broadcast) {senderEp.Address}:{senderEp.Port} -> {otherIface.BroadcastAddress}:{ROON_PORT}");
                            this.SendRawPacket(senderEp.Address, senderEp.Port, MULTICAST_GROUP, ROON_PORT, packet);
                            Console.WriteLine($"[{this._config.SiteName}] RAW (multicast) {senderEp.Address}:{senderEp.Port} -> {MULTICAST_GROUP}:{ROON_PORT}");
                        }
                        else
                        {
                            // Normal forwarding without IP spoofing for local subnet traffic
                            // Send via broadcast to reach all devices on the subnet
                            IPEndPoint broadcastEp = new IPEndPoint(otherIface.BroadcastAddress, ROON_PORT);
                            this._lanSocket.SendTo(packet, broadcastEp);
                            Console.WriteLine($"[{this._config.SiteName}] LAN (broadcast) -> {otherIface.BroadcastAddress}:{ROON_PORT}");

                            // Also send via multicast for devices listening on multicast group
                            IPEndPoint multicastEp = new IPEndPoint(MULTICAST_GROUP, ROON_PORT);
                            this._lanSocket.SendTo(packet, multicastEp);
                            Console.WriteLine($"[{this._config.SiteName}] LAN (multicast) -> {MULTICAST_GROUP}:{ROON_PORT}");
                        }
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[{this._config.SiteName}] LAN error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Listens for packets on the tunnel socket from remote relay and forwards them to:
        /// - All local interfaces (broadcast/multicast)
        /// - Unicast targets
        /// </summary>
        private void ListenTunnel()
        {
            // Allocate receive buffer for tunnel packets
            byte[] buffer = new byte[4096];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            // Main receive loop for tunnel traffic
            while (true)
            {
                try
                {
                    // Receive packet from tunnel socket (from remote relay)
                    int received = this._tunnelSocket.ReceiveFrom(buffer, ref remoteEp);
                    IPEndPoint senderEp = (IPEndPoint)remoteEp;

                    // Validate minimum packet size (6 bytes header + at least 1 byte payload)
                    if (received < 7)
                        continue;

                    // Extract custom header containing original source information
                    // Header format: [4 bytes IP][2 bytes port][payload]
                    // This allows us to preserve the original sender's IP and port across the tunnel
                    IPAddress originalIp = new IPAddress(new byte[] { buffer[0], buffer[1], buffer[2], buffer[3] });
                    int originalPort = buffer[4] << 8 | buffer[5];
                    byte[] packet = new byte[received - 6];
                    Array.Copy(buffer, 6, packet, 0, received - 6);

                    Console.WriteLine($"[{this._config.SiteName}] TUNNEL <- {senderEp.Address}:{senderEp.Port} ({packet.Length} bytes, src: {originalIp}:{originalPort})");

                    // Forward to all local interfaces using IP spoofing
                    // This makes the packet appear to come from the original sender
                    foreach (LanInterface iface in this._lanInterfaces)
                    {
                        DateTime now = DateTime.Now;

                        // Clean up old ports (> 100ms)
                        // This prevents duplicate packet forwarding
                        List<int> toRemove = new List<int>();
                        foreach (KeyValuePair<int, DateTime> kv in this._recentPorts)
                        {
                            if ((now - kv.Value).TotalMilliseconds > 100)
                                toRemove.Add(kv.Key);
                        }
                        foreach (int port in toRemove)
                            this._recentPorts.Remove(port);

                        // Only forward if we haven't seen this source port recently
                        // This prevents packet loops when multiple interfaces are bridged
                        if (!this._recentPorts.ContainsKey(originalPort))
                        {
                            // Mark this port as recently seen
                            this._recentPorts[originalPort] = now;

                            // Send with spoofed source IP/port to preserve original sender info
                            // Broadcast to subnet
                            this.SendRawPacket(originalIp, originalPort, iface.BroadcastAddress, ROON_PORT, packet);
                            Console.WriteLine($"[{this._config.SiteName}] RAW (broadcast) {originalIp}:{originalPort} -> {iface.BroadcastAddress}:{ROON_PORT}");

                            // Multicast to Roon group
                            this.SendRawPacket(originalIp, originalPort, MULTICAST_GROUP, ROON_PORT, packet);
                            Console.WriteLine($"[{this._config.SiteName}] RAW (multicast) {originalIp}:{originalPort} -> {MULTICAST_GROUP}:{ROON_PORT}");
                        }
                    }

                    // Forward to configured unicast targets
                    // Send without IP spoofing since these are direct unicast transmissions
                    foreach (IPAddress target in this._unicastTargets)
                    {
                        // Skip sending back to the original sender
                        if (target.Equals(originalIp))
                            continue;

                        IPEndPoint targetEp = new IPEndPoint(target, ROON_PORT);
                        this._lanSocket.SendTo(packet, targetEp);
                        Console.WriteLine($"[{this._config.SiteName}] UNICAST -> {target}:{ROON_PORT}");
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[{this._config.SiteName}] Tunnel error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Sends a packet to the remote relay via tunnel with original source IP and port in header.
        /// </summary>
        /// <param name="packet">Packet data to send.</param>
        /// <param name="srcIp">Original source IP address.</param>
        /// <param name="srcPort">Original source port.</param>
        private void SendToTunnel(byte[] packet, IPAddress srcIp, int srcPort)
        {
            // Build custom header to preserve original source information
            // Header format: [4 bytes IP][2 bytes port][payload]
            byte[] tunnelPacket = new byte[6 + packet.Length];

            // Pack source IP address (4 bytes)
            byte[] ipBytes = srcIp.GetAddressBytes();
            Array.Copy(ipBytes, 0, tunnelPacket, 0, 4);

            // Pack source port (2 bytes, big-endian)
            tunnelPacket[4] = (byte)(srcPort >> 8);
            tunnelPacket[5] = (byte)(srcPort & 0xFF);

            // Append original packet data
            Array.Copy(packet, 0, tunnelPacket, 6, packet.Length);

            // Send to remote relay
            this._tunnelSocket.SendTo(tunnelPacket, this._remoteRelayEp);
            Console.WriteLine($"[{this._config.SiteName}] TUNNEL -> {this._remoteRelayEp} (src: {srcIp}:{srcPort})");
        }
    }
}
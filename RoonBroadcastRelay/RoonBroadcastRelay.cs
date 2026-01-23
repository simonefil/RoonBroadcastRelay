using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RoonBroadcastRelay
{
    /// <summary>
    /// Main relay service for forwarding discovery protocol packets between networks.
    /// Supports RAAT (Roon), AirPlay (mDNS), SSDP (Chromecast/Sonos/LINN), and Squeezebox.
    /// </summary>
    public class RoonBroadcastRelay
    {
        #region Class Variables

        /// <summary>
        /// Relay configuration settings.
        /// </summary>
        readonly RelayConfig _config;

        /// <summary>
        /// List of configured LAN interfaces.
        /// </summary>
        readonly List<LanInterface> _lanInterfaces = new List<LanInterface>();

        /// <summary>
        /// List of unicast target IP addresses.
        /// </summary>
        readonly List<IPAddress> _unicastTargets = new List<IPAddress>();

        /// <summary>
        /// Set of local IP addresses for loop prevention.
        /// </summary>
        readonly HashSet<IPAddress> _localIps = new HashSet<IPAddress>();

        /// <summary>
        /// List of enabled protocol configurations.
        /// </summary>
        readonly List<ProtocolConfig> _protocols = new List<ProtocolConfig>();

        /// <summary>
        /// LAN sockets indexed by protocol port.
        /// </summary>
        readonly Dictionary<int, Socket> _lanSockets = new Dictionary<int, Socket>();

        /// <summary>
        /// Protocol configuration indexed by port for quick lookup.
        /// </summary>
        readonly Dictionary<int, ProtocolConfig> _protocolByPort = new Dictionary<int, ProtocolConfig>();

        /// <summary>
        /// Recent source ports for deduplication (thread-safe).
        /// </summary>
        ConcurrentDictionary<int, DateTime> _recentPorts = new ConcurrentDictionary<int, DateTime>();

        /// <summary>
        /// Socket for tunnel communication with remote relay.
        /// </summary>
        Socket _tunnelSocket;

        /// <summary>
        /// Remote relay endpoint for tunnel communication.
        /// </summary>
        IPEndPoint _remoteRelayEp;

        /// <summary>
        /// Raw socket for IP spoofing.
        /// </summary>
        Socket _rawSocket;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the relay with the specified configuration.
        /// </summary>
        /// <param name="config">Relay configuration settings.</param>
        public RoonBroadcastRelay(RelayConfig config)
        {
            this._config = config;

            // Initialize local interfaces and track their IPs
            foreach (InterfaceConfig iface in config.LocalInterfaces)
            {
                IPAddress ip = IPAddress.Parse(iface.LocalIp);
                this._localIps.Add(ip);

                LanInterface lanIface = new LanInterface(
                    ip,
                    IPAddress.Parse(iface.BroadcastAddress),
                    IPAddress.Parse(iface.SubnetMask)
                );
                this._lanInterfaces.Add(lanIface);
            }

            // Parse unicast target addresses
            if (config.UnicastTargets != null)
            {
                foreach (string target in config.UnicastTargets)
                {
                    this._unicastTargets.Add(IPAddress.Parse(target));
                }
            }

            // Setup remote relay endpoint
            if (!string.IsNullOrEmpty(config.RemoteRelayIp))
            {
                this._remoteRelayEp = new IPEndPoint(IPAddress.Parse(config.RemoteRelayIp), config.TunnelPort);
            }

            // Initialize enabled protocols from config
            ProtocolSettings settings = config.Protocols ?? new ProtocolSettings();

            if (settings.Raat)
            {
                this._protocols.Add(ProtocolDefinitions.Raat);
                this._protocolByPort[ProtocolDefinitions.Raat.Port] = ProtocolDefinitions.Raat;
            }

            if (settings.AirPlay)
            {
                this._protocols.Add(ProtocolDefinitions.AirPlay);
                this._protocolByPort[ProtocolDefinitions.AirPlay.Port] = ProtocolDefinitions.AirPlay;
            }

            if (settings.Ssdp)
            {
                this._protocols.Add(ProtocolDefinitions.Ssdp);
                this._protocolByPort[ProtocolDefinitions.Ssdp.Port] = ProtocolDefinitions.Ssdp;
            }

            if (settings.Squeezebox)
            {
                this._protocols.Add(ProtocolDefinitions.Squeezebox);
                this._protocolByPort[ProtocolDefinitions.Squeezebox.Port] = ProtocolDefinitions.Squeezebox;
            }

            // Ensure at least RAAT is enabled
            if (this._protocols.Count == 0)
            {
                this._protocols.Add(ProtocolDefinitions.Raat);
                this._protocolByPort[ProtocolDefinitions.Raat.Port] = ProtocolDefinitions.Raat;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the relay service and begins listening on all configured interfaces.
        /// Blocks until Ctrl+C is pressed.
        /// </summary>
        public void Start()
        {
            Console.WriteLine($"[{this._config.SiteName}] Starting Roon Relay");

            // Create LAN sockets for each enabled protocol
            foreach (ProtocolConfig protocol in this._protocols)
            {
                Socket socket = this.CreateLanSocketForProtocol(protocol);
                if (socket != null)
                {
                    this._lanSockets[protocol.Port] = socket;
                }
                else
                {
                    // Remove protocol from active list if bind failed
                    this._protocolByPort.Remove(protocol.Port);
                }
            }

            // Create raw socket (shared by all protocols)
            this._rawSocket = this.CreateRawSocket();

            // Log configured protocols
            foreach (ProtocolConfig protocol in this._protocols)
            {
                if (this._lanSockets.ContainsKey(protocol.Port))
                {
                    string mcast = string.IsNullOrEmpty(protocol.MulticastGroup) ? "broadcast only" : $"multicast {protocol.MulticastGroup}";
                    Console.WriteLine($"[{this._config.SiteName}] Protocol {protocol.Name}: port {protocol.Port}, {mcast}, TTL={protocol.Ttl}");
                }
            }

            // Log configured LAN interfaces
            foreach (LanInterface iface in this._lanInterfaces)
            {
                Console.WriteLine($"[{this._config.SiteName}] LAN interface: {iface.LocalIp} (broadcast: {iface.BroadcastAddress}, mask: {iface.SubnetMask})");
            }

            // Create tunnel socket if remote relay is configured
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

            // Start LAN listener thread for EACH protocol
            foreach (ProtocolConfig protocol in this._protocols)
            {
                if (this._lanSockets.ContainsKey(protocol.Port))
                {
                    int port = protocol.Port;
                    Thread lanThread = new Thread(() => this.ListenLanForProtocol(port)) { IsBackground = true };
                    lanThread.Start();
                }
            }

            // Start tunnel listener thread if tunnel is configured
            if (this._tunnelSocket != null)
            {
                Thread tunnelThread = new Thread(this.ListenTunnel) { IsBackground = true };
                tunnelThread.Start();
            }

            Console.WriteLine($"[{this._config.SiteName}] Relay running. Press Ctrl+C to stop.");

            // Wait for Ctrl+C signal
            ManualResetEvent exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; exitEvent.Set(); };
            exitEvent.WaitOne();

            Console.WriteLine($"[{this._config.SiteName}] Shutting down...");
        }

        #endregion

        #region Private Methods - Socket Creation

        /// <summary>
        /// Creates a raw socket for sending packets with custom IP headers (IP spoofing).
        /// </summary>
        /// <returns>Configured raw socket.</returns>
        private Socket CreateRawSocket()
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Udp);

            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

            Console.WriteLine($"[{this._config.SiteName}] Raw socket created for IP spoofing");
            return socket;
        }

        /// <summary>
        /// Creates a UDP socket for a specific protocol.
        /// </summary>
        /// <param name="protocol">Protocol configuration.</param>
        /// <returns>Configured UDP socket bound to the protocol port, or null if bind fails.</returns>
        private Socket CreateLanSocketForProtocol(ProtocolConfig protocol)
        {
            Socket socket = null;

            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

                // Bind to protocol port
                socket.Bind(new IPEndPoint(IPAddress.Any, protocol.Port));

                // Join multicast group if configured
                if (!string.IsNullOrEmpty(protocol.MulticastGroup))
                {
                    IPAddress multicastAddr = IPAddress.Parse(protocol.MulticastGroup);
                    foreach (LanInterface iface in this._lanInterfaces)
                    {
                        MulticastOption mcastOption = new MulticastOption(multicastAddr, iface.LocalIp);
                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, mcastOption);
                    }
                }

                Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} socket bound to 0.0.0.0:{protocol.Port}");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[{this._config.SiteName}] WARNING: Cannot bind {protocol.Name} on port {protocol.Port}: {ex.Message}");
                Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} protocol will be DISABLED (port may be in use by another service)");

                if (socket != null)
                {
                    socket.Close();
                    socket = null;
                }
            }

            return socket;
        }

        /// <summary>
        /// Creates and configures a UDP socket for tunnel communication with remote relay.
        /// </summary>
        /// <returns>Configured UDP socket for tunnel traffic.</returns>
        private Socket CreateTunnelSocket()
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, this._config.TunnelPort));

            Console.WriteLine($"[{this._config.SiteName}] Tunnel socket bound to 0.0.0.0:{this._config.TunnelPort}");
            return socket;
        }

        #endregion

        #region Private Methods - Packet Sending

        /// <summary>
        /// Sends a UDP packet with spoofed source IP and port using raw socket.
        /// </summary>
        /// <param name="srcIp">Source IP address to spoof.</param>
        /// <param name="srcPort">Source port to spoof.</param>
        /// <param name="dstIp">Destination IP address.</param>
        /// <param name="dstPort">Destination port.</param>
        /// <param name="payload">Packet payload.</param>
        /// <param name="ttl">IP TTL value.</param>
        private void SendRawPacket(IPAddress srcIp, int srcPort, IPAddress dstIp, int dstPort, byte[] payload, int ttl = 64)
        {
            byte[] packet = this.BuildIpUdpPacket(srcIp, srcPort, dstIp, dstPort, payload, ttl);
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
        /// <param name="ttl">IP TTL value.</param>
        /// <returns>Complete IP/UDP packet as byte array.</returns>
        private byte[] BuildIpUdpPacket(IPAddress srcIp, int srcPort, IPAddress dstIp, int dstPort, byte[] payload, int ttl = 64)
        {
            // Calculate packet sizes
            int udpLength = 8 + payload.Length;
            int ipLength = 20 + udpLength;

            byte[] packet = new byte[ipLength];

            // Build IP Header (20 bytes, RFC 791)
            packet[0] = 0x45;
            packet[1] = 0x00;
            packet[2] = (byte)(ipLength >> 8);
            packet[3] = (byte)(ipLength & 0xFF);
            packet[4] = 0x00;
            packet[5] = 0x00;
            packet[6] = 0x40;
            packet[7] = 0x00;
            packet[8] = (byte)ttl; // TTL from parameter
            packet[9] = 0x11;
            packet[10] = 0x00;
            packet[11] = 0x00;

            // Copy source and destination IP addresses
            byte[] srcBytes = srcIp.GetAddressBytes();
            byte[] dstBytes = dstIp.GetAddressBytes();
            Array.Copy(srcBytes, 0, packet, 12, 4);
            Array.Copy(dstBytes, 0, packet, 16, 4);

            // Calculate and set IP header checksum
            ushort ipChecksum = this.CalculateChecksum(packet, 0, 20);
            packet[10] = (byte)(ipChecksum >> 8);
            packet[11] = (byte)(ipChecksum & 0xFF);

            // Build UDP Header (8 bytes, RFC 768)
            packet[20] = (byte)(srcPort >> 8);
            packet[21] = (byte)(srcPort & 0xFF);
            packet[22] = (byte)(dstPort >> 8);
            packet[23] = (byte)(dstPort & 0xFF);
            packet[24] = (byte)(udpLength >> 8);
            packet[25] = (byte)(udpLength & 0xFF);
            packet[26] = 0x00;
            packet[27] = 0x00;

            // Copy payload
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

            for (int i = 0; i < length; i += 2)
            {
                ushort word;
                if (i + 1 < length)
                {
                    word = (ushort)(data[offset + i] << 8 | data[offset + i + 1]);
                }
                else
                {
                    word = (ushort)(data[offset + i] << 8);
                }
                sum += word;
            }

            while (sum >> 16 != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }

            return (ushort)~sum;
        }

        /// <summary>
        /// Sends packet to tunnel with extended 8-byte header.
        /// Header format: [4 bytes IP][2 bytes src port][2 bytes dst port][payload]
        /// </summary>
        /// <param name="packet">Packet data to send.</param>
        /// <param name="srcIp">Original source IP address.</param>
        /// <param name="srcPort">Original source port.</param>
        /// <param name="dstPort">Destination port (protocol port).</param>
        private void SendToTunnelExtended(byte[] packet, IPAddress srcIp, int srcPort, int dstPort)
        {
            byte[] tunnelPacket = new byte[8 + packet.Length];

            // Pack source IP address (4 bytes)
            byte[] ipBytes = srcIp.GetAddressBytes();
            Array.Copy(ipBytes, 0, tunnelPacket, 0, 4);

            // Pack source port (2 bytes, big-endian)
            tunnelPacket[4] = (byte)(srcPort >> 8);
            tunnelPacket[5] = (byte)(srcPort & 0xFF);

            // Pack destination port (2 bytes, big-endian)
            tunnelPacket[6] = (byte)(dstPort >> 8);
            tunnelPacket[7] = (byte)(dstPort & 0xFF);

            // Append original packet data
            Array.Copy(packet, 0, tunnelPacket, 8, packet.Length);

            this._tunnelSocket.SendTo(tunnelPacket, this._remoteRelayEp);
            Console.WriteLine($"[{this._config.SiteName}] TUNNEL -> {this._remoteRelayEp} (src: {srcIp}:{srcPort}, dstPort: {dstPort})");
        }

        #endregion

        #region Private Methods - Helpers

        /// <summary>
        /// Finds the LAN interface that matches the sender's subnet.
        /// </summary>
        /// <param name="sender">IP address of the sender.</param>
        /// <returns>Matching LAN interface, or null if not found.</returns>
        private LanInterface FindInterfaceForSender(IPAddress sender)
        {
            LanInterface result = null;

            foreach (LanInterface iface in this._lanInterfaces)
            {
                if (iface.IsInSubnet(sender))
                {
                    result = iface;
                    break;
                }
            }

            return result;
        }

        #endregion

        #region Private Methods - Listeners

        /// <summary>
        /// Listens for packets on a specific protocol port.
        /// </summary>
        /// <param name="port">Protocol port to listen on.</param>
        private void ListenLanForProtocol(int port)
        {
            ProtocolConfig protocol = this._protocolByPort[port];
            Socket lanSocket = this._lanSockets[port];
            byte[] buffer = new byte[4096];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    int received = lanSocket.ReceiveFrom(buffer, ref remoteEp);
                    IPEndPoint senderEp = (IPEndPoint)remoteEp;

                    // Ignore packets from our own interfaces
                    if (this._localIps.Contains(senderEp.Address))
                    {
                        continue;
                    }

                    bool fromUnicastTarget = this._unicastTargets.Contains(senderEp.Address);
                    LanInterface sourceIface = this.FindInterfaceForSender(senderEp.Address);

                    // Drop packet if sender is not in any subnet and not a unicast target
                    if (sourceIface == null && !fromUnicastTarget)
                    {
                        continue;
                    }

                    Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} <- {senderEp.Address}:{senderEp.Port} ({received} bytes){(fromUnicastTarget ? " [unicast target]" : "")}");

                    byte[] packet = new byte[received];
                    Array.Copy(buffer, packet, received);

                    // Forward to tunnel with extended header
                    if (this._tunnelSocket != null && this._remoteRelayEp != null)
                    {
                        this.SendToTunnelExtended(packet, senderEp.Address, senderEp.Port, port);
                    }

                    // Forward to unicast targets
                    foreach (IPAddress target in this._unicastTargets)
                    {
                        if (target.Equals(senderEp.Address))
                        {
                            continue;
                        }

                        IPEndPoint targetEp = new IPEndPoint(target, port);
                        lanSocket.SendTo(packet, targetEp);
                        Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} UNICAST -> {target}:{port}");
                    }

                    // Bridge to other interfaces
                    foreach (LanInterface otherIface in this._lanInterfaces)
                    {
                        if (sourceIface != null && otherIface.LocalIp.Equals(sourceIface.LocalIp))
                        {
                            continue;
                        }

                        if (fromUnicastTarget)
                        {
                            DateTime now = DateTime.Now;

                            // Clean up old ports (thread-safe)
                            foreach (KeyValuePair<int, DateTime> kv in this._recentPorts)
                            {
                                if ((now - kv.Value).TotalMilliseconds > 100)
                                {
                                    this._recentPorts.TryRemove(kv.Key, out _);
                                }
                            }

                            if (this._recentPorts.ContainsKey(senderEp.Port))
                            {
                                continue;
                            }

                            this._recentPorts[senderEp.Port] = now;

                            // Send with protocol-specific TTL
                            if (protocol.UseBroadcast)
                            {
                                this.SendRawPacket(senderEp.Address, senderEp.Port, otherIface.BroadcastAddress, port, packet, protocol.Ttl);
                                Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} RAW (broadcast) {senderEp.Address}:{senderEp.Port} -> {otherIface.BroadcastAddress}:{port}");
                            }

                            if (!string.IsNullOrEmpty(protocol.MulticastGroup))
                            {
                                IPAddress mcastAddr = IPAddress.Parse(protocol.MulticastGroup);
                                this.SendRawPacket(senderEp.Address, senderEp.Port, mcastAddr, port, packet, protocol.Ttl);
                                Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} RAW (multicast) {senderEp.Address}:{senderEp.Port} -> {mcastAddr}:{port}");
                            }
                        }
                        else
                        {
                            if (protocol.UseBroadcast)
                            {
                                IPEndPoint broadcastEp = new IPEndPoint(otherIface.BroadcastAddress, port);
                                lanSocket.SendTo(packet, broadcastEp);
                                Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} (broadcast) -> {otherIface.BroadcastAddress}:{port}");
                            }

                            if (!string.IsNullOrEmpty(protocol.MulticastGroup))
                            {
                                IPEndPoint multicastEp = new IPEndPoint(IPAddress.Parse(protocol.MulticastGroup), port);
                                lanSocket.SendTo(packet, multicastEp);
                                Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} (multicast) -> {protocol.MulticastGroup}:{port}");
                            }
                        }
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Listens for packets on the tunnel socket from remote relay.
        /// Uses extended 8-byte header format.
        /// </summary>
        private void ListenTunnel()
        {
            byte[] buffer = new byte[4096];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    int received = this._tunnelSocket.ReceiveFrom(buffer, ref remoteEp);
                    IPEndPoint senderEp = (IPEndPoint)remoteEp;

                    // Minimum size: 8 byte header + 1 byte payload
                    if (received < 9)
                    {
                        continue;
                    }

                    // Extract extended header
                    IPAddress originalIp = new IPAddress(new byte[] { buffer[0], buffer[1], buffer[2], buffer[3] });
                    int originalPort = buffer[4] << 8 | buffer[5];
                    int destPort = buffer[6] << 8 | buffer[7];
                    byte[] packet = new byte[received - 8];
                    Array.Copy(buffer, 8, packet, 0, received - 8);

                    // Find protocol config for this port
                    ProtocolConfig protocol = null;
                    if (!this._protocolByPort.TryGetValue(destPort, out protocol))
                    {
                        Console.WriteLine($"[{this._config.SiteName}] TUNNEL: unknown protocol port {destPort}, ignoring");
                        continue;
                    }

                    Console.WriteLine($"[{this._config.SiteName}] TUNNEL <- {senderEp.Address}:{senderEp.Port} ({packet.Length} bytes, src: {originalIp}:{originalPort}, proto: {protocol.Name})");

                    // Forward to all local interfaces
                    foreach (LanInterface iface in this._lanInterfaces)
                    {
                        DateTime now = DateTime.Now;

                        // Clean up old ports (thread-safe)
                        foreach (KeyValuePair<int, DateTime> kv in this._recentPorts)
                        {
                            if ((now - kv.Value).TotalMilliseconds > 100)
                            {
                                this._recentPorts.TryRemove(kv.Key, out _);
                            }
                        }

                        if (!this._recentPorts.ContainsKey(originalPort))
                        {
                            this._recentPorts[originalPort] = now;

                            // Send with protocol-specific TTL
                            if (protocol.UseBroadcast)
                            {
                                this.SendRawPacket(originalIp, originalPort, iface.BroadcastAddress, destPort, packet, protocol.Ttl);
                                Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} RAW (broadcast) {originalIp}:{originalPort} -> {iface.BroadcastAddress}:{destPort}");
                            }

                            if (!string.IsNullOrEmpty(protocol.MulticastGroup))
                            {
                                IPAddress mcastAddr = IPAddress.Parse(protocol.MulticastGroup);
                                this.SendRawPacket(originalIp, originalPort, mcastAddr, destPort, packet, protocol.Ttl);
                                Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} RAW (multicast) {originalIp}:{originalPort} -> {mcastAddr}:{destPort}");
                            }
                        }
                    }

                    // Forward to unicast targets
                    Socket lanSocket = null;
                    if (this._lanSockets.TryGetValue(destPort, out lanSocket))
                    {
                        foreach (IPAddress target in this._unicastTargets)
                        {
                            if (target.Equals(originalIp))
                            {
                                continue;
                            }

                            IPEndPoint targetEp = new IPEndPoint(target, destPort);
                            lanSocket.SendTo(packet, targetEp);
                            Console.WriteLine($"[{this._config.SiteName}] {protocol.Name} UNICAST -> {target}:{destPort}");
                        }
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[{this._config.SiteName}] Tunnel error: {ex.Message}");
                }
            }
        }

        #endregion
    }
}

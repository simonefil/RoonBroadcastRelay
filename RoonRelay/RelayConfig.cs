using System.Collections.Generic;

namespace RoonRelay
{
    /// <summary>
    /// Configuration settings for the Roon Relay service.
    /// </summary>
    public class RelayConfig
    {
        /// <summary>
        /// Site identifier used in logs. Example: "SiteA", "Office".
        /// </summary>
        public string SiteName { get; set; }

        /// <summary>
        /// UDP port for tunnel communication between remote relays.
        /// </summary>
        public int TunnelPort { get; set; }

        /// <summary>
        /// IP address of the remote relay for tunnel connection. Can be null.
        /// </summary>
        public string RemoteRelayIp { get; set; }

        /// <summary>
        /// Local network interfaces for listening and forwarding Roon packets.
        /// </summary>
        public List<InterfaceConfig> LocalInterfaces { get; set; }

        /// <summary>
        /// Optional list of unicast targets to forward packets to.
        /// </summary>
        public List<string> UnicastTargets { get; set; }
    }
}
using System.Collections.Generic;

namespace RoonBroadcastRelay
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
        /// IP addresses of the remote relays for tunnel connection, one per remote site. Can be null or empty.
        /// </summary>
        public List<string> RemoteRelayIps { get; set; }

        /// <summary>
        /// Local network interfaces for listening and forwarding Roon packets.
        /// </summary>
        public List<InterfaceConfig> LocalInterfaces { get; set; }

        /// <summary>
        /// Optional list of unicast targets to forward packets to.
        /// </summary>
        public List<string> UnicastTargets { get; set; }

        /// <summary>
        /// Protocol enable/disable settings. If null, only RAAT is enabled.
        /// </summary>
        public ProtocolSettings Protocols { get; set; }
    }
}
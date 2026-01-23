namespace RoonBroadcastRelay
{
    /// <summary>
    /// Internal configuration for a discovery protocol.
    /// Not exposed in JSON config - values are hardcoded in ProtocolDefinitions.
    /// </summary>
    public class ProtocolConfig
    {
        #region Properties

        /// <summary>
        /// Protocol identifier (e.g., "Raat", "AirPlay").
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// UDP port for this protocol.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Multicast group address (null if broadcast only).
        /// </summary>
        public string MulticastGroup { get; }

        /// <summary>
        /// IP TTL for raw packets.
        /// </summary>
        public int Ttl { get; }

        /// <summary>
        /// Whether to use broadcast in addition to multicast.
        /// </summary>
        public bool UseBroadcast { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new protocol configuration with all parameters.
        /// </summary>
        /// <param name="name">Protocol identifier.</param>
        /// <param name="port">UDP port number.</param>
        /// <param name="multicastGroup">Multicast group address or null.</param>
        /// <param name="ttl">IP TTL value for raw packets.</param>
        /// <param name="useBroadcast">Whether to use broadcast.</param>
        public ProtocolConfig(string name, int port, string multicastGroup, int ttl, bool useBroadcast)
        {
            this.Name = name;
            this.Port = port;
            this.MulticastGroup = multicastGroup;
            this.Ttl = ttl;
            this.UseBroadcast = useBroadcast;
        }

        #endregion
    }
}

namespace RoonBroadcastRelay
{
    /// <summary>
    /// Configuration for a local network interface.
    /// </summary>
    public class InterfaceConfig
    {
        /// <summary>
        /// Local IP address to bind to.
        /// </summary>
        public string LocalIp { get; set; }

        /// <summary>
        /// Broadcast address for this subnet.
        /// </summary>
        public string BroadcastAddress { get; set; }

        /// <summary>
        /// Subnet mask for this interface.
        /// </summary>
        public string SubnetMask { get; set; }
    }
}

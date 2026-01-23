namespace RoonBroadcastRelay
{
    /// <summary>
    /// Protocol enable/disable settings for JSON configuration.
    /// User only specifies true/false, technical details are managed internally.
    /// </summary>
    public class ProtocolSettings
    {
        #region Properties

        /// <summary>
        /// Enable RAAT (Roon native protocol on port 9003). Default: true.
        /// </summary>
        public bool Raat { get; set; } = true;

        /// <summary>
        /// Enable AirPlay discovery relay (mDNS on port 5353). Default: false.
        /// Note: May conflict with avahi-daemon if running.
        /// </summary>
        public bool AirPlay { get; set; } = false;

        /// <summary>
        /// Enable SSDP relay for Chromecast, Sonos, LINN (port 1900). Default: false.
        /// </summary>
        public bool Ssdp { get; set; } = false;

        /// <summary>
        /// Enable Squeezebox/SlimProto discovery relay (port 3483). Default: false.
        /// </summary>
        public bool Squeezebox { get; set; } = false;

        #endregion
    }
}

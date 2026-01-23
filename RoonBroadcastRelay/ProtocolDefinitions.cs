using System.Collections.Generic;

namespace RoonBroadcastRelay
{
    /// <summary>
    /// Predefined protocol configurations for Roon-supported discovery protocols.
    /// All technical parameters are hardcoded - user only enables/disables via ProtocolSettings.
    /// </summary>
    public static class ProtocolDefinitions
    {
        #region Protocol Constants

        /// <summary>
        /// RAAT - Roon Advanced Audio Transport (native Roon protocol).
        /// UDP port 9003, multicast 239.255.90.90, TTL 64.
        /// </summary>
        public static readonly ProtocolConfig Raat = new ProtocolConfig(
            name: "Raat",
            port: 9003,
            multicastGroup: "239.255.90.90",
            ttl: 64,
            useBroadcast: true
        );

        /// <summary>
        /// AirPlay - Apple streaming via mDNS/Bonjour.
        /// UDP port 5353, multicast 224.0.0.251, TTL 255 (required by mDNS).
        /// </summary>
        public static readonly ProtocolConfig AirPlay = new ProtocolConfig(
            name: "AirPlay",
            port: 5353,
            multicastGroup: "224.0.0.251",
            ttl: 255,
            useBroadcast: false
        );

        /// <summary>
        /// SSDP - Used by Chromecast, Sonos, LINN (UPnP discovery).
        /// UDP port 1900, multicast 239.255.255.250, TTL 4 (SSDP normally uses 1).
        /// </summary>
        public static readonly ProtocolConfig Ssdp = new ProtocolConfig(
            name: "Ssdp",
            port: 1900,
            multicastGroup: "239.255.255.250",
            ttl: 4,
            useBroadcast: true
        );

        /// <summary>
        /// SlimProto - Squeezebox/Logitech Media Server discovery.
        /// UDP port 3483, broadcast only (no multicast), TTL 64.
        /// </summary>
        public static readonly ProtocolConfig Squeezebox = new ProtocolConfig(
            name: "Squeezebox",
            port: 3483,
            multicastGroup: null,
            ttl: 64,
            useBroadcast: true
        );

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns protocol configuration by name (case-insensitive).
        /// </summary>
        /// <param name="name">Protocol name.</param>
        /// <returns>Protocol configuration or null if not found.</returns>
        public static ProtocolConfig GetByName(string name)
        {
            ProtocolConfig result = null;

            // Match protocol by name
            string lowerName = name.ToLowerInvariant();
            if (lowerName == "raat")
            {
                result = Raat;
            }
            else if (lowerName == "airplay")
            {
                result = AirPlay;
            }
            else if (lowerName == "ssdp")
            {
                result = Ssdp;
            }
            else if (lowerName == "squeezebox")
            {
                result = Squeezebox;
            }

            return result;
        }

        /// <summary>
        /// Returns all available protocol names.
        /// </summary>
        /// <returns>List of protocol names.</returns>
        public static List<string> GetAllNames()
        {
            List<string> names = new List<string>();
            names.Add("Raat");
            names.Add("AirPlay");
            names.Add("Ssdp");
            names.Add("Squeezebox");
            return names;
        }

        #endregion
    }
}

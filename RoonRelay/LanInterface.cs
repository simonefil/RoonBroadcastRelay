using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RoonRelay
{
    /// <summary>
    /// Represents a local network interface used by the relay.
    /// </summary>
    public class LanInterface
    {
        /// <summary>
        /// Local IP address of this interface.
        /// </summary>
        public IPAddress LocalIp { get; }

        /// <summary>
        /// Broadcast address for this subnet.
        /// </summary>
        public IPAddress BroadcastAddress { get; }

        /// <summary>
        /// Subnet mask of this interface.
        /// </summary>
        public IPAddress SubnetMask { get; }

        /// <summary>
        /// UDP socket bound to this interface for sending and receiving packets.
        /// </summary>
        public Socket Socket { get; set; }

        /// <summary>
        /// Creates a new LAN interface instance.
        /// </summary>
        /// <param name="localIp">Local IP address to bind to.</param>
        /// <param name="broadcastAddress">Broadcast address for this subnet.</param>
        /// <param name="subnetMask">Subnet mask for this interface.</param>
        public LanInterface(IPAddress localIp, IPAddress broadcastAddress, IPAddress subnetMask)
        {
            this.LocalIp = localIp;
            this.BroadcastAddress = broadcastAddress;
            this.SubnetMask = subnetMask;
        }

        /// <summary>
        /// Checks if an IP address belongs to this interface's subnet.
        /// </summary>
        /// <param name="address">IP address to check.</param>
        /// <returns>True if the address is in the same subnet, false otherwise.</returns>
        public bool IsInSubnet(IPAddress address)
        {
            byte[] addrBytes = address.GetAddressBytes();
            byte[] localBytes = this.LocalIp.GetAddressBytes();
            byte[] maskBytes = this.SubnetMask.GetAddressBytes();

            for (int i = 0; i < 4; i++)
            {
                if ((addrBytes[i] & maskBytes[i]) != (localBytes[i] & maskBytes[i]))
                    return false;
            }
            return true;
        }
    }
}
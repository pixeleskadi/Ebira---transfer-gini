using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Ebira.Windows;

public static class NetworkSelection
{
    public static bool IsVirtual(NetworkInterfaceType type, string name, string description)
    {
        var identity = (name + " " + description).ToLowerInvariant();
        return type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Loopback ||
            new[] { "warp", "vpn", "wireguard", "tailscale", "zerotier", "virtual", "vethernet", "tap-", "tun " }.Any(identity.Contains);
    }
    public static int Rank(NetworkInterfaceType type, string name, string description, bool gateway)
    {
        var kind = IsVirtual(type, name, description) ? 100 : type == NetworkInterfaceType.Wireless80211 ? 0 : type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT ? 10 : 20;
        return kind + (gateway ? 0 : 5);
    }
    public static bool IsLocalIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 169 && bytes[1] == 254;
    }
}

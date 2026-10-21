using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ebira.Windows;

/// <summary>Responds only to bounded, nonce-bearing local discovery probes. No session tokens are advertised.</summary>
public sealed class LanDiscovery : IAsyncDisposable
{
    public const int Port = 8444;
    private readonly UdpClient socket;
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    public int ListenPort => ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    public LanDiscovery(Func<(int port, string pin, string code)> current, int listenPort = Port)
    {
        socket = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
        worker = Task.Run(() => Listen(current));
    }
    private async Task Listen(Func<(int port, string pin, string code)> current)
    {
        var window = Environment.TickCount64; var count = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var packet = await socket.ReceiveAsync(stop.Token);
                if (packet.Buffer.Length > 512) continue;
                var now = Environment.TickCount64;
                if (now - window >= 1000) { window = now; count = 0; }
                if (++count > 20) continue;
                var local = new List<(IPAddress address, IPAddress mask)>();
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).OrderBy(n => NetworkSelection.IsVirtual(n.NetworkInterfaceType, n.Name, n.Description)))
                {
                    try { foreach (var address in nic.GetIPProperties().UnicastAddresses.Where(a => NetworkSelection.IsLocalIpv4(a.Address))) local.Add((address.Address, address.IPv4Mask)); }
                    catch (NetworkInformationException) { }
                }
                var info = current();
                var reply = Reply(Encoding.UTF8.GetString(packet.Buffer), packet.RemoteEndPoint.Address, local, Environment.MachineName, info.port, info.pin, info.code);
                if (reply != null) await socket.SendAsync(Encoding.UTF8.GetBytes(reply), packet.RemoteEndPoint, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { /* Transfer service remains usable with a pasted invitation. */ }
    }
    public static string? Reply(string probe, IPAddress remote, IEnumerable<(IPAddress address, IPAddress mask)> local, string name, int port, string pin, string code)
    {
        if (probe.Length > 512 || !NetworkSelection.IsLocalIpv4(remote) || port < 1 || port > 65535 || !Regex.IsMatch(pin, "\\A[a-fA-F0-9]{64}\\z") || !Regex.IsMatch(code, "\\A[a-fA-F0-9]{12}\\z")) return null;
        try
        {
            using var json = JsonDocument.Parse(probe);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("magic", out var magic) || magic.GetString() != "ebira-discover-v1" || !root.TryGetProperty("nonce", out var nonce) || nonce.ValueKind != JsonValueKind.String || !Regex.IsMatch(nonce.GetString()!, "\\A[a-f0-9]{32}\\z")) return null;
            var peer = remote.GetAddressBytes();
            foreach (var (address, mask) in local)
            {
                if (!NetworkSelection.IsLocalIpv4(address)) continue;
                var own = address.GetAddressBytes(); var bits = mask.GetAddressBytes();
                if (bits.Length != 4 || bits.All(b => b == 0) || !Enumerable.Range(0, 4).All(i => (own[i] & bits[i]) == (peer[i] & bits[i]))) continue;
                return JsonSerializer.Serialize(new { magic = "ebira-host-v1", nonce = nonce.GetString(), name = name[..Math.Min(name.Length, 80)], host = $"https://{address}:{port}", pin, code });
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        return null;
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); socket.Dispose(); await worker; stop.Dispose(); }
}

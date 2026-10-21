using System.Net;
using System.Net.NetworkInformation;
using Ebira.Windows;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware); Application.EnableVisualStyles();
            Check(NetworkSelection.Rank(NetworkInterfaceType.Wireless80211, "Wi-Fi", "Intel Wi-Fi", true) < NetworkSelection.Rank(NetworkInterfaceType.Ethernet, "CloudflareWARP", "Cloudflare WARP", true), "physical Wi-Fi precedes WARP");
            Check(NetworkSelection.Rank(NetworkInterfaceType.Ethernet, "Ethernet", "Intel Ethernet", true) < NetworkSelection.Rank(NetworkInterfaceType.Ethernet, "vEthernet", "Hyper-V Virtual Ethernet", true), "physical Ethernet precedes virtual Ethernet");
            Check(NetworkSelection.IsVirtual(NetworkInterfaceType.Ethernet, "CloudflareWARP", "Cloudflare WARP"), "VPN adapter identified for the selection warning");
            foreach (var ip in new[] { "10.0.0.1", "172.16.0.2", "172.31.1.1", "192.168.1.4", "169.254.1.2" }) Check(NetworkSelection.IsLocalIpv4(IPAddress.Parse(ip)), "local IPv4 allowed: " + ip);
            foreach (var ip in new[] { "127.0.0.1", "8.8.8.8", "172.32.0.1", "100.64.0.1", "::1" }) Check(!NetworkSelection.IsLocalIpv4(IPAddress.Parse(ip)), "unsupported address excluded: " + ip);
            var nonce = new string('a', 32); var pin = new string('b', 64);
            var probe = "{\"magic\":\"ebira-discover-v1\",\"nonce\":\"" + nonce + "\"}";
            var local = new[] { (IPAddress.Parse("10.0.0.8"), IPAddress.Any), (IPAddress.Parse("192.168.42.6"), IPAddress.Parse("255.255.255.0")), (IPAddress.Parse("10.0.0.7"), IPAddress.Parse("255.255.255.0")) };
            var reply = LanDiscovery.Reply(probe, IPAddress.Parse("192.168.42.1"), local, "Test laptop", 8443, pin, "0123456789AB");
            Check(reply != null && reply.Contains("https://192.168.42.6:8443") && reply.Contains(nonce), "discovery uses the interface on the phone subnet and echoes its nonce");
            Check(LanDiscovery.Reply(probe, IPAddress.Parse("8.8.8.8"), local, "test", 8443, pin, "0123456789AB") == null, "discovery ignores public sources");
            Check(LanDiscovery.Reply(probe, IPAddress.Parse("192.168.2.1"), local, "test", 8443, pin, "0123456789AB") == null, "discovery ignores other subnets");
            foreach (var invalid in new[] { "{}", "[]", "{", "{\"magic\":42}", probe.Replace("discover-v1", "discover-v0"), probe.Replace(nonce, "bad"), new string('a', 513) }) Check(LanDiscovery.Reply(invalid, IPAddress.Parse("192.168.42.1"), local, "test", 8443, pin, "0123456789AB") == null, "malformed discovery probe is ignored");
            Check(LanDiscovery.Reply(probe, IPAddress.Parse("192.168.42.1"), local, "test", 8443, "bad", "0123456789AB") == null, "discovery never advertises an invalid certificate pin");
            DiscoverySocketCheck(probe, pin).GetAwaiter().GetResult();
            foreach (var scenario in new[] { (1f, 1000, 850), (1.25f, 1054, 904), (1.5f, 1000, 740), (2f, 900, 650), (1f, 640, 540) })
            {
                using var form = new MainForm();
                Descendants(form).OfType<Label>().First(l => l.Text.StartsWith("Pairing code")).Text = "Pairing code  0123456789AB\nCertificate ends  ABCDEF012345";
                Descendants(form).OfType<Label>().First(l => l.Text.StartsWith("Starting")).Text = "Layout test: encrypted host listening on port 8443. Allow Ebira through Windows Firewall on private networks.";
                var fonts = Descendants(form).Prepend(form).Select(c => (control: c, font: c.Font)).ToArray();
                foreach (var (control, font) in fonts) control.Font = new Font(font.FontFamily, font.Size * scenario.Item1, font.Style);
                form.Size = new Size(scenario.Item2, scenario.Item3);
                _ = form.Handle; foreach (var c in Descendants(form)) _ = c.Handle;
                form.PerformLayout(); Application.DoEvents();
                var viewport = (Panel)form.Controls.Find("viewport", true).Single(); var content = (TableLayoutPanel)form.Controls.Find("content", true).Single();
                Check(content.Right <= viewport.ClientSize.Width + 1, $"{scenario}: content fits viewport width");
                Check(!viewport.HorizontalScroll.Visible, $"{scenario}: no page horizontal scrollbar");
                foreach (var flow in Descendants(form).OfType<FlowLayoutPanel>())
                {
                    Check(!flow.AutoScroll && flow.WrapContents, $"{scenario}: action row wraps without nested scrolling");
                    foreach (Control child in flow.Controls)
                        Check(child.Right <= flow.ClientSize.Width + 1 && child.Bottom <= flow.ClientSize.Height + 1, $"{scenario}: {child.Text} fits its action row ({child.Bounds} within {flow.ClientRectangle})");
                }
                foreach (var label in content.Controls.OfType<Label>()) Check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, $"{scenario}: label text fits its row");
                var footer = content.Controls.OfType<Label>().Last(); viewport.ScrollControlIntoView(footer); Application.DoEvents();
                var bottom = viewport.PointToClient(footer.PointToScreen(new Point(0, footer.Height)));
                Check(bottom.Y <= viewport.ClientSize.Height + 1, $"{scenario}: bottom controls and footer remain reachable by vertical scrolling");
                if (args.Length > 0)
                {
                    Directory.CreateDirectory(args[0]); using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(args[0], $"layout-{scenario.Item1:0.00}-{form.Width}x{form.Height}.png"));
                }
            }
            Console.WriteLine("Windows discovery, network selection and layout checks passed (100–200% font sizes, including screenshot dimensions). No transfer host or desktop interaction was started."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static IEnumerable<Control> Descendants(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static async Task DiscoverySocketCheck(string probe, string pin)
    {
        var address = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).FirstOrDefault(NetworkSelection.IsLocalIpv4);
        if (address == null) { Console.WriteLine("SKIP: local UDP socket check needs an active private IPv4 adapter."); return; }
        await using var host = new LanDiscovery(() => (8443, pin, "0123456789AB"), 0);
        using var client = new System.Net.Sockets.UdpClient(new IPEndPoint(address, 0));
        await client.SendAsync(System.Text.Encoding.UTF8.GetBytes(probe), new IPEndPoint(address, host.ListenPort));
        var response = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check(System.Text.Encoding.UTF8.GetString(response.Buffer).Contains($"https://{address}:8443"), "real UDP discovery round trip uses the private adapter address");
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
}

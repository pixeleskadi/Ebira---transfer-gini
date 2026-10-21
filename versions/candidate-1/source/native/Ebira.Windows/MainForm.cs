using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Ebira.Core;

namespace Ebira.Windows;

public sealed class MainForm : Form
{
    private Engine? engine; private TransferServer? server;
    private System.Security.Cryptography.X509Certificates.X509Certificate2? identity;
    private readonly Label status = new() { AutoSize = true, Text = "Starting encrypted local host…", ForeColor = Color.DimGray };
    private readonly ComboBox addresses = new() { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox invitation = new() { ReadOnly = true, Multiline = true, Height = 70, Dock = DockStyle.Fill, WordWrap = true };
    private readonly Label code = new() { AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
    private readonly ListView devices = new() { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill, Height = 135 };
    private readonly ListView transfers = new() { View = View.Details, FullRowSelect = true, MultiSelect = true, Dock = DockStyle.Fill };
    private readonly ComboBox scheduling = new() { Width = 210, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Dictionary<string, (long bytes, DateTime time, double mbps)> samples = new();
    private bool closing;
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ebira");
    public MainForm()
    {
        Text = "Ebira · Local file transfer · v1 candidate"; MinimumSize = new Size(900, 780); Size = new Size(1080, 920); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(247, 249, 251);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 10 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label { Text = "Ebira", Font = new Font("Segoe UI", 27, FontStyle.Bold), AutoSize = true }, 0, 0); layout.Controls.Add(status, 0, 1);
        var connect = Row(); connect.Controls.Add(new Label { Text = "1  Connect Android on the same Wi-Fi", AutoSize = true, Margin = new Padding(0, 7, 16, 0) }); connect.Controls.Add(addresses);
        connect.Controls.Add(Button("Copy invitation", () => { if (invitation.Text.Length > 0) Clipboard.SetText(invitation.Text); })); layout.Controls.Add(connect, 0, 2); layout.Controls.Add(invitation, 0, 3);
        var pair = Row(); pair.Controls.Add(code); pair.Controls.Add(Button("New pairing code", () => { engine?.RotateCode(); UpdateInvitation(); RefreshLists(); })); layout.Controls.Add(pair, 0, 4);
        devices.Columns.Add("2  Approve and check recipients", 330); devices.Columns.Add("Session", 150); devices.Columns.Add("Expires", 170); layout.Controls.Add(devices, 0, 5);
        var actions = Row(); actions.Controls.Add(Button("Approve selected", () => DeviceAction(true))); actions.Controls.Add(Button("End selected session", () => DeviceAction(false)));
        actions.Controls.Add(Button("3  Add files…", async () => await AddFiles()));
        scheduling.Items.AddRange(["Sequential recipients", "Round-robin · per file"]); scheduling.SelectedIndex = 0;
        scheduling.SelectedIndexChanged += (_, _) => engine?.SetScheduling(scheduling.SelectedIndex == 0 ? "sequential" : "round-robin"); actions.Controls.Add(scheduling); layout.Controls.Add(actions, 0, 6);
        foreach (var col in new[] { ("File", 240), ("Device", 140), ("Direction", 80), ("State", 90), ("Progress", 140), ("Mbps", 70), ("Details", 320) }) transfers.Columns.Add(col.Item1, col.Item2);
        layout.Controls.Add(transfers, 0, 7);
        var queue = Row(); foreach (var a in new[] { "Pause", "Resume", "Cancel" }) { var action = a.ToLowerInvariant(); queue.Controls.Add(Button(a, () => TransferAction(action))); }
        queue.Controls.Add(Button("Open received files", () => { if (engine != null) Process.Start(new ProcessStartInfo("explorer.exe", engine.ReceiveRoot) { UseShellExecute = true }); }));
        queue.Controls.Add(Button("Clear completed history", () => engine?.ClearHistory())); queue.Controls.Add(Button("Clean cancelled files", async () => { if (engine != null) await engine.CleanupCancelled(); })); layout.Controls.Add(queue, 0, 8);
        layout.Controls.Add(new Label { Text = "60 GB per file · TLS with certificate pinning · SHA-256 verification\nKeep Windows awake. Use a trusted private network; speeds depend on your devices and router.", AutoSize = true, ForeColor = Color.DimGray }, 0, 9);
        Controls.Add(layout); addresses.SelectedIndexChanged += (_, _) => UpdateInvitation();
        Shown += async (_, _) => await Start(); timer.Tick += (_, _) => RefreshLists();
        FormClosing += async (_, e) => { if (closing || server == null) { identity?.Dispose(); return; } e.Cancel = true; closing = true; timer.Stop(); Enabled = false; await server.DisposeAsync(); server = null; identity?.Dispose(); Close(); };
    }
    private static FlowLayoutPanel Row() => new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
    private Button Button(string label, Action action)
    {
        var b = new Button { Text = label, AutoSize = true, Height = 32, Padding = new Padding(6, 0, 6, 0), FlatStyle = FlatStyle.System };
        b.Click += (_, _) => { try { action(); } catch (Exception e) { MessageBox.Show(this, e.Message, "Ebira"); } }; return b;
    }
    private async Task Start()
    {
        try
        {
            engine = await Task.Run(() => new Engine(root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Ebira"), new WindowsProtector()));
            var cert = identity = WindowsProtector.Certificate(root);
            try { server = await TransferServer.Start(engine, cert); }
            catch (IOException) { server = await TransferServer.Start(engine, cert, 0); }
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
                foreach (var address in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))) addresses.Items.Add(new Endpoint(nic.Name, $"https://{address.Address}:{server.Port}"));
            if (addresses.Items.Count > 0) addresses.SelectedIndex = 0;
            scheduling.SelectedIndex = engine.State.Scheduling == "round-robin" ? 1 : 0;
            status.Text = $"Encrypted host listening on port {server.Port}. Allow Ebira through Windows Firewall on private networks.";
            UpdateInvitation(); timer.Start(); RefreshLists();
        }
        catch (Exception e) { status.Text = "Could not start. " + e.Message; MessageBox.Show(this, e.ToString(), "Ebira startup error"); }
    }
    private void UpdateInvitation()
    {
        if (server == null || engine == null) return;
        code.Text = "Pairing code  " + engine.PairCode;
        invitation.Text = addresses.SelectedItem is not Endpoint endpoint ? "Connect Windows to Wi-Fi, then restart Ebira." : $"ebira://pair?host={Uri.EscapeDataString(endpoint.Url)}&pin={server.Fingerprint}&code={engine.PairCode}";
    }
    private void DeviceAction(bool approve)
    {
        if (engine == null) return; foreach (ListViewItem row in devices.SelectedItems) engine.Session((string)row.Tag!, approve); RefreshLists();
    }
    private void TransferAction(string action)
    {
        if (engine == null) return; foreach (ListViewItem row in transfers.SelectedItems) engine.Control((string)row.Tag!, action); RefreshLists();
    }
    private async Task AddFiles()
    {
        if (engine == null) return;
        var ids = devices.CheckedItems.Cast<ListViewItem>().Select(r => (string)r.Tag!).ToArray();
        if (ids.Length == 0) { MessageBox.Show(this, "Approve and check at least one recipient first."); return; }
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose files to send directly" }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { foreach (var file in dialog.FileNames) { status.Text = "Hashing " + Path.GetFileName(file) + "…"; await engine.Offer(file, ids); } status.Text = "Files queued. The selected Android device receives while its transfer service is running."; }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Could not add files"); }
        RefreshLists();
    }
    private void RefreshLists()
    {
        if (engine == null || IsDisposed) return;
        engine.ExpireSessions();
        lock (engine.Sync)
        {
            SyncRows(devices, engine.State.Devices.Where(d => d.State != "ended").Select(d => (d.Id, new[] { d.Name, d.Expires <= DateTimeOffset.UtcNow ? "expired" : d.State, d.Expires.LocalDateTime.ToString("g") })));
            var now = DateTime.UtcNow;
            SyncRows(transfers, engine.State.Transfers.Select(t =>
            {
                double speed = 0;
                if (samples.TryGetValue(t.Id, out var old) && now > old.time) speed = Math.Max(0, (t.Offset - old.bytes) * 8 / (now - old.time).TotalSeconds / 1_000_000);
                samples[t.Id] = (t.Offset, now, speed);
                return (t.Id, new[] { t.Name, engine.State.Devices.FirstOrDefault(d => d.Id == t.DeviceId)?.Name ?? "Ended session", t.Direction == "send" ? "→ Android" : "← Android", t.State, $"{t.Offset / 1e6:0.0} / {t.Size / 1e6:0.0} MB", speed.ToString("0.0"), t.Error.Length > 0 ? t.Error : t.State == "complete" ? "SHA-256 verified" : "" });
            }));
        }
    }
    private static void SyncRows(ListView view, IEnumerable<(string id, string[] cells)> rows)
    {
        var list = rows.ToList(); var ids = list.Select(r => r.id).ToHashSet(); view.BeginUpdate();
        foreach (var item in view.Items.Cast<ListViewItem>().Where(i => !ids.Contains((string)i.Tag!)).ToList()) view.Items.Remove(item);
        foreach (var (id, cells) in list)
        {
            var item = view.Items.Cast<ListViewItem>().FirstOrDefault(i => (string)i.Tag! == id);
            if (item == null) { item = new ListViewItem(cells) { Tag = id }; view.Items.Add(item); }
            else for (var i = 0; i < cells.Length; i++) item.SubItems[i].Text = cells[i];
        }
        view.EndUpdate();
    }
    private sealed record Endpoint(string Name, string Url) { public override string ToString() => $"{Name} · {Url}"; }
}

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
    private readonly ComboBox addresses = new FittingComboBox() { Width = 420, DropDownWidth = 650, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox invitation = new() { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, WordWrap = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label code = new() { AutoSize = true, Text = "Pairing code  —", Font = new Font("Segoe UI", 16, FontStyle.Bold) };
    private readonly Label networkNote = new() { AutoSize = true, ForeColor = Color.DimGray, Text = "Choose the adapter on the same local network as Android. VPNs can affect local connectivity." };
    private readonly ListView devices = new() { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill, Height = 135 };
    private readonly ListView transfers = new() { View = View.Details, FullRowSelect = true, MultiSelect = true, Dock = DockStyle.Fill };
    private readonly ComboBox scheduling = new FittingComboBox() { Width = 210, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Dictionary<string, (long bytes, DateTime time, double mbps)> samples = new();
    private bool closing;
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ebira");
    public MainForm()
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Ebira · Local file transfer · v1 candidate 2"; MinimumSize = new Size(600, 500); Size = new Size(1000, 850); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(247, 249, 251);
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Name = "viewport" };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), ColumnCount = 1, Name = "content" };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control) { var row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(control, 0, row); }
        Add(new Label { Text = "Ebira", Font = new Font("Segoe UI", 27, FontStyle.Bold), AutoSize = true }); Add(status);
        Add(new Label { Text = "1  Connect Android on the same Wi-Fi", AutoSize = true });
        var connect = Row(); connect.Controls.Add(addresses);
        connect.Controls.Add(Button("Copy invitation", () => { if (invitation.Text.Length > 0) Clipboard.SetText(invitation.Text); })); Add(connect); Add(networkNote); Add(invitation);
        var pair = Row(); pair.Controls.Add(code); pair.Controls.Add(Button("New pairing code", () => { engine?.RotateCode(); UpdateInvitation(); RefreshLists(); })); Add(pair);
        devices.Height = 125; devices.MinimumSize = new Size(0, 125);
        devices.Columns.Add("2  Approve and check recipients", 330); devices.Columns.Add("Session", 150); devices.Columns.Add("Expires", 170); Add(devices);
        var actions = Row(); actions.Controls.Add(Button("Approve selected", () => DeviceAction(true))); actions.Controls.Add(Button("End selected session", () => DeviceAction(false)));
        actions.Controls.Add(Button("3  Add files…", async () => await AddFiles()));
        scheduling.Items.AddRange(["Sequential recipients", "Round-robin · per file"]); scheduling.SelectedIndex = 0;
        scheduling.SelectedIndexChanged += (_, _) => engine?.SetScheduling(scheduling.SelectedIndex == 0 ? "sequential" : "round-robin"); actions.Controls.Add(scheduling); Add(actions);
        foreach (var col in new[] { ("File", 240), ("Device", 140), ("Direction", 80), ("State", 90), ("Progress", 140), ("Mbps", 70), ("Details", 320) }) transfers.Columns.Add(col.Item1, col.Item2);
        transfers.Height = 220; transfers.MinimumSize = new Size(0, 220); Add(transfers);
        var queue = Row(); foreach (var a in new[] { "Pause", "Resume", "Cancel" }) { var action = a.ToLowerInvariant(); queue.Controls.Add(Button(a, () => TransferAction(action))); }
        queue.Controls.Add(Button("Open received files", () => { if (engine != null) Process.Start(new ProcessStartInfo("explorer.exe", engine.ReceiveRoot) { UseShellExecute = true }); }));
        queue.Controls.Add(Button("Clear completed history", () => engine?.ClearHistory())); queue.Controls.Add(Button("Clean cancelled files", async () => { try { if (engine != null) await engine.CleanupCancelled(); } catch (Exception e) { MessageBox.Show(this, e.Message, "Ebira"); } })); Add(queue);
        Add(new Label { Text = "60 GB per file · TLS with certificate pinning · SHA-256 verification\nKeep Windows awake. Use a trusted private network; speeds depend on your devices and router.", AutoSize = true, ForeColor = Color.DimGray });
        viewport.Controls.Add(layout); Controls.Add(viewport);
        void FitContent()
        {
            var width = Math.Max(1, viewport.ClientSize.Width);
            var contentWidth = Math.Max(100, width - layout.Padding.Horizontal - 12);
            layout.MaximumSize = new Size(width, 0);
            foreach (var label in layout.Controls.OfType<Label>().Append(code)) label.MaximumSize = new Size(contentWidth, 0);
            addresses.Width = Math.Min(contentWidth, (int)(420 * DeviceDpi / 96f));
            var scheduleWidth = TextRenderer.MeasureText("Round-robin · per file", scheduling.Font).Width + SystemInformation.VerticalScrollBarWidth + 16;
            scheduling.Width = Math.Min(contentWidth, Math.Max((int)(210 * DeviceDpi / 96f), scheduleWidth));
            scheduling.DropDownWidth = Math.Max(scheduling.Width, scheduleWidth);
            invitation.MinimumSize = new Size(0, invitation.Font.Height * 3 + 12);
            invitation.Height = invitation.MinimumSize.Height;
        }
        viewport.ClientSizeChanged += (_, _) => FitContent(); FontChanged += (_, _) => FitContent(); FitContent();
        scheduling.FontChanged += (_, _) => FitContent();
        foreach (var list in new[] { devices, transfers })
        {
            void FitHeaders() { if (!list.IsHandleCreated) return; using var graphics = list.CreateGraphics(); foreach (ColumnHeader column in list.Columns) column.Width = Math.Max(column.Width, TextRenderer.MeasureText(graphics, column.Text, list.Font).Width + (int)(20 * list.DeviceDpi / 96f)); }
            list.HandleCreated += (_, _) => FitHeaders(); list.FontChanged += (_, _) => FitHeaders(); list.DpiChangedAfterParent += (_, _) => FitHeaders();
        }
        addresses.SelectedIndexChanged += (_, _) => { UpdateInvitation(); UpdateNetworkNote(); };
        Shown += async (_, _) => await Start(); timer.Tick += (_, _) => RefreshLists();
        FormClosing += async (_, e) => { if (closing || server == null) { identity?.Dispose(); return; } e.Cancel = true; closing = true; timer.Stop(); Enabled = false; await server.DisposeAsync(); server = null; identity?.Dispose(); Close(); };
    }
    private static FlowLayoutPanel Row() => new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 4, 0, 4) };
    private Button Button(string label, Action action)
    {
        var b = new Button { Text = label, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 4), FlatStyle = FlatStyle.System };
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
            var endpoints = new List<Endpoint>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                try
                {
                    var properties = nic.GetIPProperties();
                    var isVirtual = NetworkSelection.IsVirtual(nic.NetworkInterfaceType, nic.Name, nic.Description);
                    var rank = NetworkSelection.Rank(nic.NetworkInterfaceType, nic.Name, nic.Description, properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));
                    foreach (var address in properties.UnicastAddresses.Where(a => NetworkSelection.IsLocalIpv4(a.Address))) endpoints.Add(new Endpoint(nic.Name, $"https://{address.Address}:{server.Port}", isVirtual, rank));
                }
                catch (NetworkInformationException) { /* An adapter may disappear while its properties are read. */ }
            }
            foreach (var endpoint in endpoints.OrderBy(e => e.Rank).ThenBy(e => e.Name)) addresses.Items.Add(endpoint);
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
    private void UpdateNetworkNote()
    {
        var virtualSelected = addresses.SelectedItem is Endpoint { Virtual: true };
        networkNote.ForeColor = virtualSelected ? Color.DarkGoldenrod : Color.DimGray;
        networkNote.Text = virtualSelected ? "VPN/virtual adapter selected. Choose the Wi-Fi or Ethernet network shared with Android." : "Choose the adapter on the same local network as Android. VPNs can affect local connectivity.";
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
    private sealed record Endpoint(string Name, string Url, bool Virtual, int Rank) { public override string ToString() => $"{Name}{(Virtual ? " [VPN/virtual]" : "")} · {Url}"; }
    private sealed class FittingComboBox : ComboBox
    {
        public FittingComboBox() { AutoSize = true; }
        // Native combo height can exceed its cached preferred size after a font/DPI
        // change. Report the actual height so wrapped rows reserve enough room.
        public override Size GetPreferredSize(Size proposedSize) => new(Width, Math.Max(Height, PreferredHeight));
    }
}

using System.Security.Cryptography;

namespace Ebira.Core;

public sealed class Engine
{
    public readonly object Sync = new();
    public State State { get; }
    public string ReceiveRoot { get; }
    public string PairCode { get; private set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
    private readonly Store store;
    public event Action? Changed;
    public Engine(string dataRoot, string receiveRoot, ISecretProtector protector)
    {
        ReceiveRoot = Path.GetFullPath(receiveRoot); Directory.CreateDirectory(ReceiveRoot);
        store = new Store(dataRoot, protector); State = store.Read();
        lock (Sync)
        {
            foreach (var d in State.Devices.Where(d => d.State == "pending" || d.Expires <= DateTimeOffset.UtcNow)) d.State = "ended";
            foreach (var t in State.Transfers.Where(t => t.State is not ("complete" or "cancelled")))
            {
                if (!State.Devices.Any(d => d.Id == t.DeviceId && d.State == "approved")) { t.State = "cancelled"; t.Lifetime.Cancel(); continue; }
                // Only offsets durably acknowledged in state are trusted after a crash.
                if (t.Direction == "receive")
                {
                    if (t.FinalPath != null && File.Exists(t.FinalPath) && !File.Exists(t.Path))
                    {
                        if (new FileInfo(t.FinalPath).Length == t.Size && Protocol.HashFile(t.FinalPath).GetAwaiter().GetResult() == t.Sha256) { t.Path = t.FinalPath; t.State = "complete"; t.Offset = t.Size; continue; }
                        t.State = "failed"; t.Error = "Final file changed; inspect it before retrying."; continue;
                    }
                    if (!File.Exists(t.Path)) { t.State = "failed"; t.Error = "Partial file missing"; continue; }
                    using var f = new FileStream(t.Path, FileMode.Open, FileAccess.Write);
                    t.Offset = Math.Min(t.Offset, f.Length); f.SetLength(t.Offset); f.Flush(true);
                }
                t.State = "paused";
            }
            Save();
        }
    }
    public void Save() { lock (Sync) store.Write(State); Changed?.Invoke(); }
    public void RotateCode() { lock (Sync) { PairCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)); foreach (var d in State.Devices.Where(d => d.State == "pending")) d.State = "ended"; Save(); } }
    public Device Join(string code, string name)
    {
        lock (Sync)
        {
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(code.ToUpperInvariant()), System.Text.Encoding.UTF8.GetBytes(PairCode))) throw new UnauthorizedAccessException("Incorrect pairing code");
            if (State.Devices.Count(d => d.State != "ended" && d.Expires > DateTimeOffset.UtcNow) >= 20) throw new InvalidOperationException("End an existing session before pairing another device");
            State.Devices.RemoveAll(d => d.State == "ended");
            var d = new Device { Name = Protocol.SafeName(name), Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() };
            State.Devices.Add(d); Save(); return d;
        }
    }
    public Device Authenticate(string token, bool pending = false)
    {
        lock (Sync)
        {
            var d = State.Devices.FirstOrDefault(d => d.Token == token && d.Expires > DateTimeOffset.UtcNow && (d.State == "approved" || pending && d.State == "pending"));
            return d ?? throw new UnauthorizedAccessException("Session ended or approval required. Pair again.");
        }
    }
    public void Session(string id, bool approve)
    {
        lock (Sync)
        {
            var d = State.Devices.First(d => d.Id == id);
            if (approve && (d.State != "pending" || d.Expires <= DateTimeOffset.UtcNow)) throw new InvalidOperationException("Pair again to create a new session");
            d.State = approve ? "approved" : "ended";
            if (!approve) foreach (var t in State.Transfers.Where(t => t.DeviceId == id && t.State is not ("complete" or "cancelled"))) { t.State = "cancelled"; t.Lifetime.Cancel(); }
            Schedule(); Save();
        }
    }
    public void SetScheduling(string mode) { lock (Sync) { if (mode is not ("sequential" or "round-robin")) throw new ArgumentException("Unknown scheduling mode"); State.Scheduling = mode; Save(); } }
    public void ExpireSessions()
    {
        lock (Sync)
        {
            var expired = State.Devices.Where(d => d.State != "ended" && d.Expires <= DateTimeOffset.UtcNow).ToList();
            if (expired.Count == 0) return;
            foreach (var d in expired)
            {
                d.State = "ended";
                foreach (var t in State.Transfers.Where(t => t.DeviceId == d.Id && t.State is not ("complete" or "cancelled"))) { t.State = "cancelled"; t.Lifetime.Cancel(); }
            }
            Schedule(); Save();
        }
    }
    public void Schedule()
    {
        lock (Sync)
        {
            if (State.Transfers.Any(t => t.Direction == "send" && t.State is "active" or "verifying")) return;
            var candidates = State.Transfers.Where(t => t.Direction == "send" && t.State == "queued" && State.Devices.Any(d => d.Id == t.DeviceId && d.State == "approved" && d.Expires > DateTimeOffset.UtcNow)).ToList();
            if (candidates.Count == 0) return;
            Transfer next;
            if (State.Scheduling == "sequential") next = candidates.FirstOrDefault(t => t.DeviceId == State.LastRecipient) ?? candidates[0];
            else
            {
                var recipients = State.Devices.Select(d => d.Id).ToList(); var start = recipients.IndexOf(State.LastRecipient);
                next = candidates[0];
                for (var i = 1; i <= recipients.Count; i++) { var id = recipients[(start + i + recipients.Count) % recipients.Count]; var found = candidates.FirstOrDefault(t => t.DeviceId == id); if (found != null) { next = found; break; } }
            }
            next.State = "active"; State.LastRecipient = next.DeviceId;
        }
    }
    public async Task<List<Transfer>> Offer(string source, IEnumerable<string> recipients, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(source); var info = new FileInfo(full);
        if (!info.Exists || info.Length > Protocol.MaxFile) throw new ArgumentException("Choose an existing file of at most 60 GB");
        var size = info.Length; var changed = info.LastWriteTimeUtc;
        var hash = await Protocol.HashFile(full, ct);
        info.Refresh(); if (info.Length != size || info.LastWriteTimeUtc != changed) throw new IOException("Source changed while hashing; choose it again");
        lock (Sync)
        {
            var ids = recipients.Distinct().ToList(); if (ids.Count == 0) throw new ArgumentException("Choose at least one approved recipient");
            foreach (var id in ids) if (!State.Devices.Any(d => d.Id == id && d.State == "approved" && d.Expires > DateTimeOffset.UtcNow)) throw new InvalidOperationException("Recipient session ended");
            CheckQueue(ids.Count);
            var added = ids.Select(id => new Transfer { DeviceId = id, Name = Protocol.SafeName(info.Name), Size = size, Sha256 = hash, Path = full, Direction = "send" }).ToList();
            State.Transfers.AddRange(added); Schedule(); Save(); return added;
        }
    }
    private void CheckQueue(int count = 1) { if (State.Transfers.Count + count > 10000) throw new InvalidOperationException("History limit reached. Clear completed history."); }
    public Transfer Incoming(Device d, string id, string name, long size, string sha256)
    {
        lock (Sync)
        {
            Authenticate(d.Token);
            if (id.Length != 32 || !id.All(Uri.IsHexDigit) || size < 0 || size > Protocol.MaxFile || !Protocol.ValidHash(sha256)) throw new ArgumentException("Invalid file metadata (limit 60 GB)");
            id = id.ToLowerInvariant(); sha256 = sha256.ToLowerInvariant();
            var existing = State.Transfers.FirstOrDefault(t => t.Id == id);
            if (existing != null)
            {
                if (existing.DeviceId != d.Id || existing.Direction != "receive" || existing.Size != size || existing.Sha256 != sha256 || existing.Name != Protocol.SafeName(name)) throw new InvalidOperationException("Transfer identity does not match");
                return existing;
            }
            CheckQueue();
            var drive = new DriveInfo(Path.GetPathRoot(ReceiveRoot)!);
            var reserved = State.Transfers.Where(t => t.Direction == "receive" && t.State is not ("complete" or "cancelled")).Sum(t => t.Size - t.Offset);
            if (drive.AvailableFreeSpace - reserved < size + 16 * 1024 * 1024) throw new IOException("Not enough free disk space for queued receive files");
            var dir = Path.Combine(ReceiveRoot, ".partial"); Directory.CreateDirectory(dir);
            var t = new Transfer { Id = id, DeviceId = d.Id, Name = Protocol.SafeName(name), Size = size, Sha256 = sha256.ToLowerInvariant(), Path = Path.Combine(dir, id + ".part"), Direction = "receive", State = "active" };
            using (new FileStream(t.Path, FileMode.CreateNew, FileAccess.Write)) { } State.Transfers.Add(t); Save(); return t;
        }
    }
    public Transfer Get(Device d, string id)
    {
        lock (Sync) { Authenticate(d.Token); return State.Transfers.FirstOrDefault(t => t.Id == id.ToLowerInvariant() && t.DeviceId == d.Id) ?? throw new KeyNotFoundException("Transfer not found"); }
    }
    public void Control(string id, string action)
    {
        lock (Sync)
        {
            var t = State.Transfers.First(t => t.Id == id);
            if (t.State is "complete" or "cancelled" || t.State == "verifying") return;
            if (action == "cancel") { t.State = "cancelled"; t.Lifetime.Cancel(); }
            else if (action == "pause") t.State = "paused";
            else if (action == "resume") { if (!State.Devices.Any(d => d.Id == t.DeviceId && d.State == "approved" && d.Expires > DateTimeOffset.UtcNow)) throw new InvalidOperationException("Pair again before resuming"); t.State = t.Direction == "send" ? "queued" : "active"; t.Error = ""; }
            else throw new ArgumentException("Unknown action");
            t.Updated = DateTimeOffset.UtcNow; Schedule(); Save();
        }
    }
    public void ClearHistory()
    {
        lock (Sync) { State.Transfers.RemoveAll(t => t.State == "complete"); Save(); }
    }
    public async Task CleanupCancelled()
    {
        List<Transfer> cancelled; lock (Sync) cancelled = State.Transfers.Where(t => t.State == "cancelled").ToList();
        foreach (var t in cancelled)
        {
            await t.Gate.WaitAsync();
            try { lock (Sync) { if (t.Direction == "receive" && File.Exists(t.Path) && t.Path.EndsWith(".part")) File.Delete(t.Path); State.Transfers.Remove(t); Save(); } }
            finally { t.Gate.Release(); }
        }
    }
    public async Task WriteChunk(Device d, Transfer t, long offset, byte[] bytes, string hash, CancellationToken ct)
    {
        await t.Gate.WaitAsync(ct);
        try
        {
            lock (Sync) { Authenticate(d.Token); if (t.Direction != "receive" || t.State != "active") throw new InvalidOperationException("Transfer is not receiving"); if (offset != t.Offset) throw new InvalidOperationException("Offset mismatch"); if (bytes.Length == 0 || bytes.Length > Protocol.ChunkSize || offset + bytes.Length > t.Size) throw new ArgumentException("Invalid chunk size"); }
            if (!Protocol.ValidHash(hash) || Protocol.Hash(bytes) != hash.ToLowerInvariant()) throw new ArgumentException("Chunk checksum mismatch");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, t.Lifetime.Token);
            await using var stream = new FileStream(t.Path, FileMode.Open, FileAccess.Write, FileShare.Read, 1024 * 1024, true);
            stream.Position = offset; await stream.WriteAsync(bytes, linked.Token); stream.Flush(true);
            lock (Sync) { Authenticate(d.Token); if (t.State == "cancelled") throw new OperationCanceledException(); t.Offset += bytes.Length; t.Updated = DateTimeOffset.UtcNow; Save(); }
        }
        finally { t.Gate.Release(); }
    }
    public async Task FinishIncoming(Device d, Transfer t, CancellationToken ct)
    {
        await t.Gate.WaitAsync(ct);
        try
        {
            lock (Sync) { Authenticate(d.Token); if (t.Direction != "receive") throw new InvalidOperationException("Wrong direction"); if (t.State == "complete") return; if (t.Offset != t.Size || t.State != "active") throw new InvalidOperationException("Upload incomplete or paused"); t.State = "verifying"; Save(); }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, t.Lifetime.Token);
            var hash = await Protocol.HashFile(t.Path, linked.Token);
            lock (Sync)
            {
                Authenticate(d.Token); if (t.State == "cancelled") throw new OperationCanceledException();
                if (hash != t.Sha256) { t.State = "failed"; t.Error = "SHA-256 mismatch. Cancel this transfer and select the source again."; Save(); throw new InvalidDataException(t.Error); }
                var folder = Path.Combine(ReceiveRoot, Protocol.Category(t.Name)); Directory.CreateDirectory(folder);
                var ext = Path.GetExtension(t.Name); var stem = Path.GetFileNameWithoutExtension(t.Name);
                for (var i = 0; ; i++)
                {
                    var dest = Path.Combine(folder, i == 0 ? t.Name : $"{stem} ({i}){ext}");
                    t.FinalPath = dest; Save(); // Journal destination before atomic rename.
                    try { File.Move(t.Path, dest, false); t.Path = dest; break; } catch (IOException) when (File.Exists(dest)) { }
                }
                t.State = "complete"; t.Updated = DateTimeOffset.UtcNow; Save();
            }
        }
        catch { lock (Sync) { if (t.State == "verifying") { t.State = "active"; Save(); } } throw; }
        finally { t.Gate.Release(); }
    }
    public async Task<byte[]> ReadChunk(Device d, Transfer t, long offset, CancellationToken ct)
    {
        await t.Gate.WaitAsync(ct);
        try
        {
            lock (Sync) { Authenticate(d.Token); if (t.Direction != "send" || t.State != "active" || offset < 0 || offset > t.Size) throw new InvalidOperationException("Transfer unavailable or invalid offset"); }
            await using var source = new FileStream(t.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            if (source.Length != t.Size) throw new IOException("Source file changed; cancel and select it again");
            source.Position = offset; var bytes = new byte[(int)Math.Min(Protocol.ChunkSize, t.Size - offset)]; await source.ReadExactlyAsync(bytes, ct);
            lock (Sync) { Authenticate(d.Token); if (t.State != "active") throw new OperationCanceledException(); }
            return bytes;
        }
        finally { t.Gate.Release(); }
    }
    public void Acknowledge(Device d, Transfer t, long offset, string? hash)
    {
        lock (Sync)
        {
            Authenticate(d.Token); if (t.Direction != "send") throw new InvalidOperationException("Wrong direction");
            if (t.State == "complete" && offset == t.Size && hash == t.Sha256) return;
            if (t.State != "active" || offset < t.Offset || offset > t.Size || offset - t.Offset > Protocol.ChunkSize) throw new InvalidOperationException("Invalid acknowledgement");
            if (hash != null && (offset != t.Size || hash != t.Sha256)) throw new InvalidDataException("Receiver checksum mismatch");
            t.Offset = offset; t.Updated = DateTimeOffset.UtcNow;
            if (hash != null) t.State = "complete"; Schedule(); Save();
        }
    }
}

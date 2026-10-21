using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ebira.Core;

public static class Protocol
{
    public const long MaxFile = 60_000_000_000;
    public const int ChunkSize = 8 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static string Id() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static async Task<string> HashFile(string file, CancellationToken ct = default)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }
    public static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static string SafeName(string? name)
    {
        var clean = new string((name ?? "file").Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (clean.Length > 160) clean = clean[..160];
        if (string.IsNullOrWhiteSpace(clean)) clean = "file";
        var stem = clean.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) clean = "_" + clean;
        return clean;
    }
    public static string Category(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".heic" or ".avif" => "Pictures",
        ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" => "Videos",
        ".mp3" or ".m4a" or ".wav" or ".aac" or ".flac" or ".ogg" => "Music",
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".csv" or ".odt" => "Documents",
        _ => "Other"
    };
}

public sealed class Device
{
    public string Id { get; set; } = Protocol.Id();
    public string Name { get; set; } = "Android";
    public string Token { get; set; } = "";
    public string State { get; set; } = "pending";
    public DateTimeOffset Expires { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
}
public sealed class Transfer
{
    public string Id { get; set; } = Protocol.Id();
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "file";
    public long Size { get; set; }
    public long Offset { get; set; }
    public string Sha256 { get; set; } = "";
    public string Direction { get; set; } = "receive";
    public string State { get; set; } = "queued";
    public string Path { get; set; } = "";
    public string? FinalPath { get; set; }
    public string Error { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public SemaphoreSlim Gate { get; } = new(1, 1);
    [JsonIgnore] public CancellationTokenSource Lifetime { get; } = new();
    public object Public() => new { Id, DeviceId, Name, Size, Offset, Sha256, Direction, State, Error, Updated };
}
public sealed class State
{
    public string Scheduling { get; set; } = "sequential";
    public List<Device> Devices { get; set; } = [];
    public List<Transfer> Transfers { get; set; } = [];
    public string LastRecipient { get; set; } = "";
}
public interface ISecretProtector
{
    byte[] Protect(byte[] bytes);
    byte[] Unprotect(byte[] bytes);
}

public sealed class Store(string root, ISecretProtector protector)
{
    private readonly string file = System.IO.Path.Combine(root, "state.bin");
    public State Read()
    {
        Directory.CreateDirectory(root);
        if (!File.Exists(file)) return new();
        return JsonSerializer.Deserialize<State>(protector.Unprotect(File.ReadAllBytes(file)), Protocol.Json) ?? throw new IOException("Invalid local state");
    }
    public void Write(State state)
    {
        var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state, Protocol.Json));
        var temp = file + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        File.Move(temp, file, true);
    }
}

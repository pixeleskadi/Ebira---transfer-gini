using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Ebira.Core;

var root = Path.Combine(Path.GetTempPath(), "ebira-native-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root); int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); passed++; }
void Reject(Action action, string message) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }
async Task RejectAsync(Func<Task> action, string message) { bool rejected = false; try { await action(); } catch { rejected = true; } Check(rejected, message); }
var protector = new TestProtector();
try
{
    var data = Path.Combine(root, "state"); var received = Path.Combine(root, "received"); var engine = new Engine(data, received, protector);
    Check(Protocol.MaxFile == 60_000_000_000L && Protocol.ChunkSize == 8 * 1024 * 1024, "60 GB decimal per-file limit and 8 MiB chunk contract");
    Check(Protocol.SafeName("../CON:bad.txt").Contains('/') == false && Protocol.SafeName("CON.txt") == "_CON.txt" && Protocol.SafeName("...") == "file", "path traversal and Windows reserved filename handling");
    foreach (var pair in new[] { ("a.JPG", "Pictures"), ("a.mkv", "Videos"), ("a.flac", "Music"), ("a.pdf", "Documents"), ("a.zip", "Other") }) Check(Protocol.Category(pair.Item1) == pair.Item2, "category: " + pair.Item1);
    Reject(() => engine.Join("bad", "attacker"), "wrong pairing code rejected");
    var phone = engine.Join(engine.PairCode, "Test phone"); Reject(() => engine.Authenticate(phone.Token), "pending device cannot transfer"); engine.Session(phone.Id, true);
    var tablet = engine.Join(engine.PairCode, "Test tablet"); engine.Session(tablet.Id, true);
    var bytes = RandomNumberGenerator.GetBytes(Protocol.ChunkSize + 73); var hash = Protocol.Hash(bytes);
    var incoming = engine.Incoming(phone, Protocol.Id(), "report.pdf", bytes.Length, hash);
    Check(engine.Incoming(phone, incoming.Id, "report.pdf", bytes.Length, hash).Id == incoming.Id, "upload registration is idempotent after response loss");
    Check(engine.Incoming(phone, incoming.Id.ToUpperInvariant(), "report.pdf", bytes.Length, hash.ToUpperInvariant()).Id == incoming.Id, "case-normalized ID cannot truncate an existing Windows partial");
    Reject(() => engine.Incoming(tablet, incoming.Id, "report.pdf", bytes.Length, hash), "cross-session transfer identity isolated");
    Reject(() => engine.Incoming(phone, Protocol.Id(), "huge", Protocol.MaxFile + 1, hash), "60 GB plus one byte rejected");
    await RejectAsync(() => engine.WriteChunk(phone, incoming, 0, bytes[..128], new string('0', 64), default), "bad chunk checksum rejected");
    var chunk = bytes[..Protocol.ChunkSize]; await engine.WriteChunk(phone, incoming, 0, chunk, Protocol.Hash(chunk), default);
    await RejectAsync(() => engine.WriteChunk(phone, incoming, 0, chunk, Protocol.Hash(chunk), default), "duplicate chunk offset rejected without corruption");
    // Simulate process death after unacknowledged bytes reached the receive file.
    using (var file = new FileStream(incoming.Path, FileMode.Append)) file.Write(new byte[] { 1, 2, 3 });
    engine = new Engine(data, received, protector); phone = engine.Authenticate(phone.Token); tablet = engine.Authenticate(tablet.Token); incoming = engine.Get(phone, incoming.Id);
    Check(incoming.Offset == Protocol.ChunkSize && new FileInfo(incoming.Path).Length == incoming.Offset && incoming.State == "paused", "restart restores durable offset and truncates uncommitted tail");
    engine.Control(incoming.Id, "resume"); var tail = bytes[Protocol.ChunkSize..]; await engine.WriteChunk(phone, incoming, incoming.Offset, tail, Protocol.Hash(tail), default); await engine.FinishIncoming(phone, incoming, default);
    Check(File.ReadAllBytes(Path.Combine(received, "Documents", "report.pdf")).SequenceEqual(bytes), "resumed incoming file is byte-identical and SHA-256 verified");
    await engine.FinishIncoming(phone, incoming, default); Check(incoming.State == "complete", "finish is idempotent");
    var empty = engine.Incoming(phone, Protocol.Id(), "report.pdf", 0, Protocol.Hash([])); await engine.FinishIncoming(phone, empty, default);
    Check(File.Exists(Path.Combine(received, "Documents", "report (1).pdf")), "zero-byte collision creates distinct file without overwriting");
    var journal = engine.Incoming(phone, Protocol.Id(), "journal.png", 0, Protocol.Hash([])); Directory.CreateDirectory(Path.Combine(received, "Pictures")); journal.FinalPath = Path.Combine(received, "Pictures", "journal.png"); engine.Save(); File.Move(journal.Path, journal.FinalPath);
    engine = new Engine(data, received, protector); phone = engine.Authenticate(phone.Token); tablet = engine.Authenticate(tablet.Token);
    Check(engine.Get(phone, journal.Id).State == "complete", "restart recovers a verified receive-file rename before metadata commit");
    var corrupt = engine.Incoming(phone, Protocol.Id(), "corrupt.zip", 1, Protocol.Hash([2])); await engine.WriteChunk(phone, corrupt, 0, [1], Protocol.Hash([1]), default);
    await RejectAsync(() => engine.FinishIncoming(phone, corrupt, default), "full-file mismatch cannot publish a completed file");
    Check(corrupt.State == "failed" && !File.Exists(Path.Combine(received, "Other", "corrupt.zip")), "failed integrity file remains partial"); engine.Control(corrupt.Id, "cancel"); await engine.CleanupCancelled(); Check(!File.Exists(corrupt.Path), "explicit cancellation cleanup removes partial payload");
    var source = Path.Combine(root, "source.bin"); await File.WriteAllBytesAsync(source, bytes);
    engine.SetScheduling("round-robin"); var offers1 = await engine.Offer(source, [phone.Id, tablet.Id]); var offers2 = await engine.Offer(source, [phone.Id, tablet.Id]);
    Check(offers1[0].State == "active" && offers1.Skip(1).Concat(offers2).All(t => t.State == "queued"), "only one outgoing recipient active");
    async Task Deliver(Device device, Transfer transfer)
    {
        var delivered = new List<byte>();
        for (long offset = 0; offset < transfer.Size;)
        {
            var part = await engine.ReadChunk(device, transfer, offset, default); delivered.AddRange(part); offset += part.Length; engine.Acknowledge(device, transfer, offset, null);
        }
        engine.Acknowledge(device, transfer, transfer.Size, Protocol.Hash(delivered.ToArray()));
        Check(delivered.SequenceEqual(bytes), "direct outgoing bytes match source");
    }
    await Deliver(phone, offers1[0]); Check(offers1[1].State == "active" && offers2[0].State == "queued", "round-robin switches recipients after each file");
    await Deliver(tablet, offers1[1]); Check(offers2[0].State == "active", "round-robin returns to first recipient");
    engine.Control(offers2[0].Id, "pause"); Check(offers2[1].State == "active", "paused recipient releases scheduling slot");
    await Deliver(tablet, offers2[1]); engine.Control(offers2[0].Id, "resume");
    engine.SetScheduling("sequential");
    var seq = await engine.Offer(source, [phone.Id, tablet.Id]); await Deliver(phone, offers2[0]); Check(seq[0].State == "active", "sequential retains recipient while it has queued files");
    engine.Session(phone.Id, false); await RejectAsync(() => engine.ReadChunk(phone, seq[0], 0, default), "revocation prevents further file reads");
    Check(seq[1].State == "active", "revocation releases recipient slot");
    // A source changed after selection never produces a successfully verified transfer.
    await File.AppendAllTextAsync(source, "changed"); await RejectAsync(() => engine.ReadChunk(tablet, seq[1], 0, default), "changed source length rejected"); engine.Control(seq[1].Id, "cancel");
    engine.ClearHistory(); Check(engine.State.Transfers.All(t => t.State != "complete"), "clear history retains completed receive files");
    Check(File.Exists(Path.Combine(received, "Documents", "report.pdf")), "received files survive history cleanup");
    var expiring = engine.Join(engine.PairCode, "Expiring phone"); engine.Session(expiring.Id, true); var expiredJob = (await engine.Offer(source, [expiring.Id]))[0]; expiring.Expires = DateTimeOffset.UtcNow.AddSeconds(-1); engine.ExpireSessions();
    Reject(() => engine.Authenticate(expiring.Token), "expired session cannot authenticate"); Check(expiredJob.State == "cancelled", "expiration releases an active recipient and cancels its jobs");

    // Exercise the actual HTTPS protocol, with a pinned self-signed certificate.
    using var rsa = RSA.Create(2048); var csr = new CertificateRequest("CN=Ebira test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    bool httpOnly = args.Contains("--loopback-http");
    using var generated = csr.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
    using var cert = new X509Certificate2(generated.Export(X509ContentType.Pfx), (string?)null, httpOnly ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.UserKeySet);
    await using var server = httpOnly ? await TransferServer.StartLoopbackForChecks(engine) : await TransferServer.Start(engine, cert, 0, true);
    using HttpMessageHandler handler = args.Contains("--node-tls") ? new NodeTlsHandler(server.Fingerprint) : new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c != null && Protocol.Hash(c.RawData) == server.Fingerprint };
    using var client = new HttpClient(handler) { BaseAddress = new Uri($"{(httpOnly ? "http" : "https")}://127.0.0.1:{server.Port}") };
    async Task<HttpResponseMessage> Send(string route, object? body = null, string? token = null, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? (body == null ? HttpMethod.Get : HttpMethod.Post), route); if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"); return await client.SendAsync(request);
    }
    Check((await Send("/v1/info")).IsSuccessStatusCode, httpOnly ? "API reachable over test-only loopback HTTP" : "HTTPS endpoint reachable with exact certificate pin");
    using HttpMessageHandler badHandler = args.Contains("--node-tls") ? new NodeTlsHandler(new string('0', 64)) : new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c != null && Protocol.Hash(c.RawData) == new string('0', 64) };
    using var badClient = new HttpClient(badHandler);
    if (httpOnly) Console.WriteLine("SKIP: Windows TLS handshake and certificate-pin checks (sandbox SSPI unavailable)");
    else await RejectAsync(async () => { await badClient.GetAsync(client.BaseAddress); }, "incorrect certificate pin fails TLS");
    Check((await Send("/v1/transfers")).StatusCode == HttpStatusCode.Unauthorized, "unauthenticated transfer listing rejected");
    var join = await Send("/v1/join", new { code = engine.PairCode, name = "Protocol phone" }); var jd = await join.Content.ReadFromJsonAsync<JsonElement>(); var token = jd.GetProperty("token").GetString()!; var deviceId = jd.GetProperty("id").GetString()!;
    Check((await Send("/v1/session", token: token)).IsSuccessStatusCode && (await Send("/v1/transfers", token: token)).StatusCode == HttpStatusCode.Unauthorized, "pending client can poll status but cannot access files"); engine.Session(deviceId, true);
    var uploadId = Protocol.Id(); var payload = RandomNumberGenerator.GetBytes(1_048_593);
    var reg = await Send("/v1/transfers", new { id = uploadId, name = "api.bin", size = payload.Length, sha256 = Protocol.Hash(payload) }, token); Check(reg.IsSuccessStatusCode, "HTTPS upload registration");
    var put = new HttpRequestMessage(HttpMethod.Put, $"/v1/transfers/{uploadId}/chunk?offset=0") { Content = new ByteArrayContent(payload) }; put.Headers.Authorization = new("Bearer", token); put.Headers.Add("X-Ebira-SHA256", Protocol.Hash(payload));
    Check((await client.SendAsync(put)).IsSuccessStatusCode, "HTTPS chunk upload");
    Check((await Send($"/v1/transfers/{uploadId}/finish", new { }, token)).IsSuccessStatusCode, "HTTPS full-file completion");
    Check((await Send($"/v1/transfers/{uploadId}", token: tablet.Token)).StatusCode == HttpStatusCode.NotFound, "HTTPS session isolation");
    var own = engine.Authenticate(token); var download = (await engine.Offer(Path.Combine(received, "Other", "api.bin"), [own.Id]))[0];
    var response = await Send($"/v1/transfers/{download.Id}/chunk?offset=0", token: token); var actual = await response.Content.ReadAsByteArrayAsync();
    Check(response.IsSuccessStatusCode && actual.SequenceEqual(payload) && response.Headers.GetValues("X-Ebira-SHA256").Single() == Protocol.Hash(payload), "HTTPS direct download is byte-identical with chunk hash");
    Check((await Send($"/v1/transfers/{download.Id}/ack", new { offset = payload.Length, sha256 = Protocol.Hash(payload) }, token)).IsSuccessStatusCode && download.State == "complete", "HTTPS verified receiver acknowledgement completes delivery");
    Check((await Send("/v1/end", new { }, token)).IsSuccessStatusCode && (await Send("/v1/transfers", token: token)).StatusCode == HttpStatusCode.Unauthorized, "HTTPS device can revoke its session");
    for (var i = 0; i < 4; i++) await Send("/v1/join", new { code = "bad", name = "test" });
    Check((await Send("/v1/join", new { code = "bad", name = "test" })).StatusCode == HttpStatusCode.TooManyRequests, "pairing endpoint rate limited");
    Console.WriteLine($"\n{passed} checks passed. This is local automated evidence; physical Android, 60 GB and 1 Gbps certification remain separate.");
    if (args.Contains("--android-transport")) await AndroidTransport.Run(root, generated);
}
catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
finally { try { Directory.Delete(root, true); } catch (Exception e) { Console.Error.WriteLine("Test cleanup: " + e.Message); Environment.ExitCode = 1; } }

sealed class TestProtector : ISecretProtector
{
    public byte[] Protect(byte[] bytes) => bytes; public byte[] Unprotect(byte[] bytes) => bytes;
}

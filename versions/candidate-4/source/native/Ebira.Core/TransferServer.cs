using System.Collections.Concurrent;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ebira.Core;

public sealed class TransferServer : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly SemaphoreSlim io = new(1, 1);
    public int Port { get; private set; }
    public string Fingerprint { get; }
    private TransferServer(WebApplication app, X509Certificate2? certificate) { this.app = app; Fingerprint = certificate == null ? "" : Protocol.Hash(certificate.RawData); }
    public static Task<TransferServer> Start(Engine engine, X509Certificate2 cert, int port = 8443, bool loopback = false) => StartTransport(engine, cert, port, loopback);
    internal static Task<TransferServer> StartLoopbackForChecks(Engine engine) => StartTransport(engine, null, 0, true);
    private static async Task<TransferServer> StartTransport(Engine engine, X509Certificate2? cert, int port, bool loopback)
    {
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false; k.Limits.MaxRequestBodySize = Protocol.ChunkSize;
            k.Limits.MaxConcurrentConnections = 32; k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            if (cert == null && !loopback) throw new InvalidOperationException("TLS is required for network transfers");
            k.Listen(loopback ? IPAddress.Loopback : IPAddress.Any, port, listen => { if (cert != null) listen.UseHttps(cert, h => h.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13); });
        });
        var app = builder.Build(); var server = new TransferServer(app, cert); server.Map(engine);
        try { await app.StartAsync(); }
        catch { await app.DisposeAsync(); throw; }
        server.Port = new Uri(app.Urls.First()).Port; return server;
    }
    private void Map(Engine engine)
    {
        var attempts = new ConcurrentDictionary<string, (long minute, int count)>();
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.CacheControl = "no-store"; ctx.Response.Headers.XContentTypeOptions = "nosniff";
            try { engine.ExpireSessions(); await next(ctx); }
            catch (Exception e) when (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = e switch { UnauthorizedAccessException => 401, KeyNotFoundException => 404, InvalidOperationException => 409, InvalidDataException => 422, OperationCanceledException => 409, IOException => 507, _ => 400 };
                // Local file paths and exception traces never leave the host.
                await ctx.Response.WriteAsJsonAsync(new { error = e is IOException ? "Disk or source file unavailable. Check the Windows app." : e.Message });
            }
        });
        Device Auth(HttpContext ctx, bool pending = false) => engine.Authenticate(ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal), pending);
        async Task<T> Json<T>(HttpContext ctx)
        {
            if (ctx.Request.ContentLength is null or > 65536) throw new ArgumentException("Invalid metadata size");
            return await ctx.Request.ReadFromJsonAsync<T>(Protocol.Json, ctx.RequestAborted) ?? throw new ArgumentException("Missing metadata");
        }
        app.MapGet("/v1/info", () => Results.Json(new { protocol = 1, maxFileBytes = Protocol.MaxFile, chunkBytes = Protocol.ChunkSize, host = Environment.MachineName }));
        app.MapPost("/v1/join", async (HttpContext ctx) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"; var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            if (attempts.Count > 1024) foreach (var pair in attempts.Where(p => p.Value.minute < minute).ToList()) attempts.TryRemove(pair.Key, out _);
            var count = attempts.AddOrUpdate(ip, (minute, 1), (_, v) => v.minute == minute ? (minute, v.count + 1) : (minute, 1));
            if (count.count > 5) return Results.Json(new { error = "Too many pairing attempts. Wait one minute." }, statusCode: 429);
            var b = await Json<JoinRequest>(ctx); var d = engine.Join(b.Code, b.Name);
            return Results.Json(new { d.Id, d.Token, d.State, d.Expires });
        });
        app.MapGet("/v1/session", (HttpContext ctx) => { var d = Auth(ctx, true); return Results.Json(new { d.Id, d.State, d.Expires }); });
        app.MapPost("/v1/end", (HttpContext ctx) => { var d = Auth(ctx, true); engine.Session(d.Id, false); return Results.Ok(); });
        app.MapGet("/v1/transfers", (HttpContext ctx) =>
        {
            var d = Auth(ctx); lock (engine.Sync) return Results.Json(engine.State.Transfers.Where(t => t.DeviceId == d.Id).Select(t => t.Public()).ToArray());
        });
        app.MapPost("/v1/transfers", async (HttpContext ctx) =>
        {
            var d = Auth(ctx); var b = await Json<IncomingRequest>(ctx); var t = engine.Incoming(d, b.Id, b.Name, b.Size, b.Sha256); return Results.Json(t.Public());
        });
        app.MapGet("/v1/transfers/{id}", (HttpContext ctx, string id) => Results.Json(engine.Get(Auth(ctx), id).Public()));
        app.MapPost("/v1/transfers/{id}/control", async (HttpContext ctx, string id) =>
        {
            var d = Auth(ctx); var t = engine.Get(d, id); var b = await Json<ControlRequest>(ctx); engine.Control(t.Id, b.Action); return Results.Json(t.Public());
        });
        app.MapPut("/v1/transfers/{id}/chunk", async (HttpContext ctx, string id) =>
        {
            var d = Auth(ctx); var t = engine.Get(d, id);
            if (!long.TryParse(ctx.Request.Query["offset"], out var offset) || ctx.Request.ContentLength is not (> 0 and <= Protocol.ChunkSize)) throw new ArgumentException("Invalid chunk");
            await io.WaitAsync(ctx.RequestAborted);
            try
            {
                var bytes = new byte[(int)ctx.Request.ContentLength.Value]; await ctx.Request.Body.ReadExactlyAsync(bytes, ctx.RequestAborted);
                await engine.WriteChunk(d, t, offset, bytes, ctx.Request.Headers["X-Ebira-SHA256"].ToString(), ctx.RequestAborted);
                return Results.Json(new { t.Offset });
            }
            finally { io.Release(); }
        });
        app.MapPost("/v1/transfers/{id}/finish", async (HttpContext ctx, string id) =>
        {
            var d = Auth(ctx); var t = engine.Get(d, id); await engine.FinishIncoming(d, t, ctx.RequestAborted); return Results.Json(t.Public());
        });
        app.MapGet("/v1/transfers/{id}/chunk", async (HttpContext ctx, string id) =>
        {
            var d = Auth(ctx); var t = engine.Get(d, id);
            if (!long.TryParse(ctx.Request.Query["offset"], out var offset)) throw new ArgumentException("Missing offset");
            await io.WaitAsync(ctx.RequestAborted);
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, t.Lifetime.Token);
                var bytes = await engine.ReadChunk(d, t, offset, linked.Token); ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.ContentLength = bytes.Length; ctx.Response.Headers["X-Ebira-SHA256"] = Protocol.Hash(bytes);
                await ctx.Response.Body.WriteAsync(bytes, linked.Token);
            }
            finally { io.Release(); }
        });
        app.MapPost("/v1/transfers/{id}/ack", async (HttpContext ctx, string id) =>
        {
            var d = Auth(ctx); var t = engine.Get(d, id); var b = await Json<AckRequest>(ctx); engine.Acknowledge(d, t, b.Offset, b.Sha256); return Results.Json(t.Public());
        });
    }
    public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); io.Dispose(); }
    private sealed record JoinRequest(string Code, string Name);
    private sealed record IncomingRequest(string Id, string Name, long Size, string Sha256);
    private sealed record ControlRequest(string Action);
    private sealed record AckRequest(long Offset, string? Sha256);
}

using System.Diagnostics;
using System.Text.Json;

// An independent OpenSSL client for environments where the Windows client SSPI
// credential API is unavailable. The production server still serves real TLS.
sealed class NodeTlsHandler(string pin) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var info = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "node-tls.mjs"));
        using var process = Process.Start(info) ?? throw new IOException("Could not start independent TLS client");
        var headers = request.Headers.ToDictionary(p => p.Key, p => string.Join(",", p.Value));
        if (request.Content != null) foreach (var h in request.Content.Headers) headers[h.Key] = string.Join(",", h.Value);
        var bytes = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct); if (request.Content != null) headers["Content-Length"] = bytes.Length.ToString();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { url = request.RequestUri!.ToString(), method = request.Method.Method, headers, body = Convert.ToBase64String(bytes), pin })); process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(ct); var error = process.StandardError.ReadToEndAsync(ct); await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new HttpRequestException(await error);
        var result = JsonSerializer.Deserialize<JsonElement>(await output); var response = new HttpResponseMessage((System.Net.HttpStatusCode)result.GetProperty("status").GetInt32()) { Content = new ByteArrayContent(Convert.FromBase64String(result.GetProperty("body").GetString()!)) };
        foreach (var h in result.GetProperty("headers").EnumerateObject()) if (!response.Headers.TryAddWithoutValidation(h.Name, h.Value.ToString())) response.Content.Headers.TryAddWithoutValidation(h.Name, h.Value.ToString());
        return response;
    }
}

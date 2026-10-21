using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Ebira.Core;

static class AndroidTransport
{
    public static async Task Run(string root, X509Certificate2 cert)
    {
        var java = Environment.GetEnvironmentVariable("EBIRA_TEST_JAVA") ?? throw new InvalidOperationException("Set EBIRA_TEST_JAVA to the JDK java executable");
        var cp = Environment.GetEnvironmentVariable("EBIRA_TEST_CLASSPATH") ?? throw new InvalidOperationException("Compile android/transport-checks and set EBIRA_TEST_CLASSPATH first");
        var engine = new Engine(Path.Combine(root, "android-state"), Path.Combine(root, "android-receive"), new TestProtector());
        await using var host = await TransferServer.StartLoopbackForChecks(engine);
        var bridgeStart = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, UseShellExecute = false };
        bridgeStart.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "node-bridge.mjs"));
        using var bridge = Process.Start(bridgeStart)!;
        try
        {
            await bridge.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { pfx = Convert.ToBase64String(cert.Export(X509ContentType.Pfx)), port = host.Port })); await bridge.StandardInput.FlushAsync();
            var port = await bridge.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var start = new ProcessStartInfo(java) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, UseShellExecute = false };
            foreach (var arg in new[] { "-cp", cp, "app.ebira.ApiCheck", $"https://127.0.0.1:{port}", Protocol.Hash(cert.RawData), engine.PairCode }) start.ArgumentList.Add(arg);
            using var client = Process.Start(start)!; var errors = client.StandardError.ReadToEndAsync(); bool complete = false;
            while (await client.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)) is { } line)
            {
                if (line.StartsWith("APPROVE ")) { engine.Session(line[8..], true); await client.StandardInput.WriteLineAsync("approved"); await client.StandardInput.FlushAsync(); }
                else if (line.StartsWith("OFFER "))
                {
                    var offered = await engine.Offer(Path.Combine(engine.ReceiveRoot, "Other", "android-api.bin"), [line[6..]]);
                    await client.StandardInput.WriteLineAsync(offered[0].Id); await client.StandardInput.FlushAsync();
                }
                else { Console.WriteLine(line); if (line.StartsWith("ANDROID_CHECKS_PASSED ")) complete = true; }
            }
            await client.WaitForExitAsync(); if (client.ExitCode != 0 || !complete) throw new Exception("Android transport checks failed: " + await errors);
            if (!engine.State.Transfers.All(t => t.State == "complete")) throw new Exception("Android checks left an incomplete transfer");
            Console.WriteLine("NOTE: Android transport uses an OpenSSL test TLS bridge. Windows Schannel and Android OS UI/storage/lifecycle still require device tests.");
        }
        finally { bridge.StandardInput.Close(); if (!bridge.HasExited) { bridge.Kill(true); await bridge.WaitForExitAsync(); } }
    }
}

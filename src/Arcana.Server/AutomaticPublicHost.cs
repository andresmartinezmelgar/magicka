using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Arcana.Core;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace Arcana.Server;

/// <summary>Optional PC hosting: private credentials never cross into the client.</summary>
public sealed class AutomaticPublicHost : IAsyncDisposable
{
    private readonly Process _tunnel;
    public AccountOptions Options { get; }
    private AutomaticPublicHost(Process tunnel, AccountOptions options) { _tunnel = tunnel; Options = options; }

    public static async Task<AutomaticPublicHost?> Prepare(AccountOptions options, int port)
    {
        if (Environment.GetEnvironmentVariable("ARCANA_AUTO_PUBLIC_HOST") != "true") return null;
        if (!OperatingSystem.IsWindows() || !options.Enabled) throw new InvalidOperationException("El alojamiento automático necesita Windows y Firebase configurado.");
        string configPath = Environment.GetEnvironmentVariable("ARCANA_FIREBASE_CONFIG") ?? throw new InvalidOperationException("Falta la configuración privada.");
        string executable = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "cloudflared-2026.10.0.exe");
        const string checksum = "86AEE4017B26625CEE8484C113558F48EFFA4CD47F7AA05FCF425604E5D2B23C";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var token = deadline.Token;
        if (!File.Exists(executable))
        {
            using var download = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            using var response = await download.GetAsync("https://github.com/cloudflare/cloudflared/releases/download/2026.10.0/cloudflared-windows-amd64.exe", HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            string temporary = executable + ".download";
            try
            {
                await using (var output = File.Create(temporary)) await response.Content.CopyToAsync(output, token);
                await using (var input = File.OpenRead(temporary))
                    if (Convert.ToHexString(await SHA256.HashDataAsync(input, token)) != checksum) throw new IOException("La descarga de cloudflared no supera la verificación.");
                File.Move(temporary, executable, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        await using (var input = File.OpenRead(executable))
            if (Convert.ToHexString(await SHA256.HashDataAsync(input, token)) != checksum) throw new IOException("cloudflared no coincide con la versión verificada.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in new[] { "tunnel", "--url", $"http://127.0.0.1:{port}", "--no-autoupdate", "--protocol", "http2" }) start.ArgumentList.Add(argument);
        // The tunnel has no reason to inherit the private Firebase credential location.
        foreach (string key in new[] { "GOOGLE_APPLICATION_CREDENTIALS", "ARCANA_FIREBASE_CONFIG" }) start.Environment.Remove(key);
        var tunnel = new Process { StartInfo = start, EnableRaisingEvents = true };
        var published = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        tunnel.ErrorDataReceived += (_, message) =>
        {
            var match = Regex.Match(message.Data ?? "", @"https://[a-z0-9-]+\.trycloudflare\.com");
            if (match.Success) published.TrySetResult(match.Value);
        };
        tunnel.OutputDataReceived += (_, _) => { };
        tunnel.Exited += (_, _) => published.TrySetException(new IOException("La conexión HTTPS se ha cerrado."));
        try
        {
            if (!tunnel.Start()) throw new IOException("No se pudo iniciar HTTPS.");
            tunnel.BeginErrorReadLine(); tunnel.BeginOutputReadLine();
            string url = await published.Task.WaitAsync(token);
            await Authorize(options.ProjectId, new Uri(url).Host, token);
            var updated = options with { PublicUrl = url };
            var config = JsonNode.Parse(await File.ReadAllTextAsync(configPath, token))!.AsObject();
            config["publicUrl"] = url;
            string temp = configPath + ".tmp";
            await File.WriteAllTextAsync(temp, config.ToJsonString(), token);
            File.Move(temp, configPath, true);
            return new(tunnel, updated);
        }
        catch { try { if (!tunnel.HasExited) tunnel.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } tunnel.Dispose(); throw; }
    }

    private static async Task Authorize(string project, string host, CancellationToken token)
    {
        var credential = GoogleCredential.GetApplicationDefault().CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: token));
        http.DefaultRequestHeaders.Add("x-goog-user-project", project);
        string endpoint = $"https://identitytoolkit.googleapis.com/admin/v2/projects/{project}/config";
        using var response = await http.GetAsync(endpoint, token);
        response.EnsureSuccessStatusCode();
        using var config = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var domains = config.RootElement.GetProperty("authorizedDomains").EnumerateArray().Select(d => d.GetString()!).ToList();
        if (domains.Contains(host)) return;
        domains.Add(host);
        using var patch = await http.PatchAsJsonAsync(endpoint + "?updateMask=authorizedDomains", new { authorizedDomains = domains }, token);
        patch.EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync()
    {
        try { if (!_tunnel.HasExited) _tunnel.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        _tunnel.Dispose(); return ValueTask.CompletedTask;
    }
}

public sealed class PublicAccountServerPublisher(AccountOptions options, int port, ILogger<PublicAccountServerPublisher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var database = new FirestoreDbBuilder { ProjectId = options.ProjectId }.Build();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
        while (!stoppingToken.IsCancellationRequested)
        {
            int delay = 5;
            try
            {
                // Publish only while both the local server and its HTTPS tunnel respond.
                using var local = await http.GetAsync($"http://127.0.0.1:{port}/health", stoppingToken);
                local.EnsureSuccessStatusCode();
                using var remote = await http.GetAsync(options.PublicUrl + "/health", stoppingToken);
                remote.EnsureSuccessStatusCode();
                using var health = JsonDocument.Parse(await remote.Content.ReadAsStringAsync(stoppingToken));
                if (health.RootElement.GetProperty("version").GetString() != BuildInfo.Version) throw new IOException("HTTPS apunta a otra versión del servidor.");
                await database.Document("publicAccountServer/current").SetAsync(new Dictionary<string, object>
                {
                    ["url"] = options.PublicUrl.Replace("https://", "wss://") + "/play",
                    ["expiresAt"] = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(2)),
                    ["protocol"] = Protocol.Version
                }, cancellationToken: stoppingToken);
                delay = 60;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { log.LogWarning("No se pudo anunciar el servidor de cuentas: {Kind}", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}

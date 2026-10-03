using System.Diagnostics;

namespace Arcana.Core;

public sealed record LocalServerOptions(string Name, int Port, int DiscoveryPort, string Password);

/// <summary>Arranca únicamente el servidor distribuido con el juego. No instala ni ejecuta comandos de shell.</summary>
public sealed class LocalServerHost : IDisposable
{
    private Process? _process;
    private readonly object _logGate = new();
    public bool Running => _process is { HasExited: false };
    public string Address { get; private set; } = "";
    public string LogPath { get; private set; } = "";
    public async Task StartAsync(string packageDirectory, string userDirectory, LocalServerOptions options,
        CancellationToken token = default)
    {
        if (Running) throw new InvalidOperationException("El servidor local ya está iniciado.");
        if (options.Port is < 1024 or > 65535 || options.DiscoveryPort is < 1024 or > 65535)
            throw new ArgumentException("Los puertos deben estar entre 1024 y 65535.");
        string executable = Path.Combine(packageDirectory, "server", OperatingSystem.IsWindows() ? "Arcana.Server.exe" : "Arcana.Server");
        if (!File.Exists(executable))
            throw new InvalidOperationException("No se encuentra el servidor incluido. Extrae el paquete completo antes de abrir el juego.");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) })
        {
            try
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{options.Port}/health", token).ConfigureAwait(false);
                throw new InvalidOperationException("Ese puerto ya está ocupado. Usa Por IP para entrar o elige otro puerto.");
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { }
        }
        Stop(); Directory.CreateDirectory(userDirectory);
        LogPath = Path.Combine(userDirectory, "server.log");
        File.WriteAllText(LogPath, $"Arcana Arena — {DateTimeOffset.Now:u}\n");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // El servidor no hereda opciones de otra partida o de una consola de desarrollo.
        foreach (string key in new[] { "GAME_MODE", "MAX_PLAYERS", "BOTS", "FRIENDLY_FIRE", "SCORE_LIMIT", "TICKETS" })
            start.Environment.Remove(key);
        start.Environment["SERVER_NAME"] = options.Name;
        start.Environment["GAME_PORT"] = options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["DISCOVERY_PORT"] = options.DiscoveryPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["SERVER_PASSWORD"] = options.Password;
        start.Environment["ARCANA_PARENT_PID"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _process = new Process { StartInfo = start };
        _process.OutputDataReceived += (_, e) => Log(e.Data);
        _process.ErrorDataReceived += (_, e) => Log(e.Data);
        Address = $"127.0.0.1:{options.Port}";
        try
        {
            if (!_process.Start()) throw new IOException("No se pudo iniciar el servidor.");
            _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            for (int i = 0; i < 60; i++)
            {
                token.ThrowIfCancellationRequested();
                if (_process.HasExited) throw new IOException("El servidor no ha podido arrancar. Revisa los puertos y el registro.");
                try
                {
                    using var response = await http.GetAsync(RoomBrowser.Endpoint(Address, "info"), token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var info = Protocol.Deserialize<ServerInfo>(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                        if (info?.Name == options.Name && info.Protocol == Protocol.Version) return;
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!token.IsCancellationRequested) { }
                await Task.Delay(150, token).ConfigureAwait(false);
            }
            throw new IOException("El servidor no respondió a tiempo. Revisa el registro del servidor.");
        }
        catch { Stop(); throw; }
    }
    private void Log(string? line)
    {
        if (line == null) return;
        lock (_logGate) try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch (IOException) { }
    }
    public void Stop()
    {
        var process = _process; _process = null;
        if (process == null) return;
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(2000); } }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
    public void Dispose() => Stop();
}

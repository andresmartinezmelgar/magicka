using System.Diagnostics;
using System.Globalization;

namespace Arcana.Core;

public sealed record LocalServerOptions(string Name, int Port, int DiscoveryPort, string Password);

/// <summary>Arranca el servidor distribuido, verifica su identidad y cancela arranques al cerrar el launcher.</summary>
public sealed class LocalServerHost : IDisposable
{
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _starting;
    private DiagnosticLog? _log;
    private bool _disposed;
    public event Action<int>? UnexpectedExit;
    public bool Running
    {
        get { lock (_gate) { try { return _process is { HasExited: false }; } catch (InvalidOperationException) { return false; } } }
    }
    public string Address { get; private set; } = "";
    public string LogPath { get; private set; } = "";

    public async Task StartAsync(string packageDirectory, string userDirectory, LocalServerOptions options, CancellationToken token = default)
    {
        if (options.Port is < 1024 or > 65535 || options.DiscoveryPort is < 1024 or > 65535)
            throw new ArgumentException("Los puertos deben estar entre 1024 y 65535.");
        if (string.IsNullOrWhiteSpace(options.Name) || options.Name.Length > 32 || options.Name.Any(char.IsControl) || options.Password.Length > 128)
            throw new ArgumentException("Nombre o contraseña del servidor inválidos.");
        string executable = Path.Combine(packageDirectory, "server", OperatingSystem.IsWindows() ? "Arcana.Server.exe" : "Arcana.Server");
        if (!File.Exists(executable)) throw new InvalidOperationException("No se encuentra el servidor incluido. Extrae el paquete completo antes de abrir el juego.");
        CancellationTokenSource startup;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_starting != null || Running) throw new InvalidOperationException("El servidor local ya está iniciado o arrancando.");
            _process?.Dispose(); _process = null;
            _log?.Dispose();
            LogPath = Path.Combine(userDirectory, "logs");
            _log = new DiagnosticLog(LogPath, "host"); _log.ProtectSecret(options.Password);
            startup = CancellationTokenSource.CreateLinkedTokenSource(token); _starting = startup;
        }
        Diagnostics.Log.ProtectSecret(options.Password);
        string instance = Guid.NewGuid().ToString("N");
        Process? process = null;
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) })
            {
                try
                {
                    using var response = await http.GetAsync($"http://127.0.0.1:{options.Port}/health", startup.Token).ConfigureAwait(false);
                    throw new InvalidOperationException("Ese puerto ya está ocupado. Usa Por IP para entrar o elige otro puerto.");
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!startup.IsCancellationRequested) { }
            }
            var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string key in new[] { "GAME_MODE", "ARENA_MAP", "MAX_PLAYERS", "BOTS", "FRIENDLY_FIRE", "SCORE_LIMIT", "TICKETS" }) start.Environment.Remove(key);
            foreach (string key in new[] { "ARCANA_FIREBASE_CONFIG", "FIREBASE_PROJECT_ID", "FIREBASE_API_KEY", "FIREBASE_AUTH_DOMAIN", "ARCANA_PUBLIC_URL", "GOOGLE_APPLICATION_CREDENTIALS", "ARCANA_REWARD_OUTBOX", "ARCANA_AUTO_PUBLIC_HOST" }) start.Environment.Remove(key);
            bool localFirebase = LocalFirebaseHosting.Configure(start, userDirectory);
            start.Environment["SERVER_NAME"] = options.Name;
            start.Environment["GAME_PORT"] = options.Port.ToString(CultureInfo.InvariantCulture);
            start.Environment["DISCOVERY_PORT"] = options.DiscoveryPort.ToString(CultureInfo.InvariantCulture);
            start.Environment["SERVER_PASSWORD"] = options.Password;
            start.Environment["ARCANA_PARENT_PID"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            start.Environment["ARCANA_SERVER_INSTANCE"] = instance;
            start.Environment["ARCANA_LOG_DIRECTORY"] = LogPath;
            var hostLog = _log!;
            process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) hostLog.Write(DiagnosticLevel.Information, "server.stdout", e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) hostLog.Write(DiagnosticLevel.Error, "server.stderr", e.Data); };
            var ownedProcess = process;
            process.Exited += (_, _) =>
            {
                lock (_gate)
                {
                    if (_process != ownedProcess || _starting != null || _disposed) return;
                    int code = ownedProcess.ExitCode;
                    Diagnostics.Error("host.unexpected_exit", "El servidor local ha terminado inesperadamente.", context:
                        new Dictionary<string, object?> { ["exitCode"] = code, ["instance"] = instance });
                    UnexpectedExit?.Invoke(code);
                }
            };
            lock (_gate)
            {
                startup.Token.ThrowIfCancellationRequested();
                if (!process.Start()) throw new IOException("No se pudo iniciar el servidor.");
                _process = process; Address = $"127.0.0.1:{options.Port}";
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
            }
            Diagnostics.Info("host.start", "Arrancando servidor incluido.", new Dictionary<string, object?>
                { ["instance"] = instance, ["port"] = options.Port, ["discoveryPort"] = options.DiscoveryPort, ["firebase"] = localFirebase });
            using var poll = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            bool automaticHost = start.Environment.TryGetValue("ARCANA_AUTO_PUBLIC_HOST", out var automatic) && automatic == "true";
            for (int i = 0; i < (automaticHost ? 1200 : 60); i++)
            {
                startup.Token.ThrowIfCancellationRequested();
                lock (_gate) if (process.HasExited) throw new IOException("El servidor no ha podido arrancar. Revisa los puertos y el registro.");
                try
                {
                    using var response = await poll.GetAsync(RoomBrowser.Endpoint(Address, "info"), startup.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var info = Protocol.Deserialize<ServerInfo>(await RoomBrowser.ReadBoundedAsync(response.Content, 8192, startup.Token).ConfigureAwait(false));
                        if (info?.InstanceId == instance && info.Protocol == Protocol.Version)
                        {
                            startup.Token.ThrowIfCancellationRequested();
                            Diagnostics.Info("host.ready", "Servidor local preparado.", new Dictionary<string, object?> { ["instance"] = instance });
                            return;
                        }
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!startup.IsCancellationRequested) { }
                await Task.Delay(150, startup.Token).ConfigureAwait(false);
            }
            throw new IOException("El servidor no respondió a tiempo. Revisa el registro del servidor.");
        }
        catch (Exception e)
        {
            Diagnostics.Warn("host.start_failed", "No se pudo arrancar el servidor local.", e,
                new Dictionary<string, object?> { ["instance"] = instance, ["port"] = options.Port });
            lock (_gate) { if (_process == process) _process = null; Terminate(process); }
            if (startup.IsCancellationRequested) throw new OperationCanceledException("Arranque del servidor cancelado.", e, startup.Token);
            throw;
        }
        finally
        {
            lock (_gate) { if (_starting == startup) _starting = null; }
            startup.Dispose();
        }
    }
    private static void Terminate(Process? process)
    {
        if (process == null) return;
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(2000); } }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Diagnostics.Warn("host.stop_failed", "No se pudo detener el proceso del servidor.", e); }
        finally { process.Dispose(); }
    }
    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            _starting?.Cancel();
            process = _process; _process = null;
        }
        Terminate(process);
        if (process != null) Diagnostics.Info("host.stop", "Servidor local detenido.");
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        Stop(); lock (_gate) { _log?.Dispose(); _log = null; }
    }
}

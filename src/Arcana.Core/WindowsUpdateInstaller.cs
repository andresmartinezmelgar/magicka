using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Arcana.Core;

/// <summary>El ayudante vive fuera del juego para poder sustituir archivos tras su cierre.</summary>
public static class WindowsUpdateInstaller
{
    public static string? PreviousFailure(string updatesDirectory, string version)
    {
        try
        {
            string path = Path.Combine(updatesDirectory, "last-failure.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.GetProperty("version").GetString() == version
                ? json.RootElement.GetProperty("reason").GetString() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return null; }
    }

    public static async Task PrepareAsync(string archive, string installation, string version,
        string sha256, string updatesDirectory, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string work = Path.Combine(updatesDirectory, "install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string script = Path.Combine(work, "Apply-WindowsUpdate.ps1"), ready = Path.Combine(work, "ready");
        using var resource = typeof(WindowsUpdateInstaller).Assembly.GetManifestResourceStream("Arcana.WindowsUpdate.ps1")
            ?? throw new InvalidOperationException("Falta el instalador integrado de Windows.");
        using var reader = new StreamReader(resource, Encoding.UTF8);
        await File.WriteAllTextAsync(script, await reader.ReadToEndAsync(token), new UTF8Encoding(true), token);
        using var parent = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = work
        };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-ArchivePath", Path.GetFullPath(archive), "-InstallDirectory", Path.GetFullPath(installation),
            "-ExpectedVersion", version, "-ExpectedSha256", sha256, "-WorkDirectory", work,
            "-ParentPid", parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-ParentStartTicks", parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar el instalador.");
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                token.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    string error = Path.Combine(work, "error.txt");
                    throw new IOException(File.Exists(error) ? await File.ReadAllTextAsync(error, token) :
                        "El instalador terminó antes de preparar la actualización.");
                }
                if (deadline.Elapsed > TimeSpan.FromMinutes(3)) throw new IOException("El instalador no responde.");
                await Task.Delay(150, token);
            }
            // El cliente solo se cierra tras validar y preparar por completo el paquete.
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}

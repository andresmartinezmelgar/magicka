using System.Text.Json;
using System.Diagnostics;
using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Consulta una Release pública; Android instala con el instalador del sistema, nunca silenciosamente.</summary>
public partial class ReleaseUpdateNotice : Node
{
    private const string Latest = "https://api.github.com/repos/andresmartinezmelgar/magicka/releases/latest";
    private const string Releases = "https://github.com/andresmartinezmelgar/magicka/releases/latest";
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly CancellationTokenSource _stop = new();
    private AcceptDialog? _dialog;

    public override void _Ready()
    {
        if (OS.HasFeature("editor") || OS.GetCmdlineUserArgs().Any(a => a.Contains("smoke") || a.Contains("preview"))) return;
        _ = Check();
    }

    private static bool Newer(string remote, string local)
    {
        if (!Version.TryParse(remote.TrimStart('v', 'V'), out var available) ||
            !Version.TryParse(local.TrimStart('v', 'V'), out var installed)) return false;
        return available > installed;
    }

    private async Task Check()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
            request.Headers.UserAgent.ParseAdd("ArcanaArena-Updater/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, _stop.Token);
            if (!response.IsSuccessStatusCode) return; // Sin conexión: el juego sigue funcionando.
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_stop.Token));
            var release = document.RootElement;
            string version = release.GetProperty("tag_name").GetString() ?? "";
            if (!Newer(version, BuildInfo.Version)) return;
            bool mobile = OS.HasFeature("Android");
            string expected = mobile ? "-Android-arm64.apk" : "-Windows-x64.zip";
            string? url = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(expected, StringComparison.OrdinalIgnoreCase)) continue;
                var candidate = asset.GetProperty("browser_download_url").GetString();
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                    url = candidate;
            }
            if (url == null) return;
            string download = url;
            Callable.From(() => ShowNotice(version, download)).CallDeferred();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* No interrumpir nunca el modo sin conexión. */ }
    }

    private void ShowNotice(string version, string download)
    {
        if (!IsInsideTree() || _stop.IsCancellationRequested) return;
        _dialog = new ConfirmationDialog
        {
            Title = "Actualización de Arcana Arena",
            DialogText = $"Versión instalada: {BuildInfo.Version}\\nNueva versión: {version}\\n\\n¿Descargar la actualización oficial? En Android deberás confirmar la instalación del sistema.",
            OkButtonText = "Actualizar",
            CancelButtonText = "Ahora no"
        };
        AddChild(_dialog);
        _dialog.Confirmed += () => _ = DownloadUpdate(download);
        _dialog.PopupCentered(new Vector2I(650, 240));
    }

    private async Task DownloadUpdate(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ArcanaArena-Updater/1.0");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _stop.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 1024L * 1024 * 1024)
                throw new InvalidDataException("La actualización supera el tamaño permitido.");
            string extension = OS.HasFeature("Android") ? ".apk" : ".zip";
            string folder = System.IO.Path.Combine(OS.GetUserDataDir(), "updates");
            System.IO.Directory.CreateDirectory(folder);
            string target = System.IO.Path.Combine(folder, "ArcanaArena-update" + extension);
            string temporary = target + ".part";
            try
            {
                await using var input = await response.Content.ReadAsStreamAsync(_stop.Token);
                await using var output = System.IO.File.Create(temporary);
                byte[] buffer = new byte[65536];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, _stop.Token)) != 0)
                {
                    total += count;
                    if (total > 1024L * 1024 * 1024) throw new InvalidDataException("Descarga demasiado grande.");
                    await output.WriteAsync(buffer.AsMemory(0, count), _stop.Token);
                }
                await output.FlushAsync(_stop.Token);
            }
            catch { System.IO.File.Delete(temporary); throw; }
            System.IO.File.Move(temporary, target, true);
            Callable.From(() => ShowDownloaded(target)).CallDeferred();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            Callable.From(() => { if (IsInsideTree()) OS.ShellOpen(url); }).CallDeferred();
        }
    }

    private void ShowDownloaded(string path)
    {
        if (!IsInsideTree() || _stop.IsCancellationRequested) return;
        var ready = new AcceptDialog
        {
            Title = "Actualización descargada",
            DialogText = "La nueva versión se ha descargado en:\\n" + path +
                "\\n\\nAndroid: instala el APK con el sistema. Windows: cierra el juego y extrae el ZIP sustituyendo la carpeta anterior.",
            OkButtonText = "Aceptar"
        };
        AddChild(ready);
        ready.PopupCentered(new Vector2I(750, 260));
    }

    public override void _ExitTree() { _stop.Cancel(); _stop.Dispose(); }
}

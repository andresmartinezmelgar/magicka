using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Launcher
{
    private async Task Host()
    {
        if (_host.Running) { OpenRooms(_host.Address, _hostPassword); return; }
        var options = new LocalServerOptions(_hostName.Text.Trim(), (int)_hostPort.Value, (int)_hostDiscovery.Value, _password.Text);
        if (options.Name.Length == 0) { ShowError("Escribe un nombre para el servidor."); return; }
        // Alojar inicia siempre el servidor local. No exigir contraseña por
        // una publicUrl antigua: protegerlo será decisión explícita del usuario.
        // Una URL WSS pública no debe activarse implícitamente desde este botón.
        SetBusy(true, "Iniciando el servidor incluido…");
        try
        {
            await _host.StartAsync(PackageDirectory(), ProjectSettings.GlobalizePath("user://"), options);
            _main.Post(() =>
            {
                _hostPassword = options.Password; SetBusy(false); _stopHost.Disabled = false; _password.Editable = false;
                _hostName.Editable = _hostPort.Editable = _hostDiscovery.Editable = false;
                string addresses = "Busca el servidor en LAN";
                try
                {
                    var ips = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()).Where(ip =>
                        ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip)).Take(3);
                    string list = string.Join(" · ", ips.Select(ip => ip + ":" + options.Port));
                    if (list.Length > 0) addresses = "IP para compartir: " + list;
                }
                catch (System.Net.Sockets.SocketException) { }
                string firebaseDir = LocalFirebaseHosting.DirectoryPath(ProjectSettings.GlobalizePath("user://"));
                bool firebasePresent = System.IO.File.Exists(System.IO.Path.Combine(firebaseDir, LocalFirebaseHosting.ConfigFileName));
                string? publicGame = LocalFirebaseHosting.PublicGameAddress(ProjectSettings.GlobalizePath("user://"));
                string firebaseStatus = !firebasePresent
                    ? "\nFirebase: desactivado; las partidas LAN siguen disponibles."
                    : publicGame?.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) == true
                        ? "\nFirebase configurado. Para jugar con recompensas desde el móvil, usa «Servidor online con Google»."
                        : "\nFirebase local configurado, pero falta la dirección WSS pública para el móvil.";
                _hostInfo.Text = PvpServer.DisplayText($"Servidor activo · Juego TCP {options.Port} · LAN UDP {options.DiscoveryPort}\n{addresses}" + firebaseStatus);
                // El anfitrión siempre gestiona sus propias salas por loopback.
                // Un túnel trycloudflare es temporal y su publicUrl puede quedar caducado.
                // La dirección pública se comparte por separado con jugadores externos.
                string roomAddress = _host.Address;
                UpdateConnectLabel(); OpenRooms(roomAddress, _hostPassword);
            });
        }
        catch (Exception e)
        {
            Diagnostics.Warn("launcher.host_failed", "No se pudo iniciar el servidor incluido.", e);
            _main.Post(() => ShowError(e is InvalidOperationException or ArgumentException or IOException ? e.Message : "No se pudo iniciar el servidor incluido. Consulta los registros."));
        }
    }

    public static string PackageDirectory() => OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://../../artifacts/windows")
        : System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!;

    public void BrowseAddress(string address, bool create = false)
    { OpenRooms(address, _password.Text); if (create) _lobby!.OpenCreation(); }

    public async Task<RoomInfo> StartSmokeRoom()
    {
        await _host.StartAsync(PackageDirectory(), ProjectSettings.GlobalizePath("user://"), new("Prueba de launcher", 29025, 29026, ""));
        return await RoomBrowser.CreateAsync(_host.Address, new() { Name = "Sala del launcher", Bots = 1 });
    }

    private async void ConfigureLan()
    {
        if (_busy || !OperatingSystem.IsWindows()) return;
        string script = System.IO.Path.Combine(PackageDirectory(), "Configurar-LAN.ps1");
        string server = System.IO.Path.Combine(PackageDirectory(), "server", "Arcana.Server.exe");
        if (!File.Exists(script) || !File.Exists(server)) { ShowError("Extrae el paquete Windows completo para configurar LAN."); return; }
        var start = new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = true, Verb = "runas", WorkingDirectory = PackageDirectory() };
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-ServerPath", server,
            "-GamePort", ((int)_hostPort.Value).ToString(), "-DiscoveryPort", ((int)_hostDiscovery.Value).ToString() }) start.ArgumentList.Add(arg);
        SetBusy(true, "Windows solicitará permiso para permitir el servidor en redes privadas…");
        try
        {
            using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("No se abrió la configuración de Windows.");
            await process.WaitForExitAsync();
            int code = process.ExitCode;
            _main.Post(() => { if (code == 0) SetBusy(false, "Acceso LAN configurado para estos puertos en redes privadas.");
                else ShowError("Windows no pudo configurar el acceso. Comprueba el permiso de administrador."); });
        }
        catch (Exception e)
        {
            Diagnostics.Warn("launcher.firewall_failed", "La configuración LAN no se completó.", e);
            _main.Post(() => ShowError(e is System.ComponentModel.Win32Exception ? "La configuración de LAN se canceló en Windows." : e.Message));
        }
    }

    private void StopHost()
    {
        _host.Stop(); _hostPassword = ""; _stopHost.Disabled = true; _password.Editable = true;
        _hostName.Editable = _hostPort.Editable = _hostDiscovery.Editable = true;
        _hostInfo.Text = "Servidor detenido. Puedes cambiar sus opciones e iniciarlo otra vez.";
        _status.Text = "El servidor y sus salas se han cerrado."; UpdateConnectLabel();
    }

    private void UpdateConnectLabel() => _connect.Text = _tabs.CurrentTab switch
    { 0 => "Ver salas", 1 => "Ver salas del servidor LAN", 2 => "Entrenar con bots", _ => _host.Running ? "Ver mis salas" : "Iniciar servidor y ver salas" };

    public override void _ExitTree() { GetViewport().SizeChanged -= UpdateResponsiveMenu; CancelOnlineSearch(); _search?.Cancel(); _search?.Dispose(); _host.Dispose(); }
}

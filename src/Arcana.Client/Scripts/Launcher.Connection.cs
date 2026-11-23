using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Launcher
{
    private void Remember()
    {
        _config.SetValue("player", "name", PlayerName);
        _config.SetValue("player", "wizard_type", (int)SelectedWizardType);
        _config.SetValue("player", "team", Ui.SelectedTeam(_team));
        _config.SetValue("practice", "map", _practiceMap.GetSelectedId());
        _config.SetValue("host", "name", _hostName.Text); _config.SetValue("host", "port", (int)_hostPort.Value);
        _config.SetValue("host", "discovery", (int)_hostDiscovery.Value);
        var saved = _config.Save("user://launcher.cfg");
        if (saved != Error.Ok) Diagnostics.Warn("launcher.config_failed", "No se pudo guardar la configuración.", context:
            new Dictionary<string, object?> { ["error"] = saved });
    }

    private void Connect()
    {
        if (_busy || !_main.CanStartRegularMatch()) return;
        Remember();
        int team = Ui.SelectedTeam(_team);
        if (_tabs.CurrentTab == 2)
        {
            _main.StartPractice(PlayerName, team, (GameMode)_practiceMode.GetSelectedId(), (int)_bots.Value,
                SelectedWizardType, (ArenaMapId)_practiceMap.GetSelectedId(), DefenseFlow.Continuous); return;
        }
        if (_tabs.CurrentTab == 3) { _ = Host(); return; }
        string address = PvpServer.Automatic;
        if (_tabs.CurrentTab == 1)
        {
            int selected = _servers.SelectedIndex;
            if (selected < 0 || selected >= _lan.Length) { ShowError("Selecciona un servidor de la lista LAN."); return; }
            address = _lan[selected].Address;
        }
        OpenRooms(address, _password.Text);
    }

    /// <summary>
    /// La dirección pública cambia al reiniciar Cloudflare, pero el anuncio
    /// de Firestore se renueva. Se valida el anuncio y se comprueba que el
    /// servidor real responde antes de abrir las salas. No se guardan tokens
    /// Google ni se entregan credenciales a direcciones no verificadas.
    /// </summary>
    private async void ConnectOfficialOnline()
    {
        if (_busy || _page != 1 || !_main.CanStartRegularMatch()) return;
        if (!_main.Accounts.IsDirectFirebaseSession)
        {
            ShowError("Inicia sesión con Google en Inicio → Mi perfil antes de entrar al servidor online con recompensas.");
            return;
        }
        CancelOnlineSearch();
        var pending = new CancellationTokenSource();
        _onlineSearch = pending;
        SetBusy(true, "Buscando el servidor seguro anunciado en Firebase…");
        try
        {
            string address = await PublicAccountServer.Resolve(FirebaseProjectId, pending.Token);
            RoomDirectory rooms = await RoomBrowser.ListAsync(address, pending.Token);
            if (!rooms.Server.RewardsAvailable)
                throw new InvalidOperationException("El servidor online no tiene las recompensas activadas.");
            _main.Post(() =>
            {
                if (_onlineSearch != pending || pending.IsCancellationRequested || _page != 1) return;
                _onlineSearch = null;
                SetBusy(false);
                Remember();
                OpenRooms(address, "");
            });
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error)
        {
            Diagnostics.Warn("launcher.online_discovery_failed", "No se pudo encontrar el servidor online.", error);
            _main.Post(() =>
            {
                if (_onlineSearch != pending || pending.IsCancellationRequested || _page != 1) return;
                _onlineSearch = null;
                ShowError(error is InvalidOperationException or IOException or InvalidDataException
                    ? error.Message
                    : "No se pudo abrir el servidor online. Comprueba Internet y que el anfitrión tenga Firebase y Cloudflare activos.");
            });
        }
        finally { pending.Dispose(); }
    }

    private void CancelOnlineSearch()
    {
        var pending = _onlineSearch;
        _onlineSearch = null;
        if (pending == null) return;
        // La tarea de búsqueda puede haber terminado y dispuesto su CTS
        // mientras el resultado espera a ser procesado en el hilo de Godot.
        try { pending.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void OpenRooms(string address, string password)
    {
        CancelOnlineSearch(); CancelSearch(); _main.CancelPendingConnection(); CloseRooms();
        _lobby = new RoomLobby(); AddChild(_lobby);
        _lobby.Build(_main, address, password, PlayerName, Ui.SelectedTeam(_team), SelectedWizardType,
            refreshRooms: RefreshRoomsOnBuild);
    }

    public void CloseRooms()
    { if (GodotObject.IsInstanceValid(_lobby)) _lobby!.QueueFree(); _lobby = null; }

    public bool HandleBack()
    {
        if (!GodotObject.IsInstanceValid(_lobby)) { if (_page == 0) return false; OpenPage(0); return true; }
        CancelOnlineSearch(); _main.CancelPendingConnection(); CloseRooms(); SetBusy(false); return true;
    }

    private async void StartSearch()
    {
        if (_busy || _refresh.Disabled) return;
        CancelSearch(); var search = new CancellationTokenSource(); _search = search;
        _refresh.Disabled = true; _status.Text = "Buscando partidas en la red local…";
        int port = (int)_discoveryPort.Value;
        try
        {
            var servers = await LanDiscovery.FindAsync(port, search.Token);
            _main.Post(() =>
            {
                if (_search != search || search.IsCancellationRequested) return;
                _lan = servers; _servers.Clear();
                foreach (var s in servers)
                    _servers.AddItem($"{s.Info.Name}  ·  {s.Address}  ·  {s.Info.Players}/{s.Info.MaxPlayers} plazas");
                if (servers.Length > 0) _servers.Select(0);
                _status.Text = servers.Length > 0 ? $"{servers.Length} servidor(es) encontrado(s)." : "No se encontraron servidores locales. Puedes volver a Online para consultar las salas de PvP.";
                _refresh.Disabled = false;
            });
        }
        catch (OperationCanceledException) { _main.Post(() => { if (_search == search) _refresh.Disabled = false; }); }
        catch (Exception e)
        {
            if (search.IsCancellationRequested) return;
            Diagnostics.Warn("launcher.discovery_failed", "No se pudo buscar en LAN.", e);
            _main.Post(() => { if (_search != search) return; _refresh.Disabled = false; ShowError("No se ha podido buscar en LAN. Comprueba el puerto UDP y el cortafuegos."); });
        }
    }

    private void CancelSearch()
    { var search = _search; _search = null; search?.Cancel(); search?.Dispose(); _refresh.Disabled = false; }
}

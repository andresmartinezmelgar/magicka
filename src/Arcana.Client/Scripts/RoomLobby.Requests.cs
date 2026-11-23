using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class RoomLobby
{
    public void SetBusy(bool busy)
    {
        _busy = busy; foreach (var b in _buttons) b.Disabled = busy;
        if (_serverStarting) foreach (var b in _matchButtons) b.Disabled = true;
        _list.Disabled = busy;
        _roomName.Editable = _roomPassword.Editable = _joinPassword.Editable =
            _serverPasswordEntry.Editable = !busy;
        _mode.Disabled = _map.Disabled = _friendly.Disabled = _teamChoice.Disabled = busy;
        _bestOf.Disabled = busy || (GameMode)_mode.GetSelectedId() == GameMode.Arena;
        _bots.Editable = _score.Editable = _tickets.Editable = !busy;
        _capacity.Editable = !busy && (GameMode)_mode.GetSelectedId() != GameMode.Duel;
        ShowCreationObjective();
    }

    public void ShowError(string error)
    {
        error = PvpServer.DisplayText(error);
        SetBusy(false);
        _status.Text = error;
        _status.AddThemeColorOverride("font_color", Ui.Red);
        // El error de conexión se muestra AQUÍ, no en el launcher oculto detrás.
        if (!Ui.IsMobile && error.Contains("Contraseña de la sala", StringComparison.OrdinalIgnoreCase))
            _joinPassword.GrabFocus();
        else if (!Ui.IsMobile && error.Contains("Contraseña del servidor", StringComparison.OrdinalIgnoreCase))
            _serverPasswordEntry.GrabFocus();
    }

    private void Loading(string text) { SetBusy(true); _status.Text = text; _status.AddThemeColorOverride("font_color", Ui.Muted); }

    private async void Refresh()
    {
        if (_busy) return;
        Loading("Consultando salas…");
        try
        {
            string target = UsesPvpDirectory ? await PublicAccountServer.Resolve(PublicAccountServer.Project, _lifetime.Token) : _address;
            RoomDirectory directory = await RoomBrowser.ListAsync(target, _lifetime.Token);
            // Al descubrir por LAN un servidor que requiere Firebase, sustituir
            // automáticamente su ws:// por la WSS publicada. Así tanto Entrar
            // como Crear sala usan HTTPS y no transmiten claves por LAN.
            var endpoint = ArenaConnection.Address(target);
            if (directory.Server.RewardsAvailable && endpoint.Scheme == "ws" && !endpoint.IsLoopback &&
                _main.Accounts.IsDirectFirebaseSession)
            {
                (target, directory) = await PublicAccountServer.ForInstance(directory.Server, _lifetime.Token);
            }
            string secureTarget = target;
            RoomDirectory result = directory;
            _main.Post(() =>
            {
                if (!Active) return;
                _serverStarting = false;
                _address = secureTarget;
                _heading.Text = "SALAS  /  " + PvpServer.DisplayText(result.Server.Name);
                string? previous = Selected()?.Id; _rooms = result.Rooms;
                _serverPasswordRequired = result.Server.PasswordRequired;
                _rewardsConfigured = result.Server.RewardsAvailable;
                UpdateRewardsHint();
                int at = Array.FindIndex(_rooms, r => r.Id == previous);
                if (at >= 0) _roomPageStart = at / VisibleRoomsPerPage * VisibleRoomsPerPage;
                else _roomPageStart = 0;
                ShowRoomPage(_roomPageStart);
                if (at >= 0)
                {
                    _list.Select(at - _roomPageStart);
                    Callable.From(_list.RevealSelection).CallDeferred();
                }
                ShowObjective();
                SetBusy(false); _status.Text = $"{_rooms.Length} sala(s). Selecciona una o crea tu propia partida." +
                    (secureTarget.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) ? " · Conexión segura" : "");
            });
        }
        catch (Exception e) { Failure(e); }
    }

    private void ShowRoomPage(int requestedStart)
    {
        _roomPageStart = Math.Clamp(requestedStart, 0,
            Math.Max(0, (_rooms.Length - 1) / VisibleRoomsPerPage * VisibleRoomsPerPage));
        _list.Clear();
        _emptyRooms.Visible = _rooms.Length == 0;
        foreach (var room in _rooms.Skip(_roomPageStart).Take(VisibleRoomsPerPage))
            _list.AddItem($"{room.Name}{(room.PasswordRequired ? " · CON CLAVE" : "")}  ·  {Ui.ModeName(room.Mode)}  ·  {ArenaMap.Get(room.Map).Name}  ·  {room.Players}/{room.MaxPlayers} plazas" +
                (room.InProgress && room.BestOf > 1 ? " · EN PARTIDA" : " · DISPONIBLE"));
        if (_list.ItemCount > 0) _list.Select(0);
        if (_roomPagePrevious != null) _roomPagePrevious.Disabled = _roomPageStart == 0;
        if (_roomPageNext != null) _roomPageNext.Disabled = _roomPageStart + VisibleRoomsPerPage >= _rooms.Length;
        if (_roomPageLabel != null) _roomPageLabel.Text = _rooms.Length == 0
            ? "0 salas"
            : $"{_roomPageStart + 1}–{Math.Min(_rooms.Length, _roomPageStart + VisibleRoomsPerPage)} / {_rooms.Length}";
        ShowObjective();
    }

    private RoomInfo? Selected()
    {
        int index = _list.SelectedIndex >= 0 ? _roomPageStart + _list.SelectedIndex : -1;
        return index >= 0 && index < _rooms.Length ? _rooms[index] : null;
    }

    public void OpenCreation() => SwitchPane(true);

    private void Join()
    {
        if (_serverStarting) { ShowStartup(); return; }
        if (_busy) return;
        var room = Selected(); if (room == null) { ShowError("Selecciona una sala."); return; }
        if (room.InProgress && room.BestOf > 1) { ShowError("Esta serie está en curso. Actualiza las salas para entrar en la siguiente."); return; }
        if (room.Players >= room.MaxPlayers) { ShowError("La sala está llena. Crea otra o actualiza la lista."); return; }
        if (_serverPasswordRequired && _serverPassword.Length == 0)
        {
            ShowError("Falta la contraseña del SERVIDOR (campo 1). Es distinta de la de la sala.");
            return;
        }
        string roomPassword = _joinPassword.Text;
        if (room.PasswordRequired && roomPassword.Length == 0)
        {
            ShowError("Falta la contraseña de la SALA (campo 2). Usa la clave con la que se creó.");
            return;
        }
        string serverPassword = _serverPasswordEntry.Text;
        Loading("Entrando en " + room.Name + "…");
        _ = _main.ConnectOnline(_address, _playerName, Ui.SelectedTeam(_teamChoice),
            serverPassword, room.Id, roomPassword, _wizardType, error => { if (Active) ShowError(error); });
    }

    private async void Create()
    {
        if (_serverStarting) { ShowStartup(); return; }
        if (_busy) return;
        int team = Ui.SelectedTeam(_teamChoice);
        var request = new CreateRoomRequest { Name = _roomName.Text, Mode = (GameMode)_mode.GetSelectedId(), Map = (ArenaMapId)_map.GetSelectedId(),
            MaxPlayers = (int)_capacity.Value, Bots = (int)_bots.Value, FriendlyFire = _friendly.ButtonPressed,
            ScoreLimit = (int)_score.Value, Tickets = (int)_tickets.Value,
            BestOf = (GameMode)_mode.GetSelectedId() is GameMode.Arena or GameMode.Defense ? 1 : _bestOf.GetSelectedId(),
            DefenseFlow = DefenseFlow.Continuous,
            Password = _roomPassword.Text, ServerPassword = _serverPasswordEntry.Text };
        Loading("Creando sala…");
        try
        {
            if (UsesPvpDirectory) _address = await PublicAccountServer.Resolve(PublicAccountServer.Project, _lifetime.Token);
            string accountToken = await _main.AccountTokenForRoomServer(_address, _lifetime.Token);
            var room = await RoomBrowser.CreateAsync(_address, request, _lifetime.Token, accountToken);
            _main.Post(() =>
            {
                if (!Active) return;
                _ = _main.ConnectOnline(_address, _playerName, team, request.ServerPassword,
                    room.Id, request.Password, _wizardType,
                    error => { if (Active) ShowError("Sala creada, pero no se pudo entrar: " + error); });
            });
        }
        catch (Exception e) { Failure(e); }
    }

    private void Failure(Exception e)
    {
        if (_lifetime.IsCancellationRequested) return;
        if (e is ServerStartingException)
        {
            _main.Post(() =>
            {
                if (!Active) return;
                _serverStarting = true; _startupMessage = PvpServer.DisplayText(e.Message); ShowStartup();
                if (!_startupRetry) { _startupRetry = true; _ = RetryAfterStartup(); }
            });
            return;
        }
        Diagnostics.Warn("lobby.operation_failed", "Fallo en la gestión de salas.", e);
        string message = e is InvalidOperationException or ArgumentException ? e.Message
            : "No se pudo consultar el servidor. Comprueba la conexión a Internet y reintenta.";
        _main.Post(() => { if (Active) ShowError(message); });
    }

    private void ShowStartup()
    {
        SetBusy(false); _status.Text = _startupMessage; _status.AddThemeColorOverride("font_color", Ui.Gold);
    }

    private async Task RetryAfterStartup()
    {
        try
        {
            await Task.Delay(2000, _lifetime.Token);
            _main.Post(() => { _startupRetry = false; if (Active && _serverStarting && !_busy) Refresh(); });
        }
        catch (OperationCanceledException) { }
    }

    private void Close() { _lifetime.Cancel(); _main.CancelPendingConnection(); QueueFree(); }

    public override void _ExitTree() { GetViewport().SizeChanged -= UpdateRoomResponsive; _lifetime.Cancel(); _lifetime.Dispose(); }
}

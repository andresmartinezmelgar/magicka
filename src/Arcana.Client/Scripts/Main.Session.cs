using System.Collections.Concurrent;
using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private string GoogleConnectionMessage() =>
        GoogleIdentityKnown
            ? "Google aparece conectado, pero la sesión Firebase no está disponible para jugar. Abre Perfil y comprueba que Firestore haya cargado sin errores; después reintenta Google."
            : "Debes iniciar sesión con Google en Perfil antes de entrar a una sala con recompensas.";

    public async Task ConnectOnline(string address, string name, int team, string password,
        string roomId = "default", string roomPassword = "", WizardType wizardType = WizardType.Balanced,
        Action<string>? onError = null, string resumeToken = "", Action? onConnected = null)
    {
        if (Volatile.Read(ref _exiting) != 0 || !CanStartRegularMatch()) return;
        var connection = new ArenaConnection();
        string connectionAddress = address;
        connection.AccountChanged += message => _ = RefreshRewardAccount(message);
        connection.Disconnected += reason => Post(() => { if (_connection == connection) _ = RecoverConnection(connection, connectionAddress, name, team, password, roomId, roomPassword, wizardType, reason); });
        ArenaConnection? previous;
        lock (_connectionGate) { previous = _pendingConnection; _pendingConnection = connection; }
        if (previous != null) _ = previous.DisposeAsync();
        try
        {
            string destination = address;
            Uri endpoint = ArenaConnection.Address(destination);

            // La búsqueda LAN descubre salas mediante ws://, pero NO debe enviar
            // el ID token Firebase por un transporte sin cifrar. Si el servidor
            // LAN exige Google, localizar su misma instancia pública en Firestore
            // y cambiar a WSS antes de enviar cualquier credencial.
            if (resumeToken.Length == 0 && endpoint.Scheme == "ws" && !endpoint.IsLoopback)
            {
                var localRooms = await RoomBrowser.ListAsync(destination, _lifetime.Token).ConfigureAwait(false);
                if (localRooms.Server.RewardsAvailable)
                {
                    if (!Accounts.IsDirectFirebaseSession)
                        throw new InvalidOperationException(GoogleConnectionMessage());
                    var (officialUrl, officialRooms) = await PublicAccountServer.ForInstance(localRooms.Server, _lifetime.Token).ConfigureAwait(false);
                    if (!PublicAccountServer.SameInstance(localRooms.Server, officialRooms.Server) ||
                        !officialRooms.Rooms.Any(room => room.Id == roomId))
                        throw new InvalidOperationException("Esta sala LAN no coincide con el servidor seguro anunciado en Firebase. Usa «Servidor online con Google».");
                    destination = officialUrl;
                    Diagnostics.Info("client.secure_route", "Sala LAN redirigida al mismo servidor por WSS.");
                }
            }

            string accountToken = Accounts.TokenFor(destination);
            if (accountToken.Length == 0 && Accounts.IsDirectFirebaseSession && ArenaConnection.Address(destination).Scheme == "wss")
            {
                accountToken = await AccountTokenForRoomServer(destination, _lifetime.Token).ConfigureAwait(false);
                if (accountToken.Length == 0)
                    throw new InvalidOperationException("Este servidor no coincide con el servidor oficial anunciado en Firebase.");
            }
            // Las salas WSS sin Firebase continúan admitiendo invitados.
            // Solo el servidor que exige cuentas debe bloquear un token vacío.
            if (resumeToken.Length == 0 && accountToken.Length == 0 && ArenaConnection.Address(destination).Scheme == "wss")
            {
                var publicRooms = await RoomBrowser.ListAsync(destination, _lifetime.Token).ConfigureAwait(false);
                if (publicRooms.Server.RewardsAvailable)
                    throw new InvalidOperationException(GoogleConnectionMessage());
            }
            connectionAddress = destination;
            await connection.ConnectAsync(destination, name, team, password, _lifetime.Token, roomId: roomId, roomPassword: roomPassword,
                wizardType: wizardType, deferReady: true, accountToken: accountToken, resumeToken: resumeToken).ConfigureAwait(false);
            if (!Post(() =>
            {
                lock (_connectionGate)
                {
                    if (_pendingConnection != connection) { _ = connection.DisposeAsync(); return; }
                    _pendingConnection = null;
                }
                if (!connection.Connected) { _ = connection.DisposeAsync(); _launcher.ShowError("La conexión se cerró antes de entrar en la partida."); return; }
                if (_connection != null) _ = _connection.DisposeAsync();
                _connection = connection; _practice = null; _practiceSession = null; _localId = connection.PlayerId;
                _serverName = PvpServer.DisplayText((connection.Server?.Name ?? PvpServer.Name) + " / " + (connection.Room?.Name ?? "Sala principal"));
                CancelReconnect(); StartGame(connection.Latest!, resumeToken.Length > 0); onConnected?.Invoke();
            })) await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            await connection.DisposeAsync();
            lock (_connectionGate) if (_pendingConnection != connection || _lifetime.IsCancellationRequested) return;
            Diagnostics.Warn("client.connect_failed", "No se pudo entrar en la partida.", e,
                new Dictionary<string, object?> { ["room"] = roomId });
            string message = e is OperationCanceledException ? "El servidor no responde. Comprueba la conexión a Internet y reintenta."
                : e is ArgumentException or InvalidOperationException ? e.Message
                : "No se pudo conectar con el servidor. Comprueba la conexión a Internet y reintenta.";
            Post(() =>
            {
                lock (_connectionGate)
                {
                    if (_pendingConnection != connection) return;
                    _pendingConnection = null;
                }
                if (onError != null) onError(message);
                else _launcher.ShowError(message);
            });
        }
    }

    public void CancelPendingConnection()
    {
        ArenaConnection? pending;
        lock (_connectionGate) { pending = _pendingConnection; _pendingConnection = null; }
        if (pending != null) _ = pending.DisposeAsync();
    }

    public void StartPractice(string name, int team, GameMode mode, int bots, WizardType wizardType = WizardType.Balanced,
        ArenaMapId map = ArenaMapId.Classic, DefenseFlow defenseFlow = DefenseFlow.Continuous)
    {
        if (!CanStartRegularMatch()) return;
        _resumingSurvival = null;
        CancelPendingConnection();
        var old = _connection; _connection = null;
        if (old != null) _ = old.DisposeAsync();
        _practiceSession = map == ArenaMapId.Practice ? new PracticeSession(name, wizardType) : null;
        if (_practiceSession != null)
        {
            _practice = _practiceSession.World; _localId = _practiceSession.Player.Id; _localSequence = 0;
            _serverName = "Práctica · " + _practice.Map.Name;
            ApplyPracticeEquipment(); StartGame(_practice.Snapshot()); return;
        }
        _practice = new(new WorldOptions { Mode = mode, Map = map, MaxPlayers = mode == GameMode.Defense ? 4 : mode == GameMode.Duel ? 2 : 8,
            FriendlyFire = mode != GameMode.Defense, ScoreLimit = mode == GameMode.Duel ? 5 : 15, Tickets = 75, DefenseFlow = defenseFlow });
        var player = _practice.AddPlayer(name, team, wizardType: wizardType)!; _localId = player.Id;
        ApplyPracticeEquipment();
        for (int i = 0; i < (mode == GameMode.Defense ? Math.Clamp(bots, 0, 3) : mode == GameMode.Duel ? 1 : Math.Clamp(bots, 1, 7)); i++)
            _practice.AddPlayer($"Bot {i + 1}", -1, true, (WizardType)(i % 5));
        _serverName = "Práctica · " + _practice.Map.Name; _localSequence = 0;
        Diagnostics.Info("practice.start", "Práctica iniciada.", new Dictionary<string, object?> { ["mode"] = mode, ["bots"] = bots, ["wizardType"] = wizardType });
        StartGame(_practice.Snapshot());
    }

    private void ActivateGame(WorldSnapshot snapshot, bool prepared = false)
    {
        _lastElementSelectionReport = null;
        _playing = true; _paused = false; _moveTo = false; _snapshotAge = 0; _lastTick = -1;
        SynchronizeAndroidSession();
        ClosePlayerEquipment();
        BeginLocalBestiary();
        _inputTime = 0;
        _networkStatsTime = 0; _statsConnection = null; _previousSent = _previousReceived = 0;
        Diagnostics.Info("game.start", "Partida iniciada.", Context());
        _ignoreLeftUntilRelease = true;
        _launcher.Visible = false; _launcher.CloseRooms(); _launcher.SetBusy(false); _help.Visible = _pause.Visible = false;
        if (GodotObject.IsInstanceValid(_equipmentPanel)) _equipmentPanel!.QueueFree(); _equipmentPanel = null;
        if (!prepared) _renderer.Clear(); _snapshot = snapshot;
        var local = snapshot.Players.FirstOrDefault(p => p.Id == _localId);
        _localWasAlive = local?.Alive ?? false; _localDeaths = local?.Deaths ?? 0;
        ResetPersonalDamageSummary();
        if (!prepared) PrepareGameControls(snapshot);
        _ui.MoveChild(_networkPanel, -1);
        _ui.MoveChild(_help, -1); _ui.MoveChild(_pause, -1);
        if (_connection != null)
        {
            _waitingPanel?.QueueFree();
            _waitingPanel = new RoomWaitingPanel();
            _ui.AddChild(_waitingPanel);
            _waitingPanel.Build(Submit, () => ReturnToLauncher());
            _waitingPanel.Update(snapshot, _localId, _connection.Server?.RewardsAvailable == true);
            _ui.MoveChild(_waitingPanel, -1);
        }
        // El punto anterior permanece disponible mientras carga; activarlo renueva el autoguardado.
        AutosaveSurvival("start");
        SynchronizeElementSelectionStatistics();
    }

    public void ReturnToLauncher(string error = "")
    {
        AutosaveSurvival("leave");
        CaptureStatistics(MatchRecordStatus.Left);
        CancelReconnect();
        CancelGameLoading();
        CancelPendingConnection();
        CaptureLocalBestiary(); _localBestiaryRun = ""; _localBestiaryUid = ""; _bestiaryClock = 10; _resumingSurvival = null;
        CloseRelics();
        ClosePersonalDamageSummary();
        if (_playing) Diagnostics.Info("game.leave", "Vuelta al launcher.", Context());
        ClosePlayerEquipment();
        _defensePanel?.QueueFree(); _defensePanel = null;
        EndTutorial();
        _playing = false; _paused = false; _practice = null; _practiceSession = null; _snapshot = null;
        _aimMarker.Hide();
        _practicePanel?.QueueFree(); _practicePanel = null;
        var old = _connection; _connection = null;
        if (old != null) _ = CloseStatisticsConnection(old);
        SynchronizeAndroidSession();
        _hud?.QueueFree(); _hud = null;
        _waitingPanel?.QueueFree(); _waitingPanel = null;
        _pause.Visible = false; _help.Visible = false;
        _networkPanel.Visible = false; _statsConnection = null; _networkStatsTime = 0;
        _touch?.Reset(); _touch?.QueueFree(); _touch = null;
        _renderer.Clear(); _launcher.Visible = true; _launcher.SetBusy(false); _launcher.UpdateSurvivalSave();
        if (error.Length > 0) _launcher.ShowError(error);
    }
}

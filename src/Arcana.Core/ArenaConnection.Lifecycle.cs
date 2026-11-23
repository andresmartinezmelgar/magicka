using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Arcana.Core;

public sealed partial class ArenaConnection
{
    public Task ConnectAsync(string address, string name, int team, string password, CancellationToken token = default,
        string roomId = "default", string roomPassword = "", WizardType wizardType = WizardType.Balanced, bool deferReady = false, string accountToken = "", string resumeToken = "")
    {
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
            if (_connecting != null) throw new InvalidOperationException("Esta conexión ya se ha iniciado.");
            if (!Enum.IsDefined(wizardType)) throw new ArgumentOutOfRangeException(nameof(wizardType));
            return _connecting = ConnectCore(address, name, team, password, token, roomId, roomPassword, wizardType, deferReady, accountToken, resumeToken);
        }
    }

    private async Task ConnectCore(string address, string name, int team, string password, CancellationToken token,
        string roomId, string roomPassword, WizardType wizardType, bool deferReady, string accountToken, string resumeToken)
    {
        Diagnostics.Log.ProtectSecret(password); Diagnostics.Log.ProtectSecret(roomPassword);
        Diagnostics.Log.ProtectSecret(accountToken); Diagnostics.Log.ProtectSecret(resumeToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(accountToken.Length > 0 ? 25 : 7));
        try
        {
            var endpoint = Address(address);
            Diagnostics.Info("network.connect", "Iniciando conexión.", new Dictionary<string, object?> { ["endpoint"] = endpoint, ["roomId"] = roomId, ["connection"] = _connectionId });
            _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            if (resumeToken.Length != 0) _socket.Options.SetRequestHeader("X-Arcana-Version", BuildInfo.Version);
            await _socket.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
            var hello = new ClientMessage { Type = "hello", CompactActions = true, Name = name, Team = team, Password = password,
                RoomId = roomId, RoomPassword = roomPassword, Protocol = Protocol.Version, GameVersion = BuildInfo.Version, WizardType = wizardType, AccountToken = accountToken, ResumeToken = resumeToken };
            await SendFrame(hello, timeout.Token).ConfigureAwait(false);
            var welcome = await Read(timeout.Token).ConfigureAwait(false);
            if (welcome?.Type == "error") throw new InvalidOperationException(welcome.Error);
            if (welcome is not { Type: "welcome", Snapshot: not null, Room: not null } || welcome.PlayerId <= 0 ||
                !LobbyValidation.IsValid(welcome.Server) || !welcome.Snapshot.Players.Any(p => p.Id == welcome.PlayerId))
                throw new InvalidDataException("El servidor no ha enviado un saludo válido.");
            LobbyValidation.Check(welcome.Room);
            if (!RoomGeneration.Matches(roomId, welcome.Room, welcome.Server!) || welcome.Room.Mode != welcome.Snapshot.Mode || welcome.Room.Map != welcome.Snapshot.Map ||
                welcome.Room.ScoreLimit != welcome.Snapshot.ScoreLimit || welcome.Room.Tickets != welcome.Snapshot.StartingTickets ||
                welcome.Room.Players != welcome.Snapshot.Players.Length)
                throw new InvalidDataException("El saludo no corresponde a la sala solicitada.");
            if (!string.Equals(welcome.Server!.GameVersion, BuildInfo.Version, StringComparison.Ordinal))
                throw new InvalidOperationException($"Versión incompatible. Servidor: {welcome.Server.GameVersion ?? "desconocida"}; cliente: {BuildInfo.Version}.");
            PlayerId = welcome.PlayerId; Server = welcome.Server; Room = welcome.Room;
            _compactActions = welcome.CompactActions;
            _requiresReady = welcome.RequiresReady;
            ApplyReadiness(welcome);
            _snapshotReceiver = new(welcome); ResumeToken = welcome.ResumeToken;
            Diagnostics.Log.ProtectSecret(ResumeToken);
            Volatile.Write(ref _latest, welcome.Snapshot); Volatile.Write(ref _connected, 1);
            Diagnostics.Info("network.connected", "Conexión establecida.", Context());
            _writer = WriteLoop(); _reader = ReadLoop();
            if (!deferReady && !CompleteLoading()) throw new IOException("No se pudo confirmar la preparación del cliente.");
        }
        catch (Exception e)
        {
            Diagnostics.Warn("network.connect_failed", "No se pudo establecer la conexión.", e, Context());
            Volatile.Write(ref _connected, 0); _socket.Abort(); throw;
        }
    }

    public void ReconnectAfterStall() => Fail("Restableciendo la conexión…", null, "network.stalled");

    public bool CompleteLoading()
    {
        if (!Connected) return false;
        if (!_requiresReady || Interlocked.Exchange(ref _readyRequested, 1) != 0) return true;
        if (Send(new() { Type = "ready" })) return true;
        Volatile.Write(ref _readyRequested, 0); return false;
    }

    private void ApplyReadiness(ServerMessage message)
    {
        if (!_requiresReady) return;
        if (message.ReadyPlayerIds == null) throw new InvalidDataException("Falta el estado de preparación de la sala.");
        Volatile.Write(ref _preparingPlayers, message.PreparingPlayers);
        Volatile.Write(ref _ready, message.ReadyPlayerIds.Contains(PlayerId) ? 1 : 0);
    }

    private void Fail(string reason, Exception? error, string code)
    {
        Volatile.Write(ref _connected, 0);
        if (Volatile.Read(ref _disposing) == 0 && Interlocked.Exchange(ref _notified, 1) == 0)
        {
            Diagnostics.Warn(code, reason, error, Context());
            try { Disconnected?.Invoke(reason); } catch (Exception e) { Diagnostics.Error("network.callback_failed", "Fallo en el manejador de desconexión.", e, Context()); }
        }
        _lifetime.Cancel(); _outgoing.Writer.TryComplete(); _socket.Abort();
    }

    private static void CheckReadiness(ServerMessage message)
    {
        var players = message.Snapshot!.Players;
        if (message.PreparingPlayers is < 0 or > 16 || message.PreparingPlayers > players.Length ||
            message.ReadyPlayerIds is { Length: > 16 })
            throw new InvalidDataException("Estado de preparación de sala inválido.");
        if (message.ReadyPlayerIds is not { } ids) return;
        for (int i = 0; i < ids.Length; i++)
        {
            bool found = false;
            foreach (var player in players) if (player.Id == ids[i]) { found = true; break; }
            if (!found) throw new InvalidDataException("Jugador preparado desconocido.");
            for (int j = 0; j < i; j++) if (ids[j] == ids[i]) throw new InvalidDataException("Jugador preparado repetido.");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposal ??= DisposeCore());
    }

    private async Task DisposeCore()
    {
        Interlocked.Exchange(ref _disposing, 1); Volatile.Write(ref _connected, 0); _outgoing.Writer.TryComplete();
        if (_connecting is { IsCompleted: false })
        {
            _lifetime.Cancel(); _socket.Abort();
            try { await _connecting.ConfigureAwait(false); }
            catch (Exception) { } // ConnectCore ya registra y propaga el fallo a quien inició la conexión.
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await _socketSend.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Salir", timeout.Token).ConfigureAwait(false);
            }
            finally { _socketSend.Release(); }
            // Deja que el servidor reciba la salida normal y confirme el cierre.
            // Abortar inmediatamente puede convertirla en una reserva por pérdida de red.
            if (_reader != null) await _reader.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        _lifetime.Cancel(); _socket.Abort();
        if (_reader != null) await _reader.ConfigureAwait(false);
        if (_writer != null) await _writer.ConfigureAwait(false);
        Diagnostics.Info("network.disposed", "Conexión cerrada.", Context());
        _socket.Dispose(); _lifetime.Dispose(); _socketSend.Dispose(); _receiveStream.Dispose();
    }
}

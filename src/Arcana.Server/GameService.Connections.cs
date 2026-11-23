using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Arcana.Core;

namespace Arcana.Server;

public sealed partial class GameService
{
    public async Task HandleConnection(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        if (!await _handshakes.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 503; return; }
        bool handshakeSlotHeld = true;
        WebSocket? socket = null;
        PlayerSession? session = null;
        string connectionKey = Guid.NewGuid().ToString("N");
        Dictionary<string, object?> Context() => new() { ["connection"] = connectionKey, ["session"] = session?.Key,
            ["room"] = session?.Room.Id, ["player"] = session?.Id, ["tick"] = CurrentTick };
        async Task RejectLogged(string reason, CancellationToken token)
        {
            Diagnostics.Warn("connection.rejected", reason, context: Context());
            await Reject(socket!, reason, token);
        }
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            var receiveBuffer = new byte[Protocol.MaxMessageBytes + 1];
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var hello = await Receive(socket, receiveBuffer, handshake.Token);
            if (hello is not { Type: "hello" } || hello.Protocol != Protocol.Version)
            { await RejectLogged("La versión del cliente no coincide con la del servidor.", handshake.Token); return; }
            if (!string.Equals(hello.GameVersion, BuildInfo.Version, StringComparison.Ordinal))
            { await RejectLogged($"Versión incompatible. Servidor: {BuildInfo.Version}; cliente: {hello.GameVersion}. Actualiza el juego.", handshake.Token); return; }
            if (!Enum.IsDefined(hello.WizardType))
            { await RejectLogged("Tipo de mago desconocido.", handshake.Token); return; }
            if (!PasswordMatches(hello.Password, settings.Password))
            { await RejectLogged("Contraseña del servidor incorrecta.", handshake.Token); return; }
            AccountIdentity? identity = null; PreparedEquipment equipment = new(EquipmentModifiers.None, PublicEquipment.Empty);
            bool testGuest = Environment.GetEnvironmentVariable("ARCANA_CI_LOOPBACK_GUESTS") == "1" &&
                System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? System.Net.IPAddress.None);
            // Los servidores locales sin cuentas admiten invitados (sin recompensas
            // ni inventario persistente). Los servidores con Firebase requieren Google.
            if (hello.AccountToken.Length == 0 && accounts.GameEnabled && !testGuest)
            { await RejectLogged("Debes iniciar sesión con Google para jugar online.", handshake.Token); return; }
            if (hello.AccountToken.Length > 0 && accounts.GameEnabled)
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(20));
                Diagnostics.Log.ProtectSecret(hello.AccountToken);
                identity = await accounts.AuthenticateGame(hello.AccountToken, handshake.Token);
                if (identity == null) { await RejectLogged("La sesión de cuenta ha caducado. Inicia sesión de nuevo con Google.", handshake.Token); return; }
                var profile = await accounts.ProfileGame(identity, handshake.Token);
                equipment = PublicEquipment.Prepare(profile);
            }
            if (hello.ResumeToken == null || hello.ResumeToken.Length is not (0 or 64) || hello.ResumeToken.Any(c => !Uri.IsHexDigit(c)))
            { await RejectLogged("Identificador de reconexión inválido.", handshake.Token); return; }
            string error = ""; ServerMessage? welcome = null;
            Diagnostics.Log.ProtectSecret(hello.ResumeToken);
            lock (_gate)
            {
                string? roomKey = hello.RoomId == _rooms["default"].Id ? "default" : hello.RoomId;
                if (roomKey == null || !_rooms.TryGetValue(roomKey, out var room)) error = "La sala ya no existe. Actualiza la lista.";
                else if (!PasswordMatches(hello.RoomPassword, room.Password)) error = "Contraseña de la sala incorrecta.";
                else
                {
                    ExpireReservations(room);
                    var active = hello.ResumeToken.Length == 64 ? room.Sessions.Values.FirstOrDefault(s =>
                        s.ResumeToken == hello.ResumeToken && s.Account?.Uid == identity?.Uid) : null;
                    bool resume = room.Reservations.TryGetValue(hello.ResumeToken, out var reservation) && reservation.Account?.Uid == identity?.Uid;
                    if (!AcceptingNewGames && !(room.Started && !Completed(room) && (resume || active != null))) error = UpdatingMessage;
                    else if (active != null) { active.Socket.Abort(); error = "Cerrando la conexión anterior. Reintentando…"; }
                    else if (hello.ResumeToken.Length > 0 && !resume) error = "Ha terminado el plazo para recuperar esta partida.";
                    else if (identity != null && _sessions.Values.Any(s => s.Account?.Uid == identity.Uid)) error = "Tu cuenta ya está jugando en este servidor.";
                    else if (!resume && identity != null && _rooms.Values.Any(r => r.Reservations.Values.Any(v => v.Account?.Uid == identity.Uid)))
                        error = "Tu cuenta tiene una partida reservada para reconexión. Recupera esa partida o espera a que termine el plazo.";
                    else if (!resume && room.BestOf > 1 && room.Started) error = "La serie está en curso. Espera al siguiente encuentro o crea otra sala.";
                    else
                    {
                        var player = resume ? room.World.Players.GetValueOrDefault(reservation!.Id)
                            : room.World.AddPlayer(hello.Name, hello.Team, wizardType: hello.WizardType);
                        if (player == null) error = "La sala está llena o el personaje ya no está disponible.";
                        else
                        {
                            room.World.SetDefensePlayerActive(player.Id, false);
                            bool restoredDefense = false;
                            if (resume) room.Reservations.Remove(hello.ResumeToken);
                            else
                            {
                                room.World.EquipPlayer(player.Id, equipment);
                                if (identity != null) restoredDefense = room.World.AttachDefenseAccount(player.Id, identity.Uid);
                            }
                            session = new(player.Id, room, socket) { Account = identity, Equipment = resume ? reservation!.Equipment : equipment,
                                LastActivityTick = resume ? reservation!.LastActivityTick : room.World.Tick,
                                ResumeToken = resume ? hello.ResumeToken : Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                                Resumed = resume, RestoredDefense = restoredDefense,
                                ReconnectUntil = resume ? reservation!.ExpiresAt : 0, LobbyReady = resume && reservation!.LobbyReady };
                            Diagnostics.Log.ProtectSecret(session.ResumeToken);
                            if (room.HostPlayerId == 0) room.HostPlayerId = player.Id;
                            room.Sessions.Add(player.Id, session); _sessions[session.Key] = session; room.LastOccupied = Environment.TickCount64;
                            if (!resume && !restoredDefense) player.Invulnerable = 1.2f;
                            welcome = DefenseFrame(RoomSnapshot(room), player.Id); welcome.Type = "welcome"; welcome.PlayerId = player.Id;
                            welcome.CompactActions = hello.CompactActions;
                            welcome.ResumeToken = session.ResumeToken; welcome.Server = Info(); welcome.Room = room.Info(); welcome.RequiresReady = true;
                            session.Network = new(welcome);
                        }
                    }
                }
            }

            if (session == null) { await RejectLogged(error, handshake.Token); return; }
            // El límite protege solamente la fase de saludo. Las sesiones establecidas no deben ocuparlo.
            _handshakes.Release(); handshakeSlotHeld = false;
            await Send(socket, welcome!, handshake.Token);
            Diagnostics.Info("connection.accepted", "Jugador conectado.", Context());
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
            var sendTask = SendLoop(session, connection.Token);
            try
            {
                long window = Environment.TickCount64; int count = 0;
                while (socket.State == WebSocketState.Open && !connection.IsCancellationRequested)
                {
                    var message = await Receive(socket, receiveBuffer, connection.Token);
                    if (message == null) break;
                    long now = Environment.TickCount64; Volatile.Write(ref session.LastPacketAt, now);
                    if (now - window >= 1000) { window = now; count = 0; }
                    if (++count > 180)
                    { Diagnostics.Warn("connection.rate_limit", "Límite de mensajes o cola de comandos alcanzado.", context: Context()); socket.Abort(); break; }
                    if (message.Type == "baseline_ack") { session.Network.Acknowledge(message.BaselineId); }
                    else if (message.Type == "ping")
                    {
                        if (message.PingId > 0 && now - session.LastPingAt >= 500)
                        {
                            session.LastPingAt = now;
                            await SendSession(session, Protocol.SerializePong(message.PingId), connection.Token);
                        }
                    }
                    else if (message.Type == "statistics")
                    {
                        MatchStatisticsReport? report = null;
                        lock (_gate)
                        {
                            if (session.Ready && now - session.LastStatisticsAt >= 250)
                            { session.LastStatisticsAt = now; report = session.Room.World.MatchStatistics(); statistics.Record(report); }
                        }
                        if (report != null) await SendSession(session, Protocol.SerializeSnapshot(new ServerMessage { Type = "statistics", Statistics = report }), connection.Token);
                    }
                    else if (message.Type == "inspect")
                    {
                        var response = new ServerMessage { Type = "equipment", InspectionRequestId = message.InspectionRequestId };
                        lock (_gate)
                        {
                            if (now - session.LastInspectionAt >= 1000) { session.LastInspectionAt = now; session.InspectionsInWindow = 0; }
                            if (++session.InspectionsInWindow > 10) response.Error = "Espera un momento antes de consultar otra ficha.";
                            else if (!session.Ready || message.InspectionRequestId <= 0 || !session.Room.World.Players.TryGetValue(message.InspectPlayerId, out var target))
                                response.Error = "Este mago ya no está disponible en la sala.";
                            else response.Inspection = new(target.Id, target.Name, target.WizardType, target.Bot, target.PublicEquipment);
                        }
                        // Respuesta fiable fuera de la simulación; no pasa por la cola de snapshots descartables.
                        await SendSession(session, Protocol.SerializeUtf8(response), connection.Token);
                    }
                    else if (!session.Commands.Writer.TryWrite(message))
                    { Diagnostics.Warn("connection.rate_limit", "Cola de comandos alcanzada.", context: Context()); socket.Abort(); break; }
                }
            }
            finally
            {
                connection.Cancel(); session.Outbox.Writer.TryComplete();
                try { await sendTask; } catch (OperationCanceledException) { }
                catch (WebSocketException e) { Diagnostics.Warn("connection.send_failed", "Fallo al enviar el estado.", e, Context()); }
            }
        }
        catch (OperationCanceledException)
        {
            if (!_stopping.IsCancellationRequested && !context.RequestAborted.IsCancellationRequested)
                Diagnostics.Warn("connection.timeout", "El saludo del cliente no llegó a tiempo.", context: Context());
        }
        catch (WebSocketException e) { Diagnostics.Warn("connection.transport_failed", "La conexión se interrumpió.", e, Context()); }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        { Diagnostics.Warn("connection.invalid_message", "El cliente envió un mensaje inválido.", e, Context()); }
        finally
        {
            if (session != null)
            {
                lock (_gate)
                {
                    _sessions.TryRemove(session.Key, out _);
                    CaptureStatistics(session.Room, MatchRecordStatus.Disconnected);
                    SaveSessionBestiary(session, force: true);
                    session.Room.Sessions.Remove(session.Id);
                    if ((session.Ready || session.Resumed) && socket?.CloseStatus != WebSocketCloseStatus.NormalClosure &&
                        !_stopping.IsCancellationRequested && !Completed(session.Room))
                    {
                        session.Room.World.SuspendPlayer(session.Id);
                        session.Room.Reservations[session.ResumeToken] = new(session.Id, session.Account, session.Equipment,
                            session.LastActivityTick, session.Ready ? Environment.TickCount64 + 30000 : session.ReconnectUntil, session.LobbyReady);
                    }
                    else RemovePlayerFromRoom(session.Room, session.Id);
                }
                Diagnostics.Info("connection.closed", "Jugador desconectado.", Context());
            }
            if (socket?.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fin de sesión", timeout.Token); }
                catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
            }
            socket?.Dispose();
            if (handshakeSlotHeld) _handshakes.Release();
        }
    }
}

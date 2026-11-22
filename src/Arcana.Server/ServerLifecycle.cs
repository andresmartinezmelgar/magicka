using System.Net;
using System.Security.Cryptography;
using System.Text;
using Arcana.Core;

namespace Arcana.Server;

public sealed record ServerAdmission(string InstanceId, bool AcceptingNewGames, string TargetVersion = "")
{
    internal static ServerAdmission Load(ServerSettings settings)
    {
        if (settings.AdmissionFile.Length == 0) return new(settings.InstanceId, true);
        var value = Protocol.Deserialize<ServerAdmission>(File.ReadAllText(settings.AdmissionFile));
        if (value == null || value.InstanceId != settings.InstanceId || value.TargetVersion is not { Length: <= 32 })
            throw new InvalidDataException("Estado de admisión del contenedor inválido.");
        return value;
    }
}
public sealed record ServerLifecycleStatus(string InstanceId, string Version, bool AcceptingNewGames,
    string TargetVersion, int ActiveGames, int ReconnectingPlayers, int Connections, int PendingWrites, bool SafeToStop);

internal static class ServerLifecycleEndpoints
{
    internal static void MapServerLifecycle(this WebApplication app)
    {
        static bool Authorized(HttpContext context, ServerSettings settings)
        {
            var remote = context.Connection.RemoteIpAddress ?? IPAddress.None;
            if (settings.ManagementToken.Length != 64 || !(IPAddress.IsLoopback(remote) ||
                settings.ManagementPeer.Length > 0 && IPAddress.TryParse(settings.ManagementPeer, out var peer) && remote.Equals(peer))) return false;
            var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-Arcana-Management"].ToString()));
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(settings.ManagementToken));
            return CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
        app.MapGet("/internal/lifecycle", (HttpContext context, ServerSettings settings, GameService game) =>
        {
            if (Authorized(context, settings)) return (IResult)Results.Json(game.Lifecycle(), Protocol.Json);
            return DeploymentEndpoints.Authorized(context)
                ? Results.Json(game.DeploymentState(), Protocol.Json) : Results.NotFound();
        });
        app.MapPost("/internal/admission", (ServerAdmission request, HttpContext context, ServerSettings settings, GameService game) =>
        {
            if (!Authorized(context, settings)) return (IResult)Results.NotFound();
            if (request.InstanceId != settings.InstanceId || request.TargetVersion is not { Length: <= 32 } ||
                request.TargetVersion.Length > 0 && !Version.TryParse(request.TargetVersion, out _))
                return Results.Json(new ApiError("Estado de admisión inválido."), Protocol.Json, statusCode: 400);
            game.SetAdmission(request);
            return Results.Json(game.Lifecycle(), Protocol.Json);
        });
    }
}

public sealed partial class GameService
{
    private ServerAdmission _admission = ServerAdmission.Load(settings);
    private long _emptySince;
    private bool AcceptingNewGames => !_draining && _admission.AcceptingNewGames;
    private string UpdatingMessage => "Iniciando servidor con la nueva versión" +
        (_admission.TargetVersion.Length > 0 ? " " + _admission.TargetVersion : "") + ". Las partidas en curso continúan; vuelve a consultar las salas.";
    internal static bool Completed(GameRoom room) => room.World.MatchEnded && (room.World.IsDefense || room.BestOf == 1 || room.SeriesFinished);
    public void SetAdmission(ServerAdmission admission)
    {
        lock (_gate)
        {
            if (admission.InstanceId != settings.InstanceId) throw new ArgumentException("Instancia incorrecta.");
            _admission = admission; _emptySince = 0;
            foreach (var room in _rooms.Values.Where(r => !r.Started)) room.StartRequested = false;
        }
        logger.LogInformation("Admisión de partidas: {Accepting}; destino {Target}", admission.AcceptingNewGames, admission.TargetVersion);
    }
    public ServerLifecycleStatus Lifecycle()
    {
        lock (_gate)
        {
            foreach (var room in _rooms.Values) ExpireReservations(room);
            int active = _rooms.Values.Count(r => r.Started && !Completed(r) && (r.Sessions.Count > 0 || r.Reservations.Count > 0));
            int pending = _persistenceWrites.Count + rewards.UnwrittenCount + (accounts.GameEnabled ? statistics.UnwrittenCount : 0);
            if (!AcceptingNewGames && active == 0)
            {
                foreach (var room in _rooms.Values)
                {
                    if (!room.StatisticsFinal && room.Started)
                    { CaptureStatistics(room, MatchRecordStatus.Disconnected); room.StatisticsFinal = true; }
                    foreach (var session in room.Sessions.Values)
                        if (!SaveSessionBestiary(session, force: true)) pending++;
                }
                pending = Math.Max(pending, _persistenceWrites.Count + rewards.UnwrittenCount + (accounts.GameEnabled ? statistics.UnwrittenCount : 0));
                if (_emptySince == 0) _emptySince = Environment.TickCount64;
            }
            else _emptySince = 0;
            // Permite entregar el resultado final antes de cerrar el transporte.
            bool safe = !AcceptingNewGames && active == 0 && pending == 0 && _emptySince > 0 && Environment.TickCount64 - _emptySince >= 10000;
            return new(settings.InstanceId, BuildInfo.Version, AcceptingNewGames, _admission.TargetVersion, active,
                _rooms.Values.Sum(r => r.Reservations.Count), _sessions.Count, pending, safe);
        }
    }
}

using Arcana.Core;

namespace Arcana.Server;

internal sealed record ReservedPlayer(int Id, AccountIdentity? Account, PreparedEquipment Equipment,
    long LastActivityTick, long ExpiresAt, bool LobbyReady);

public sealed partial class GameService
{
    private void ExpireReservations(GameRoom room)
    {
        foreach (var entry in room.Reservations.Where(p => p.Value.ExpiresAt <= Environment.TickCount64).ToArray())
        { room.Reservations.Remove(entry.Key); RemovePlayerFromRoom(room, entry.Value.Id); }
    }

    private static void RemovePlayerFromRoom(GameRoom room, int id)
    {
        room.World.RemovePlayer(id);
        if (room.HostPlayerId == id) room.HostPlayerId = room.Sessions.Keys.FirstOrDefault();
        if (room.BestOf > 1 && room.Started && !room.SeriesFinished)
        {
            room.World.ResetForWaitingRoom(); room.Started = room.StartRequested = false;
            room.SeriesWins[0] = room.SeriesWins[1] = 0;
            room.SeriesFinished = room.ResultQueued = false; room.MatchId = Guid.NewGuid().ToString("N");
            room.Participants.Clear(); room.AwaitingRematch = true;
            room.RewardStatus = "La serie se canceló al salir un jugador o agotarse el plazo de reconexión.";
            foreach (var remaining in room.Sessions.Values) remaining.LobbyReady = false;
        }
        if (!room.Started) room.StartRequested = false;
    }
}

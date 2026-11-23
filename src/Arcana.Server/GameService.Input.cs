using Arcana.Core;

namespace Arcana.Server;

public sealed partial class GameService
{
    // Called under _gate in the simulation phase; a session reads at most 32 commands per step.
    private int ProcessRoomInput(GameRoom room)
    {
        int readyPlayers = 0;
        foreach (var session in room.Sessions.Values)
        {
            if (session.Ready && Environment.TickCount64 - Volatile.Read(ref session.LastPacketAt) > 12000) session.Socket.Abort();
            if (session.Resumed && !session.Ready && Environment.TickCount64 > session.ReconnectUntil) session.Socket.Abort();
            if (!session.Ready && Environment.TickCount64 - session.LoadingStartedAt > Protocol.LoadingTimeoutSeconds * 1000L)
                session.Socket.Abort();
            for (int n = 0; n < 32 && session.Commands.Reader.TryRead(out var message); n++)
            {
                if (message.Type == "ready" && message.Sequence > 0)
                { session.Ready = true; room.World.SetDefensePlayerActive(session.Id, true); continue; }
                if (room.BestOf > 1 && !room.Started && session.Ready)
                {
                    if (message.Type == "lobby-team" && message.Team is 0 or 1)
                    {
                        if (room.World.SetPlayerTeam(session.Id, message.Team))
                        { session.LobbyReady = false; room.StartRequested = false; }
                        continue;
                    }
                    if (message.Type == "lobby-ready")
                    { session.LobbyReady = !session.LobbyReady; room.StartRequested = false; continue; }
                    if (message.Type == "lobby-start" && session.Id == room.HostPlayerId)
                    { room.StartRequested = AcceptingNewGames; continue; }
                }
                if (message.Type == "defense_restart" && room.World.IsDefense && session.Ready && session.Id == room.HostPlayerId)
                { RestartDefenseRoom(room); continue; }
                if (!room.Started && room.BestOf > 1) { _ignoredCommands++; continue; }
                if (!session.Ready || !room.World.ApplyMessage(session.Id, message)) _ignoredCommands++;
                else if (message.Type is not ("defense_performance" or "match_performance" or "spell_selection") &&
                    (message.Type != "input" || message.MoveTo || Math.Abs(message.MoveX) + Math.Abs(message.MoveY) > 0.05f))
                    session.LastActivityTick = room.World.Tick;
            }
            if (session.Ready) readyPlayers++;
        }
        return readyPlayers;
    }
}

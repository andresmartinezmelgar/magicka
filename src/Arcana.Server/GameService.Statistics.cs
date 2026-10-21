using Arcana.Core;

namespace Arcana.Server;

public sealed partial class GameService
{
    private void CaptureStatistics(GameRoom room, MatchRecordStatus status = MatchRecordStatus.Playing)
    {
        if (!room.Started) return;
        var report = room.World.MatchStatistics(status);
        statistics.Record(report);
        var message = Protocol.SerializeUtf8(new ServerMessage { Type = "statistics", Statistics = report });
        foreach (var session in room.Sessions.Values) session.Outbox.Writer.TryWrite(message);
    }
    private void TrackStatistics(GameRoom room)
    {
        string id = room.World.MatchStatisticsRunId;
        bool changed = room.StatisticsRun != id;
        if (changed) { room.StatisticsRun = id; room.StatisticsFinal = false; room.NextStatisticsTick = 0; }
        if (room.World.MatchEnded && !room.StatisticsFinal || !room.World.MatchEnded && room.World.Tick >= room.NextStatisticsTick)
        {
            CaptureStatistics(room);
            room.NextStatisticsTick = room.World.Tick + 900;
            room.StatisticsFinal = room.World.MatchEnded;
        }
    }
}

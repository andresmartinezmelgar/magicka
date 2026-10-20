using Arcana.Core;

namespace Arcana.Server;

public sealed partial class GameService
{
    private static ServerMessage DefenseFrame(ServerMessage frame, int playerId) => frame.Snapshot?.Defense == null ? frame : new()
    {
        Type = frame.Type, Snapshot = DefenseVisibility.ForPlayer(frame.Snapshot, playerId),
        PreparingPlayers = frame.PreparingPlayers, ReadyPlayerIds = frame.ReadyPlayerIds
    };

    private void RestartDefenseRoom(GameRoom room)
    {
        foreach (var session in room.Sessions.Values) SaveSessionBestiary(session, force: true);
        if (!room.World.RestartDefense()) return;
        foreach (var session in room.Sessions.Values) { session.BestiaryKillsSaved = 0; session.NextBestiaryAt = 0; }
        room.MatchId = Guid.NewGuid().ToString("N");
        room.DefenseRewards.Clear(); room.RewardStatus = "";
        foreach (var session in room.Sessions.Values)
            if (session.Account != null) room.World.AttachDefenseAccount(session.Id, session.Account.Uid);
    }

    private void SaveSessionBestiary(PlayerSession session, bool force = false)
    {
        if (session.Account == null || !session.Room.World.IsDefense) return;
        double now = session.Room.World.Time;
        if (!force && !session.Room.World.MatchEnded && now < session.NextBestiaryAt) return;
        session.NextBestiaryAt = now + 10;
        var progress = session.Room.World.BestiaryProgress(session.Id, session.Room.MatchId, session.Account.Uid);
        if (progress == null || progress.Kills.Values.Sum() <= session.BestiaryKillsSaved) return;
        if (rewards.EnqueueBestiary(progress)) session.BestiaryKillsSaved = progress.Kills.Values.Sum();
    }

    private void TrackDefenseAccounts(GameRoom room)
    {
        foreach (var session in room.Sessions.Values) SaveSessionBestiary(session);
        if (!room.Sessions.Values.Any(s => s.Account != null))
        {
            room.RewardStatus = "Los premios permanentes requieren una cuenta de Google verificada por el servidor.";
            return;
        }
        foreach (var session in room.Sessions.Values)
        {
            if (session.Account == null || !session.Ready) continue;
            float seconds = (float)room.World.DefenseActiveSeconds(session.Id);
            int earned = Math.Min(DefenseRules.RewardLimit, (int)(seconds / DefenseRules.RewardInterval));
            for (int checkpoint = 1; checkpoint <= earned; checkpoint++)
            {
                string id = RewardRules.DefenseReceiptId(room.MatchId, session.Account.Uid, checkpoint);
                if (room.DefenseRewards.Contains(id)) continue;
                var participant = new MatchParticipant(session.Account.Uid, session.Id, 0, seconds, true);
                var reward = new RewardMatch(id, settings.InstanceId, GameMode.Defense, ArenaMapId.Bastion,
                    DateTimeOffset.UtcNow, [participant], RulesVersion: 2, DefenseRunId: room.MatchId, DefenseCheckpoint: checkpoint);
                if (!rewards.Enqueue(reward))
                {
                    room.RewardStatus = "Premio pendiente de guardar: se reintentará automáticamente.";
                    continue;
                }
                room.DefenseRewards.Add(id);
                room.RewardStatus = $"{room.World.Players[session.Id].Name}: hito de defensa {checkpoint}/{DefenseRules.RewardLimit} guardado como pendiente de entrega.";
            }
        }
    }
}

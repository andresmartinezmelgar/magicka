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
        if (!room.World.RestartDefense()) return;
        room.MatchId = Guid.NewGuid().ToString("N");
        room.DefenseRewards.Clear(); room.RewardStatus = "";
        foreach (var session in room.Sessions.Values)
            if (session.Account != null) room.World.AttachDefenseAccount(session.Id, session.Account.Uid);
    }

    private void TrackDefenseAccounts(GameRoom room)
    {
        if (!accounts.GameEnabled)
        {
            room.RewardStatus = "Partida sin premios permanentes: este servidor no tiene Firebase habilitado.";
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
                if (!room.DefenseRewards.Add(id)) continue;
                var participant = new MatchParticipant(session.Account.Uid, session.Id, 0, seconds, true);
                var reward = new RewardMatch(id, settings.InstanceId, GameMode.Defense, ArenaMapId.Bastion,
                    DateTimeOffset.UtcNow, [participant], RulesVersion: 2, DefenseRunId: room.MatchId, DefenseCheckpoint: checkpoint);
                rewards.Enqueue(reward);
                room.RewardStatus = $"{room.World.Players[session.Id].Name}: hito de defensa {checkpoint}/{DefenseRules.RewardLimit} pendiente de guardar.";
            }
        }
    }
}

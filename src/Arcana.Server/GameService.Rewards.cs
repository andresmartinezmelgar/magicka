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
    private void TrackAccounts(GameRoom room, bool endedBefore)
    {
        if (room.World.IsDefense) { TrackDefenseAccounts(room); return; }
        if (endedBefore && !room.World.MatchEnded)
        {
            room.ResultQueued = false;
            if (room.SeriesFinished)
            {
                room.SeriesWins[0] = room.SeriesWins[1] = 0;
                room.SeriesFinished = false;
                room.MatchId = Guid.NewGuid().ToString("N");
                room.Participants.Clear();
                room.Started = false;
                room.StartRequested = false;
                room.AwaitingRematch = true;
                foreach (var participant in room.Sessions.Values) participant.LobbyReady = false;
            }
            foreach (var session in room.Sessions.Values)
                room.World.EquipPlayer(session.Id, session.Equipment);
        }

        if (!room.World.MatchEnded)
        {
            foreach (var session in room.Sessions.Values)
            {
                if (session.Account == null || !session.Ready ||
                    !room.World.Players.TryGetValue(session.Id, out var player)) continue;
                var account = session.Account;
                if (!room.Participants.TryGetValue(account.Uid, out var entry))
                    entry = new(account.Uid, session.Id, player.Team, 0, false)
                    {
                        ExtraGoldEvery = session.Equipment.Modifiers.ExtraGoldEvery,
                        GoldBonusPercent = session.Equipment.Modifiers.GoldBonusPercent
                    };
                room.Participants[account.Uid] = entry with
                {
                    PlayerId = player.Id, Team = player.Team,
                    ActiveSeconds = entry.ActiveSeconds + (room.World.Tick - session.LastActivityTick < 300 ||
                        player.Alive && (player.Channel != null || player.Charging != null ||
                        room.Options.Mode == GameMode.Warfare &&
                        room.World.Map.ControlPoints.Any(point => V2.Distance(player.Position, point) < 3))
                        ? GameWorld.StepSeconds : 0)
                };
            }
            return;
        }

        if (room.ResultQueued) return;
        room.ResultQueued = true;
        int roundWinner = room.World.Winner;
        int winningTeam = room.Options.Mode == GameMode.Arena
            ? room.World.Players.Values.FirstOrDefault(p => p.Id == roundWinner)?.Team ?? -1
            : roundWinner;
        room.SeriesFinished = winningTeam is 0 or 1 &&
            MatchSeries.RecordRound(room.SeriesWins, room.BestOf, winningTeam);
        if (!room.SeriesFinished) return;

        var participants = room.Participants.Values.Select(p => p with
        {
            Winner = room.Options.Mode == GameMode.Arena
                ? p.PlayerId == roundWinner
                : p.Team == winningTeam
        }).ToArray();
        int humans = room.World.Players.Values.Count(p => !p.Bot);
        // Premios solo al terminar toda la serie; ambos equipos humanos, mismo número,
        // sin bots, y cada participante autenticado con al menos 15 s de actividad.
        var match = new RewardMatch(room.MatchId, settings.InstanceId, room.Options.Mode,
            room.Options.Map, DateTimeOffset.UtcNow, participants, RulesVersion: 4);
        bool balanced = BalancedMatchRules.RewardsEnabled(
            room.World.Players.Values.Count(p => !p.Bot && p.Team == 0),
            room.World.Players.Values.Count(p => !p.Bot && p.Team == 1), 0, 0);
        if (!accounts.GameEnabled)
            room.RewardStatus = "Sin recompensa: el servidor no tiene Firebase habilitado.";
        else if (humans != participants.Length || room.World.Players.Values.Any(p => p.Bot) || !balanced)
            room.RewardStatus = "Sin recompensa: se requieren equipos humanos equilibrados, sin bots y con Google.";
        else if (RewardRules.Eligible(match).Length != participants.Length)
            room.RewardStatus = "Sin recompensa: todos deben acreditar al menos 15 s de actividad.";
        else
        {
            room.RewardStatus = "Recompensa pendiente de confirmar en Firebase.";
            room.PendingRewardWrites[match.Id] = PersistOutsideSimulation(() => Task.FromResult(rewards.Enqueue(match)));
        }
        _ = RefreshEquipment(room.Sessions.Values.ToArray());
    }

    private async Task RefreshEquipment(PlayerSession[] sessions)
    {
        foreach (var session in sessions)
        {
            if (session.Account == null) continue;
            try
            {
                var profile = await accounts.ProfileGame(session.Account, _stopping.Token);
                var equipment = PublicEquipment.Prepare(profile);
                lock (_gate) session.Equipment = equipment;
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
            catch (Exception error) { logger.LogWarning(error, "No se pudo actualizar el equipo para la siguiente partida."); }
        }
    }

    private void NotifyAccounts(RewardMatch match)
    {
        var eligible = RewardRules.Eligible(match);
        lock (_gate)
        {
            foreach (var room in _rooms.Values)
                if (room.MatchId == match.Id || match.Mode == GameMode.Defense && room.MatchId == match.DefenseRunId)
                    room.RewardStatus = match.Mode == GameMode.Defense
                        ? "Recompensa de defensa confirmada. Los premios dependen del tiempo activo individual."
                        : "Recompensas de oro y botín confirmadas en Firebase.";
            foreach (var session in _sessions.Values)
            {
                var participant = eligible.FirstOrDefault(p => p.Uid == session.Account?.Uid);
                if (participant == null) continue;
                session.Outbox.Writer.TryWrite(Protocol.SerializeUtf8(new ServerMessage { Type = "account",
                    AccountNotice = match.Mode == GameMode.Defense ? $"Recompensa de defensa {match.DefenseCheckpoint}/4 guardada. Revisa tu inventario." : "Recompensa PvP guardada. Revisa tu oro e inventario." }));
            }
        }
    }
}

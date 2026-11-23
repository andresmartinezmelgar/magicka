namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void TickCapture(float dt)
    {
        // Se fijan los dos objetivos antes de resolver el tick: nunca se captura una cadena de golpe.
        int blueTarget = ConquestToWin ? FrontierTarget(0) : -1;
        int redTarget = ConquestToWin ? FrontierTarget(1) : -1;
        foreach (var point in _points)
        {
            int blue = 0, red = 0;
            foreach (var player in _players.Values)
            {
                if (!player.Alive || !(V2.Distance(player.Position, point.Position) < 3)) continue;
                if (player.Team == 0) blue++; else if (player.Team == 1) red++;
            }
            point.Contested = blue > 0 && red > 0 || HoldToWin && (point.Owner == 0 && red > 0 || point.Owner == 1 && blue > 0);
            if (blue > 0 && red > 0 || blue + red == 0) continue;
            int team = red > 0 ? 1 : 0;
            if (ConquestToWin && point.Owner != team && point.Id != (team == 0 ? blueTarget : redTarget)) continue;
            int previousOwner = point.Owner;
            point.Progress = Math.Clamp(point.Progress + (red > 0 ? 1 : -1) * dt * 0.24f, -1, 1);
            if (point.Owner == 0 && point.Progress > -0.6f || point.Owner == 1 && point.Progress < 0.6f) point.Owner = -1;
            if (point.Progress <= -0.99f) point.Owner = 0;
            if (point.Progress >= 0.99f) point.Owner = 1;
            if (point.Owner >= 0 && point.Owner != previousOwner)
            {
                _statistics.Total(StatisticMeasure.Captures);
                foreach (var p in _players.Values) if (p.Alive && p.Team == point.Owner && V2.Distance(p.Position, point.Position) < 3) _statistics.Actor(p.Id, StatisticMeasure.Captures);
            }
        }
        if (ConquestToWin) return;
        if (HoldToWin)
        {
            int team = _points[0].Owner;
            if (team >= 0 && _points.All(p => p.Owner == team && !p.Contested))
            {
                if (HoldingTeam != team) { HoldingTeam = team; _holdTicks = 0; }
                _holdTicks++;
            }
            else { HoldingTeam = -1; _holdTicks = 0; }
            return;
        }
        _ticketClock += dt;
        if (_ticketClock < 1) return;
        _ticketClock -= 1;
        int advantage = _points.Count(p => p.Owner == 0) - _points.Count(p => p.Owner == 1);
        if (advantage > 0) Scores[1] = Math.Max(0, Scores[1] - advantage);
        if (advantage < 0) Scores[0] = Math.Max(0, Scores[0] + advantage);
    }

    private void CheckVictory()
    {
        if (Map.IsPractice || IsDefense) return;
        int winner = -1;
        if (Options.Mode == GameMode.Warfare)
        {
            if (ConquestToWin)
            {
                int team = _points[0].Owner;
                if (team >= 0 && _points.All(p => p.Owner == team && !p.Contested)) winner = team;
            }
            else if (HoldToWin)
            {
                if (_holdTicks >= Map.HoldSeconds * 60) winner = HoldingTeam;
            }
            else if (Scores[0] <= 0) winner = 1;
            else if (Scores[1] <= 0) winner = 0;
        }
        else if (Options.Mode == GameMode.Duel)
        {
            if (Scores[0] >= Options.ScoreLimit) winner = 0;
            if (Scores[1] >= Options.ScoreLimit) winner = 1;
        }
        else
        {
            var champion = _players.Values.FirstOrDefault(p => p.Kills >= Options.ScoreLimit);
            if (champion != null) winner = champion.Id;
        }
        if (winner < 0) return;
        Winner = winner; MatchEnded = true; RestartIn = 9;
        foreach (var p in _players.Values) CancelCast(p);
        Emit("victory", V2.Zero, [], "Partida terminada");
    }

    private void ResetScores()
    {
        Scores[0] = Scores[1] = Options.Mode == GameMode.Warfare && !Map.IsPractice && !ConquestToWin ? Options.Tickets : 0;
        _ticketClock = 0;
        _holdTicks = 0; HoldingTeam = -1; _routes.Clear();
    }

    /// <summary>El servidor puede cancelar una serie incompleta al perder jugadores.</summary>
    public void ResetForWaitingRoom() => ResetMatch();

    private void ResetMatch()
    {
        MatchEnded = false; Winner = -1; RestartIn = 0;
        _arcaneBursts.Clear();
        _lightningContacts.Clear();
        ResetStatistics();
        ResetScores(); _projectiles.Clear(); _walls.Clear(); _fields.Clear();
        ResetFrontline();
        ResetDefense();
        foreach (var point in _points) { point.Owner = -1; point.Progress = 0; point.Contested = false; }
        foreach (var item in _pickups) { item.Respawn = 0; if (Map.RandomPickups) RelocatePickup(item); }
        foreach (var p in _players.Values) { p.Kills = p.Deaths = 0; RelicBuffs.Clear(p); p.MaxHealth = Wizard.BaseMaxHealth; Respawn(p); p.Energy = 100; p.ReprisalCooldown = 0; Array.Clear(p.MagickCooldowns); }
        Emit("restart", V2.Zero, [], "Nueva partida");
    }

    private void Respawn(Wizard p)
    {
        if (IsDefense && p.Deaths >= DefenseRules.PlayerLives) return;
        ClearAutomaticRockProgress(p);
        PruneLightningContacts(p.Id);
        ClearArcaneTarget(p);
        _routes.Remove(p.Id);
        ResetSlide(p);
        p.Position = RespawnPosition(p); p.Health = p.MaxHealth;
        _statistics.Bind(p);
        if (Map.IsPractice)
            _walls.RemoveAll(w => Geometry.DistanceToSegment(p.Position, w.A, w.B) < ArenaMap.WizardRadius + 0.18f);
        p.Wet = p.Burning = p.Chill = p.Frozen = p.Haste = p.Shield = p.ShieldMax = p.ShieldTime = 0;
        p.LastCastInterruption = null;
        p.StunTime = p.WaterSlowTime = 0; p.WaterSlowRank = 0;
        p.FreezeRecovery = 0;
        p.RegenerationRate = 0;
        p.Ward = []; p.Knockback = V2.Zero; p.Selection.Clear(); CancelCast(p);
        p.BurnMultiplier = 1; p.HitEffectCooldown = 0;
        p.TeleportCooldown = 0;
        p.TeleportNeedsExit = false;
        p.Input.MoveTo = false; p.Input.MoveX = p.Input.MoveY = 0; p.Respawn = 0;
        p.ShieldCharges = 1;
        p.Invulnerable = _practiceSpawns.ContainsKey(p.Id) ? 0 : Options.Mode == GameMode.Arena ? 1 : 1.2f; p.ActionCooldown = 0;
    }

    private V2 RespawnPosition(Wizard p)
    {
        if (Map.IsPractice) return _practiceSpawns.GetValueOrDefault(p.Id, PracticeLayout.Respawn);
        if (IsDefense)
        {
            if (Walkable(p.Position)) return p.Position;
            for (float radius = 0.8f; radius <= 8; radius += 0.4f)
                for (int i = 0; i < 24; i++)
                {
                    float angle = i * MathF.Tau / 24;
                    var at = p.Position + new V2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                    if (Walkable(at)) return at;
                }
        }
        if (Options.Mode == GameMode.Arena) return ArenaSpawnPosition(p, p.Deaths > 0 ? p.Position : null);
        var preferred = TeamSpawn(p.Team, p.Id % 4);
        for (int offset = 0; offset < 4; offset++)
        {
            var spawn = TeamSpawn(p.Team, (p.Id + offset) % 4);
            if (Walkable(spawn)) return spawn;
        }
        // Si los cuatro puestos están cubiertos, busca suelo libre cerca de la base.
        V2 best = preferred; float nearest = float.PositiveInfinity;
        for (float x = -Map.HalfWidth + 1; x < Map.HalfWidth; x++)
        for (float y = -Map.HalfHeight + 1; y < Map.HalfHeight; y++)
        {
            var candidate = new V2(x, y); float distance = (candidate - preferred).LengthSquared;
            if (distance >= nearest || !Walkable(candidate)) continue;
            best = candidate; nearest = distance;
        }
        return best;
    }
}

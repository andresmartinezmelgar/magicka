namespace Arcana.Core;

/// <summary>JSON válido no garantiza un estado válido. Rechaza respuestas incompletas antes de llegar al motor gráfico.</summary>
public static class SnapshotValidation
{
    private static bool Elements(Element[]? values, int maximum = 3) => values != null && values.Length <= maximum && values.All(Enum.IsDefined);
    private static bool NonNegative(params float[] values) => values.All(v => float.IsFinite(v) && v >= 0);
    public static void Check(WorldSnapshot state)
    {
        if (!Enum.IsDefined(state.Mode) || !Enum.IsDefined(state.Map) || state.Tick < 0 || !double.IsFinite(state.Time) || state.Time < 0 || !NonNegative(state.RestartIn) ||
            state.HoldingTeam is < -1 or > 1 || !NonNegative(state.HoldSeconds) || state.HoldSeconds > 15 ||
            state.HoldingTeam == -1 && state.HoldSeconds != 0 ||
            (state.Mode != GameMode.Warfare || state.Map != ArenaMapId.Citadel) && (state.HoldingTeam != -1 || state.HoldSeconds != 0) ||
            state.Scores is not { Length: 2 } || state.Scores.Any(s => s < 0) ||
            state.ScoreLimit is < 1 or > 200 || state.StartingTickets is < 1 or > 1000 ||
            state.Players is not { Length: <= 16 } || state.Projectiles is not { Length: <= 160 } ||
            state.Walls is not { Length: <= 40 } || state.Fields is not { Length: <= 40 } ||
            state.Beams is not { Length: <= 64 } || state.Points is not { Length: 3 } || state.Events is not { Length: <= 160 }) Invalid();
        var ids = new HashSet<int>();
        foreach (var p in state.Players!)
        {
            if (p == null || p.Id <= 0 || !ids.Add(p.Id) || p.Team is < 0 or > 1 || !Enum.IsDefined(p.WizardType) || p.Name == null || p.Name.Length > 24 ||
                !p.Position.IsFinite || !p.Facing.IsFinite || !Elements(p.Selected) || !Elements(p.Ward, 8) ||
                p.Cooldowns is not { Length: 4 } || !NonNegative(p.Cooldowns) ||
                !NonNegative(p.Health, p.MaxHealth, p.Shield, p.Energy, p.Respawn, p.Wet, p.Burning, p.Chill, p.Frozen, p.Haste, p.Charge) ||
                !NonNegative(p.ShieldMax, p.ShieldSeconds, p.RegenerationRate) || state.Map != ArenaMapId.Relics && p.RegenerationRate > 76 || p.ShieldMax > 0 && p.Shield > p.ShieldMax ||
                p.MaxHealth <= 0 || p.Health > p.MaxHealth || p.Energy > 100 || p.Kills < 0 || p.Deaths < 0) Invalid();
            if (p!.CastingElements != null && !Elements(p.CastingElements) ||
                p.Buffs is { Length: > RelicBuffs.Count } || state.Map == ArenaMapId.Relics && p.Buffs == null) Invalid();
            var kinds = new HashSet<BuffKind>();
            foreach (var buff in p.Buffs ?? [])
                if (buff == null || !Enum.IsDefined(buff.Kind) || !kinds.Add(buff.Kind) || buff.Stacks <= 0 || !p.Alive || state.Map != ArenaMapId.Relics) Invalid();
        }
        if (state.Pickups is { Length: > 16 } || state.Map == ArenaMapId.Relics && state.Pickups is not { Length: 14 } ||
            state.Map != ArenaMapId.Relics && state.Pickups is { Length: > 0 }) Invalid();
        var itemIds = new HashSet<int>();
        foreach (var item in state.Pickups ?? [])
            if (item == null || item.Id <= 0 || !itemIds.Add(item.Id) || !Enum.IsDefined(item.Kind) ||
                !ArenaMap.Get(state.Map).Walkable(item.Position) || !NonNegative(item.Respawn) || item.Respawn > RelicBuffs.RespawnSeconds || item.Active != (item.Respawn == 0)) Invalid();
        foreach (var p in state.Projectiles!) if (p == null || p.Id <= 0 || !p.Position.IsFinite || !p.Direction.IsFinite || !Elements(p.Elements) || !NonNegative(p.Radius)) Invalid();
        foreach (var w in state.Walls!) if (w == null || w.Id <= 0 || !w.A.IsFinite || !w.B.IsFinite || !Elements(w.Elements) || !NonNegative(w.Health, w.Lifetime)) Invalid();
        foreach (var f in state.Fields!) if (f == null || f.Id <= 0 || !f.Position.IsFinite || !Elements(f.Elements) || !NonNegative(f.Radius, f.Lifetime, f.TriggerRadius) || f.Kind is not ("meteor" or "mine" or "healing_mine" or "burning_ground" or "frost_ground" or "water_pool")) Invalid();
        foreach (var b in state.Beams!) if (b == null || !b.From.IsFinite || !b.To.IsFinite || !Enum.IsDefined(b.Shape) || !Elements(b.Elements, 10) || !NonNegative(b.Power) || b.Power > 4 || b.Interaction is not ("direct" or "merged" or "reflected" or "refracted")) Invalid();
        ids.Clear();
        foreach (var p in state.Points!) if (p == null || p.Id is < 0 or > 2 || !ids.Add(p.Id) || !p.Position.IsFinite || p.Owner is < -1 or > 1 || !float.IsFinite(p.Progress) || Math.Abs(p.Progress) > 1) Invalid();
        foreach (var e in state.Events!) if (e == null || e.Id <= 0 || !e.Position.IsFinite || !Elements(e.Elements, 10) || !NonNegative(e.Radius) || e.Radius > 20 || e.Text == null || e.Text.Length > 128 || e.Kind == null || e.Kind.Length > 24) Invalid();
    }
    private static void Invalid() => throw new InvalidDataException("El servidor envió un estado de partida incompleto o inválido.");
}

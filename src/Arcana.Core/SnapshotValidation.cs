namespace Arcana.Core;

/// <summary>JSON válido no garantiza un estado válido. Rechaza respuestas incompletas antes de llegar al motor gráfico.</summary>
public static partial class SnapshotValidation
{
    private static bool Elements(Element[]? values, int maximum = 3)
    {
        if (values == null || values.Length > maximum) return false;
        foreach (var value in values) if (!Enum.IsDefined(value)) return false;
        return true;
    }
    private static bool NonNegative(float value) => float.IsFinite(value) && value >= 0;
    private static bool Tier(ItemTier? value) => !value.HasValue || Enum.IsDefined(value.Value);
    private static bool NonNegative(ReadOnlySpan<float> values)
    {
        foreach (float value in values) if (!NonNegative(value)) return false;
        return true;
    }
    // La tolerancia cubre extremos de rayos en la frontera y pequeños desplazamientos
    // de rebote. El bastón puede emitir un destello hasta una unidad fuera del suelo.
    private static bool Position(ArenaDefinition map, V2 at, float margin = 0.1f) => at.IsFinite &&
        Math.Abs(at.X) <= map.HalfWidth + margin && Math.Abs(at.Y) <= map.HalfHeight + margin &&
        (map.Inside(at) || map.Boundary.Any(e => Geometry.DistanceToSegment(at, e.A, e.B) <= margin));
    private static bool Direction(V2 value) => value.IsFinite && value.LengthSquared <= 1.002f;
    private static bool Size(float value, float maximum) => float.IsFinite(value) && value >= 0 && value <= maximum;
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
            state.Beams is not { Length: <= 64 } || state.Points == null || state.Events is not { Length: <= 160 }) Invalid();
        if (state.SurvivalMap != null && (state.Mode != GameMode.Defense || !state.SurvivalMap.Valid)) Invalid();
        var map = ArenaMap.Get(state);
        if (!map.SupportsMode(state.Mode) || state.Points!.Length != map.ControlPoints.Count) Invalid();
        bool defense = state.Mode == GameMode.Defense;
        CheckDefense(state);
        bool hasPickups = map.HasPickups || defense;
        var ids = new HashSet<int>();
        foreach (var p in state.Players!)
        {
            if (p == null || p.Id <= 0 || !ids.Add(p.Id) || p.Team is < 0 or > 1 || !Enum.IsDefined(p.WizardType) || p.Name == null || p.Name.Length > 24 ||
                !Position(map, p.Position) || !Direction(p.Facing) || !Elements(p.Selected) || !Elements(p.Ward, 8) ||
                p.Cooldowns is not { Length: 4 } || !NonNegative(p.Cooldowns) ||
                !NonNegative([p.Health, p.MaxHealth, p.Shield, p.Energy, p.Respawn, p.Wet, p.Burning, p.Chill, p.Frozen, p.Haste, p.Charge]) ||
                !NonNegative([p.ShieldMax, p.ShieldSeconds, p.RegenerationRate, p.InvulnerableSeconds]) || p.InvulnerableSeconds > 1.2f ||
                !p.Alive && (p.InvulnerableSeconds > 0 || p.ArcaneMarked) || p.ShieldCharges is < 1 or > 3 ||
                !hasPickups && p.RegenerationRate > (p.Equipment == null ? 76 : 121.61f) || p.ShieldMax > 0 && p.Shield > p.ShieldMax ||
                p.MaxHealth <= 0 || p.Health > p.MaxHealth || p.Alive != (p.Health > 0) ||
                p.Charge > 1 || !p.Alive && p.Casting ||
                p.Energy > 100 || !float.IsFinite(p.ReanimationCost) || p.ReanimationCost is < 38.99f or > 75.01f || p.Kills < 0 || p.Deaths < 0 || p.SelectionSequence < 0) Invalid();
            if (p!.Damage != null && !p.Damage.Valid() || p.CastingElements != null && !Elements(p.CastingElements) ||
                p.Buffs is { Length: > RelicBuffs.Count } || hasPickups && p.Buffs == null) Invalid();
            if (p.Equipment is { } gear && (!Enum.IsDefined(gear.Family) ||
                !Tier(gear.Boots) || !Tier(gear.Hat) || !Tier(gear.Chest) || !Tier(gear.Weapon) || !Tier(gear.GoldRing) || !Tier(gear.MagicRing) ||
                (MythicEyes.AllMythic(gear) ? !MythicEyes.ValidMix(gear.MythicFamilies) : gear.MythicFamilies != 0))) Invalid();
            int kinds = 0;
            foreach (var buff in p.Buffs ?? [])
            {
                if (buff == null || !Enum.IsDefined(buff.Kind) || buff.Stacks <= 0 || !p.Alive && !defense || !hasPickups) Invalid();
                int bit = 1 << (int)buff!.Kind;
                if ((kinds & bit) != 0) Invalid();
                kinds |= bit;
            }
        }
        if (state.Pickups is { Length: > 16 } || map.HasPickups && state.Pickups?.Length != RelicBuffs.SpawnCount ||
            !map.HasPickups && state.Pickups is { Length: > 0 }) Invalid();
        ids.Clear();
        foreach (var item in state.Pickups ?? [])
            if (item == null || item.Id <= 0 || !ids.Add(item.Id) || !Enum.IsDefined(item.Kind) ||
                !map.Walkable(item.Position) || !NonNegative(item.Respawn) || item.Respawn > RelicBuffs.RespawnSeconds || item.Active != (item.Respawn == 0)) Invalid();
        ids.Clear();
        float wallCapacity = (1 + EquipmentCatalogue.Maximum(EquipmentStat.ShieldCapacity)) *
            (defense ? DefenseRules.ShieldLevelMultiplier(100) : 1);
        foreach (var p in state.Projectiles!) if (p == null || p.Id <= 0 || !ids.Add(p.Id) || !Position(map, p.Position) || !Direction(p.Direction) || !Elements(p.Elements) || !Size(p.Radius, 1)) Invalid();
        foreach (var w in state.Walls!) if (w == null || w.Id <= 0 || !ids.Add(w.Id) || !Position(map, w.A) || !Position(map, w.B) ||
            V2.Distance(w.A, w.B) is < 0.01f or > 5 || !Elements(w.Elements) ||
            !Size(w.Health, state.Players!.Any(p => p.Id == w.Owner && p.Equipment != null) ? 1300 * wallCapacity + .01f : 1300) ||
            !Size(w.Lifetime, map.SequentialCapture && w.Owner == 0 ? 30.01f : state.Players!.Any(p => p.Id == w.Owner && p.Equipment != null) ? 16.01f : 10.01f)) Invalid();
        foreach (var f in state.Fields!) if (f == null || f.Id <= 0 || !ids.Add(f.Id) || !Position(map, f.Position) || !Elements(f.Elements) ||
            !Size(f.Radius, SpellBalance.MeteorRadius) || !Size(f.Lifetime, 12.01f) || !Size(f.TriggerRadius, 1) || f.Kind is not ("meteor" or "defense_meteor" or "oracle_bombardment" or "mine" or "healing_mine" or "burning_ground" or "frost_ground" or "water_pool" or "equipment_water_pool")) Invalid();
        foreach (var b in state.Beams!) if (b == null || !Position(map, b.From) || !Position(map, b.To) ||
            // Una cadena eléctrica puede unir dos objetivos a lados distintos del cono.
            !Size(V2.Distance(b.From, b.To), (b.Shape switch { SpellShape.Beam => 17.1f, SpellShape.Spray => 6.6f, SpellShape.Lightning => 17.2f, SpellShape.Force => 4.1f, _ => -1 }) *
                // Un reflejo cambia de propietario, pero conserva el alcance restante del lanzador original.
                (b.Interaction is "reflected" or "refracted" or "merged" ||
                    state.Players!.Any(p => p.Id == b.Owner && p.Equipment != null) ? 1.35f : 1)) ||
            !Elements(b.Elements, 10) || !Size(b.Power, 4) || b.Interaction is not ("direct" or "merged" or "reflected" or "refracted")) Invalid();
        ids.Clear();
        foreach (var p in state.Points!) if (p == null || p.Id < 0 || p.Id >= map.ControlPoints.Count || !ids.Add(p.Id) ||
            p.Position != map.ControlPoints[p.Id] || p.Owner is < -1 or > 1 || !float.IsFinite(p.Progress) || Math.Abs(p.Progress) > 1) Invalid();
        long previousEvent = 0;
        foreach (var e in state.Events!)
        {
            if (e == null || e.Id <= previousEvent || !Position(map, e.Position, 1.1f) || !Elements(e.Elements, 10) || !Size(e.Radius, 20) || e.Text == null || e.Text.Length > 128 || e.Kind == null || e.Kind.Length > 24) Invalid();
            previousEvent = e!.Id;
        }
    }
    private static void Invalid() => throw new InvalidDataException("El servidor envió un estado de partida incompleto o inválido.");
}

namespace Arcana.Core;

public sealed partial class GameWorld
{
    private static T[] Copy<T>(T[] values) => values.Length == 0 ? [] : (T[])values.Clone();
    private static T[] Copy<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0) return [];
        var result = new T[values.Count];
        for (int i = 0; i < result.Length; i++) result[i] = values[i];
        return result;
    }
    private static T[] Allocate<T>(int count) => count == 0 ? [] : new T[count];

    /// <summary>Copia independiente del estado; ninguna matriz mutable se comparte con la simulación.</summary>
    public WorldSnapshot Snapshot()
    {
        var players = Allocate<WizardSnapshot>(_players.Count);
        int index = 0;
        foreach (var p in _players.Values)
            players[index++] = new(p.Id, p.Name, p.Team, p.Bot, p.Position, p.Facing, p.Health,
                p.MaxHealth, p.Shield, Copy(p.Ward), Copy(p.Selection.Elements), p.Alive, Math.Max(0, p.Respawn),
                p.Wet, p.Burning, p.Chill, p.Frozen, p.Haste, p.Energy, Copy(p.MagickCooldowns), p.Kills, p.Deaths,
                p.Charging != null ? p.ChargeTime / SpellBalance.ChargeSeconds : p.BeamCharge,
                p.Channel != null || p.Charging != null, p.ShieldMax, p.Shield > 0 ? p.ShieldTime : 0, p.WizardType,
                ActiveShieldRegeneration(p), RelicBuffs.Snapshot(p), Copy(p.Charging?.Elements ?? p.Channel?.Elements ?? []),
                p.ShieldCharges, p.Alive ? Math.Max(0, p.Invulnerable) : 0, p.Equipment == EquipmentModifiers.None ? null : p.Equipment.Appearance,
                SpellBalance.Magicks[3].Cost * p.Equipment.LifeCost, p.SelectionSequence)
                { Damage = p.MatchDamage.Snapshot(), ArcaneMarked = p.Alive && p.ArcaneMarkRank > 0 };
        var projectiles = Allocate<ProjectileSnapshot>(_projectiles.Count);
        for (int i = 0; i < projectiles.Length; i++)
        {
            var p = _projectiles[i];
            projectiles[i] = new(p.Id, p.Owner, p.Position, p.Velocity.Normalized, Copy(p.Recipe.Elements), p.Radius);
        }
        var walls = Allocate<WallSnapshot>(_walls.Count);
        for (int i = 0; i < walls.Length; i++)
        {
            var w = _walls[i];
            walls[i] = new(w.Id, w.Owner, w.A, w.B, w.Health, w.Lifetime, Copy(w.Recipe.Elements));
        }
        var fields = Allocate<FieldSnapshot>(_fields.Count);
        for (int i = 0; i < fields.Length; i++)
        {
            var f = _fields[i];
            fields[i] = new(f.Id, f.Owner, f.Position, f.Radius, f.Lifetime, Copy(f.Recipe.Elements), f.Kind, f.TriggerRadius, f.Delay <= 0);
        }
        var beams = Allocate<BeamSnapshot>(_beams.Count);
        for (int i = 0; i < beams.Length; i++) beams[i] = _beams[i] with { Elements = Copy(_beams[i].Elements) };
        var points = Allocate<PointSnapshot>(_points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            var p = _points[i]; points[i] = new(p.Id, p.Position, p.Owner, p.Progress, p.Contested);
        }
        var events = Allocate<GameEvent>(_events.Count);
        for (int i = 0; i < events.Length; i++) events[i] = _events[i].Event with { Elements = Copy(_events[i].Event.Elements) };
        var pickups = Allocate<PickupSnapshot>(_pickups.Length);
        for (int i = 0; i < pickups.Length; i++)
        {
            var p = _pickups[i]; pickups[i] = new(p.Id, p.Position, p.Kind, p.Respawn <= 0, Math.Max(0, p.Respawn));
        }
        return new(Tick, Time, Options.Mode, MatchEnded, Winner, Math.Max(0, RestartIn), Copy(Scores),
            players, projectiles, walls, fields, beams, points, events, Options.ScoreLimit, Options.Tickets,
            Map.Id, HoldingTeam, HoldSeconds, pickups, Defense: DefenseState(), SurvivalMap: Map.SurvivalMap);
    }
}

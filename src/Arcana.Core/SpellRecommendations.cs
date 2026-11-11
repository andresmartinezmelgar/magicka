namespace Arcana.Core;

public readonly record struct SpellSuggestion(SpellRecipe Spell, CastMode Mode);

/// <summary>Elige una mezcla a partir del estado visible. Prepararla y lanzarla son acciones separadas.</summary>
public static class SpellRecommendations
{
    private static readonly SpellSuggestion Guard = new(new([Element.Shield, Element.Earth, Element.Life]), CastMode.Self);
    private static readonly SpellSuggestion HealSelf = new(new([Element.Life, Element.Life, Element.Life]), CastMode.Self);
    private static readonly SpellSuggestion HealAlly = new(new([Element.Life, Element.Life, Element.Life]), CastMode.Forward);
    private static readonly SpellSuggestion LongLightning = new(new([Element.Arcane, Element.Lightning, Element.Lightning]), CastMode.Forward);
    private static readonly SpellSuggestion DefaultAttack = new(new([Element.Arcane, Element.Arcane, Element.Arcane]), CastMode.Forward);
    private static readonly SpellRecipe[] Attacks = SpellCatalogue.All.Select(entry => entry.Recipe)
        .Where(spell => !spell.Healing && spell.Shape != SpellShape.Barrier).ToArray();

    private readonly record struct Target(int Id, V2 Position, bool Wet, bool Frozen, float Burning, float Health,
        float MaxHealth, float Shield, Element[] Ward, WizardType WizardType, Element? Vulnerable)
    {
        public float HealthFraction => Health / MaxHealth;
    }

    private readonly record struct Coverage(Target Enemy, float Range, float Alignment, float Sideways,
        float Separation, bool Visible, bool ImpactVisible);

    public static bool Available(WorldSnapshot world, WizardSnapshot caster) => caster.Alive && caster.Frozen <= 0 && !caster.Stunned &&
        !world.MatchEnded && !world.WaitingForPlayers && world.Defense?.Paused != true;

    public static SpellSuggestion ChooseDefense(WizardSnapshot caster)
    {
        return caster.Shield > 0 && caster.ShieldSeconds > 0 ? HealSelf : Guard;
    }

    public static SpellSuggestion ChooseDefense(WorldSnapshot world, WizardSnapshot caster, EquipmentModifiers? equipped = null)
    {
        // Primero sobrevivir; fuera de peligro se puede preparar Vida para un compañero.
        if (caster.Health < caster.MaxHealth * .65f || world.Mode == GameMode.Arena) return ChooseDefense(caster);
        var gear = Equipment(world, caster, equipped);
        float range = Math.Min(10, HealAlly.Spell.Range * gear.Range);
        foreach (var ally in world.Players)
        {
            if (ally.Id == caster.Id || !ally.Alive || ally.Team != caster.Team || ally.Health >= ally.MaxHealth * .60f ||
                V2.Distance(caster.Position, ally.Position) > range || !ClearShot(world, caster, caster.Position, ally.Position)) continue;
            // Un haz cura al primer cuerpo que encuentra: no ofrecerlo a través de otros jugadores o enemigos.
            var direction = (ally.Position - caster.Position).Normalized;
            var origin = caster.Position + direction * .7f;
            if (world.Players.Any(p => p.Alive && p.Id != caster.Id && p.Id != ally.Id &&
                    Geometry.SegmentCircle(origin, ally.Position, p.Position, p.Shield > 0 && p.ShieldSeconds > 0 ? .95f : .6f) != null) ||
                world.Defense?.Minions.Any(m => m.Health > 0 &&
                    Geometry.SegmentCircle(origin, ally.Position, m.Position, m.Kind == MinionKind.Boss ? 1.4f : .6f) != null) == true) continue;
            return HealAlly;
        }
        return ChooseDefense(caster);
    }

    private static EquipmentModifiers Equipment(WorldSnapshot world, WizardSnapshot caster, EquipmentModifiers? equipped) =>
        world.Defense?.Defenders.FirstOrDefault(p => p.PlayerId == caster.Id)?.Modifiers ?? equipped ?? EquipmentModifiers.None;

    private static bool ClearShot(WorldSnapshot world, WizardSnapshot caster, V2 from, V2 at)
    {
        var map = ArenaMap.Get(world);
        return map.BoundaryHit(from, at) == null &&
            !map.Obstacles.Any(o => Geometry.SegmentCircle(from, at, o.Position, o.Radius) != null) &&
            !world.Walls.Any(w => w.Health > 0 && !(w.Owner == caster.Id && w.Elements is [Element.Shield]) &&
                Geometry.SegmentCapsule(from, at, w.A, w.B, .1f) != null);
    }

    public static SpellSuggestion ChooseAttack(WorldSnapshot world, WizardSnapshot caster, EquipmentModifiers? equipped = null)
    {
        var defense = world.Defense;
        var gear = Equipment(world, caster, equipped);

        var enemies = world.Players.Where(p => p.Alive && p.Id != caster.Id && p.InvulnerableSeconds <= 0 &&
                (world.Mode == GameMode.Arena || p.Team != caster.Team))
            .Select(p => new Target(p.Id, p.Position, p.Wet > 0, p.Frozen > 0, p.Burning, p.Health,
                p.MaxHealth, p.Shield, p.Ward, p.WizardType, null))
            .Concat(defense?.Minions.Where(m => m.Health > 0).Select(m => new Target(m.Id, m.Position, m.Wet > 0, m.Frozen > 0,
                m.Burning, m.Health, m.MaxHealth, m.Shield, [], DefenseRules.EnemyWizardType(m.Kind), m.VulnerableElement)) ?? []).ToArray();

        Target? target = null;
        float bestTarget = float.PositiveInfinity;
        foreach (var enemy in enemies)
        {
            var offset = enemy.Position - caster.Position;
            float distance = offset.Length;
            float alignment = V2.Dot(caster.Facing, offset.Normalized);
            bool inCrosshair = alignment > 0 && Math.Abs(V2.Dot(caster.Facing.Perpendicular, offset)) <= ArenaMap.WizardRadius + .2f;
            // Una línea de tiro directa tiene prioridad sobre el resto del cono.
            float priority = distance + (inCrosshair ? 0 : alignment >= .8f ? 32 : 64);
            if (distance > 20 * gear.Range + .7f || priority >= bestTarget || !ClearShot(world, caster, caster.Position, enemy.Position)) continue;
            target = enemy;
            bestTarget = priority;
        }
        // Sin un enemigo visible, prepara una mezcla acorde al equipo para un objetivo neutro a corto alcance.
        var aimed = target ?? new Target(-1, caster.Position + caster.Facing * (6 * gear.Range), false, false, 0,
            caster.MaxHealth, caster.MaxHealth, 0, [], WizardType.Balanced, null);

        float range = V2.Distance(caster.Position, aimed.Position);
        var direction = (aimed.Position - caster.Position).Normalized;
        var origin = caster.Position + direction * .7f;
        // La geometría se calcula una vez por recomendación, no por cada mezcla del catálogo.
        Coverage[] coverage = target is null ? [] : enemies.Where(e => e.Id != aimed.Id).Select(e => new Coverage(e,
                V2.Distance(caster.Position, e.Position), V2.Dot(direction, (e.Position - origin).Normalized),
                Math.Abs(V2.Dot(direction.Perpendicular, e.Position - origin)), V2.Distance(e.Position, aimed.Position),
                ClearShot(world, caster, caster.Position, e.Position),
                V2.Distance(e.Position, aimed.Position) <= CombatAffixes.RockFragmentRange && ClearShot(world, caster, aimed.Position, e.Position)))
            .OrderBy(e => e.Range).ThenBy(e => e.Enemy.Id).ToArray();
        SpellRecipe? bestSpell = null;
        float bestDamage = 0, bestDirect = 0;
        foreach (var spell in Attacks)
        {
            // La descarga directa puede dañar al mago mojado. Un haz arcano con Rayo usa otro lanzamiento.
            if (range > spell.Range * gear.Range + .7f || caster.Wet > 0 && spell.Shape == SpellShape.Lightning) continue;
            float direct = DamageValue(spell, caster, aimed, gear, range);
            // El objetivo apuntado debe recibir daño, aunque los vecinos tengan otras debilidades.
            if (direct <= 0) continue;
            float damage = GroupValue(world, spell, caster, aimed, gear, range, direct, coverage);
            if (damage < bestDamage || damage == bestDamage && direct <= bestDirect) continue;
            bestSpell = spell;
            bestDamage = damage;
            bestDirect = direct;
        }
        if (bestSpell != null) return new SpellSuggestion(bestSpell, CastMode.Forward);
        return aimed.Vulnerable == Element.Lightning ? LongLightning : DefaultAttack;
    }

    private static float UsefulDamage(Target target, float damage) => Math.Min(Math.Max(0, damage), target.Health + target.Shield);

    private static float GroupValue(WorldSnapshot world, SpellRecipe spell, WizardSnapshot caster, Target target,
        EquipmentModifiers gear, float range, float direct, Coverage[] coverage)
    {
        Span<float> contributions = coverage.Length <= 256 ? stackalloc float[coverage.Length] : new float[coverage.Length];
        contributions.Clear();
        int hits = 1, fragments = 0, pierces = gear.BeamPierce;
        int shards = Math.Max(0, 3 + spell.Count(Element.Cold) - (range <= 3.5f ? 2 : 1));
        Target lastLightning = target;
        HashSet<int>? lightningHits = spell.Shape == SpellShape.Lightning ? [target.Id] : null;
        for (int i = 0; i < coverage.Length; i++)
        {
            var other = coverage[i];
            float scale = 0;
            bool inRange = other.Range <= spell.Range * gear.Range + .7f;
            switch (spell.Shape)
            {
                case SpellShape.Lightning when inRange && other.Visible && other.Alignment > SpellBalance.LightningConeDot && hits < 3:
                    // La descarga ya golpea hasta tres enemigos sin necesitar el afijo de cadenas.
                    scale = 1; hits++; lastLightning = other.Enemy; lightningHits!.Add(other.Enemy.Id);
                    break;
                case SpellShape.Spray when inRange && other.Visible && other.Alignment > SpellBalance.ChannelConeDot:
                    scale = 1;
                    break;
                case SpellShape.Shards when inRange && other.Visible && shards > 0 &&
                    other.Alignment >= MathF.Cos((2 + spell.Count(Element.Cold)) * .055f + .1f):
                    scale = other.Range <= 3.5f ? .5f : 1; shards--;
                    break;
                case SpellShape.Rock when other.ImpactVisible:
                    if (other.Separation < spell.SplashRadius) scale = .65f * (1 - other.Separation / spell.SplashRadius);
                    if (fragments < CombatAffixes.RockFragmentCount(gear.RockFragmentation))
                    {
                        scale += CombatAffixes.RockFragmentImpactScale * .6f;
                        fragments++;
                    }
                    break;
                case SpellShape.Beam when inRange && other.Visible && other.Range > range && other.Alignment > 0 &&
                    other.Sideways <= .6f && pierces > 0 && target.Shield <= 0:
                    scale = 1; pierces--;
                    if (other.Enemy.Shield > 0) pierces = 0;
                    break;
            }
            if (scale > 0) contributions[i] = DamageValue(spell, caster, other.Enemy, gear, other.Range) * scale;
        }
        if (lightningHits != null)
        {
            for (int jump = 0; jump < Math.Min(2, gear.LightningChains); jump++)
            {
                int next = -1;
                float nearest = 5;
                for (int i = 0; i < coverage.Length; i++)
                {
                    var other = coverage[i];
                    float separation = V2.Distance(lastLightning.Position, other.Enemy.Position);
                    if (lightningHits.Contains(other.Enemy.Id) || separation > nearest ||
                        !ClearShot(world, caster, lastLightning.Position, other.Enemy.Position)) continue;
                    next = i; nearest = separation;
                }
                if (next < 0) break;
                var contact = coverage[next];
                lightningHits.Add(contact.Enemy.Id); lastLightning = contact.Enemy;
                contributions[next] += DamageValue(spell, caster, contact.Enemy, gear, contact.Range);
            }
        }
        if (spell.Shape == SpellShape.Beam && spell.Has(Element.Arcane) && gear.ArcaneEcho > 0)
        {
            for (int i = 0; i < coverage.Length; i++)
            {
                var other = coverage[i];
                if (!other.Visible || other.Range > spell.Range * gear.Range) continue;
                contributions[i] += DamageValue(spell, caster, other.Enemy, gear, other.Range) *
                    CombatAffixes.ArcaneEchoPercent(gear.ArcaneEcho) / 100f;
                break;
            }
        }
        // Penetración y eco pueden coincidir en un enemigo: limitar después de sumar sus aportes.
        float total = UsefulDamage(target, direct);
        for (int i = 0; i < coverage.Length; i++) total += UsefulDamage(coverage[i].Enemy, contributions[i]);
        return total;
    }

    private static float DamageValue(SpellRecipe spell, WizardSnapshot caster, Target target, EquipmentModifiers gear, float range)
    {
        ReadOnlySpan<Element> ward = target.Ward.Length > 0 ? target.Ward : target.Shield > 0 ? ElementInteractions.ShieldProtection : [];
        float damage = 0;
        foreach (var element in SpellBalance.Offensive)
        {
            if (target.Vulnerable is { } weakness && weakness != element) continue;
            float component = SpellBalance.Component(spell, element, target.Wet);
            if (element == Element.Water && target.Vulnerable == Element.Water && component == 0)
                component = DefenseRules.PrismWaterDamage * spell.Count(Element.Water);
            float wetBonus = element == Element.Lightning && target.Wet ? gear.WetLightningPower : 1;
            damage += component * WizardTypes.Outgoing(caster.WizardType, element) * gear.ElementPower(element) *
                WizardTypes.Incoming(target.WizardType, element) * ElementInteractions.ProtectionMultiplier(element, ward) * wetBonus *
                CombatAffixes.DamageMultiplier(gear, element, target.Wet, target.Frozen, target.HealthFraction);
        }
        if (damage <= 0) return 0;
        float effectiveness = spell.Shape switch
        {
            SpellShape.Beam => SpellBalance.BeamDamageMultiplier(.65f * gear.BeamChargeSpeed) * (target.Shield > 50 ? .3f : 1),
            SpellShape.Rock => (SpellBalance.RockMinimum + .3f * (gear.RockChargeSpeed - 1)) * .59f,
            SpellShape.Shards => SpellBalance.ShardPower * (range <= 3.5f ? 2 : 1),
            SpellShape.Lightning => target.Wet ? 1 : .72f,
            _ => 1
        };
        float directDamage = damage * effectiveness * gear.Power;
        float fireProtection = ElementInteractions.ProtectionMultiplier(Element.Fire, ward);
        if (fireProtection <= .5f || target.Vulnerable is { } vulnerable && vulnerable != Element.Fire) return directDamage;

        float burnPerSecond = SpellBalance.BurnPerSecond * WizardTypes.Outgoing(caster.WizardType, Element.Fire) *
            gear.Fire * gear.Power * gear.BurnPotency * WizardTypes.Incoming(target.WizardType, Element.Fire) * fireProtection;
        // La quemadura no se acumula por repetir Fuego. Cuenta una aplicación y acota su valor por la vida restante.
        float horizon = Math.Min(SpellBalance.BurnDuration * gear.BurnDuration,
            (target.Health + target.Shield) / Math.Max(1, directDamage));
        float activeBurn = Math.Min(target.Burning, horizon);
        if (spell.Has(Element.Fire) && !spell.Steam && !target.Wet && target.Burning <= .75f)
            return directDamage + burnPerSecond * Math.Max(0, horizon - activeBurn);

        bool extinguishes = spell.Has(Element.Cold) || spell.Has(Element.Water) && !spell.Steam && !spell.Ice;
        return Math.Max(0, directDamage - (extinguishes ? burnPerSecond * activeBurn : 0));
    }
}

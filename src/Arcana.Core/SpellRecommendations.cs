namespace Arcana.Core;

public readonly record struct SpellSuggestion(SpellRecipe Spell, CastMode Mode);

/// <summary>Elige una mezcla a partir del estado visible. Prepararla y lanzarla son acciones separadas.</summary>
public static class SpellRecommendations
{
    private static readonly SpellSuggestion Guard = new(new([Element.Shield, Element.Earth]), CastMode.Self);
    private static readonly SpellSuggestion HealSelf = new(new([Element.Life, Element.Life, Element.Life]), CastMode.Self);
    private static readonly SpellSuggestion HealAlly = new(HealSelf.Spell, CastMode.Forward);
    private static readonly SpellSuggestion DryOff = new(new([Element.Fire]), CastMode.Self);
    private static readonly SpellSuggestion Slow = new(new([Element.Cold, Element.Cold, Element.Cold]), CastMode.Forward);
    private static readonly SpellSuggestion LongLightning = new(new([Element.Arcane, Element.Lightning, Element.Lightning]), CastMode.Forward);
    private static readonly SpellSuggestion DefaultAttack = new(new([Element.Arcane, Element.Arcane, Element.Arcane]), CastMode.Forward);
    private static readonly SpellRecipe[] Attacks = SpellCatalogue.All.Select(entry => entry.Recipe)
        .Where(spell => !spell.Healing && spell.Shape != SpellShape.Barrier).ToArray();

    private readonly record struct Target(V2 Position, float Wet, float Shield, Element[] Ward,
        WizardType WizardType, Element? Vulnerable, float AttackWarning);

    public static bool Available(WorldSnapshot world, WizardSnapshot caster) => caster.Alive && caster.Frozen <= 0 && !caster.Stunned &&
        !world.MatchEnded && !world.WaitingForPlayers && world.Defense?.Paused != true;

    public static SpellSuggestion? Choose(WorldSnapshot world, WizardSnapshot caster)
    {
        if (!Available(world, caster)) return null;

        var defense = world.Defense;
        var gear = defense?.Defenders.FirstOrDefault(p => p.PlayerId == caster.Id)?.Modifiers ?? EquipmentModifiers.None;
        var map = ArenaMap.Get(world);
        bool ClearShot(V2 at) => map.BoundaryHit(caster.Position, at) == null &&
            !map.Obstacles.Any(o => Geometry.SegmentCircle(caster.Position, at, o.Position, o.Radius) != null) &&
            !world.Walls.Any(w => w.Health > 0 && !(w.Owner == caster.Id && w.Elements is [Element.Shield]) &&
                Geometry.SegmentCapsule(caster.Position, at, w.A, w.B, .1f) != null);

        bool threatened = defense?.Minions.Any(m => m.Health > 0 && m.AttackWarningSeconds > 0 &&
            V2.Distance(caster.Position, m.Position) <= DefenseRules.AreaAttackRadius(m.Kind) && ClearShot(m.Position)) == true ||
            world.Fields.Any(f => f.Lifetime > 0 && (f.Kind is "oracle_bombardment" or DefenseRules.ColossusFissureKind) &&
                V2.Distance(caster.Position, f.Position) <= f.Radius);
        if (threatened && caster.Shield < 180) return Guard;
        if (caster.Health < caster.MaxHealth * .48f) return HealSelf;

        var enemies = world.Players.Where(p => p.Alive && p.Id != caster.Id && p.InvulnerableSeconds <= 0 &&
                (world.Mode == GameMode.Arena || p.Team != caster.Team))
            .Select(p => new Target(p.Position, p.Wet, p.Shield, p.Ward, p.WizardType, null, 0))
            .Concat(defense?.Minions.Where(m => m.Health > 0).Select(m => new Target(m.Position, m.Wet,
                m.Shield, [], DefenseRules.EnemyWizardType(m.Kind), m.VulnerableElement, m.AttackWarningSeconds)) ?? []).ToArray();
        if (caster.Shield < 100 && enemies.Count(e => V2.Distance(caster.Position, e.Position) <= 3 && ClearShot(e.Position)) >= 3)
            return Guard;

        if (world.Mode != GameMode.Arena && world.Players.Any(p => p.Id != caster.Id && p.Team == caster.Team && p.Alive &&
            p.Health < p.MaxHealth * .35f && V2.Distance(caster.Position, p.Position) <= HealAlly.Spell.Range * gear.Range &&
            V2.Dot(caster.Facing, (p.Position - caster.Position).Normalized) > .65f && ClearShot(p.Position))) return HealAlly;

        Target? target = null;
        float bestTarget = float.PositiveInfinity;
        foreach (var enemy in enemies)
        {
            var offset = enemy.Position - caster.Position;
            float distance = offset.Length;
            // La dirección actual favorece al enemigo apuntado sin ignorar amenazas mucho más próximas.
            float priority = distance - 5 * Math.Max(0, V2.Dot(caster.Facing, offset.Normalized));
            if (distance > 20 * gear.Range + .7f || priority >= bestTarget || !ClearShot(enemy.Position)) continue;
            target = enemy;
            bestTarget = priority;
        }
        if (target is not { } aimed) return caster.Health < caster.MaxHealth * .8f ? HealSelf : DefaultAttack;
        if (caster.Wet > 0 && aimed.Vulnerable == Element.Lightning) return DryOff;

        float range = V2.Distance(caster.Position, aimed.Position);
        if (aimed.AttackWarning >= .8f && (aimed.Vulnerable is null or Element.Cold) && range <= Slow.Spell.Range * gear.Range)
            return Slow;

        int nearby = Math.Min(3, enemies.Count(e => V2.Distance(e.Position, aimed.Position) <= 2.5f) - 1);
        SpellRecipe? bestSpell = null;
        float bestDamage = 0;
        foreach (var spell in Attacks)
        {
            if (range > spell.Range * gear.Range + .7f || caster.Wet > 0 && spell.Has(Element.Lightning)) continue;
            float damage = DamageValue(spell, caster, aimed, gear, range, nearby);
            if (damage <= bestDamage) continue;
            bestSpell = spell;
            bestDamage = damage;
        }
        if (bestSpell != null) return new SpellSuggestion(bestSpell, CastMode.Forward);
        return aimed.Vulnerable == Element.Lightning ? LongLightning : DefaultAttack;
    }

    private static float DamageValue(SpellRecipe spell, WizardSnapshot caster, Target target, EquipmentModifiers gear, float range, int nearby)
    {
        ReadOnlySpan<Element> ward = target.Ward.Length > 0 ? target.Ward : target.Shield > 0 ? ElementInteractions.ShieldProtection : [];
        float damage = 0;
        foreach (var element in SpellBalance.Offensive)
        {
            if (target.Vulnerable is { } weakness && weakness != element) continue;
            float component = SpellBalance.Component(spell, element, target.Wet > 0);
            if (element == Element.Water && target.Vulnerable == Element.Water && component == 0)
                component = DefenseRules.PrismWaterDamage * spell.Count(Element.Water);
            float wetBonus = element == Element.Lightning && target.Wet > 0 ? gear.WetLightningPower : 1;
            damage += component * WizardTypes.Outgoing(caster.WizardType, element) * gear.ElementPower(element) *
                WizardTypes.Incoming(target.WizardType, element) * ElementInteractions.ProtectionMultiplier(element, ward) * wetBonus;
        }
        // Compara un golpe probable: carga de roca, tiempo de canal y cobertura de varios enemigos.
        float effectiveness = spell.Shape switch
        {
            SpellShape.Beam => SpellBalance.BeamDamageMultiplier(.65f) * (target.Shield > 50 ? .3f : 1),
            SpellShape.Rock => SpellBalance.RockMinimum * (.67f + (spell.SplashRadius > 0 ? .22f * nearby : 0)),
            SpellShape.Shards => SpellBalance.ShardPower * (range <= 3.5f ? 2 : 1),
            SpellShape.Lightning => target.Wet > 0 ? 1 : .72f,
            _ => 1
        };
        return damage * effectiveness;
    }
}

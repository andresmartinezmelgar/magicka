namespace Arcana.Core;

public readonly record struct SpellSuggestion(SpellRecipe Spell, CastMode Mode);

/// <summary>Elige una mezcla a partir del estado visible. Prepararla y lanzarla son acciones separadas.</summary>
public static class SpellRecommendations
{
    private static readonly SpellSuggestion Guard = new(new([Element.Shield, Element.Earth]), CastMode.Self);
    private static readonly SpellSuggestion RegeneratingGuard = new(new([Element.Shield, Element.Earth, Element.Life]), CastMode.Self);
    private static readonly SpellSuggestion HealSelf = new(new([Element.Life, Element.Life, Element.Life]), CastMode.Self);
    private static readonly SpellSuggestion LongLightning = new(new([Element.Arcane, Element.Lightning, Element.Lightning]), CastMode.Forward);
    private static readonly SpellSuggestion DefaultAttack = new(new([Element.Arcane, Element.Arcane, Element.Arcane]), CastMode.Forward);
    private static readonly SpellRecipe[] Attacks = SpellCatalogue.All.Select(entry => entry.Recipe)
        .Where(spell => !spell.Healing && spell.Shape != SpellShape.Barrier).ToArray();

    private readonly record struct Target(int Id, V2 Position, bool Wet, bool Frozen, float Burning, float Health,
        float MaxHealth, float Shield, Element[] Ward, WizardType WizardType, Element? Vulnerable)
    {
        public float HealthFraction => Health / MaxHealth;
    }

    public static bool Available(WorldSnapshot world, WizardSnapshot caster) => caster.Alive && caster.Frozen <= 0 && !caster.Stunned &&
        !world.MatchEnded && !world.WaitingForPlayers && world.Defense?.Paused != true;

    public static SpellSuggestion ChooseDefense(WizardSnapshot caster)
    {
        if (caster.Shield > 0 && caster.ShieldSeconds > 0) return HealSelf;
        return caster.Health < caster.MaxHealth * .65f ? RegeneratingGuard : Guard;
    }

    public static SpellSuggestion ChooseAttack(WorldSnapshot world, WizardSnapshot caster, EquipmentModifiers? equipped = null)
    {
        var defense = world.Defense;
        var gear = defense?.Defenders.FirstOrDefault(p => p.PlayerId == caster.Id)?.Modifiers ?? equipped ?? EquipmentModifiers.None;
        var map = ArenaMap.Get(world);
        bool ClearShot(V2 from, V2 at) => map.BoundaryHit(from, at) == null &&
            !map.Obstacles.Any(o => Geometry.SegmentCircle(from, at, o.Position, o.Radius) != null) &&
            !world.Walls.Any(w => w.Health > 0 && !(w.Owner == caster.Id && w.Elements is [Element.Shield]) &&
                Geometry.SegmentCapsule(from, at, w.A, w.B, .1f) != null);

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
            if (distance > 20 * gear.Range + .7f || priority >= bestTarget || !ClearShot(caster.Position, enemy.Position)) continue;
            target = enemy;
            bestTarget = priority;
        }
        // Sin un enemigo visible, prepara una mezcla acorde al equipo para un objetivo neutro a corto alcance.
        var aimed = target ?? new Target(-1, caster.Position + caster.Facing * (6 * gear.Range), false, false, 0,
            caster.MaxHealth, caster.MaxHealth, 0, [], WizardType.Balanced, null);

        float range = V2.Distance(caster.Position, aimed.Position);
        var neighbors = enemies.Where(e => e.Id != aimed.Id &&
                V2.Distance(e.Position, aimed.Position) <= CombatAffixes.RockFragmentRange && ClearShot(aimed.Position, e.Position))
            .Select(e => V2.Distance(e.Position, aimed.Position)).ToArray();
        bool echoTarget = gear.ArcaneEcho > 0 && enemies.Any(e => e.Id != aimed.Id &&
            V2.Distance(e.Position, caster.Position) <= LongLightning.Spell.Range * gear.Range && ClearShot(caster.Position, e.Position));
        SpellRecipe? bestSpell = null;
        float bestDamage = 0;
        foreach (var spell in Attacks)
        {
            // La descarga directa puede dañar al mago mojado. Un haz arcano con Rayo usa otro lanzamiento.
            if (range > spell.Range * gear.Range + .7f || caster.Wet > 0 && spell.Shape == SpellShape.Lightning) continue;
            float damage = DamageValue(spell, caster, aimed, gear, range, neighbors, echoTarget);
            if (damage <= bestDamage) continue;
            bestSpell = spell;
            bestDamage = damage;
        }
        if (bestSpell != null) return new SpellSuggestion(bestSpell, CastMode.Forward);
        return aimed.Vulnerable == Element.Lightning ? LongLightning : DefaultAttack;
    }

    private static float DamageValue(SpellRecipe spell, WizardSnapshot caster, Target target, EquipmentModifiers gear,
        float range, float[] neighbors, bool echoTarget)
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
        // El valor extra de fragmentar rocas depende de que haya otros enemigos donde impacta la roca.
        float effectiveness = spell.Shape switch
        {
            SpellShape.Beam => SpellBalance.BeamDamageMultiplier(.65f * gear.BeamChargeSpeed) * (target.Shield > 50 ? .3f : 1) *
                (echoTarget && spell.Has(Element.Arcane) ? 1 + CombatAffixes.ArcaneEchoPercent(gear.ArcaneEcho) / 100f : 1),
            SpellShape.Rock => (SpellBalance.RockMinimum + .3f * (gear.RockChargeSpeed - 1)) *
                (.59f + .22f * Math.Min(3, neighbors.Count(distance => distance < spell.SplashRadius)) +
                 .12f * Math.Min(3, neighbors.Length) * CombatAffixes.RockFragmentCount(gear.RockFragmentation) * CombatAffixes.RockFragmentImpactScale),
            SpellShape.Shards => SpellBalance.ShardPower * (range <= 3.5f ? 2 : 1),
            SpellShape.Lightning => (target.Wet ? 1 : .72f) *
                (1 + .24f * Math.Min(gear.LightningChains, neighbors.Count(distance => distance <= 5))),
            _ => 1
        };
        float directDamage = damage * effectiveness;
        float fireProtection = ElementInteractions.ProtectionMultiplier(Element.Fire, ward);
        if (fireProtection <= .5f || target.Vulnerable is { } vulnerable && vulnerable != Element.Fire) return directDamage;

        float burnPerSecond = SpellBalance.BurnPerSecond * WizardTypes.Outgoing(caster.WizardType, Element.Fire) *
            gear.Fire * gear.BurnPotency * WizardTypes.Incoming(target.WizardType, Element.Fire) * fireProtection;
        // La quemadura no se acumula por repetir Fuego. Cuenta una aplicación y acota su valor por la vida restante.
        float horizon = Math.Min(SpellBalance.BurnDuration * gear.BurnDuration,
            (target.Health + target.Shield) / Math.Max(1, directDamage * gear.Power));
        float activeBurn = Math.Min(target.Burning, horizon);
        if (spell.Has(Element.Fire) && !spell.Steam && !target.Wet && target.Burning <= .75f)
            return directDamage + burnPerSecond * Math.Max(0, horizon - activeBurn);

        bool extinguishes = spell.Has(Element.Cold) || spell.Has(Element.Water) && !spell.Steam && !spell.Ice;
        return Math.Max(0, directDamage - (extinguishes ? burnPerSecond * activeBurn : 0));
    }
}

namespace Arcana.Core;

public sealed partial class GameWorld
{
    private bool Allied(Wizard a, Wizard b) => a.Id == b.Id || Options.Mode != GameMode.Arena && a.Team == b.Team;

    private bool Hostile(Wizard a, Wizard b) => !Allied(a, b);

    private float Outgoing(int owner, Element element) => TryCombatSource(owner, out var source)
        ? WizardTypes.Outgoing(source.WizardType, element) * source.Equipment.ElementPower(element) : 1;

    private float ElementDamage(Wizard target, int owner, Element element, float amount)
    {
        if (amount == 0) return 0;
        TryCombatSource(owner, out var source);
        var gear = source?.Equipment ?? EquipmentModifiers.None;
        float outgoing = source == null ? 1 : WizardTypes.Outgoing(source.WizardType, element) * gear.ElementPower(element);
        float affix = source != null && Hostile(source, target)
            ? CombatAffixes.DamageMultiplier(gear, element, target.Wet > 0, target.Frozen > 0, target.Health / target.MaxHealth) : 1;
        return amount * outgoing * WizardTypes.Incoming(target.WizardType, element) * Resistance(target, element) * affix *
            (element == Element.Lightning && target.Wet > 0 ? gear.WetLightningPower : 1);
    }

    private void Hit(Wizard target, SpellRecipe spell, float scale, int owner, V2 force, float damageMultiplier = 1)
    {
        if (!target.Alive) return;
        var equipment = TryCombatSource(owner, out var attacker) ? attacker.Equipment : EquipmentModifiers.None;
        if (spell.Healing && target.VulnerableElement == null) HealAs(_statisticsAttack ?? MatchStatisticsCollector.Shape(spell.Shape), target, SpellBalance.Heal * spell.Count(Element.Life) * scale * HealingPower(owner), owner);
        if (target.Invulnerable > 0) return;
        if (TryCombatSource(owner, out var source) && source.Id != target.Id && Allied(source, target) && (!Options.FriendlyFire || IsDefense)) return;
        float damage = 0;
        Span<float> weights = stackalloc float[9];
        weights.Clear();
        _statistics.Immune(owner, target, spell.Elements);
        foreach (var element in SpellBalance.Offensive)
        { float component = ElementDamage(target, owner, element, SpellBalance.Component(spell, element, target.Wet > 0)); weights[(int)element] = component; damage += component; }
        if (target.VulnerableElement == Element.Water && SpellBalance.Component(spell, Element.Water) == 0)
            { float component = ElementDamage(target, owner, Element.Water, DefenseRules.PrismWaterDamage * spell.Count(Element.Water)); damage += component; weights[(int)Element.Water] += component; }
        if (spell.Has(Element.Water) && !spell.Steam && !spell.Ice && Resistance(target, Element.Water) > 0.5f)
        {
            target.Wet = 6 * equipment.WetDuration; target.Burning = 0;
            ApplyWaterSlow(target, owner);
            target.Knockback += force * (18 * scale * equipment.Water);
        }
        if (spell.Shape == SpellShape.Force && target.VulnerableElement == null) target.Knockback += force * (45 * scale);
        if (spell.Has(Element.Fire) && !spell.Steam && Resistance(target, Element.Fire) > 0.5f)
        {
            if (target.Wet > 0) target.Wet = Math.Max(0, target.Wet - 12 * scale);
            else { target.Burning = SpellBalance.BurnDuration * (TryCombatSource(owner, out var burner) ? burner.Equipment.BurnDuration : 1);
                target.BurnOwner = owner; target.BurnMultiplier = Outgoing(owner, Element.Fire) * equipment.BurnPotency; }
            target.Chill = Math.Max(0, target.Chill - 100 * scale);
            target.Frozen = Math.Max(0, target.Frozen - 5 * scale);
        }
        if (spell.Has(Element.Cold) && Resistance(target, Element.Cold) > 0.5f)
        {
            target.Burning = 0;
            bool recovery = IsDefense && target.Role == CombatantRole.Player;
            // La horda sigue causando daño de frío, pero no renueva el bloqueo ni
            // acumula la siguiente congelación durante el margen de recuperación.
            if (!recovery || target.Frozen <= 0 && target.FreezeRecovery <= 0)
            {
                target.Chill = Math.Min(100, target.Chill + 38 * spell.Count(Element.Cold) * (target.Wet > 0 ? 2 : 1) * scale * equipment.ChillPower);
                if (target.Chill >= 99)
                {
                    target.Frozen = 1.4f * equipment.FreezeDuration; target.Wet = 0;
                    InterruptCast(target, SpellInterruptionCause.Frozen, owner, target.Frozen);
                    if (recovery) { target.Chill = 0; target.FreezeRecovery = DefenseRules.FreezeRecoverySeconds; }
                }
            }
        }
        if (spell.Has(Element.Earth) && AcceptsDefenseElement(target, Element.Earth)) target.Knockback += force * (10 * scale);
        damage *= damageMultiplier;
        ApplyDamage(target, damage * scale, owner, spell.Elements, false, true, weights, _statisticsAttack ?? MatchStatisticsCollector.Shape(spell.Shape));
        if (weights[(int)Element.Lightning] > 0) RecordLightningContact(target, owner);
        if (target.Shield > 0) DamageProtection(target, UtilityPressure(target, spell, owner) * scale, owner, spell.Elements);
        if (TryCombatSource(owner, out var caster) && caster.Id != target.Id)
            caster.Energy = Math.Min(100, caster.Energy + damage * scale * 0.015f);
    }

    private void Damage(Wizard target, float amount, int owner, Element[]? elements = null)
        => ApplyDamage(target, amount, owner, elements, false);

    private void DamageProtection(Wizard target, float amount, int owner, Element[] elements)
        => ApplyDamage(target, amount, owner, elements, true, true);

    private void ApplyDamage(Wizard target, float amount, int owner, Element[]? elements, bool shieldOnly, bool resolvedElements = false, ReadOnlySpan<float> weights = default, StatisticAttack? cause = null, bool reflected = false, bool replayed = false)
    {
        if (!target.Alive || target.Invulnerable > 0 || amount <= 0) return;
        if (!resolvedElements)
        { if (elements != null) _statistics.Immune(owner, target, elements); amount *= DefenseElementFraction(target, elements); }
        if (amount <= 0) return;
        amount *= (reflected || replayed ? 1 : DamagePower(owner)) * RelicBuffs.Incoming(target) * target.Equipment.Incoming;
        float healthBefore = target.Health, shieldBefore = target.Shield;
        float absorbed = Math.Min(target.Shield, amount);
        target.Shield -= absorbed; amount -= absorbed;
        if (shieldOnly) amount = 0;
        if (Map.IsPractice)
        {
            var reading = PracticeReading(target.Id);
            reading.Absorbed += absorbed; reading.Damage += Math.Min(target.Health, amount);
        }
        target.Health = Math.Max(0, target.Health - amount);
        var attack = cause ?? _statisticsAttack ?? (elements is { Length: > 0 } ? MatchStatisticsCollector.Shape(new SpellRecipe(elements).Shape) : StatisticAttack.EnemyMelee);
        _statistics.Damage(owner, target, healthBefore - target.Health, shieldBefore - target.Shield, Math.Max(0, amount - healthBefore), elements, attack,
            TryCombatSource(owner, out var statisticsSource) && Allied(statisticsSource, target), weights);
        RecordArcaneDamage(target, owner, (double)(healthBefore - target.Health) + shieldBefore - target.Shield, elements, weights, attack);
        if (healthBefore > 0 && target.Health <= 0)
        {
            target.StunTime = target.WaterSlowTime = 0; target.WaterSlowRank = 0;
            PruneLightningContacts(target.Id);
            ClearAutomaticRockProgress(target);
            _statistics.Death(target, owner, attack); QueueArcaneMarkDeath(target);
            CountAutomaticRockKill(target, owner);
        }
        // Un rayo golpea a 60 Hz; limita sus impactos, pero nunca omite la rotura del escudo.
        bool broken = absorbed > 0 && target.Shield <= 0;
        if (broken) { target.RegenerationRate = 0; Emit("shield_break", target.Position, [Element.Shield], ""); }
        if (!reflected) ApplyDefensiveAffixes(target, owner, absorbed, broken, elements, weights, shieldOnly);
        if (target.HitEffectCooldown <= 0)
        {
            if (absorbed > 0 && !broken) Emit("shield_hit", target.Position, [Element.Shield], "");
            if (amount > 0) Emit("hurt", target.Position, elements ?? [], "");
            target.HitEffectCooldown = 0.14f;
        }
        if (target.Health > 0) return;
        if (DefenseDeath(target, owner)) return;
        target.Deaths++;
        target.Respawn = IsDefense ? 4 + target.Deaths * 0.5f : Map.RespawnSeconds(Options.Mode, target.Deaths);
        RelicBuffs.Clear(target);
        CancelCast(target); target.Selection.Clear(); target.Shield = target.ShieldMax = target.ShieldTime = target.RegenerationRate = 0;
        if (Options.Mode == GameMode.Warfare && !Map.IsPractice && !HoldToWin && !ConquestToWin) Scores[target.Team] = Math.Max(0, Scores[target.Team] - 1);
        string text = $"{target.Name} ha caído";
        if (TryCombatSource(owner, out var killer) && killer.Id != target.Id)
        {
            bool enemy = Hostile(killer, target);
            killer.Kills = Math.Max(0, killer.Kills + (enemy ? 1 : -1));
            killer.Energy = Math.Min(100, killer.Energy + 15);
            // Cada baja de otro mago cuenta para la ascensión, incluso con fuego amigo.
            // Una baja póstuma aumenta la capacidad, pero no reanima al autor.
            if (Map.KillHealthBonus > 0)
            {
                killer.MaxHealth += Map.KillHealthBonus;
                if (killer.Alive) killer.Health = Math.Min(killer.MaxHealth, killer.Health + Map.KillHealthBonus);
            }
            if (enemy && Options.Mode == GameMode.Duel) Scores[killer.Team]++;
            text = $"{killer.Name} → {target.Name}";
        }
        Emit("death", target.Position, [], text);
    }
}

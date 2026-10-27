namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void ApplyDefensiveAffixes(Wizard target, int owner, float absorbed, bool broken, Element[]? elements, ReadOnlySpan<float> weights, bool shieldOnly)
    {
        if (!target.Alive || target.ShieldTime <= 0 || absorbed <= 0 || !TryCombatSource(owner, out var attacker) || !Hostile(target, attacker)) return;
        if (target.Equipment.ShieldBattery > 0)
            target.Energy = Math.Min(100, target.Energy + absorbed * CombatAffixes.BatteryPerHundred(target.Equipment.ShieldBattery) / 100);
        if (target.Equipment.ShieldLeech > 0)
            HealAs(StatisticAttack.ShieldLeech, target, absorbed * CombatAffixes.ShieldLeechPercent(target.Equipment.ShieldLeech) / 100, target.Id);
        if (target.Equipment.ShieldReflection > 0 && attacker.Alive)
            ReflectShieldDamage(target, attacker, absorbed * CombatAffixes.ShieldReflectionPercent(target.Equipment.ShieldReflection) / 100, elements, weights, shieldOnly);
        if (!broken || target.Equipment.ShieldReprisal <= 0 || target.ReprisalCooldown > 0) return;
        target.ReprisalCooldown = CombatAffixes.ReprisalSeconds;
        foreach (var enemy in _combatants.Values)
        {
            if (!enemy.Alive || enemy.Invulnerable > 0 || enemy.VulnerableElement != null || !Hostile(target, enemy)) continue;
            var offset = enemy.Position - target.Position;
            if (offset.Length > CombatAffixes.ReprisalRadius || !Visible(target.Position, enemy.Position, target.Id)) continue;
            enemy.Knockback += offset.Normalized * CombatAffixes.ReprisalForce(target.Equipment.ShieldReprisal);
        }
        Emit("nova", target.Position, [Element.Shield], "", CombatAffixes.ReprisalRadius);
    }

    private void ReflectShieldDamage(Wizard defender, Wizard attacker, float amount, Element[]? elements, ReadOnlySpan<float> incoming, bool shieldOnly)
    {
        if (attacker.Invulnerable > 0) return;
        if (elements != null) _statistics.Immune(defender.Id, attacker, elements);
        Span<float> weights = stackalloc float[9];
        weights.Clear();
        if (incoming.Length == 9) incoming.CopyTo(weights);
        else if (elements is { Length: > 0 }) foreach (var element in elements) weights[(int)element]++;
        else weights[8] = 1;
        float total = 0, resolved = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            total += weights[i];
            if (i < 8) weights[i] *= WizardTypes.Incoming(attacker.WizardType, (Element)i) * Resistance(attacker, (Element)i);
            else weights[i] *= DefenseElementFraction(attacker, null);
            resolved += weights[i];
        }
        if (total <= 0 || resolved <= 0) return;
        ApplyDamage(attacker, amount * resolved / total, defender.Id, elements, shieldOnly, resolvedElements: true,
            weights: weights, cause: StatisticAttack.ShieldReflection, reflected: true);
    }

    private void RepairShieldFromHealing(Wizard target, int owner, float healed, StatisticAttack cause)
    {
        if (healed <= 0 || target.Shield <= 0 || target.ShieldTime <= 0 || cause is StatisticAttack.Regeneration or StatisticAttack.RegenerationAura or StatisticAttack.ShieldLeech ||
            !TryCombatSource(owner, out var source) || !source.Alive || !Allied(source, target) || source.Equipment.MendingWard <= 0) return;
        float repaired = Math.Min(Math.Max(0, target.ShieldMax - target.Shield), healed * CombatAffixes.MendingPercent(source.Equipment.MendingWard) / 100);
        if (repaired <= 0) return;
        target.Shield += repaired;
        _statistics.Shield(owner, repaired, repair: true);
        _statistics.Attack(owner, cause, StatisticMeasure.ShieldRepaired, repaired);
    }
}

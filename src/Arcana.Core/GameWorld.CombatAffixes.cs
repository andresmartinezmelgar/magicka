namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void ApplyDefensiveAffixes(Wizard target, int owner, float absorbed, bool broken)
    {
        if (!target.Alive || target.ShieldTime <= 0 || absorbed <= 0 || !_combatants.TryGetValue(owner, out var attacker) || !Hostile(target, attacker)) return;
        if (target.Equipment.ShieldBattery > 0)
            target.Energy = Math.Min(100, target.Energy + absorbed * CombatAffixes.BatteryPerHundred(target.Equipment.ShieldBattery) / 100);
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

    private void RepairShieldFromHealing(Wizard target, int owner, float healed, StatisticAttack cause)
    {
        if (healed <= 0 || target.Shield <= 0 || target.ShieldTime <= 0 || cause is StatisticAttack.Regeneration or StatisticAttack.RegenerationAura ||
            !_combatants.TryGetValue(owner, out var source) || !source.Alive || !Allied(source, target) || source.Equipment.MendingWard <= 0) return;
        float repaired = Math.Min(Math.Max(0, target.ShieldMax - target.Shield), healed * CombatAffixes.MendingPercent(source.Equipment.MendingWard) / 100);
        if (repaired <= 0) return;
        target.Shield += repaired;
        _statistics.Shield(owner, repaired, repair: true);
        _statistics.Attack(owner, cause, StatisticMeasure.ShieldRepaired, repaired);
    }
}

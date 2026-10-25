namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void TickRing(Wizard player)
    {
        if (player.Equipment.MagicRingFamily is not { } family || player.RingCooldown > 0) return;
        int level = player.Equipment.MagicRingLevel;
        int copies = RingEffects.ElementCopies(level);
        player.RingCooldown = RingEffects.Interval(level);
        var elements = RingEffects.ShieldElements(family, level);
        float capacity = RingEffects.BaseShield(family, level);
        _statistics.Cast(player.Id, StatisticAttack.RingShield, elements);
        if (RefreshRingShield(player, player, capacity, elements))
        {
            if (family == EquipmentFamily.Water && player.Equipment.WaterShieldPool) CreateEquipmentWaterPool(player);
            Emit("self", player.Position, elements, "");
        }
        _statistics.Cast(player.Id, StatisticAttack.RingPulse, RingEffects.ElementFor(family) is { } element ? [element] : []);
        if (family == EquipmentFamily.Lightning)
        {
            Wizard? first = null;
            for (int n = 0; n < copies; n++)
            {
                Wizard? closest = null; float distance = RingEffects.LightningRadius;
                foreach (var target in _combatants.Values)
                {
                    if (!target.Alive || target == first || !Hostile(player, target)) continue;
                    float d = V2.Distance(player.Position, target.Position);
                    if (d >= distance || !Visible(player.Position, target.Position, player.Id)) continue;
                    closest = target; distance = d;
                }
                if (closest == null) break;
                HitAs(StatisticAttack.RingPulse, closest, new([Element.Lightning]), RingEffects.PulseScale(family, level), player.Id, V2.Zero);
                _beams.Add(new(player.Id, player.Position, closest.Position, [Element.Lightning], SpellShape.Lightning));
                first = closest;
            }
            return;
        }
        // La acción solo recorre combatientes al activarse, como máximo una vez cada ocho segundos.
        // Hit puede retirar minions muertos; las retiradas se hacen en CleanupDefense tras la simulación.
        foreach (var target in _combatants.Values)
        {
            if (!target.Alive || V2.Distance(player.Position, target.Position) > RingEffects.PulseRadius ||
                !Visible(player.Position, target.Position, player.Id)) continue;
            if (family == EquipmentFamily.Life)
            {
                if (Allied(player, target)) HealAs(StatisticAttack.RingPulse, target, RingEffects.BaseHealing(level) * HealingPower(player.Id), player.Id);
                continue;
            }
            if (family == EquipmentFamily.Shield)
            {
                if (target.Id != player.Id && Allied(player, target)) RefreshRingShield(player, target, RingEffects.BaseAllyShield(level), [Element.Shield]);
                continue;
            }
            if (!Hostile(player, target)) continue;
            var direction = (target.Position - player.Position).Normalized;
            if (family == EquipmentFamily.Mixed)
            {
                if (target.Invulnerable <= 0 && target.VulnerableElement == null) target.Knockback += direction * (12 * copies);
            }
            else if (RingEffects.ElementFor(family) is { } attack)
                HitAs(StatisticAttack.RingPulse, target, new([attack]), RingEffects.PulseScale(family, level), player.Id, direction);
        }
        Emit("nova", player.Position, RingEffects.ElementFor(family) is { } pulse ? [pulse] : [], "", RingEffects.PulseRadius);
    }

    private bool RefreshRingShield(Wizard owner, Wizard target, float baseCapacity, Element[] elements)
    {
        float capacity = baseCapacity * owner.Equipment.ShieldCapacity;
        if (target.Shield > 0 && target.ShieldMax > capacity && target.ShieldTime > 0) return false;
        float before = target.Shield;
        target.Shield = target.ShieldMax = capacity;
        target.ShieldTime = target.WardTime = RingEffects.ShieldSeconds * owner.Equipment.ShieldDuration;
        target.Ward = elements.Where(e => e != Element.Shield).ToArray();
        target.ShieldCharges = elements.Length;
        target.RegenerationRate = RingEffects.RegenerationPerLife * elements.Count(e => e == Element.Life);
        _statistics.Shield(owner.Id, Math.Max(0, capacity - before));
        return true;
    }
}

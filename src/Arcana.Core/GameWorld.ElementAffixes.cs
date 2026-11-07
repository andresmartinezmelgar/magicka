namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed class LightningContact
    {
        public long LastTick = -1;
        public int Ticks;
    }
    private readonly Dictionary<(int Owner, int Target), LightningContact> _lightningContacts = [];
    private readonly List<(int Owner, int Target)> _expiredLightningContacts = [];

    private static float WaterSlowMultiplier(Wizard target) => target.Wet > 0 && target.WaterSlowTime > 0
        ? 1 - CombatAffixes.WaterSlowPercent(target.WaterSlowRank) / 100f : 1;

    private void ApplyWaterSlow(Wizard target, int owner)
    {
        if (!target.Alive || target.Invulnerable > 0 || target.Wet <= 0 || !AcceptsDefenseElement(target, Element.Water) ||
            !TryCombatSource(owner, out var source) || !Hostile(source, target) || source.Equipment.WaterSlow <= 0) return;
        int rank = source.Equipment.WaterSlow;
        // Una copia más débil no prolonga la potencia de otra más fuerte.
        if (target.WaterSlowTime > 0 && rank < target.WaterSlowRank) return;
        target.WaterSlowRank = rank;
        target.WaterSlowTime = CombatAffixes.WaterSlowSeconds;
    }

    private void RecordLightningContact(Wizard target, int owner)
    {
        if (!target.Alive || target.Invulnerable > 0 || !AcceptsDefenseElement(target, Element.Lightning) ||
            !TryCombatSource(owner, out var source) || !source.Alive || !Hostile(source, target) || source.Equipment.LightningStun <= 0) return;
        var key = (owner, target.Id);
        if (!_lightningContacts.TryGetValue(key, out var contact)) _lightningContacts.Add(key, contact = new());
        // Un haz, sus ecos y sus saltos solo acreditan una vez el mismo tick.
        if (contact.LastTick == Tick) return;
        if (contact.LastTick != Tick - 1) contact.Ticks = 0;
        contact.LastTick = Tick;
        if (++contact.Ticks < CombatAffixes.LightningStunIntervalTicks) return;
        contact.Ticks = 0;
        target.StunTime = Math.Max(target.StunTime, CombatAffixes.LightningStunSeconds(source.Equipment.LightningStun));
        CancelCast(target);
    }

    private void PruneLightningContacts(int removedId = 0)
    {
        _expiredLightningContacts.Clear();
        foreach (var pair in _lightningContacts)
            if (removedId > 0 ? pair.Key.Owner == removedId || pair.Key.Target == removedId : pair.Value.LastTick != Tick)
                _expiredLightningContacts.Add(pair.Key);
        foreach (var key in _expiredLightningContacts) _lightningContacts.Remove(key);
        _expiredLightningContacts.Clear();
    }
}

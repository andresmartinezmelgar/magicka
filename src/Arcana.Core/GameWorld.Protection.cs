namespace Arcana.Core;

public sealed partial class GameWorld
{
    private float Resistance(Wizard player, Element element) => !AcceptsDefenseElement(player, element) ? 0 : ElementInteractions.ProtectionMultiplier(element,
        player.Ward.Length > 0 && player.WardTime > 0 ? player.Ward
        : player.Shield > 0 && player.ShieldTime > 0 ? ElementInteractions.ShieldProtection : ReadOnlySpan<Element>.Empty);

    private static bool PassesOwnShield(Wall wall, int owner, BeamPart[]? parts = null)
    {
        if (owner <= 0 || wall.Owner != owner || wall.Recipe.Elements.Length != 1 || !wall.Recipe.Has(Element.Shield)) return false;
        if (parts != null) foreach (var part in parts) if (part.Owner != owner) return false;
        return true;
    }
    private float WallDamage(Wall wall, SpellRecipe attack, int owner, float amount)
    {
        float weight = 0, weighted = 0;
        for (int i = 0; i < 8; i++)
        {
            var element = (Element)i;
            float component = SpellBalance.ProtectionWeight(attack, element);
            if (component <= 0) continue;
            weight += component;
            weighted += component * Outgoing(owner, element) * ElementInteractions.ProtectionMultiplier(element, wall.Recipe.Elements);
        }
        return amount * DamagePower(owner) * (weight > 0 ? weighted / weight : 1);
    }
    private float UtilityPressure(Wizard target, SpellRecipe spell, int owner)
    {
        float pressure = target.VulnerableElement != Element.Water && SpellBalance.Component(spell, Element.Water) == 0
            ? ElementDamage(target, owner, Element.Water, SpellBalance.UtilityPressure * spell.Count(Element.Water)) : 0;
        if (!TryCombatSource(owner, out var source) || !Allied(source, target))
            pressure += ElementDamage(target, owner, Element.Life, SpellBalance.UtilityPressure * spell.Count(Element.Life));
        return pressure;
    }
}

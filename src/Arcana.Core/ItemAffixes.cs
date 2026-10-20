namespace Arcana.Core;

// Optional and separate from percentage bonuses: older items keep their original roll.
public enum ItemAffixKind { BeamPierce, LightningChain, DoubleBurn, WaterShieldPool }
public sealed record ItemAffix(ItemAffixKind Kind, int Value = 1);
public static class ItemAffixes
{
    public static ItemAffix? Roll(Random random, ItemTier tier)
    {
        if (tier < ItemTier.Diamond) return null;
        var kind = (ItemAffixKind)random.Next(4);
        int maximum = kind switch
        {
            ItemAffixKind.BeamPierce => tier == ItemTier.Mythic ? 3 : 2,
            ItemAffixKind.LightningChain => tier == ItemTier.Mythic ? 2 : 1,
            _ => 1
        };
        return new(kind, random.Next(1, maximum + 1));
    }
    public static bool Valid(ItemAffix? affix, ItemTier tier) => affix == null ||
        tier >= ItemTier.Diamond && Enum.IsDefined(affix.Kind) && affix.Value >= 1 &&
        affix.Value <= (affix.Kind switch
        {
            ItemAffixKind.BeamPierce => tier == ItemTier.Mythic ? 3 : 2,
            ItemAffixKind.LightningChain => tier == ItemTier.Mythic ? 2 : 1,
            _ => 1
        });
    public static string Describe(ItemAffix? affix) => affix == null ? "" : affix.Kind switch
    {
        ItemAffixKind.BeamPierce => $"Los haces ofensivos atraviesan {affix.Value} enemigo(s) adicionales. Los escudos y las paredes detienen la penetración.",
        ItemAffixKind.LightningChain => $"Los relámpagos saltan a {affix.Value} enemigo(s) adicionales a un máximo de 5 m por salto, con línea de visión.",
        ItemAffixKind.DoubleBurn => "Las quemaduras infligen el doble de daño por segundo. Su duración y el impacto inicial no cambian.",
        ItemAffixKind.WaterShieldPool => "Al ponerte un escudo de agua creas un charco de 3 m durante 5 s. Moja a todos dentro, incluido tú.",
        _ => ""
    };
    public static string Summary(EquipmentModifiers modifiers) => string.Join("\n", new[]
    {
        modifiers.BeamPierce > 0 ? Describe(new(ItemAffixKind.BeamPierce, modifiers.BeamPierce)) : "",
        modifiers.LightningChains > 0 ? Describe(new(ItemAffixKind.LightningChain, modifiers.LightningChains)) : "",
        modifiers.BurnPotency > 1 ? Describe(new(ItemAffixKind.DoubleBurn)) : "",
        modifiers.WaterShieldPool ? Describe(new(ItemAffixKind.WaterShieldPool)) : ""
    }.Where(s => s.Length > 0));
}

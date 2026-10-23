namespace Arcana.Core;

// Optional and separate from percentage bonuses: older items keep their original roll.
public enum ItemAffixKind { BeamPierce, LightningChain, DoubleBurn, WaterShieldPool, CoinDividend, GoldBonus }
public sealed record ItemAffix(ItemAffixKind Kind, int Value = 1);
public static class ItemAffixes
{
    public static ItemAffix? Roll(Random random, ItemTier tier)
    {
        if (tier < ItemTier.Diamond) return null;
        var kind = (ItemAffixKind)random.Next(Enum.GetValues<ItemAffixKind>().Length);
        if (kind == ItemAffixKind.GoldBonus) return new(kind, tier == ItemTier.Mythic ? 20 : 10);
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
        (affix.Kind == ItemAffixKind.GoldBonus ? affix.Value == 10 || tier == ItemTier.Mythic && affix.Value == 20 :
        affix.Value <= (affix.Kind switch
        {
            ItemAffixKind.BeamPierce => tier == ItemTier.Mythic ? 3 : 2,
            ItemAffixKind.LightningChain => tier == ItemTier.Mythic ? 2 : 1,
            _ => 1
        }));
    public static bool CanUpgrade(EquipmentItem item, int level) => level is >= 0 and < DefenseRules.ItemUpgradeLimit &&
        (item.Bonuses.Any(b => b.Current < b.Maximum) || item.Affix?.Kind == ItemAffixKind.CoinDividend);
    public static string Describe(ItemAffix? affix, int level = 0) => affix == null ? "" : affix.Kind switch
    {
        ItemAffixKind.BeamPierce => $"Los haces ofensivos atraviesan {affix.Value} enemigo(s) adicionales. Los escudos y las paredes detienen la penetración.",
        ItemAffixKind.LightningChain => $"Los relámpagos saltan a {affix.Value} enemigo(s) adicionales a un máximo de 5 m por salto, con línea de visión.",
        ItemAffixKind.DoubleBurn => "Las quemaduras infligen el doble de daño por segundo. Su duración y el impacto inicial no cambian.",
        ItemAffixKind.WaterShieldPool => "Al ponerte un escudo de agua creas un charco de 3 m durante 5 s. Moja a todos dentro, incluido tú.",
        ItemAffixKind.CoinDividend => $"1 oro extra cada {GoldBonuses.EveryForLevel(level)} moneda(s) base · nivel {level}/10. " +
            (level < 10 ? "Cada mejora reduce el intervalo en 1; al nivel 10, 1 extra por moneda. " : "") +
            "Supervivencia: recogidas. PvP: premio final. No afecta a ventas.",
        ItemAffixKind.GoldBonus => $"+{affix.Value}% de oro base. Supervivencia: oro recogido durante la partida. PvP: recompensa final. No afecta a ventas.",
        _ => ""
    };
    public static string Summary(EquipmentModifiers modifiers) => string.Join("\n", new[]
    {
        modifiers.BeamPierce > 0 ? Describe(new(ItemAffixKind.BeamPierce, modifiers.BeamPierce)) : "",
        modifiers.LightningChains > 0 ? Describe(new(ItemAffixKind.LightningChain, modifiers.LightningChains)) : "",
        modifiers.BurnPotency > 1 ? Describe(new(ItemAffixKind.DoubleBurn)) : "",
        modifiers.WaterShieldPool ? Describe(new(ItemAffixKind.WaterShieldPool)) : "",
        modifiers.ExtraGoldEvery > 0 ? Describe(new(ItemAffixKind.CoinDividend), 11 - modifiers.ExtraGoldEvery) : "",
        modifiers.GoldBonusPercent > 0 ? Describe(new(ItemAffixKind.GoldBonus, modifiers.GoldBonusPercent)) : ""
    }.Where(s => s.Length > 0));
}

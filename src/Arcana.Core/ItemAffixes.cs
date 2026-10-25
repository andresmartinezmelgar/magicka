namespace Arcana.Core;

// Optional and separate from percentage bonuses: older items keep their original roll.
public enum ItemAffixKind { BeamPierce, LightningChain, DoubleBurn, WaterShieldPool, CoinDividend, GoldBonus }
public sealed record ItemAffix(ItemAffixKind Kind, int Value = 1);
public static class ItemAffixes
{
    private static readonly ItemAffixKind[] CombatKinds =
        [ItemAffixKind.BeamPierce, ItemAffixKind.LightningChain, ItemAffixKind.DoubleBurn, ItemAffixKind.WaterShieldPool];
    private static readonly ItemAffixKind[] GoldRingKinds = Enum.GetValues<ItemAffixKind>();
    public static bool IsGold(ItemAffixKind kind) => kind is ItemAffixKind.CoinDividend or ItemAffixKind.GoldBonus;
    public static bool Allowed(ItemAffixKind kind, EquipmentSlot slot) =>
        Enum.IsDefined(kind) && Enum.IsDefined(slot) && (!IsGold(kind) || slot == EquipmentSlot.GoldRing);
    public static ItemAffix? Active(ItemAffix? affix, EquipmentSlot slot) =>
        affix != null && Allowed(affix.Kind, slot) ? affix : null;

    public static ItemAffix? Roll(Random random, ItemTier tier, EquipmentSlot slot)
    {
        if (tier < ItemTier.Diamond) return null;
        var kinds = slot == EquipmentSlot.GoldRing ? GoldRingKinds : CombatKinds;
        var kind = kinds[random.Next(kinds.Length)];
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
    // Los catálogos 1–5 siguen pudiendo cargarse, venderse y equiparse sin volver a sortear piezas.
    // Active desactiva sus afijos de oro fuera del anillo; desde catálogo 6 no pueden generarse ni validarse allí.
    public static bool Valid(ItemAffix? affix, ItemTier tier, EquipmentSlot slot, int generationVersion) =>
        Valid(affix, tier) && (affix == null || Allowed(affix.Kind, slot) || generationVersion < 6);
    public static bool CanUpgrade(EquipmentItem item, int level) => level is >= 0 and < DefenseRules.ItemUpgradeLimit &&
        (item.Bonuses.Any(b => b.Current < b.Maximum) || RingEffects.IsRing(item.Slot));
    public static string Describe(ItemAffix? affix, int level = 0, EquipmentSlot? slot = null) => affix == null ? ""
        : slot.HasValue && !Allowed(affix.Kind, slot.Value)
            ? "Afijo de oro antiguo inactivo: los bonos de oro solo funcionan en el anillo de oro."
        : affix.Kind switch
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
        modifiers.MagicRingFamily is { } family ? RingEffects.Describe(EquipmentSlot.MagicRing, family, modifiers.MagicRingLevel) : "",
        modifiers.ExtraGoldEvery > 0 ? Describe(new(ItemAffixKind.CoinDividend), 11 - modifiers.ExtraGoldEvery) : "",
        modifiers.GoldBonusPercent > 0 ? Describe(new(ItemAffixKind.GoldBonus, modifiers.GoldBonusPercent)) : ""
    }.Where(s => s.Length > 0));
}

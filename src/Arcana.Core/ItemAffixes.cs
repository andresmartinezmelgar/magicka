namespace Arcana.Core;

// Optional and separate from percentage bonuses: older items keep their original roll.
// Añadir al final: los identificadores guardados nunca cambian.
public enum ItemAffixKind { BeamPierce, LightningChain, DoubleBurn, WaterShieldPool, CoinDividend, GoldBonus,
    Icebreaker, SteamImpact, ArcaneExecution, ShieldBattery, MendingWard, ShieldReprisal, ShieldLeech, ShieldReflection }
public sealed record ItemAffix(ItemAffixKind Kind, int Value = 1);
public static class ItemAffixes
{
    private static readonly ItemAffixKind[] CombatKinds =
        [ItemAffixKind.BeamPierce, ItemAffixKind.LightningChain, ItemAffixKind.DoubleBurn, ItemAffixKind.WaterShieldPool,
            ItemAffixKind.Icebreaker, ItemAffixKind.SteamImpact, ItemAffixKind.ArcaneExecution,
            ItemAffixKind.ShieldBattery, ItemAffixKind.MendingWard, ItemAffixKind.ShieldReprisal,
            ItemAffixKind.ShieldLeech, ItemAffixKind.ShieldReflection];
    private static readonly ItemAffixKind[] GoldRingKinds = [ItemAffixKind.CoinDividend, ItemAffixKind.GoldBonus];
    public static bool IsExtended(ItemAffixKind kind) => kind is ItemAffixKind.Icebreaker or ItemAffixKind.SteamImpact or
        ItemAffixKind.ArcaneExecution or ItemAffixKind.ShieldBattery or ItemAffixKind.MendingWard or ItemAffixKind.ShieldReprisal or
        ItemAffixKind.ShieldLeech or ItemAffixKind.ShieldReflection;
    public static int MinimumGeneration(ItemAffixKind kind) => kind is ItemAffixKind.ShieldLeech or ItemAffixKind.ShieldReflection
        ? 9 : IsExtended(kind) ? 8 : 1;
    public static bool IsGold(ItemAffixKind kind) => kind is ItemAffixKind.CoinDividend or ItemAffixKind.GoldBonus;
    public static bool Allowed(ItemAffixKind kind, EquipmentSlot slot) =>
        Enum.IsDefined(kind) && Enum.IsDefined(slot) && slot != EquipmentSlot.MagicRing &&
        (!IsGold(kind) || slot == EquipmentSlot.GoldRing);
    public static ItemAffix? Active(ItemAffix? affix, EquipmentSlot slot) =>
        affix != null && Allowed(affix.Kind, slot) ? affix : null;

    public static ItemAffix? Roll(Random random, ItemTier tier, EquipmentSlot slot)
    {
        if (tier < ItemTier.Diamond || slot == EquipmentSlot.MagicRing) return null;
        // Mantener el 1/3 de afijos económicos del anillo de oro al ampliar la variedad de combate.
        var kinds = slot == EquipmentSlot.GoldRing && random.Next(3) == 0 ? GoldRingKinds : CombatKinds;
        var kind = kinds[random.Next(kinds.Length)];
        if (kind == ItemAffixKind.GoldBonus) return new(kind, tier == ItemTier.Mythic ? 20 : 10);
        if (IsExtended(kind)) return new(kind, tier == ItemTier.Mythic ? 2 : 1);
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
        IsExtended(affix.Kind) ? affix.Value <= (tier == ItemTier.Mythic ? 2 : 1) :
        affix.Value <= (affix.Kind switch
        {
            ItemAffixKind.BeamPierce => tier == ItemTier.Mythic ? 3 : 2,
            ItemAffixKind.LightningChain => tier == ItemTier.Mythic ? 2 : 1,
            _ => 1
        }));
    // Los objetos anteriores conservan sus tiradas. Active excluye sus efectos retirados.
    public static bool Valid(ItemAffix? affix, ItemTier tier, EquipmentSlot slot, int generationVersion) =>
        Valid(affix, tier) && (affix == null || generationVersion >= MinimumGeneration(affix.Kind)) &&
        (affix == null || Allowed(affix.Kind, slot) ||
            generationVersion < (IsGold(affix.Kind) ? 6 : 7));
    public static bool CanUpgrade(EquipmentItem item, int level) => level is >= 0 and < DefenseRules.ItemUpgradeLimit &&
        (item.Bonuses.Any(b => b.Current < b.Maximum) || RingEffects.IsRing(item.Slot));
    public static string Describe(ItemAffix? affix, int level = 0, EquipmentSlot? slot = null) => affix == null ? ""
        : slot.HasValue && !Allowed(affix.Kind, slot.Value)
            ? slot == EquipmentSlot.MagicRing
                ? "Afijo antiguo inactivo: el anillo mágico solo aporta capacidad de escudo y su habilidad elemental."
                : "Afijo de oro antiguo inactivo: los bonos de oro solo funcionan en el anillo de oro."
        : affix.Kind switch
    {
        ItemAffixKind.BeamPierce => $"Los haces ofensivos atraviesan {affix.Value} enemigo(s) adicionales. Los escudos y las paredes detienen la penetración.",
        ItemAffixKind.LightningChain => $"Los relámpagos saltan a {affix.Value} enemigo(s) adicionales a un máximo de 5 m por salto, con línea de visión.",
        ItemAffixKind.DoubleBurn => "Las quemaduras infligen el doble de daño por segundo. Su duración y el impacto inicial no cambian.",
        ItemAffixKind.WaterShieldPool => "Al ponerte un escudo de agua creas un charco de 3 m durante 5 s. Moja a todos dentro, incluido tú.",
        ItemAffixKind.Icebreaker => $"Quebrantahielo: +{CombatAffixes.IcebreakerPercent(affix.Value)} % al daño de Tierra de tus hechizos contra enemigos congelados.",
        ItemAffixKind.SteamImpact => $"Choque térmico: +{CombatAffixes.SteamImpactPercent(affix.Value)} % al impacto de Fuego de tus hechizos contra enemigos mojados. No aumenta la quemadura ni convierte el agua en daño de vida.",
        ItemAffixKind.ArcaneExecution => $"Remate arcano: +{CombatAffixes.ArcaneExecutionPercent(affix.Value)} % al daño Arcano de tus hechizos contra enemigos con un 30 % de vida o menos.",
        ItemAffixKind.ShieldBattery => $"Batería protectora: recuperas {CombatAffixes.BatteryPerHundred(affix.Value)} de energía por cada 100 HP de daño enemigo absorbido por tu escudo. Máximo 100 de energía.",
        ItemAffixKind.MendingWard => $"Remiendo protector: tus curaciones directas reparan el escudo activo propio o aliado un {CombatAffixes.MendingPercent(affix.Value)} % de los HP realmente curados. No crea escudos, no aumenta su máximo ni duración y no actúa con regeneración.",
        ItemAffixKind.ShieldReprisal => $"Última barrera: cuando un enemigo rompe tu escudo y sobrevives, empujas a los enemigos visibles hasta {CombatAffixes.ReprisalRadius} m. Fuerza {CombatAffixes.ReprisalForce(affix.Value)}; recarga {CombatAffixes.ReprisalSeconds} s. No causa daño ni interrumpe hechizos.",
        ItemAffixKind.ShieldLeech => $"Escudo vital: recuperas {CombatAffixes.ShieldLeechPercent(affix.Value)} % de los HP de daño enemigo absorbido por tu escudo personal mientras sigas vivo. No cuenta barreras, daño propio ni fuego amigo. No repara el escudo ni aumenta su duración.",
        ItemAffixKind.ShieldReflection => $"Espejo protector: devuelve al atacante el {CombatAffixes.ShieldReflectionPercent(affix.Value)} % del daño absorbido por tu escudo personal si sobrevives. Conserva los elementos y respeta sus defensas e inmunidades, sin multiplicadores de ataque. El reflejo no activa afijos de absorción ni otro reflejo.",
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
        modifiers.Icebreaker > 0 ? Describe(new(ItemAffixKind.Icebreaker, modifiers.Icebreaker)) : "",
        modifiers.SteamImpact > 0 ? Describe(new(ItemAffixKind.SteamImpact, modifiers.SteamImpact)) : "",
        modifiers.ArcaneExecution > 0 ? Describe(new(ItemAffixKind.ArcaneExecution, modifiers.ArcaneExecution)) : "",
        modifiers.ShieldBattery > 0 ? Describe(new(ItemAffixKind.ShieldBattery, modifiers.ShieldBattery)) : "",
        modifiers.MendingWard > 0 ? Describe(new(ItemAffixKind.MendingWard, modifiers.MendingWard)) : "",
        modifiers.ShieldReprisal > 0 ? Describe(new(ItemAffixKind.ShieldReprisal, modifiers.ShieldReprisal)) : "",
        modifiers.ShieldLeech > 0 ? Describe(new(ItemAffixKind.ShieldLeech, modifiers.ShieldLeech)) : "",
        modifiers.ShieldReflection > 0 ? Describe(new(ItemAffixKind.ShieldReflection, modifiers.ShieldReflection)) : "",
        modifiers.MagicRingFamily is { } family ? RingEffects.Describe(EquipmentSlot.MagicRing, family, modifiers.MagicRingLevel, modifiers) : "",
        modifiers.ExtraGoldEvery > 0 ? Describe(new(ItemAffixKind.CoinDividend), 11 - modifiers.ExtraGoldEvery) : "",
        modifiers.GoldBonusPercent > 0 ? Describe(new(ItemAffixKind.GoldBonus, modifiers.GoldBonusPercent)) : ""
    }.Where(s => s.Length > 0));
}

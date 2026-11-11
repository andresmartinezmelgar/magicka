namespace Arcana.Core;

// Optional and separate from percentage bonuses: older items keep their original roll.
// Añadir al final: los identificadores guardados nunca cambian.
public enum ItemAffixKind { BeamPierce, LightningChain, DoubleBurn, WaterShieldPool, CoinDividend, GoldBonus,
    Icebreaker, SteamImpact, ArcaneExecution, ShieldBattery, MendingWard, ShieldReprisal, ShieldLeech, ShieldReflection,
    RockFragmentation, ArcaneMark, ArcaneEcho, AutomaticRock, LightningStun, WaterSlow }
public sealed record ItemAffix(ItemAffixKind Kind, int Value = 1);
public static class ItemAffixes
{
    private static readonly ItemAffixKind[] CombatKinds =
        [ItemAffixKind.BeamPierce, ItemAffixKind.LightningChain, ItemAffixKind.DoubleBurn, ItemAffixKind.WaterShieldPool,
            ItemAffixKind.Icebreaker, ItemAffixKind.SteamImpact, ItemAffixKind.ArcaneExecution,
            ItemAffixKind.ShieldBattery, ItemAffixKind.MendingWard, ItemAffixKind.ShieldReprisal,
            ItemAffixKind.ShieldLeech, ItemAffixKind.ShieldReflection, ItemAffixKind.RockFragmentation,
            ItemAffixKind.ArcaneMark, ItemAffixKind.ArcaneEcho, ItemAffixKind.AutomaticRock,
            ItemAffixKind.LightningStun, ItemAffixKind.WaterSlow];
    private static readonly ItemAffixKind[] GoldRingKinds = [ItemAffixKind.CoinDividend, ItemAffixKind.GoldBonus];
    public static int CombatEffectCount => CombatKinds.Length;
    public static string Name(ItemAffixKind kind) => kind switch
    {
        ItemAffixKind.BeamPierce => "Penetración de haces", ItemAffixKind.LightningChain => "Saltos de relámpago",
        ItemAffixKind.DoubleBurn => "Doble quemadura", ItemAffixKind.WaterShieldPool => "Charco de escudo de agua",
        ItemAffixKind.Icebreaker => "Quebrantahielo", ItemAffixKind.SteamImpact => "Choque térmico", ItemAffixKind.ArcaneExecution => "Remate arcano",
        ItemAffixKind.ShieldBattery => "Batería protectora", ItemAffixKind.MendingWard => "Remiendo protector",
        ItemAffixKind.ShieldReprisal => "Última barrera", ItemAffixKind.ShieldLeech => "Escudo vital", ItemAffixKind.ShieldReflection => "Espejo protector",
        ItemAffixKind.RockFragmentation => "Fragmentación de roca", ItemAffixKind.ArcaneMark => "Marca arcana", ItemAffixKind.ArcaneEcho => "Eco arcano",
        ItemAffixKind.AutomaticRock => "Roca automática", ItemAffixKind.LightningStun => "Aturdimiento eléctrico", ItemAffixKind.WaterSlow => "Agua ralentizante",
        ItemAffixKind.CoinDividend => "Monedas extra", ItemAffixKind.GoldBonus => "Bonificación de oro", _ => throw new ArgumentException("Afijo desconocido.")
    };
    public static bool IsExtended(ItemAffixKind kind) => kind is ItemAffixKind.Icebreaker or ItemAffixKind.SteamImpact or
        ItemAffixKind.ArcaneExecution or ItemAffixKind.ShieldBattery or ItemAffixKind.MendingWard or ItemAffixKind.ShieldReprisal or
        ItemAffixKind.ShieldLeech or ItemAffixKind.ShieldReflection or ItemAffixKind.RockFragmentation or ItemAffixKind.ArcaneMark or ItemAffixKind.ArcaneEcho or
        ItemAffixKind.AutomaticRock or ItemAffixKind.LightningStun or ItemAffixKind.WaterSlow;
    public static int MinimumGeneration(ItemAffixKind kind) => kind is ItemAffixKind.AutomaticRock or ItemAffixKind.LightningStun or ItemAffixKind.WaterSlow ? 13
        : kind is ItemAffixKind.ArcaneMark or ItemAffixKind.ArcaneEcho ? 11
        : kind == ItemAffixKind.RockFragmentation ? 10
        : kind is ItemAffixKind.ShieldLeech or ItemAffixKind.ShieldReflection
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
        : slot.HasValue && !Allowed(affix.Kind, slot.Value) ? ""
        : affix.Kind switch
    {
        ItemAffixKind.BeamPierce => $"Los haces ofensivos atraviesan {affix.Value} enemigo{(affix.Value == 1 ? " adicional" : "s adicionales")}. Arcano sigue recto, sin perder daño ni buscar objetivos. La penetración se detiene en escudos y paredes.",
        ItemAffixKind.LightningChain => $"Los relámpagos alcanzan {affix.Value} enemigo{(affix.Value == 1 ? " adicional" : "s adicionales")}: hasta 5 m por salto, con línea de visión.",
        ItemAffixKind.DoubleBurn => "Duplica el daño por segundo de las quemaduras, sin cambiar su duración ni el impacto inicial.",
        ItemAffixKind.WaterShieldPool => "Al ponerte un escudo de agua creas un charco de 3 m durante 5 s. Moja a todos dentro, incluido tú.",
        ItemAffixKind.Icebreaker => $"Quebrantahielo: +{CombatAffixes.IcebreakerPercent(affix.Value)} % al daño de Tierra de tus hechizos contra enemigos congelados.",
        ItemAffixKind.SteamImpact => $"Choque térmico: +{CombatAffixes.SteamImpactPercent(affix.Value)} % al impacto de Fuego contra enemigos mojados; no aumenta la quemadura ni convierte el agua en daño de vida.",
        ItemAffixKind.ArcaneExecution => $"Remate arcano: +{CombatAffixes.ArcaneExecutionPercent(affix.Value)} % al daño Arcano de tus hechizos contra enemigos con un 30 % de vida o menos.",
        ItemAffixKind.ShieldBattery => $"Batería protectora: recuperas {CombatAffixes.BatteryPerHundred(affix.Value)} de energía por cada 100 HP de daño enemigo absorbido por tu escudo. Máximo 100 de energía.",
        ItemAffixKind.MendingWard => $"Remiendo protector: tus curaciones directas reparan un escudo activo propio o aliado por el {CombatAffixes.MendingPercent(affix.Value)} % de los HP curados. Respeta su máximo y duración; no crea escudos ni actúa con regeneración.",
        ItemAffixKind.ShieldReprisal => $"Última barrera: si un enemigo rompe tu escudo y sobrevives, empujas a los enemigos visibles en un radio de {CombatAffixes.ReprisalRadius} m. Fuerza {CombatAffixes.ReprisalForce(affix.Value)} · recarga {CombatAffixes.ReprisalSeconds} s. Sin daño ni interrupción de hechizos.",
        ItemAffixKind.ShieldLeech => $"Escudo vital: si sobrevives, recuperas el {CombatAffixes.ShieldLeechPercent(affix.Value)} % del daño enemigo absorbido por tu escudo personal. Excluye barreras, daño propio y fuego amigo; no repara ni prolonga el escudo.",
        ItemAffixKind.ShieldReflection => $"Espejo protector: si sobrevives, devuelve al atacante el {CombatAffixes.ShieldReflectionPercent(affix.Value)} % del daño absorbido por tu escudo personal. Conserva elementos y respeta defensas e inmunidades, sin bonos de ataque. No activa afijos de absorción ni otros reflejos.",
        ItemAffixKind.RockFragmentation => $"Fragmentación de roca: al impactar, la roca lanza {CombatAffixes.RockFragmentCount(affix.Value)} fragmentos en todas las direcciones, hasta {CombatAffixes.RockFragmentRange:0.#} m. Cada uno conserva la mezcla y sus estados, con el {CombatAffixes.RockFragmentImpactScale * 100:0} % del daño base de impacto de la roca lanzada. " +
            $"Con Fuego, Frío o Agua dejan un área de {CombatAffixes.RockFragmentGroundRadius:0.#} m durante {CombatAffixes.RockFragmentGroundSeconds:0.#} s al chocar o caer, con el {CombatAffixes.RockFragmentGroundScale * 100:0} % del daño directo habitual del suelo. Los fragmentos no vuelven a dividirse. No concede disparos automáticos.",
        ItemAffixKind.AutomaticRock => $"Roca automática: cada {CombatAffixes.AutomaticRockKillThreshold(affix.Value)} enemigos que mates con cualquier ataque preparan una roca a máxima carga, con dos de Tierra y un elemento que alterna entre Fuego, Frío, Agua y Arcano. " +
            $"Busca al enemigo visible más cercano en {CombatAffixes.AutomaticRockRange:0.#} m, ampliables con alcance. Si no hay objetivo, conserva el disparo. No interrumpe tus hechizos. Solo se divide si también equipas Fragmentación de roca. Las copias no se acumulan y perder el afijo o morir reinicia el contador.",
        ItemAffixKind.LightningStun => $"Aturdimiento eléctrico: cada segundo de impactos continuos de tus hechizos con Rayo sobre un enemigo lo aturde durante {CombatAffixes.LightningStunSeconds(affix.Value):0.0} s. Detiene su movimiento e interrumpe sus hechizos. Dejar de impactarlo reinicia el segundo; los saltos cuentan por separado. No aumenta el daño ni afecta a aliados, invulnerables o inmunes a Rayo.",
        ItemAffixKind.WaterSlow => $"Agua ralentizante: al mojar a un enemigo con tus hechizos, charcos o aura de Agua reduces su velocidad un {CombatAffixes.WaterSlowPercent(affix.Value)} % durante {CombatAffixes.WaterSlowSeconds:0.#} s. Se renueva sin acumularse y termina antes si deja de estar mojado. No añade frío ni congela; no afecta a aliados ni a ti.",
        ItemAffixKind.ArcaneMark => $"Marca arcana: causar daño arcano marca al enemigo hasta que muera. Al morir, repite sobre cada enemigo visible a {CombatAffixes.ArcaneMarkRadius(affix.Value):0.#} m el daño real que recibió en los últimos {CombatAffixes.ArcaneMarkSeconds(affix.Value)} s, incluido el golpe mortal y el daño a escudos, sin exceso. Cuenta los ataques de cualquier origen. " +
            "La explosión es arcana y respeta las defensas del nuevo objetivo, sin volver a multiplicar los bonos del lanzador. No crea marcas nuevas. Solo una marca por objetivo; prevalece el rango mayor y, a igual rango, el último autor.",
        ItemAffixKind.ArcaneEcho => $"Eco arcano: mientras canalizas un haz con Arcano contra un enemigo, lanzas desde tu mago otro haz automático al enemigo más cercano distinto del que apuntas, con trayectoria libre y dentro del alcance original. Conserva mezcla y carga, con el {CombatAffixes.ArcaneEchoPercent(affix.Value)} % del daño. No atraviesa enemigos ni genera más ecos; puede reflejarse en escudos.",
        ItemAffixKind.CoinDividend => $"1 oro extra cada {GoldBonuses.EveryForLevel(level)} moneda{(GoldBonuses.EveryForLevel(level) == 1 ? "" : "s")} base · nivel {level}/10. " +
            (level < 10 ? "Cada mejora reduce el intervalo en 1, hasta una por moneda. " : "") +
            "Supervivencia: recogidas. PvP: premio final. No afecta a ventas.",
        ItemAffixKind.GoldBonus => $"+{affix.Value}% de oro base: recogidas en Supervivencia y premio final en PvP. No afecta a ventas.",
        _ => ""
    };
    internal readonly record struct ActiveEffect(string Name, string Description, bool Arcane);

    public static string Summary(EquipmentModifiers modifiers) => string.Join("\n", Effects(modifiers).Select(effect => effect.Description));

    internal static ActiveEffect[] Effects(EquipmentModifiers modifiers)
    {
        var effects = new List<ActiveEffect>();
        void Add(ItemAffixKind kind, int value, int level = 0)
        {
            if (value <= 0) return;
            effects.Add(new(Name(kind), Describe(new(kind, value), level),
                kind is ItemAffixKind.BeamPierce or ItemAffixKind.ArcaneExecution or ItemAffixKind.ArcaneMark or ItemAffixKind.ArcaneEcho));
        }
        Add(ItemAffixKind.BeamPierce, modifiers.BeamPierce);
        Add(ItemAffixKind.LightningChain, modifiers.LightningChains);
        Add(ItemAffixKind.DoubleBurn, modifiers.BurnPotency > 1 ? 1 : 0);
        Add(ItemAffixKind.WaterShieldPool, modifiers.WaterShieldPool ? 1 : 0);
        Add(ItemAffixKind.Icebreaker, modifiers.Icebreaker);
        Add(ItemAffixKind.SteamImpact, modifiers.SteamImpact);
        Add(ItemAffixKind.ArcaneExecution, modifiers.ArcaneExecution);
        Add(ItemAffixKind.ShieldBattery, modifiers.ShieldBattery);
        Add(ItemAffixKind.MendingWard, modifiers.MendingWard);
        Add(ItemAffixKind.ShieldReprisal, modifiers.ShieldReprisal);
        Add(ItemAffixKind.ShieldLeech, modifiers.ShieldLeech);
        Add(ItemAffixKind.ShieldReflection, modifiers.ShieldReflection);
        Add(ItemAffixKind.RockFragmentation, modifiers.RockFragmentation);
        Add(ItemAffixKind.AutomaticRock, modifiers.AutomaticRock);
        Add(ItemAffixKind.LightningStun, modifiers.LightningStun);
        Add(ItemAffixKind.WaterSlow, modifiers.WaterSlow);
        Add(ItemAffixKind.ArcaneMark, modifiers.ArcaneMark);
        Add(ItemAffixKind.ArcaneEcho, modifiers.ArcaneEcho);
        if (modifiers.MagicRingFamily is { } family)
        {
            string description = RingEffects.Describe(EquipmentSlot.MagicRing, family, modifiers.MagicRingLevel, modifiers);
            if (description.Length > 0)
                effects.Add(new("Anillo mágico · " + EquipmentCatalogue.FamilyName(family), description, family == EquipmentFamily.Arcane));
        }
        Add(ItemAffixKind.CoinDividend, modifiers.ExtraGoldEvery > 0 ? 1 : 0, 11 - modifiers.ExtraGoldEvery);
        Add(ItemAffixKind.GoldBonus, modifiers.GoldBonusPercent);
        return effects.ToArray();
    }
}

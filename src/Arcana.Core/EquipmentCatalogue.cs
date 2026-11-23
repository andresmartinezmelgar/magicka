using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Arcana.Core;

public static partial class EquipmentCatalogue
{
    public const int GenerationVersion = 13, InventoryLimit = 500;
    private const int StandardSecondaryWeight = 8;
    public static bool BonusActive(EquipmentSlot slot, EquipmentStat stat) =>
        slot != EquipmentSlot.MagicRing || stat == EquipmentStat.ShieldCapacity;
    public static string BonusDescription(EquipmentSlot slot, ItemBonus bonus) =>
        StatName(bonus.Stat) + " " + Percent(bonus.Current) +
        (BonusActive(slot, bonus.Stat) ? "" : " · atributo antiguo inactivo");
    private static readonly int[] Sales = [10, 25, 60, 150, 400];
    private static readonly float[] MainMin = [0.01f, 0.02f, 0.03f, 0.06f, 0.08f], MainMax = [0.05f, 0.07f, 0.09f, 0.11f, 0.13f];
    private static readonly float[] ExtraMin = [-0.04f, -0.03f, -0.02f, 0.04f, 0.07f], ExtraMax = [0.06f, 0.08f, 0.10f, 0.10f, 0.14f];
    private static readonly float[] RollBudget = [0.10f, 0.19f, 0.25f, 0.30f, 0.46f];
    public static string SlotName(EquipmentSlot slot) => slot switch { EquipmentSlot.Boots => "Botas", EquipmentSlot.Hat => "Gorro", EquipmentSlot.Chest => "Pecho", EquipmentSlot.Weapon => "Arma",
        EquipmentSlot.GoldRing => "Anillo de oro", EquipmentSlot.MagicRing => "Anillo mágico", _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    public static string TierName(ItemTier tier) => tier switch { ItemTier.Silver => "Plata", ItemTier.Gold => "Oro", ItemTier.Platinum => "Platino", ItemTier.Diamond => "Diamante", _ => "Mítico" };
    public static string FamilyName(EquipmentFamily family) => family switch
    { EquipmentFamily.Life => "Vida", EquipmentFamily.Shield => "Escudo", EquipmentFamily.Fire => "Fuego", EquipmentFamily.Water => "Agua",
        EquipmentFamily.Cold => "Frío", EquipmentFamily.Lightning => "Rayo", EquipmentFamily.Arcane => "Arcano", EquipmentFamily.Earth => "Tierra", _ => "Mixto" };
    public static string StatName(EquipmentStat stat) => stat switch
    {
        EquipmentStat.MovementSpeed => "Velocidad de movimiento", EquipmentStat.SpellRange => "Alcance de hechizos", EquipmentStat.Defense => "Defensa",
        EquipmentStat.Power => "Potencia de ataque", EquipmentStat.HealingPower => "Potencia de curación", EquipmentStat.FirePower => "Daño de fuego",
        EquipmentStat.ShieldCapacity => "Absorción del escudo", EquipmentStat.ShieldDuration => "Duración del escudo",
        EquipmentStat.EnergyRegeneration => "Regeneración de energía", EquipmentStat.LifeEfficiency => "Ahorro de energía al reanimar",
        EquipmentStat.BurnDuration => "Duración de las quemaduras", EquipmentStat.WaterPower => "Potencia de agua", EquipmentStat.ColdPower => "Daño de frío",
        EquipmentStat.LightningPower => "Daño de rayo", EquipmentStat.ArcanePower => "Daño arcano", EquipmentStat.EarthPower => "Daño de tierra",
        EquipmentStat.WetDuration => "Duración del estado Mojado", EquipmentStat.ChillPower => "Potencia de enfriamiento", EquipmentStat.FreezeDuration => "Duración de la congelación",
        EquipmentStat.WetLightningPower => "Daño de rayo contra objetivos mojados", EquipmentStat.BeamChargeSpeed => "Velocidad de carga del haz arcano", EquipmentStat.RockChargeSpeed => "Velocidad de carga de rocas",
        _ => throw new ArgumentOutOfRangeException(nameof(stat))
    };
    public static string Percent(float value) => MathF.Round(value * 100, 2).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%";
    public static string ItemName(EquipmentItem item) => SlotName(item.Slot) + " · " + TierName(item.Tier) + " · " + FamilyName(item.Family);
}

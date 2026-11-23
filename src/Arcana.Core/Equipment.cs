using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Arcana.Core;

public enum EquipmentSlot { Boots, Hat, Chest, Weapon, GoldRing, MagicRing }
public enum ItemTier { Silver, Gold, Platinum, Diamond, Mythic }
// Se añaden valores al final: las instancias existentes conservan sus identificadores.
public enum EquipmentFamily { Mixed = 0, Life = 1, Shield = 2, Fire = 3, Water = 4, Cold = 5, Lightning = 6, Arcane = 7, Earth = 8 }
public enum EquipmentStat { MovementSpeed, SpellRange, Defense, Power, HealingPower, FirePower, ShieldCapacity, ShieldDuration, EnergyRegeneration, LifeEfficiency, BurnDuration,
    WaterPower, ColdPower, LightningPower, ArcanePower, EarthPower, WetDuration, ChillPower, FreezeDuration, WetLightningPower, BeamChargeSpeed, RockChargeSpeed }

// Los intervalos pertenecen a la instancia: una nueva versión del catálogo no vuelve a sortear un objeto.
public sealed record ItemBonus(EquipmentStat Stat, float Minimum, float Maximum, float Current);
public sealed record EquipmentItem(string Id, EquipmentSlot Slot, ItemTier Tier, EquipmentFamily Family,
    ItemBonus[] Bonuses, int SaleGold, int GenerationVersion = 1, string SourceMatchId = "", DateTimeOffset CreatedAt = default)
{
    public ItemAffix? Affix { get; init; }
    public int UpgradeLevel { get; init; }
}
public sealed record EquipmentLoadout(string Boots = "", string Hat = "", string Chest = "", string Weapon = "", string GoldRing = "", string MagicRing = "")
{
    public string Get(EquipmentSlot slot) => slot switch { EquipmentSlot.Boots => Boots, EquipmentSlot.Hat => Hat, EquipmentSlot.Chest => Chest, EquipmentSlot.Weapon => Weapon, EquipmentSlot.GoldRing => GoldRing, EquipmentSlot.MagicRing => MagicRing, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    public EquipmentLoadout With(EquipmentSlot slot, string id) => slot switch
    { EquipmentSlot.Boots => this with { Boots = id }, EquipmentSlot.Hat => this with { Hat = id }, EquipmentSlot.Chest => this with { Chest = id }, EquipmentSlot.Weapon => this with { Weapon = id },
        EquipmentSlot.GoldRing => this with { GoldRing = id }, EquipmentSlot.MagicRing => this with { MagicRing = id }, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
}
public sealed record AccountProfile(string Uid, string Name, long Gold, int Wins, int Losses, EquipmentLoadout Loadout,
    EquipmentItem[] Items, int SchemaVersion = 1)
{
    public Dictionary<string, long> Bestiary { get; init; } = new(StringComparer.Ordinal);
    public AccountAccess Access { get; init; } = new();
}
public sealed record InventoryRequest(string ItemId, EquipmentSlot Slot = EquipmentSlot.Boots);
public sealed record InventoryResult(AccountProfile Profile, string Message);
public readonly record struct EquipmentOutfit(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EquipmentFamily? Boots = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EquipmentFamily? Hat = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EquipmentFamily? Chest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EquipmentFamily? Weapon = null)
{
    public EquipmentOutfit With(EquipmentSlot slot, EquipmentFamily? family) => slot switch
    {
        EquipmentSlot.Boots => this with { Boots = family },
        EquipmentSlot.Hat => this with { Hat = family },
        EquipmentSlot.Chest => this with { Chest = family },
        EquipmentSlot.Weapon => this with { Weapon = family },
        _ => this
    };
}
public sealed record EquipmentAppearance(ItemTier? Boots = null, ItemTier? Hat = null, ItemTier? Chest = null, ItemTier? Weapon = null,
    EquipmentFamily Family = EquipmentFamily.Mixed)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ItemTier? GoldRing { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ItemTier? MagicRing { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public EquipmentOutfit MythicOutfit { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ulong MythicFamilies { get; init; }
}
public sealed record EquipmentStatTotal(EquipmentStat Stat, float Items, float Set, float Effective)
{
    public float Sum => Items + Set;
    public bool Limited => Math.Abs(Sum - Effective) > 0.00001f;
}
public sealed record EquipmentSummary(EquipmentModifiers Modifiers, EquipmentStatTotal[] Stats);

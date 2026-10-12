using System.Globalization;
using System.Security.Cryptography;

namespace Arcana.Core;

public enum EquipmentSlot { Boots, Hat, Chest, Weapon }
public enum ItemTier { Silver, Gold, Platinum, Diamond, Mythic }
public enum EquipmentFamily { Mixed, Life, Shield, Fire }
public enum EquipmentStat { MovementSpeed, SpellRange, Defense, Power, HealingPower, FirePower, ShieldCapacity, ShieldDuration, EnergyRegeneration, LifeEfficiency, BurnDuration }

// Los intervalos pertenecen a la instancia: una nueva versión del catálogo no vuelve a sortear un objeto.
public sealed record ItemBonus(EquipmentStat Stat, float Minimum, float Maximum, float Current);
public sealed record EquipmentItem(string Id, EquipmentSlot Slot, ItemTier Tier, EquipmentFamily Family,
    ItemBonus[] Bonuses, int SaleGold, int GenerationVersion = 1, string SourceMatchId = "", DateTimeOffset CreatedAt = default);
public sealed record EquipmentLoadout(string Boots = "", string Hat = "", string Chest = "", string Weapon = "")
{
    public string Get(EquipmentSlot slot) => slot switch { EquipmentSlot.Boots => Boots, EquipmentSlot.Hat => Hat, EquipmentSlot.Chest => Chest, _ => Weapon };
    public EquipmentLoadout With(EquipmentSlot slot, string id) => slot switch
    { EquipmentSlot.Boots => this with { Boots = id }, EquipmentSlot.Hat => this with { Hat = id }, EquipmentSlot.Chest => this with { Chest = id }, _ => this with { Weapon = id } };
}
public sealed record AccountProfile(string Uid, string Name, long Gold, int Wins, int Losses, EquipmentLoadout Loadout,
    EquipmentItem[] Items, int SchemaVersion = 1);
public sealed record InventoryRequest(string ItemId, EquipmentSlot Slot = EquipmentSlot.Boots);
public sealed record InventoryResult(AccountProfile Profile, string Message);
public sealed record EquipmentAppearance(ItemTier? Boots = null, ItemTier? Hat = null, ItemTier? Chest = null, ItemTier? Weapon = null,
    EquipmentFamily Family = EquipmentFamily.Mixed);

/// <summary>Calculado una vez al equipar. La simulación no recorre el inventario ni consulta servicios.</summary>
public sealed record EquipmentModifiers
{
    public static EquipmentModifiers None { get; } = new();
    public float Speed { get; init; } = 1;
    public float Range { get; init; } = 1;
    public float Incoming { get; init; } = 1;
    public float Power { get; init; } = 1;
    public float Healing { get; init; } = 1;
    public float Fire { get; init; } = 1;
    public float ShieldCapacity { get; init; } = 1;
    public float ShieldDuration { get; init; } = 1;
    public float EnergyRegeneration { get; init; } = 1;
    public float LifeCost { get; init; } = 1;
    public float BurnDuration { get; init; } = 1;
    public int LifePieces { get; init; }
    public int ShieldPieces { get; init; }
    public int FirePieces { get; init; }
    public EquipmentAppearance Appearance { get; init; } = new();
    public static EquipmentModifiers Calculate(AccountProfile profile)
    {
        if (profile.Items.Length > EquipmentCatalogue.InventoryLimit) throw new InvalidDataException("Inventario demasiado grande.");
        var stats = new float[Enum.GetValues<EquipmentStat>().Length];
        var sets = new int[4];
        var tiers = new ItemTier?[4];
        var families = new int[4];
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            string id = profile.Loadout.Get(slot);
            if (id.Length == 0) continue;
            var item = profile.Items.SingleOrDefault(i => i.Id == id);
            if (item == null || item.Slot != slot || !EquipmentCatalogue.Valid(item)) throw new InvalidDataException("Equipamiento inválido o ajeno.");
            tiers[(int)slot] = item.Tier; families[(int)item.Family]++;
            foreach (var b in item.Bonuses) stats[(int)b.Stat] += b.Current;
            if (item.Tier == ItemTier.Mythic) sets[(int)item.Family]++;
        }
        if (sets[(int)EquipmentFamily.Life] >= 2) stats[(int)EquipmentStat.HealingPower] += sets[1] == 4 ? 0.15f : 0.05f;
        if (sets[1] == 4) stats[(int)EquipmentStat.LifeEfficiency] += 0.10f;
        if (sets[2] >= 2)
        {
            stats[(int)EquipmentStat.ShieldDuration] += sets[2] == 4 ? 0.20f : 0.10f;
            if (sets[2] == 4) stats[(int)EquipmentStat.ShieldCapacity] += 0.20f;
        }
        if (sets[3] >= 2) stats[(int)EquipmentStat.FirePower] += sets[3] == 4 ? 0.15f : 0.05f;
        if (sets[3] == 4) stats[(int)EquipmentStat.BurnDuration] += 0.20f;
        float Bonus(EquipmentStat s, float upper = 0.60f) => Math.Clamp(stats[(int)s], -0.25f, upper);
        var dominant = Enumerable.Range(1, 3).OrderByDescending(i => families[i]).ThenBy(i => i).First();
        return new()
        {
            Speed = 1 + Bonus(EquipmentStat.MovementSpeed, 0.30f), Range = 1 + Bonus(EquipmentStat.SpellRange, 0.35f),
            Incoming = 1 - Bonus(EquipmentStat.Defense, 0.35f), Power = 1 + Bonus(EquipmentStat.Power, 0.40f),
            Healing = 1 + Bonus(EquipmentStat.HealingPower), Fire = 1 + Bonus(EquipmentStat.FirePower, 0.50f),
            ShieldCapacity = 1 + Bonus(EquipmentStat.ShieldCapacity), ShieldDuration = 1 + Bonus(EquipmentStat.ShieldDuration),
            EnergyRegeneration = 1 + Bonus(EquipmentStat.EnergyRegeneration), LifeCost = 1 - Bonus(EquipmentStat.LifeEfficiency, 0.35f),
            BurnDuration = 1 + Bonus(EquipmentStat.BurnDuration), LifePieces = sets[1], ShieldPieces = sets[2], FirePieces = sets[3],
            Appearance = new(tiers[0], tiers[1], tiers[2], tiers[3], families[dominant] > 0 ? (EquipmentFamily)dominant : EquipmentFamily.Mixed)
        };
    }
}

public static class EquipmentCatalogue
{
    public const int GenerationVersion = 1, InventoryLimit = 500;
    private static readonly int[] Sales = [10, 25, 60, 150, 400];
    private static readonly float[] MainMin = [0.01f, 0.02f, 0.03f, 0.06f, 0.08f], MainMax = [0.05f, 0.07f, 0.09f, 0.11f, 0.13f];
    private static readonly float[] ExtraMin = [-0.04f, -0.03f, -0.02f, 0.04f, 0.07f], ExtraMax = [0.06f, 0.08f, 0.10f, 0.10f, 0.14f];
    private static readonly float[] RollBudget = [0.10f, 0.19f, 0.25f, 0.30f, 0.46f];
    public static string SlotName(EquipmentSlot slot) => slot switch { EquipmentSlot.Boots => "Botas", EquipmentSlot.Hat => "Gorro", EquipmentSlot.Chest => "Pecho", _ => "Arma" };
    public static string TierName(ItemTier tier) => tier switch { ItemTier.Silver => "Plata", ItemTier.Gold => "Oro", ItemTier.Platinum => "Platino", ItemTier.Diamond => "Diamante", _ => "Mítico" };
    public static string FamilyName(EquipmentFamily family) => family switch { EquipmentFamily.Life => "Vida", EquipmentFamily.Shield => "Escudo", EquipmentFamily.Fire => "Fuego", _ => "Mixto" };
    public static string StatName(EquipmentStat stat) => stat switch
    {
        EquipmentStat.MovementSpeed => "Velocidad", EquipmentStat.SpellRange => "Alcance", EquipmentStat.Defense => "Defensa",
        EquipmentStat.Power => "Potencia de ataque", EquipmentStat.HealingPower => "Curación", EquipmentStat.FirePower => "Daño de fuego",
        EquipmentStat.ShieldCapacity => "Absorción del escudo", EquipmentStat.ShieldDuration => "Duración del escudo",
        EquipmentStat.EnergyRegeneration => "Regeneración de energía", EquipmentStat.LifeEfficiency => "Ahorro de energía al reanimar", _ => "Duración de quemaduras"
    };
    public static string Percent(float value) => value.ToString("+0.#%;-0.#%;0%", CultureInfo.InvariantCulture);
    public static string ItemName(EquipmentItem item) => SlotName(item.Slot) + " · " + TierName(item.Tier) + " · " + FamilyName(item.Family);
    public static EquipmentStat MainStat(EquipmentSlot slot, EquipmentFamily family) => slot switch
    {
        EquipmentSlot.Boots => EquipmentStat.MovementSpeed, EquipmentSlot.Hat => EquipmentStat.SpellRange, EquipmentSlot.Chest => EquipmentStat.Defense,
        _ => family == EquipmentFamily.Life ? EquipmentStat.HealingPower : family == EquipmentFamily.Shield ? EquipmentStat.ShieldCapacity
            : family == EquipmentFamily.Fire ? EquipmentStat.FirePower : EquipmentStat.Power
    };
    public static EquipmentStat[] Pool(EquipmentFamily family) => family switch
    {
        EquipmentFamily.Life => [EquipmentStat.HealingPower, EquipmentStat.EnergyRegeneration, EquipmentStat.LifeEfficiency, EquipmentStat.ShieldDuration],
        EquipmentFamily.Shield => [EquipmentStat.ShieldCapacity, EquipmentStat.ShieldDuration, EquipmentStat.Defense, EquipmentStat.EnergyRegeneration],
        EquipmentFamily.Fire => [EquipmentStat.FirePower, EquipmentStat.BurnDuration, EquipmentStat.Power, EquipmentStat.EnergyRegeneration],
        _ => Enum.GetValues<EquipmentStat>()
    };
    public static IEnumerable<(string Id, object Data)> Definitions()
    {
        foreach (var stat in Enum.GetValues<EquipmentStat>())
            yield return (Protocol.Json.PropertyNamingPolicy!.ConvertName(stat.ToString()), new
            {
                stat, generationVersion = GenerationVersion, weight = 1,
                primarySlots = Enum.GetValues<EquipmentSlot>().Where(slot => Enum.GetValues<EquipmentFamily>().Any(family => MainStat(slot, family) == stat)).ToArray(),
                families = Enum.GetValues<EquipmentFamily>().Where(family => Pool(family).Contains(stat)).ToArray(),
                tiers = Enum.GetValues<ItemTier>().Select(tier => new { tier, primaryMinimum = MainMin[(int)tier], primaryMaximum = MainMax[(int)tier],
                    minimum = ExtraMin[(int)tier], maximum = ExtraMax[(int)tier], initialBudget = RollBudget[(int)tier] }).ToArray()
            });
    }
    public static EquipmentItem Generate(Random random, string sourceMatchId = "", ItemTier? tier = null, EquipmentSlot? slot = null,
        EquipmentFamily? family = null, string? id = null)
    {
        int roll = random.Next(100);
        var quality = tier ?? (roll < 48 ? ItemTier.Silver : roll < 75 ? ItemTier.Gold : roll < 90 ? ItemTier.Platinum : roll < 98 ? ItemTier.Diamond : ItemTier.Mythic);
        var position = slot ?? (EquipmentSlot)random.Next(4);
        var identity = quality >= ItemTier.Diamond ? family is > EquipmentFamily.Mixed and <= EquipmentFamily.Fire ? family.Value : (EquipmentFamily)random.Next(1, 4) : EquipmentFamily.Mixed;
        if (!Enum.IsDefined(quality) || !Enum.IsDefined(position)) throw new ArgumentException("Tipo de objeto inválido.");
        var primary = MainStat(position, identity);
        float Value(float min, float max) => MathF.Round(min + (float)random.NextDouble() * (max - min), 4);
        int t = (int)quality;
        var bonuses = new List<ItemBonus> { new(primary, MainMin[t], MainMax[t], Value(MainMin[t], MainMax[t])) };
        var choices = Pool(identity).Where(s => s != primary).ToList();
        int extra = quality == ItemTier.Silver ? 1 : quality == ItemTier.Mythic ? 3 : 2;
        for (int n = 0; n < extra && choices.Count > 0; n++)
        {
            int pick = random.Next(choices.Count); var stat = choices[pick]; choices.RemoveAt(pick);
            bonuses.Add(new(stat, ExtraMin[t], ExtraMax[t], Value(ExtraMin[t], ExtraMax[t])));
        }
        float excess = bonuses.Sum(b => Math.Max(0, b.Current)) - RollBudget[t];
        for (int n = 1; n < bonuses.Count && excess > 0.0001f; n++)
        {
            var b = bonuses[n]; float decrease = Math.Min(excess, Math.Max(0, b.Current - Math.Max(0, b.Minimum)));
            bonuses[n] = b with { Current = MathF.Round(b.Current - decrease, 4) }; excess -= decrease;
        }
        return new(id ?? Guid.NewGuid().ToString("N"), position, quality, identity, bonuses.ToArray(), Sales[t], GenerationVersion, sourceMatchId, DateTimeOffset.UtcNow);
    }
    public static bool Valid(EquipmentItem item) => item != null && Guid.TryParseExact(item.Id, "N", out _) &&
        Enum.IsDefined(item.Slot) && Enum.IsDefined(item.Tier) && Enum.IsDefined(item.Family) && item.GenerationVersion > 0 &&
        item.SaleGold is > 0 and <= 100000 && item.Bonuses is { Length: >= 1 and <= 4 } &&
        item.Bonuses.Select(b => b.Stat).Distinct().Count() == item.Bonuses.Length && item.Bonuses.Any(b => b.Current > 0) &&
        item.Bonuses.All(b => Enum.IsDefined(b.Stat) && float.IsFinite(b.Minimum) && float.IsFinite(b.Maximum) && float.IsFinite(b.Current) &&
            b.Minimum >= -0.50f && b.Maximum <= 0.50f && b.Minimum <= b.Maximum && b.Current >= b.Minimum && b.Current <= b.Maximum);
}

public sealed class LootRandom : Random
{
    public override int Next(int maxValue) => RandomNumberGenerator.GetInt32(maxValue);
    public override int Next(int minValue, int maxValue) => RandomNumberGenerator.GetInt32(minValue, maxValue);
    public override double NextDouble()
    {
        Span<byte> bytes = stackalloc byte[8]; RandomNumberGenerator.Fill(bytes);
        return (BitConverter.ToUInt64(bytes) >> 11) / 9007199254740992d;
    }
}

namespace Arcana.Core;

public sealed record NamedEquipment(string Name, EquipmentLoadout Loadout);

/// <summary>Preferencias de interfaz por cuenta; nunca modifica el oro ni los objetos autoritativos.</summary>
public sealed class InventoryPreferences
{
    public HashSet<string> Favorites { get; set; } = new(StringComparer.Ordinal);
    public List<NamedEquipment> Loadouts { get; set; } = [];
    public List<AutoSellRule> AutoSellRules { get; set; } = [];
    public int AutoSellRuleVersion { get; set; }
    public const int CurrentAutoSellRuleVersion = 2;
    public SurvivalEquipmentSetup SurvivalEquipment { get; set; } = new();

    public void Normalize(AccountProfile? profile = null)
    {
        Favorites ??= new(StringComparer.Ordinal); Loadouts ??= []; AutoSellRules ??= [];
        if (SurvivalEquipment?.Valid != true) SurvivalEquipment = new();
        // Las reglas v0 vendían la calidad exacta; no ampliar esas reglas al migrar.
        if (AutoSellRuleVersion is not (1 or CurrentAutoSellRuleVersion))
        { AutoSellRules.Clear(); AutoSellRuleVersion = CurrentAutoSellRuleVersion; }
        Favorites.RemoveWhere(id => id is not { Length: 32 });
        AutoSellRules = AutoSellRules.Where(r => r != null && r.Style.Valid && r.Style.Tier > ItemTier.Silver && r.ExistingIds != null &&
            r.ExistingIds.Count <= EquipmentCatalogue.InventoryLimit && r.ExistingIds.All(id => id is { Length: 32 }))
            .DistinctBy(r => (r.Style.Slot, r.Style.Family)).Take(EquipmentStyle.Limit).ToList();
        if (AutoSellRuleVersion == 1 && profile == null) return;
        bool migrating = AutoSellRuleVersion == 1;
        AutoSellRules = EquipmentStyle.SlotLimits(AutoSellRules.Select(r => r.Style)).Select(style =>
        {
            var existing = AutoSellRules.Where(r => r.Style.SameSlot(style)).SelectMany(r => r.ExistingIds).ToHashSet(StringComparer.Ordinal);
            // La ampliación a otros elementos solo afecta al botín futuro, no al inventario anterior.
            if (migrating) existing.UnionWith(profile!.Items.Where(style.MatchesLowerQuality).Select(i => i.Id));
            if (profile != null) existing.IntersectWith(profile.Items.Select(i => i.Id));
            return new AutoSellRule(style, existing);
        }).ToList();
        AutoSellRuleVersion = CurrentAutoSellRuleVersion;
    }

    public bool AutoSells(EquipmentStyle style) => AutoSellRuleVersion == CurrentAutoSellRuleVersion &&
        AutoSellRules.Any(r => r.Style.SameSlot(style) && r.Style.Tier == style.Tier);
    public void SetAutoSell(EquipmentStyle style, bool enabled, AccountProfile profile)
    {
        if (!style.Valid) throw new ArgumentException("Estilo de objeto inválido.");
        Normalize(profile);
        if (!enabled) { AutoSellRules.RemoveAll(r => r.Style.SameSlot(style)); return; }
        if (style.Tier == ItemTier.Silver) throw new ArgumentException("No hay calidades inferiores a Plata.");
        if (AutoSells(style)) return;
        AutoSellRules.RemoveAll(r => r.Style.SameSlot(style));
        AutoSellRules.Add(new(style,
            profile.Items.Where(style.MatchesLowerQuality).Select(i => i.Id).ToHashSet(StringComparer.Ordinal)));
    }
    public EquipmentItem[] AutomaticSales(AccountProfile profile)
    {
        Normalize(profile);
        return profile.Items.Where(i => i.Tier != ItemTier.Mythic && !Protected(i.Id) && profile.Loadout.Get(i.Slot) != i.Id &&
            AutoSellRules.Any(r => r.Style.MatchesLowerQuality(i) && !r.ExistingIds.Contains(i.Id))).ToArray();
    }

    public bool Protected(string id) => Favorites.Contains(id);
    public void Save(string name, EquipmentLoadout loadout, AccountProfile profile)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 24 || name.Any(char.IsControl)) throw new ArgumentException("Usa un nombre de 1 a 24 caracteres.");
        AccountInventory.ApplyLoadout(profile, loadout);
        int index = Loadouts.FindIndex(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && Loadouts.Count >= 8) throw new InvalidOperationException("Puedes guardar hasta 8 equipos en este dispositivo.");
        if (index < 0) Loadouts.Add(new(name, loadout)); else Loadouts[index] = new(name, loadout);
    }
    public static int Missing(NamedEquipment saved, AccountProfile profile) => Enum.GetValues<EquipmentSlot>().Count(s =>
        saved.Loadout.Get(s) is { Length: > 0 } id && !profile.Items.Any(i => i.Id == id && i.Slot == s));
    public EquipmentItem[] Sellable(AccountProfile profile, IEnumerable<string> selection)
    {
        var ids = selection.ToHashSet(StringComparer.Ordinal);
        return profile.Items.Where(i => ids.Contains(i.Id) && !Protected(i.Id) && profile.Loadout.Get(i.Slot) != i.Id).ToArray();
    }
}

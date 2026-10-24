namespace Arcana.Core;

public sealed record NamedEquipment(string Name, EquipmentLoadout Loadout);

/// <summary>Preferencias de interfaz por cuenta; nunca modifica el oro ni los objetos autoritativos.</summary>
public sealed class InventoryPreferences
{
    public HashSet<string> Favorites { get; set; } = new(StringComparer.Ordinal);
    public List<NamedEquipment> Loadouts { get; set; } = [];
    public List<AutoSellRule> AutoSellRules { get; set; } = [];

    public void Normalize()
    {
        Favorites ??= new(StringComparer.Ordinal); Loadouts ??= []; AutoSellRules ??= [];
        Favorites.RemoveWhere(id => id is not { Length: 32 });
        AutoSellRules = AutoSellRules.Where(r => r != null && r.Style.Valid && r.ExistingIds != null &&
            r.ExistingIds.Count <= EquipmentCatalogue.InventoryLimit && r.ExistingIds.All(id => id is { Length: 32 }))
            .DistinctBy(r => r.Style).Take(EquipmentStyle.Limit).ToList();
    }

    public bool AutoSells(EquipmentStyle style) => AutoSellRules.Any(r => r.Style == style);
    public void SetAutoSell(EquipmentStyle style, bool enabled, AccountProfile profile)
    {
        if (!style.Valid) throw new ArgumentException("Estilo de objeto inválido.");
        if (!enabled) { AutoSellRules.RemoveAll(r => r.Style == style); return; }
        if (!AutoSells(style)) AutoSellRules.Add(new(style,
            profile.Items.Where(style.Matches).Select(i => i.Id).ToHashSet(StringComparer.Ordinal)));
    }
    public EquipmentItem[] AutomaticSales(AccountProfile profile) => profile.Items.Where(i =>
        !Protected(i.Id) && profile.Loadout.Get(i.Slot) != i.Id &&
        AutoSellRules.Any(r => r.Style.Matches(i) && !r.ExistingIds.Contains(i.Id))).ToArray();

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

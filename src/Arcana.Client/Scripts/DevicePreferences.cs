using Arcana.Core;
using Godot;
using System.Text.Json;

namespace Arcana.Client;

internal static class DevicePreferences
{
    private static readonly Dictionary<string, InventoryPreferences> Temporary = new(StringComparer.Ordinal);
    private static Dictionary<string, InventoryPreferences>? _accounts;
    internal static InventoryPreferences Inventory(string uid, bool temporary, AccountProfile? profile = null)
    {
        if (!temporary && _accounts == null)
        {
            _accounts = Read<Dictionary<string, InventoryPreferences>>("inventory-preferences") ?? new(StringComparer.Ordinal);
            foreach (string key in _accounts.Keys.ToArray())
            { if (_accounts[key] == null) _accounts[key] = new(); _accounts[key].Normalize(); }
        }
        var accounts = temporary ? Temporary : _accounts!;
        if (!accounts.TryGetValue(uid, out var preferences)) accounts[uid] = preferences = new();
        if (profile != null && profile.Uid == uid && preferences.AutoSellRuleVersion == 1)
        {
            int previous = preferences.AutoSellRuleVersion;
            preferences.Normalize(profile);
            if (!temporary && previous != preferences.AutoSellRuleVersion) SaveInventory();
        }
        return preferences;
    }
    internal static bool SaveInventory() => Write("inventory-preferences", _accounts ?? new());
    internal static T? Read<T>(string name)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(ProjectSettings.GlobalizePath("user://" + name + ".json"))); }
        catch (Exception) { return default; }
    }
    internal static bool Write<T>(string name, T value)
    {
        try
        {
            string path = ProjectSettings.GlobalizePath("user://" + name + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
            File.Move(path + ".tmp", path, true); return true;
        }
        catch (Exception error) { Diagnostics.Warn("preferences.save", "No se pudieron guardar las preferencias del dispositivo.", error); return false; }
    }
}

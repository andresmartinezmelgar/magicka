using Arcana.Core;
using Godot;
using System.Text.Json;

namespace Arcana.Client;

internal static class DevicePreferences
{
    private static readonly Dictionary<string, InventoryPreferences> Temporary = new(StringComparer.Ordinal);
    private static Dictionary<string, InventoryPreferences>? _accounts;
    internal static InventoryPreferences Inventory(string uid, bool temporary)
    {
        var accounts = temporary ? Temporary : _accounts ??= Read<Dictionary<string, InventoryPreferences>>("inventory-preferences") ?? new(StringComparer.Ordinal);
        if (!accounts.TryGetValue(uid, out var preferences)) accounts[uid] = preferences = new();
        preferences.Favorites ??= new(StringComparer.Ordinal);
        preferences.Loadouts ??= [];
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

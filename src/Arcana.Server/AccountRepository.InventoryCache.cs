using System.Collections.Concurrent;
using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    // La revisión es el UpdateTime de Firestore, nunca el saldo: evita el caso
    // comprar/vender/recibir oro y volver al mismo saldo con objetos distintos.
    // También invalida cambios del servidor anterior durante el despliegue.
    private sealed record CachedInventory(Timestamp Revision, EquipmentItem[] Items, long Until);
    private readonly ConcurrentDictionary<string, CachedInventory> _inventoryCache = new(StringComparer.Ordinal);
    private bool CachedItems(string uid, Timestamp revision, out EquipmentItem[] items)
    {
        if (_inventoryCache.TryGetValue(uid, out var cached) && cached.Until > Environment.TickCount64 && cached.Revision == revision)
        { items = cached.Items.ToArray(); return true; }
        items = []; return false;
    }
    private void CacheItems(string uid, Timestamp revision, EquipmentItem[] items)
    {
        if (_inventoryCache.Count >= 128) _inventoryCache.Clear();
        _inventoryCache[uid] = new(revision, items.ToArray(), Environment.TickCount64 + 10000);
    }
    private void CacheSale(string uid, Timestamp before, Timestamp after, EquipmentItem sold)
    {
        if (!_inventoryCache.TryGetValue(uid, out var cached) || cached.Revision != before) return;
        _inventoryCache.TryUpdate(uid, new(after, cached.Items.Where(i => i.Id != sold.Id).ToArray(), cached.Until), cached);
    }
}

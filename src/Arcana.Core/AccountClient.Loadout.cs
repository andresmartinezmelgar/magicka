using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Arcana.Core;

public sealed partial class AccountClient
{
    public void RestoreOfflineProfile(AccountProfile profile)
    {
        AccountInventory.CheckProfile(profile);
        Profile = profile;
    }

    public void EquipOffline(EquipmentSlot slot, string itemId)
    {
        if (Profile == null) throw new InvalidOperationException("No hay un inventario guardado para practicar.");
        Profile = AccountInventory.Equip(Profile, new(itemId, slot));
    }

    public void EquipOffline(EquipmentLoadout loadout)
    {
        if (Profile == null) throw new InvalidOperationException("No hay un inventario guardado para practicar.");
        Profile = AccountInventory.ApplyLoadout(Profile, loadout);
    }

    public async Task<InventoryResult> Equip(EquipmentSlot slot, string itemId, CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (identity.Direct)
        {
            if (identity.Profile == null) throw new InvalidOperationException("El inventario todavía no se ha cargado.");
            var updated = AccountInventory.Equip(identity.Profile, new InventoryRequest(itemId, slot));
            await identity.Reader!.SaveLoadout(identity.Uid, identity.AccessToken, updated.Loadout, token).ConfigureAwait(false);
            updated = PublishLoadout(updated.Loadout, identity.Generation);
            return new InventoryResult(updated, "Equipamiento guardado en Firebase.");
        }
        var result = await Request<InventoryResult>(HttpMethod.Post, "account/equip", new InventoryRequest(itemId, slot), token, identity).ConfigureAwait(false);
        return result with { Profile = PublishLoadout(result.Profile.Loadout, identity.Generation) };
    }

    /// <summary>Guarda las posiciones juntas, con una sola escritura del equipamiento.</summary>
    public async Task<InventoryResult> SaveLoadout(EquipmentLoadout loadout, CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (identity.Profile == null) throw new InvalidOperationException("El inventario todavía no se ha cargado.");
        var updated = AccountInventory.ApplyLoadout(identity.Profile, loadout);
        if (identity.Direct)
        {
            await identity.Reader!.SaveLoadout(identity.Uid, identity.AccessToken, updated.Loadout, token).ConfigureAwait(false);
            updated = PublishLoadout(updated.Loadout, identity.Generation);
            return new(updated, "Equipamiento guardado en Firebase.");
        }
        var result = await Request<InventoryResult>(HttpMethod.Post, "account/loadout", loadout, token, identity).ConfigureAwait(false);
        return result with { Profile = PublishLoadout(result.Profile.Loadout, identity.Generation) };
    }
}

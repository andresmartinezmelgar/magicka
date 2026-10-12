using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    public AccountClient Accounts { get; } = new();
    private EquipmentPanel? _equipmentPanel;
    private string AccountCache => ProjectSettings.GlobalizePath("user://account-profile.json");
    private void RestoreAccountCache()
    {
        try
        {
            if (!System.IO.File.Exists(AccountCache)) return;
            var file = new System.IO.FileInfo(AccountCache);
            if (file.Length > 1024 * 1024) throw new System.IO.InvalidDataException("Inventario guardado demasiado grande.");
            var profile = Protocol.Deserialize<AccountProfile>(System.IO.File.ReadAllText(AccountCache));
            if (profile != null) Accounts.RestoreOfflineProfile(profile);
        }
        catch (Exception error) { Diagnostics.Warn("account.cache_invalid", "No se pudo recuperar el inventario para práctica.", error); }
    }
    public bool SaveAccountCache()
    {
        try
        {
            if (Accounts.Profile == null) { System.IO.File.Delete(AccountCache); return true; }
            string temporary = AccountCache + ".tmp";
            System.IO.File.WriteAllText(temporary, Protocol.Serialize(Accounts.Profile));
            System.IO.File.Move(temporary, AccountCache, true);
            return true;
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { Diagnostics.Warn("account.cache_failed", "No se pudo guardar el inventario para práctica.", error); return false; }
    }
    public void OpenEquipment(string address)
    {
        if (_playing) return;
        if (GodotObject.IsInstanceValid(_equipmentPanel)) _equipmentPanel!.QueueFree();
        _equipmentPanel = new EquipmentPanel(); _ui.AddChild(_equipmentPanel);
        _equipmentPanel.Build(this, address);
    }
    private void ApplyPracticeEquipment()
    {
        if (_practice != null && Accounts.Profile != null) _practice.EquipPlayer(_localId, EquipmentModifiers.Calculate(Accounts.Profile));
    }
    private async Task RefreshRewardAccount(string message)
    {
        if (!Accounts.SignedIn) return;
        try
        {
            var before = Accounts.Profile;
            await Accounts.Refresh(_lifetime.Token).ConfigureAwait(false);
            var after = Accounts.Profile;
            if (before != null && after != null)
            {
                var added = after.Items.FirstOrDefault(i => before.Items.All(old => old.Id != i.Id));
                if (added != null) message = "Recompensa PvP: " + EquipmentCatalogue.ItemName(added);
                else if (after.Gold > before.Gold) message = "Recompensa PvP: +" + (after.Gold - before.Gold) + " de oro.";
            }
            Post(() => { SaveAccountCache(); _hud?.RewardNotice(message); });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Diagnostics.Warn("account.reward_refresh_failed", "La recompensa está guardada; no se pudo actualizar el inventario del cliente.", error); }
    }
}

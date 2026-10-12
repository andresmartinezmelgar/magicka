using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    public AccountClient Accounts { get; } = new();
    private EquipmentPanel? _equipmentPanel;
    private bool _equipmentSmoke;
    private void EquipmentFixture(bool smoke)
    {
        _equipmentSmoke = smoke;
        var random = new Random(91); var items = Enum.GetValues<ItemTier>().SelectMany(t => Enum.GetValues<EquipmentSlot>()
            .Select(s => EquipmentCatalogue.Generate(random, tier: t, slot: s, family: EquipmentFamily.Life))).ToArray();
        string Id(EquipmentSlot s) => items.Single(i => i.Tier == ItemTier.Mythic && i.Slot == s).Id;
        Accounts.RestoreOfflineProfile(new("fixture-user", "Mago de Vida", 1250, 24, 7,
            new(Id(EquipmentSlot.Boots), Id(EquipmentSlot.Hat), Id(EquipmentSlot.Chest), Id(EquipmentSlot.Weapon)), items));
        OpenEquipment("wss://servidor-central.example");
    }
    private bool CheckEquipmentSmoke()
    {
        if (!_equipmentSmoke || _renderFrames < 12) return false;
        bool valid = _engineLog.ErrorCount == 0 && _equipmentPanel?.VisibleItems == 20 && _equipmentPanel.Details.Contains("Límites:") &&
            Accounts.Profile != null && EquipmentModifiers.Calculate(Accounts.Profile).LifePieces == 4;
        if (valid) GD.Print("EQUIPMENT_SMOKE_OK: inventario, cinco calidades, límites y conjunto completo.");
        else GD.PushError("EQUIPMENT_SMOKE_FAILED: interfaz incompleta o errores de Godot.");
        GetTree().Quit(valid ? 0 : 1); return true;
    }
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
    public void SaveAccountCache()
    {
        try
        {
            if (Accounts.Profile == null) { System.IO.File.Delete(AccountCache); return; }
            string temporary = AccountCache + ".tmp";
            System.IO.File.WriteAllText(temporary, Protocol.Serialize(Accounts.Profile));
            System.IO.File.Move(temporary, AccountCache, true);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { Diagnostics.Warn("account.cache_failed", "No se pudo guardar el inventario para práctica.", error); }
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

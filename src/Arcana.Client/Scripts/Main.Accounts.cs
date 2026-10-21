using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    public AccountClient Accounts { get; private set; } = new();
    private EquipmentPanel? _equipmentPanel;
    private AccountProfilePanel? _accountProfilePanel;
    private ReleaseUpdateNotice? _updateNotice;
    public Task<string> CheckForUpdates() => _updateNotice?.CheckNow() ?? Task.FromResult("El actualizador se está preparando.");
    private PlayerEquipmentPanel? _playerEquipmentPanel;
    private bool InspectingEquipment => _defensePanel?.ShopOpen == true || GodotObject.IsInstanceValid(_playerEquipmentPanel) && !_playerEquipmentPanel!.IsQueuedForDeletion();
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
    public void OpenAccountProfile()
    {
        if (_playing) return;
        if (GodotObject.IsInstanceValid(_accountProfilePanel)) _accountProfilePanel!.QueueFree();
        _accountProfilePanel = new AccountProfilePanel();
        _ui.AddChild(_accountProfilePanel);
        _accountProfilePanel.Build(this);
        RefreshStatisticsAccess();
    }
    private void CloseAccountProfile()
    {
        if (!GodotObject.IsInstanceValid(_accountProfilePanel)) return;
        _accountProfilePanel!.QueueFree();
        _accountProfilePanel = null;
    }
    public void OpenEquipment(string address)
    {
        if (_playing) return;
        if (GodotObject.IsInstanceValid(_equipmentPanel) && !_equipmentPanel!.IsQueuedForDeletion()) return;
        _equipmentPanel = new EquipmentPanel(); _ui.AddChild(_equipmentPanel);
        var panel = _equipmentPanel;
        panel.Closed += () => { if (_equipmentPanel == panel) _equipmentPanel = null; };
        _equipmentPanel.Build(this, address);
    }
    /// <summary>Navegación uniforme de menús; nunca modifica la interfaz en partida.</summary>
    public void NavigateOptionAMenu(int page)
    {
        if (_playing) return;
        CloseItemShop();
        if (GodotObject.IsInstanceValid(_equipmentPanel) && !_equipmentPanel!.IsQueuedForDeletion())
        {
            if (page != 4) _equipmentPanel.Close(() => NavigateOptionAMenu(page));
            return;
        }
        CloseMatchStatistics();
        CloseBestiary();
        CloseAccountProfile();
        CloseOptionAHelp();
        _launcher.CloseRooms();
        if (page == 7) { OpenItemShop(); return; }
        _launcher.NavigateOptionA(page);
    }
    private void ApplyPracticeEquipment()
    {
        if (_practice != null && Accounts.Profile != null) _practice.EquipPlayer(_localId, PublicEquipment.Prepare(Accounts.Profile));
    }
    private void OpenPlayerEquipment()
    {
        if (!_playing || InspectingEquipment) return;
        if (_defensePanel != null) { _defensePanel.OpenShop(); return; }
        StopInput();
        var connection = _connection; var practice = _practice;
        _playerEquipmentPanel = new PlayerEquipmentPanel(); _ui.AddChild(_playerEquipmentPanel);
        _playerEquipmentPanel.Closed += () => { _playerEquipmentPanel = null; _ignoreLeftUntilRelease = true; UpdateTouchVisibility(); };
        _playerEquipmentPanel.Build(this, () => (_connection?.Latest ?? _snapshot)?.Players ?? [], _localId, (id, token) =>
        {
            if (connection != null) return connection.InspectEquipmentAsync(id, token);
            if (practice != null && practice.Players.TryGetValue(id, out var player))
                return Task.FromResult(new EquipmentInspection(id, player.Name, player.WizardType, player.Bot, player.PublicEquipment));
            throw new InvalidOperationException("Este mago ya no está disponible en la sala.");
        });
        UpdateTouchVisibility();
    }
    private void ClosePlayerEquipment()
    {
        if (GodotObject.IsInstanceValid(_playerEquipmentPanel)) _playerEquipmentPanel!.Close();
        _playerEquipmentPanel = null;
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

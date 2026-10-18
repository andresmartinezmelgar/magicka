using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private ItemShopPanel? _itemShopPanel;
    private string _verifiedShopAddress = "";
    public void OpenItemShop()
    {
        if (_playing) return;
        if (GodotObject.IsInstanceValid(_equipmentPanel) && !_equipmentPanel!.IsQueuedForDeletion())
        { _equipmentPanel.Close(OpenItemShop); return; }
        if (GodotObject.IsInstanceValid(_itemShopPanel) && !_itemShopPanel!.IsQueuedForDeletion()) return;
        _itemShopPanel = new ItemShopPanel(); _ui.AddChild(_itemShopPanel); _itemShopPanel.Build(this);
    }
    internal async Task EnsureShopServer(CancellationToken token)
    {
        if (!Accounts.SignedIn) throw new InvalidOperationException("Inicia sesión con Google para acceder a la tienda de tu cuenta.");
        if (Accounts.ServerAddress.Length > 0 && Accounts.ServerAddress != _verifiedShopAddress) return;
        var announced = await PublicAccountServer.Resolve(FirebaseProjectId, token).ConfigureAwait(false);
        var verified = ArenaConnection.Address(announced);
        Accounts.SetProtectedActionsServer(new UriBuilder(verified) { Scheme = "https", Port = 443, Path = "/" }.Uri.AbsoluteUri);
        _verifiedShopAddress = Accounts.ServerAddress;
    }
    internal void CloseItemShop(bool inventory = false)
    {
        if (GodotObject.IsInstanceValid(_itemShopPanel)) _itemShopPanel!.Close();
        _itemShopPanel = null;
        if (inventory) OpenEquipment("automatic");
    }
}

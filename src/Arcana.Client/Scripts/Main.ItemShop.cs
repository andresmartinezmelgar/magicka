using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private ItemShopPanel? _itemShopPanel;
    private Func<CancellationToken, Task<string>> _accountServerResolver = token => PublicAccountServer.Resolve(FirebaseProjectId, token);
    public void OpenItemShop()
    {
        if (_playing) return;
        if (GodotObject.IsInstanceValid(_equipmentPanel) && !_equipmentPanel!.IsQueuedForDeletion())
        { _equipmentPanel.Close(OpenItemShop); return; }
        if (GodotObject.IsInstanceValid(_itemShopPanel) && !_itemShopPanel!.IsQueuedForDeletion()) return;
        if (GodotObject.IsInstanceValid(_accountProfilePanel))
        { _accountProfilePanel!.QueueFree(); _accountProfilePanel = null; }
        CloseOptionAHelp(); _launcher.CloseRooms();
        _itemShopPanel = new ItemShopPanel(); _ui.AddChild(_itemShopPanel); _itemShopPanel.Build(this);
    }
    internal async Task EnsureShopServer(CancellationToken token)
    {
        if (!Accounts.SignedIn) throw new InvalidOperationException("Inicia sesión con Google para acceder a la tienda de tu cuenta.");
        if (!Accounts.IsDirectFirebaseSession) return;
        var announced = await _accountServerResolver(token).ConfigureAwait(false);
        var verified = ArenaConnection.Address(announced);
        Accounts.SetProtectedActionsServer(new UriBuilder(verified) { Scheme = "https", Port = 443, Path = "/" }.Uri.AbsoluteUri);
    }
    internal void CloseItemShop(bool inventory = false)
    {
        if (GodotObject.IsInstanceValid(_itemShopPanel)) _itemShopPanel!.Close();
        _itemShopPanel = null;
        if (inventory) OpenEquipment("automatic");
    }
}

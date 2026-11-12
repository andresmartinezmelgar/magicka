using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private ItemShopPanel? _itemShopPanel;
    private Func<CancellationToken, Task<string>> _accountServerResolver = _ => Task.FromResult(PvpServer.Address);
    private readonly AccountShopPreload _preparedShop = new();
    private bool _shopPreloadEnabled;

    private void InitializeShopPreload(string[] arguments) => _shopPreloadEnabled = !arguments.Any(a =>
        a.Contains("smoke", StringComparison.Ordinal) || a.Contains("preview", StringComparison.Ordinal) ||
        a.StartsWith("--profile", StringComparison.Ordinal) || a.StartsWith("--capture", StringComparison.Ordinal));

    private void TickItemShopPreload()
    {
        if (_shopPreloadEnabled && !_playing && !InventoryAutoSelling && _preparedShop.NeedsPreload(Accounts))
            _ = PreloadItemShop();
    }

    private async Task PreloadItemShop()
    {
        try { await LoadPreparedShop().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception error)
        { Diagnostics.Warn("shop.preload_failed", "No se pudo preparar la tienda en segundo plano. Se reintentará al abrirla.", error); }
    }

    internal ShopResult? PreparedShop => _preparedShop.Get(Accounts);

    internal Task<ShopResult> LoadPreparedShop(CancellationToken waiter = default, bool refresh = false)
    {
        var account = Accounts;
        long session = account.SessionRevision;
        return _preparedShop.Load(account, async token =>
        {
            await EnsureShopServer(token, account).ConfigureAwait(false);
            if (!ReferenceEquals(Accounts, account) || account.SessionRevision != session)
                throw new OperationCanceledException("La cuenta cambió durante la preparación de la tienda.");
            return await account.Shop(token).ConfigureAwait(false);
        }, _lifetime.Token, waiter, refresh);
    }

    internal bool RememberShop(AccountClient account, long session, ShopResult result) =>
        ReferenceEquals(Accounts, account) && _preparedShop.Store(account, session, result);

    internal void ForgetShop(AccountClient account, long session)
    {
        if (ReferenceEquals(Accounts, account)) _preparedShop.Invalidate(account, session);
    }

    public void OpenItemShop()
    {
        if (_playing) return;
        if (GodotObject.IsInstanceValid(_equipmentPanel) && !_equipmentPanel!.IsQueuedForDeletion())
        { _equipmentPanel.Close(OpenItemShop); return; }
        if (GodotObject.IsInstanceValid(_itemShopPanel) && !_itemShopPanel!.IsQueuedForDeletion()) return;
        if (GodotObject.IsInstanceValid(_accountProfilePanel))
        { _accountProfilePanel!.QueueFree(); _accountProfilePanel = null; }
        CloseMenuHelp(); _launcher.CloseRooms();
        _itemShopPanel = new ItemShopPanel(); _ui.AddChild(_itemShopPanel); _itemShopPanel.Build(this);
    }
    internal async Task EnsureShopServer(CancellationToken token, AccountClient? account = null)
    {
        account ??= Accounts;
        if (!account.SignedIn) throw new InvalidOperationException("Inicia sesión con Google para acceder a la tienda de tu cuenta.");
        if (!account.IsDirectFirebaseSession) return;
        var announced = await _accountServerResolver(token).ConfigureAwait(false);
        var verified = ArenaConnection.Address(announced);
        account.SetProtectedActionsServer(new UriBuilder(verified) { Scheme = "https", Port = 443, Path = "/" }.Uri.AbsoluteUri);
    }
    internal void CloseItemShop(bool inventory = false)
    {
        if (GodotObject.IsInstanceValid(_itemShopPanel)) _itemShopPanel!.Close();
        _itemShopPanel = null;
        if (inventory) OpenEquipment("automatic");
    }
}

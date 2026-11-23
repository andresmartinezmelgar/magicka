using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ItemShopPanel
{
    private void SetBusy(bool value, string? status = null)
    {
        _busy = value; _reload.Disabled = value;
        bool used = _shop?.RewardedRefreshAvailableAt > _serverTime.AddMilliseconds(Time.GetTicksMsec() - _receivedAt);
        bool noAds = WithoutAd;
        _adsRemoved = noAds;
        RefreshCommercialButtons();
        bool supported = !noAds && _main.RewardedAds.Supports(_ads);
        _refresh.Disabled = value || !_connected || !(noAds || supported) || used;
        _refresh.Text = used ? "Renovación usada hoy" : noAds ? "Renovar ofertas · 1/día · Sin anuncios" :
            supported ? "Ver anuncio y renovar ofertas · 1/día" : "Anuncios no disponibles";
        _refresh.TooltipText = !_adsEnabled
            ? "Los anuncios están desactivados para todos. Puedes renovar una vez al día sin publicidad."
            : noAds
            ? "Tu cuenta tiene activada la opción sin anuncios. Puedes sustituir las seis ofertas una vez al día, también desde otro dispositivo."
            : "Un anuncio completado permite sustituir las seis ofertas una vez al día. El día se reinicia a las 00:00, hora peninsular.";
        foreach (var button in _buyButtons.Values) button.Disabled = value || !_connected || !button.GetMeta("available").AsBool();
        if (status != null) _status.Text = status;
        RefreshStoreStatusVisibility();
    }

    private void Publish(int request, Action action) => _main.Post(() =>
    { if (!_lifetime.IsCancellationRequested && request == _requestVersion) action(); });

    private void Failed(Exception error)
    {
        Diagnostics.Warn("shop.unavailable", "No se pudo consultar o completar la compra de la tienda.", error);
        ShowUnavailable(!_main.Accounts.SignedIn
            ? "Inicia sesión con Google desde Perfil para consultar las ofertas y comprar objetos."
            : error is System.IO.IOException or System.Net.Http.HttpRequestException or OperationCanceledException
            ? "Sin conexión con la tienda. No hay objetos disponibles para comprar. Pulsa «Consultar» para reintentar."
            : PvpServer.DisplayText(error.Message) + " Pulsa «Consultar» para actualizar las ofertas.");
    }

    private async Task Load(bool refresh = false)
    {
        if (_busy) return;
        if (_main.InventoryAutoSelling)
        { _waitingForAutoSales = true; SetBusy(false, "Terminando la venta automática…"); return; }
        _waitingForAutoSales = false;
        int request = ++_requestVersion;
        if (!refresh && _main.PreparedShop is { } prepared) { Render(prepared); return; }
        ShowUnavailable("Conectando con el servidor de la tienda…");
        SetBusy(true, "Consultando ofertas…");
        try
        {
            var result = await _main.LoadPreparedShop(_lifetime.Token, refresh).ConfigureAwait(false);
            Publish(request, () => Render(result));
        }
        catch (Exception error) { Publish(request, () => Failed(error)); }
    }

    private async Task Buy(string itemId)
    {
        if (_busy || !_connected || _shop == null || !_main.Accounts.SignedIn || !_buyButtons.TryGetValue(itemId, out var button) || button.Disabled) return;
        int request = ++_requestVersion;
        var purchase = new ShopPurchaseRequest(_shop.Id, itemId);
        var account = _main.Accounts;
        long session = account.SessionRevision;
        _main.ForgetShop(account, session);
        SetBusy(true, "Comprando objeto…");
        try
        {
            var result = await account.BuyShopItem(purchase, _lifetime.Token).ConfigureAwait(false);
            // Actualizar la caché aunque la vista se haya cerrado durante la compra.
            bool remembered = _main.RememberShop(account, session, result);
            Publish(request, () =>
            {
                if (remembered && ReferenceEquals(account, _main.Accounts) && account.SessionRevision == session)
                    Render(_main.PreparedShop ?? result, "Objeto añadido al inventario.");
                else ShowUnavailable("La cuenta ha cambiado. Consulta las ofertas de la sesión actual.");
            });
        }
        catch (Exception error) { _main.ForgetShop(account, session); Publish(request, () => Failed(error)); }
    }

    private async Task RenewAfterAd()
    {
        if (_busy || _refresh.Disabled || _shop == null || !_main.Accounts.SignedIn) return;
        int request = ++_requestVersion; var account = _main.Accounts; long session = account.SessionRevision;
        var shopId = _shop.Id; SetBusy(true, "Preparando anuncio recompensado…");
        try
        {
            // La política precargada puede haber cambiado desde otro dispositivo.
            // Antes de mostrar publicidad se consulta el estado actual del servidor.
            var latest = await account.Shop(_lifetime.Token);
            if (!ReferenceEquals(account, _main.Accounts) || session != account.SessionRevision) return;
            if (latest.Shop.Id != shopId)
            {
                _main.RememberShop(account, session, latest);
                Publish(request, () => Render(latest, "Las ofertas han cambiado. Consulta el catálogo antes de renovarlo."));
                return;
            }
            _adsEnabled = latest.AdsEnabled; _ads = latest.RewardedAds;
            if (!latest.AdsEnabled || latest.Profile.Access.WithoutAds(latest.ServerTime ?? ShopNow))
            {
                SetBusy(true, "Renovando ofertas sin anuncios…");
                var renewed = await account.RefreshShopWithoutAd(shopId, _lifetime.Token);
                _main.RememberShop(account, session, renewed);
                Publish(request, () => Render(renewed, "Ofertas renovadas sin anuncios. Podrás renovar de nuevo mañana."));
                return;
            }
            var ticket = await account.StartShopAd(shopId, _lifetime.Token);
            if (!await _main.RewardedAds.Show(ticket, _lifetime.Token))
            { Publish(request, () => SetBusy(false, "Anuncio cerrado. Las ofertas anteriores se conservan.")); return; }
            Publish(request, () => SetBusy(true, "Anuncio completado. Esperando confirmación del servidor…"));
            ShopResult? result = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try { result = await account.CompleteShopAd(new(shopId, ticket.Id), _lifetime.Token); break; }
                catch (InvalidOperationException) when (attempt < 19) { await Task.Delay(1500, _lifetime.Token); }
            }
            if (result == null) throw new InvalidOperationException("La confirmación está pendiente. Consulta la tienda en unos segundos.");
            _main.RememberShop(account, session, result);
            Publish(request, () => Render(result, "Las seis ofertas anteriores se han sustituido. Podrás renovar de nuevo mañana."));
        }
        catch (Exception error) { Publish(request, () => SetBusy(false, PvpServer.DisplayText(error.Message))); }
    }

    internal void HandleBack() { if (_comparison.Visible) _comparison.Hide(); else _main.NavigateOptionAMenu(0); }

    private async Task RefreshAdPolicy()
    {
        _checkingAdPolicy = true; _adPolicyRetryAt = Time.GetTicksMsec() + 60000;
        var account = _main.Accounts; long session = account.SessionRevision; int request = _requestVersion;
        AdvertisingSettings? settings = null;
        try { settings = await account.Advertising(_lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or System.IO.IOException or
            System.Net.Http.HttpRequestException or InvalidOperationException) { }
        finally
        {
            _main.Post(() =>
            {
                if (_lifetime.IsCancellationRequested) return;
                _checkingAdPolicy = false;
                if (settings == null || request != _requestVersion || !ReferenceEquals(account, _main.Accounts) ||
                    session != account.SessionRevision || _busy || !_connected) return;
                bool changed = settings.Enabled != _adsEnabled;
                _adsEnabled = settings.Enabled; SetBusy(false);
                // Solo al reactivarse se necesita recuperar la unidad publicitaria.
                if (changed && settings.Enabled && !WithoutAd && _ads == null) _ = Load(refresh: true);
            });
        }
    }

    internal void Close()
    {
        _lifetime.Cancel(); QueueFree();
    }

    public override void _ExitTree() { _lifetime.Cancel(); _billing?.Dispose(); }
}

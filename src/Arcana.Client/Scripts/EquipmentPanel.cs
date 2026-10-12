using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel : Control
{
    private Main _main = null!;
    private ItemList _items = null!;
    private OptionButton _filter = null!;
    private LineEdit _address = null!;
    private Label _profile = null!, _details = null!, _summary = null!, _status = null!;
    private Button _equip = null!, _sell = null!;
    private ConfirmationDialog _confirm = null!;
    private EquipmentPreview _preview = null!;
    private EquipmentItem[] _visibleItems = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Button> _actions = [];
    private bool _busy;
    private string _selling = "";
    internal int VisibleItems => _visibleItems.Length;
    internal string Details => _details.Text;
    public void Build(Main main, string address)
    {
        _main = main; Name = "EquipmentPanel"; Theme = Ui.Theme(); Ui.Full(this);
        AddChild(new ColorRect { Color = new(0.01f, 0.025f, 0.045f, 0.98f), LayoutMode = 1,
            AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer(); AddChild(center); Ui.Full(center);
        var panel = new PanelContainer { CustomMinimumSize = new(1000, 0) }; center.AddChild(panel);
        var scroll = new ScrollContainer { CustomMinimumSize = new(960, Math.Min(650, GetViewport().GetVisibleRect().Size.Y - 70)),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; panel.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 10); scroll.AddChild(box);
        box.AddChild(Ui.Label("MI MAGO", 28, Ui.Gold));
        _profile = Ui.Label("", 16); box.AddChild(_profile);
        var connection = new HBoxContainer(); connection.AddThemeConstantOverride("separation", 10); box.AddChild(connection);
        _address = new LineEdit { Text = address, PlaceholderText = "wss://servidor-central", CustomMinimumSize = new(340, 42), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        connection.AddChild(_address); Action(connection, "Google", () => _ = Login(), true);
        Action(connection, "Actualizar", () => _ = Refresh()); Action(connection, "Cerrar sesión", () => _ = SignOut());
        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", 20); box.AddChild(columns);
        var inventory = new VBoxContainer { CustomMinimumSize = new(440, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }; columns.AddChild(inventory);
        _filter = new OptionButton { CustomMinimumSize = new(0, 42) }; _filter.AddItem("Todos los objetos", 4);
        foreach (var slot in Enum.GetValues<EquipmentSlot>()) _filter.AddItem(EquipmentCatalogue.SlotName(slot), (int)slot);
        inventory.AddChild(_filter); _filter.ItemSelected += _ => Render();
        _items = new ItemList { CustomMinimumSize = new(0, 265), SelectMode = ItemList.SelectModeEnum.Single };
        inventory.AddChild(_items); _items.ItemSelected += _ => ShowSelection();
        var itemActions = new HBoxContainer(); itemActions.AddThemeConstantOverride("separation", 10); inventory.AddChild(itemActions);
        _equip = Action(itemActions, "Equipar", () => _ = EquipSelection(), true);
        _sell = Action(itemActions, "Eliminar por oro", ConfirmSale);
        var info = new VBoxContainer { CustomMinimumSize = new(450, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }; columns.AddChild(info);
        _preview = new EquipmentPreview { CustomMinimumSize = new(0, 110) }; info.AddChild(_preview);
        _details = Ui.Label("Selecciona un objeto para ver su valor actual y sus límites.", 15);
        _details.AutowrapMode = TextServer.AutowrapMode.WordSmart; info.AddChild(_details);
        _summary = Ui.Label("", 15, Ui.Gold); _summary.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_summary);
        var unequip = new HBoxContainer(); unequip.AddThemeConstantOverride("separation", 10); box.AddChild(unequip);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        { var current = slot; Action(unequip, "Quitar " + EquipmentCatalogue.SlotName(slot).ToLowerInvariant(), () => _ = Equip(current, "")); }
        _status = Ui.Label("", 15, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_status);
        box.AddChild(Ui.Label("El equipo funciona en PvP y contra IA. Los conjuntos Míticos se activan con 2 o 4 piezas de la misma familia.", 14, Ui.Muted));
        var close = Action(box, "Volver", () => { _lifetime.Cancel(); QueueFree(); }); _actions.Remove(close);
        _confirm = new ConfirmationDialog { Title = "Eliminar objeto", OkButtonText = "Eliminar y recibir oro", CancelButtonText = "Cancelar" };
        AddChild(_confirm); _confirm.Confirmed += () => _ = Sell();
        Render();
        if (_main.Accounts.SignedIn) _ = Refresh();
    }
    private Button Action(Node parent, string text, Action action, bool primary = false)
    { var button = Ui.Button(text, action, primary); parent.AddChild(button); _actions.Add(button); return button; }
    private EquipmentItem? Selected()
    { var indices = _items.GetSelectedItems(); return indices.Length > 0 && indices[0] < _visibleItems.Length ? _visibleItems[indices[0]] : null; }
    private void Render()
    {
        var account = _main.Accounts.Profile;
        _profile.Text = account == null ? "Inicia sesión con Google para recuperar tu inventario." :
            $"{account.Name} · Oro: {account.Gold} · Objetos: {account.Items.Length}/{EquipmentCatalogue.InventoryLimit} · Victorias: {account.Wins}";
        var old = Selected()?.Id;
        _items.Clear(); int filter = _filter.GetSelectedId();
        _visibleItems = (account?.Items ?? []).Where(i => filter == 4 || (int)i.Slot == filter).OrderByDescending(i => i.Tier).ThenBy(i => i.Slot).ThenBy(i => i.Id).ToArray();
        int selected = 0;
        for (int i = 0; i < _visibleItems.Length; i++)
        {
            var item = _visibleItems[i]; bool equipped = account?.Loadout.Get(item.Slot) == item.Id;
            _items.AddItem((equipped ? "[E] " : "") + EquipmentCatalogue.ItemName(item));
            _items.SetItemCustomFgColor(i, TierColor(item.Tier)); if (item.Id == old) selected = i;
        }
        if (_visibleItems.Length > 0) _items.Select(selected);
        if (account != null)
        {
            var m = EquipmentModifiers.Calculate(account); _preview.Appearance = m.Appearance;
            string P(float factor) => EquipmentCatalogue.Percent(factor - 1);
            _summary.Text = $"EQUIPO · Velocidad {P(m.Speed)} · Alcance {P(m.Range)} · Defensa {EquipmentCatalogue.Percent(1 - m.Incoming)} · Ataque {P(m.Power)}\n"
                + $"Curación {P(m.Healing)} · Fuego {P(m.Fire)} · Escudo {P(m.ShieldCapacity)} absorción / {P(m.ShieldDuration)} duración\n"
                + $"Energía {P(m.EnergyRegeneration)} · Quemaduras {P(m.BurnDuration)} · Ahorro al reanimar {EquipmentCatalogue.Percent(1 - m.LifeCost)}\n"
                + "CONJUNTOS · Vida " + Set(m.LifePieces) + " · Escudo " + Set(m.ShieldPieces) + " · Fuego " + Set(m.FirePieces);
            _summary.TooltipText = "Vida: 2 piezas +5 % curación; 4 piezas +15 % curación y +10 % ahorro al reanimar.\n"
                + "Escudo: 2 piezas +10 % duración; 4 piezas +20 % duración y +20 % absorción.\n"
                + "Fuego: 2 piezas +5 % daño de fuego; 4 piezas +15 % daño de fuego y +20 % duración de quemadura. El bonus de 4 sustituye al de 2.";
        }
        else { _summary.Text = "El mago empieza con sus estadísticas básicas. Gana partidas PvP para conseguir objetos."; _preview.Appearance = new(); }
        _preview.QueueRedraw(); ShowSelection();
    }
    private static string Set(int pieces) => pieces + "/4" + (pieces == 4 ? " (bonus completo)" : pieces >= 2 ? " (bonus de 2)" : "");
    private void ShowSelection()
    {
        var item = Selected();
        _equip.Disabled = _busy || item == null;
        _sell.Disabled = _busy || item == null || !_main.Accounts.SignedIn;
        if (item == null) { _details.Text = "No hay objetos en esta selección."; return; }
        _details.Text = EquipmentCatalogue.ItemName(item) + "\n\n" + string.Join("\n", item.Bonuses.Select(b =>
            EquipmentCatalogue.StatName(b.Stat) + ": " + EquipmentCatalogue.Percent(b.Current) + "\nLímites: " + EquipmentCatalogue.Percent(b.Minimum) + " a " + EquipmentCatalogue.Percent(b.Maximum)))
            + $"\n\nEliminar: +{item.SaleGold} de oro.\nEl oro se conserva para futuras mejoras.";
    }
    public static Color TierColor(ItemTier tier) => tier switch
    { ItemTier.Silver => new("c4cbd7"), ItemTier.Gold => new("e8c574"), ItemTier.Platinum => new("82dfd6"), ItemTier.Diamond => new("73bfff"), _ => new("cf9cff") };
    private void Busy(bool value, string message = "")
    {
        _busy = value; foreach (var button in _actions) button.Disabled = value;
        _address.Editable = !value; if (message.Length > 0) _status.Text = message;
        ShowSelection();
    }
    private async Task Login()
    {
        if (_busy) return; Busy(true, "Preparando el acceso con Google…");
        string address = _address.Text;
        try
        {
            var config = await _main.Accounts.Configuration(address, _lifetime.Token);
            if (!config.Enabled) throw new InvalidOperationException(config.Message);
            var challenge = await _main.Accounts.BeginLogin(address, _lifetime.Token);
            _main.Post(() => { OS.ShellOpen(challenge.Url); _status.Text = "Completa el acceso con Google en el navegador y vuelve al juego."; });
            while (DateTimeOffset.UtcNow < challenge.ExpiresAt)
            {
                if (await _main.Accounts.PollLogin(challenge, _lifetime.Token))
                { Done("Cuenta cargada. Puedes personalizar tu mago."); return; }
                await Task.Delay(1000, _lifetime.Token);
            }
            throw new InvalidOperationException("El acceso ha caducado. Inícialo de nuevo.");
        }
        catch (Exception error) { Failed(error); }
    }
    private async Task Refresh()
    {
        if (_busy) return; Busy(true, "Cargando inventario…");
        try { await _main.Accounts.Refresh(_lifetime.Token); Done("Inventario actualizado."); }
        catch (Exception error) { Failed(error); }
    }
    private Task EquipSelection() => Selected() is { } item ? Equip(item.Slot, item.Id) : Task.CompletedTask;
    private async Task Equip(EquipmentSlot slot, string id)
    {
        if (_busy) return; Busy(true, "Guardando equipo…");
        try
        {
            if (_main.Accounts.SignedIn) { var result = await _main.Accounts.Equip(slot, id, _lifetime.Token); Done(result.Message); }
            else { _main.Accounts.EquipOffline(slot, id); Done("Equipo seleccionado para jugar contra IA sin conexión."); }
        }
        catch (Exception error) { Failed(error); }
    }
    private void ConfirmSale()
    {
        var item = Selected(); if (_busy || item == null || !_main.Accounts.SignedIn) return;
        _selling = item.Id;
        _confirm.DialogText = "Eliminar " + EquipmentCatalogue.ItemName(item) + $" por {item.SaleGold} de oro.\nSi está equipado, se quitará de tu mago. Esta eliminación es permanente.";
        _confirm.PopupCentered(new(640, 220));
    }
    private async Task Sell()
    {
        if (_busy || _selling.Length == 0) return; Busy(true, "Eliminando objeto y abonando oro…");
        try { var result = await _main.Accounts.Sell(_selling, _lifetime.Token); Done(result.Message); }
        catch (Exception error) { Failed(error); }
        finally { _selling = ""; }
    }
    private async Task SignOut()
    {
        if (_busy) return; Busy(true);
        try { await _main.Accounts.SignOut(_lifetime.Token); Done("Sesión cerrada."); }
        catch (Exception error) { Failed(error); }
    }
    private void Done(string message) => _main.Post(() =>
    { if (_lifetime.IsCancellationRequested) return; _main.SaveAccountCache(); Render(); Busy(false, message); });
    private void Failed(Exception error) => _main.Post(() =>
    { if (_lifetime.IsCancellationRequested) return; Busy(false, error is OperationCanceledException ? "Operación cancelada." : error.Message); });
    public override void _ExitTree() { _lifetime.Cancel(); _lifetime.Dispose(); }
}

using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel : Control
{
    private sealed record SlotControls(Button Previous, Button Inspect, Button Next);
    private Main _main = null!;
    private LineEdit _address = null!;
    private bool _automaticServer;
    private Label _profile = null!, _detailTitle = null!, _empty = null!, _sets = null!, _status = null!;
    private GridContainer _details = null!, _totals = null!;
    private Button _sell = null!;
    private ConfirmationDialog _confirm = null!;
    private AcceptDialog _setsInfo = null!;
    private readonly SlotControls[] _slots = new SlotControls[4];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Button> _actions = [];
    private EquipmentSlot _selectedSlot;
    private bool _busy;
    private string _selling = "";
    internal int VisibleItems => _main.Accounts.Profile?.Items.Length ?? 0;
    internal EquipmentSlot SelectedSlot => _selectedSlot;
    internal string Details => string.Join("\n", _details.GetChildren().OfType<Label>().Select(l => l.Text));
    internal string Totals => string.Join("\n", _totals.GetChildren().OfType<Label>().Select(l => l.Text));
    internal bool Saving => _busy;
    internal string Sets => _sets.Text;

    public void Build(Main main, string address)
    {
        _automaticServer = address == "automatic";
        _main = main; Name = "EquipmentPanel"; Theme = Ui.Theme(); Ui.Full(this);
        AddChild(new ColorRect { Color = new(0.01f, 0.025f, 0.045f, 0.98f), LayoutMode = 1, AnchorRight = 1, AnchorBottom = 1 });
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
        var panel = new PanelContainer(); margin.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); panel.AddChild(box);
        var heading = new HBoxContainer(); box.AddChild(heading);
        heading.AddChild(Ui.Label("MI MAGO · EQUIPACIÓN", 25, Ui.Gold));
        _profile = Ui.Label("", 16); _profile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _profile.HorizontalAlignment = HorizontalAlignment.Right; heading.AddChild(_profile);
        var connection = new HBoxContainer(); connection.AddThemeConstantOverride("separation", 8); box.AddChild(connection);
        _address = new LineEdit { Text = address, PlaceholderText = "wss://servidor-central", CustomMinimumSize = new(240, 48), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _address.AddThemeFontSizeOverride("font_size", 20); connection.AddChild(_address);
        _address.Visible = !_automaticServer;
        Action(connection, "Entrar con Google", () => _ = Login(), true);
        Action(connection, "Actualizar", () => _ = Refresh());
        Action(connection, "Cerrar sesión", () => _ = SignOut());
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 24); box.AddChild(columns);
        var inventoryScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.1f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        columns.AddChild(inventoryScroll);
        var inventory = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; inventory.AddThemeConstantOverride("separation", 6); inventoryScroll.AddChild(inventory);
        var selectors = new GridContainer { Columns = 2 }; selectors.AddThemeConstantOverride("h_separation", 10);
        selectors.AddThemeConstantOverride("v_separation", 6); inventory.AddChild(selectors);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var current = slot;
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 4); selectors.AddChild(row);
            var previous = Action(row, "‹", () => Cycle(current, -1)); previous.CustomMinimumSize = new(54, 54);
            previous.TooltipText = "Pieza anterior: " + EquipmentCatalogue.SlotName(slot);
            var inspect = Action(row, "", () => SelectSlot(current)); inspect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            inspect.TooltipText = "Ver los atributos individuales de " + EquipmentCatalogue.SlotName(slot).ToLowerInvariant();
            var next = Action(row, "›", () => Cycle(current, 1)); next.CustomMinimumSize = new(54, 54);
            next.TooltipText = "Pieza siguiente: " + EquipmentCatalogue.SlotName(slot);
            _slots[(int)slot] = new(previous, inspect, next);
        }
        _detailTitle = Ui.Label("", 21, Ui.Gold); inventory.AddChild(_detailTitle);
        _details = new GridContainer { Columns = 3 }; _details.AddThemeConstantOverride("h_separation", 14);
        _details.AddThemeConstantOverride("v_separation", 3); inventory.AddChild(_details);
        _empty = Ui.Label("", 16, Ui.Muted); _empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; inventory.AddChild(_empty);
        _sell = Action(inventory, "Eliminar pieza por oro", ConfirmSale);
        var totalsScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        columns.AddChild(totalsScroll);
        var summary = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; summary.AddThemeConstantOverride("separation", 8); totalsScroll.AddChild(summary);
        var summaryHeading = new HBoxContainer(); summaryHeading.AddThemeConstantOverride("separation", 10); summary.AddChild(summaryHeading);
        var summaryTitle = Ui.Label("SUMA DE TODO EL EQUIPO", 22, Ui.Gold); summaryTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        summaryTitle.TooltipText = "Suma = piezas + conjunto. Aplicado incluye los límites de combate. Valores respecto al mago base.";
        summaryHeading.AddChild(summaryTitle);
        Action(summaryHeading, "Conjuntos", () => _setsInfo.PopupCentered());
        _sets = Ui.Label("", 16, Ui.Gold); _sets.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.AddChild(_sets);
        _totals = new GridContainer { Columns = 5 }; _totals.AddThemeConstantOverride("h_separation", 12);
        _totals.AddThemeConstantOverride("v_separation", 3); summary.AddChild(_totals);
        _status = Ui.Label("", 15, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_status);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 14); box.AddChild(footer);
        var hint = Ui.Label("Suma = piezas + conjunto. Aplicado = valor real en combate.", 14, Ui.Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; hint.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(hint);
        var close = Action(footer, "Volver", () => { _lifetime.Cancel(); QueueFree(); }); _actions.Remove(close);
        _confirm = new ConfirmationDialog { Title = "Eliminar objeto", OkButtonText = "Eliminar y recibir oro", CancelButtonText = "Cancelar" };
        AddChild(_confirm); _confirm.Confirmed += () => _ = Sell();
        _setsInfo = new AcceptDialog { Title = "Conjuntos Míticos de los ocho elementos", OkButtonText = "Cerrar" }; AddChild(_setsInfo);
        var setsScroll = new ScrollContainer { CustomMinimumSize = new(920, Math.Min(480, GetViewport().GetVisibleRect().Size.Y - 180)),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _setsInfo.AddChild(setsScroll);
        var descriptions = Ui.Label(string.Join("\n\n", EquipmentCatalogue.FocusedFamilies.Select(f => EquipmentCatalogue.FamilyName(f) + "\n"
            + "2 o 3 piezas: " + SetDescription(f, false) + "\n4 piezas: " + SetDescription(f, true)))
            + "\n\nEl bono de cuatro sustituye al de dos. Se pueden combinar dos conjuntos de dos piezas.", 17);
        descriptions.AutowrapMode = TextServer.AutowrapMode.WordSmart; descriptions.SizeFlagsHorizontal = SizeFlags.ExpandFill; setsScroll.AddChild(descriptions);
        Render();
        if (_main.Accounts.SignedIn) _ = Refresh();
    }

    private Button Action(Node parent, string text, Action action, bool primary = false)
    {
        var button = Ui.Button(text, action, primary); button.CustomMinimumSize = new(0, 48);
        parent.AddChild(button);
        button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 20 : 17);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var style = (StyleBoxFlat)button.GetThemeStylebox(state).Duplicate();
            style.ContentMarginTop = style.ContentMarginBottom = 8; style.ContentMarginLeft = style.ContentMarginRight = 10;
            button.AddThemeStyleboxOverride(state, style);
        }
        _actions.Add(button); return button;
    }
    internal static string StatName(EquipmentStat stat) => stat switch
    {
        EquipmentStat.Power => "Ataque", EquipmentStat.FirePower => "Daño de fuego", EquipmentStat.ShieldCapacity => "Absorción escudo",
        EquipmentStat.ShieldDuration => "Duración escudo", EquipmentStat.EnergyRegeneration => "Regen. energía",
        EquipmentStat.LifeEfficiency => "Ahorro reanimar", EquipmentStat.BurnDuration => "Quemaduras", EquipmentStat.WetDuration => "Mojado",
        EquipmentStat.ChillPower => "Enfriamiento", EquipmentStat.FreezeDuration => "Congelación", EquipmentStat.WetLightningPower => "Rayo a mojados",
        EquipmentStat.BeamChargeSpeed => "Carga del haz", EquipmentStat.RockChargeSpeed => "Carga de rocas", _ => EquipmentCatalogue.StatName(stat)
    };
    internal static string SetDescription(EquipmentFamily family, bool four) => string.Join(", ", EquipmentCatalogue.SetBonuses(family)
        .Where(b => (four ? b.FourPieces : b.TwoPieces) != 0)
        .Select(b => StatName(b.Stat) + " " + EquipmentCatalogue.Percent(four ? b.FourPieces : b.TwoPieces)));
    private static void Clear(Node parent)
    { foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); } }
    private static Label Cell(GridContainer grid, string text, bool number = false, Color? color = null)
    {
        var label = Ui.Label(text, 16, color); label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (number) label.HorizontalAlignment = HorizontalAlignment.Right;
        grid.AddChild(label); return label;
    }
    private EquipmentItem[] Choices(EquipmentSlot slot) => (_main.Accounts.Profile?.Items ?? [])
        .Where(i => i.Slot == slot).OrderByDescending(i => i.Tier).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
    private EquipmentItem? Selected()
    {
        var account = _main.Accounts.Profile;
        return account?.Items.FirstOrDefault(i => i.Id == account.Loadout.Get(_selectedSlot));
    }
    private void SelectSlot(EquipmentSlot slot) { _selectedSlot = slot; Render(); }
    internal void PressArrow(EquipmentSlot slot, int direction)
    {
        var button = direction < 0 ? _slots[(int)slot].Previous : _slots[(int)slot].Next;
        if (!button.Disabled) button.EmitSignal(Button.SignalName.Pressed);
    }
    internal void PressInspect(EquipmentSlot slot) => _slots[(int)slot].Inspect.EmitSignal(Button.SignalName.Pressed);
    private void Cycle(EquipmentSlot slot, int direction)
    {
        if (_busy || _main.Accounts.Profile == null || direction is not (-1 or 1)) return;
        _selectedSlot = slot;
        var choices = Choices(slot); if (choices.Length == 0) { Render(); return; }
        string old = _main.Accounts.Profile.Loadout.Get(slot);
        int index = Array.FindIndex(choices, i => i.Id == old) + 1; // 0 permite quitar la pieza.
        int next = (index + direction + choices.Length + 1) % (choices.Length + 1);
        _ = Equip(slot, next == 0 ? "" : choices[next - 1].Id);
    }
    private void Render()
    {
        var account = _main.Accounts.Profile;
        _profile.Text = account == null ? "Sin cuenta" : $"{account.Name} · {account.Gold} oro · {account.Items.Length}/{EquipmentCatalogue.InventoryLimit} objetos";
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var view = _slots[(int)slot]; var choices = Choices(slot);
            int index = Array.FindIndex(choices, i => i.Id == account?.Loadout.Get(slot));
            var item = index >= 0 ? choices[index] : null;
            view.Inspect.Text = EquipmentCatalogue.SlotName(slot) + " · " + (item == null ? "Sin equipo" : EquipmentCatalogue.TierName(item.Tier))
                + "\n" + (choices.Length == 0 ? "Sin objetos" : (item == null ? "" : EquipmentCatalogue.FamilyName(item.Family) + " · ") + $"{index + 1}/{choices.Length}");
            view.Inspect.AddThemeColorOverride("font_color", slot == _selectedSlot ? Ui.Gold : item == null ? Ui.Muted : TierColor(item.Tier));
            view.Previous.Disabled = view.Next.Disabled = _busy || choices.Length == 0;
            view.Inspect.Disabled = _busy;
        }
        RenderDetails();
        Clear(_totals);
        foreach (string heading in new[] { "Atributo", "Piezas", "Conjunto", "Suma", "Aplicado" }) Cell(_totals, heading, heading != "Atributo", Ui.Muted);
        var breakdown = EquipmentModifiers.Summarize(account ?? new("", "", 0, 0, 0, new(), []));
        var equippedStats = account?.Items.Where(i => account.Loadout.Get(i.Slot) == i.Id).SelectMany(i => i.Bonuses).Select(b => b.Stat).ToHashSet() ?? [];
        foreach (var stat in breakdown.Stats.Where(s => (int)s.Stat <= (int)EquipmentStat.Power || s.Items != 0 || s.Set != 0 || equippedStats.Contains(s.Stat)))
        {
            Cell(_totals, StatName(stat.Stat)).TooltipText = EquipmentCatalogue.StatName(stat.Stat);
            Cell(_totals, EquipmentCatalogue.Percent(stat.Items), true);
            Cell(_totals, EquipmentCatalogue.Percent(stat.Set), true, stat.Set > 0 ? Ui.Gold : Ui.Muted);
            Cell(_totals, EquipmentCatalogue.Percent(stat.Sum), true);
            var applied = Cell(_totals, EquipmentCatalogue.Percent(stat.Effective), true, stat.Limited ? Ui.Gold : stat.Effective < 0 ? Ui.Red : Ui.Blue);
            if (stat.Limited) applied.TooltipText = "La suma supera el límite de este atributo. Este es el valor que se aplica en combate.";
        }
        var m = breakdown.Modifiers;
        var activeSets = EquipmentCatalogue.FocusedFamilies.Where(f => m.Pieces(f) > 0).ToArray();
        _sets.Text = "Míticos · " + (activeSets.Length == 0 ? "sin piezas" : string.Join(" · ", activeSets.Select(f => EquipmentCatalogue.FamilyName(f) + " " + m.Pieces(f) + "/4")));
        _sets.TooltipText = "Solo cuentan las piezas Míticas de la misma familia. Con 3 piezas sigue activo el bonus de 2. El bonus de 4 sustituye al de 2.\n"
            + string.Join("\n", EquipmentCatalogue.FocusedFamilies.Select(f => Set(EquipmentCatalogue.FamilyName(f), m.Pieces(f), SetDescription(f, false), SetDescription(f, true))));
        if (!_busy && _status.Text.Length == 0) _status.Text = account == null ? "Inicia sesión con Google para recuperar tu inventario y conseguir objetos en PvP." :
            _main.Accounts.SignedIn ? "Elige con las flechas: se guarda automáticamente en tu cuenta para PvP y contra IA." : "Elige con las flechas: se guarda en este dispositivo para jugar contra IA.";
    }
    private static string Set(string name, int pieces, string two, string four) => name + " " + pieces + "/4 · " + (pieces == 4 ? four : pieces >= 2 ? two : "sin bonificación");
    private void RenderDetails()
    {
        Clear(_details); var item = Selected();
        _detailTitle.Text = EquipmentCatalogue.SlotName(_selectedSlot) + " · ATRIBUTOS INDIVIDUALES";
        foreach (string heading in new[] { "Atributo", "Actual", "Límites (mín. / máx.)" }) Cell(_details, heading, heading != "Atributo", Ui.Muted);
        if (item != null) foreach (var bonus in item.Bonuses)
        {
            Cell(_details, StatName(bonus.Stat)).TooltipText = EquipmentCatalogue.StatName(bonus.Stat);
            Cell(_details, EquipmentCatalogue.Percent(bonus.Current), true, bonus.Current < 0 ? Ui.Red : Ui.Blue);
            Cell(_details, EquipmentCatalogue.Percent(bonus.Minimum) + " / " + EquipmentCatalogue.Percent(bonus.Maximum), true, Ui.Muted);
        }
        _empty.Text = item == null ? "Sin pieza equipada. Usa las flechas para recorrer los objetos de esta posición o pulsa otra pieza para ver sus atributos." :
            "Pulsa otra pieza para ver sus atributos. El oro se guarda para futuras mejoras.";
        _sell.Text = item == null ? "Eliminar pieza por oro" : $"Eliminar esta pieza · +{item.SaleGold} oro";
        _sell.Disabled = _busy || item == null || !_main.Accounts.SignedIn;
    }
    public static Color TierColor(ItemTier tier) => tier switch
    { ItemTier.Silver => new("c4cbd7"), ItemTier.Gold => new("e8c574"), ItemTier.Platinum => new("82dfd6"), ItemTier.Diamond => new("73bfff"), _ => new("cf9cff") };
    private void Busy(bool value, string message = "")
    {
        _busy = value; foreach (var button in _actions) button.Disabled = value;
        _address.Editable = !value; if (message.Length > 0) _status.Text = message;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var view = _slots[(int)slot]; view.Previous.Disabled = view.Next.Disabled = value || Choices(slot).Length == 0;
        }
        _sell.Disabled = value || Selected() == null || !_main.Accounts.SignedIn;
    }
    private async Task Login()
    {
        if (_busy) return; Busy(true, "Preparando el acceso con Google…");
        string address = _address.Text;
        try
        {
            if (_automaticServer)
            {
                using var settings = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://account-server.json"));
                address = await PublicAccountServer.Resolve(settings.RootElement.GetProperty("projectId").GetString() ?? "", _lifetime.Token);
                _main.Post(() => { if (!_lifetime.IsCancellationRequested) _address.Text = address; });
            }
            var config = await _main.Accounts.Configuration(address, _lifetime.Token);
            if (!config.Enabled) throw new InvalidOperationException(config.Message);
            var challenge = await _main.Accounts.BeginLogin(address, _lifetime.Token);
            _main.Post(() => { if (_lifetime.IsCancellationRequested) return; OS.ShellOpen(challenge.Url); _status.Text = "Completa el acceso con Google en el navegador y vuelve al juego."; });
            while (DateTimeOffset.UtcNow < challenge.ExpiresAt)
            {
                if (await _main.Accounts.PollLogin(challenge, _lifetime.Token)) { Done("Cuenta cargada. Puedes elegir tu equipo con las flechas."); return; }
                await Task.Delay(1000, _lifetime.Token);
            }
            throw new InvalidOperationException("El acceso ha caducado. Inícialo de nuevo.");
        }
        catch (Exception error) { Failed(error); }
    }
    internal void BeginGoogleLogin() => _ = Login();
    private async Task Refresh()
    {
        if (_busy) return;
        if (!_main.Accounts.SignedIn) { Busy(false, "Inicia sesión con Google para actualizar el inventario de tu cuenta."); return; }
        Busy(true, "Cargando inventario…");
        try { await _main.Accounts.Refresh(_lifetime.Token); Done("Inventario actualizado; se ha recuperado tu equipo guardado."); }
        catch (Exception error) { Failed(error); }
    }
    private async Task Equip(EquipmentSlot slot, string id)
    {
        if (_busy) return; Busy(true, "Guardando equipo…");
        try
        {
            if (_main.Accounts.SignedIn) { await _main.Accounts.Equip(slot, id, _lifetime.Token); Done("Equipo guardado en tu cuenta para las siguientes partidas."); }
            else
            {
                _main.Accounts.EquipOffline(slot, id);
                bool saved = _main.SaveAccountCache(); Render();
                Busy(false, saved ? "Equipo guardado en este dispositivo para las siguientes partidas contra IA." : "Equipo cambiado para esta sesión; no se pudo guardar en el dispositivo.");
            }
        }
        catch (Exception error) { Failed(error); }
    }
    private void ConfirmSale()
    {
        var item = Selected(); if (_busy || item == null || !_main.Accounts.SignedIn) return;
        _selling = item.Id;
        _confirm.DialogText = "Eliminar " + EquipmentCatalogue.ItemName(item) + $" por {item.SaleGold} de oro.\nSe quitará de tu mago. Esta eliminación es permanente.";
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
        catch (Exception error)
        {
            _main.Post(() =>
            {
                _main.SaveAccountCache();
                if (_lifetime.IsCancellationRequested) return;
                Render(); Busy(false, "Sesión cerrada en este dispositivo. " + error.Message);
            });
        }
    }
    private void Done(string message) => _main.Post(() =>
    {
        bool saved = _main.SaveAccountCache();
        if (_lifetime.IsCancellationRequested) return;
        Render(); Busy(false, saved ? message : message + " No se pudo actualizar la copia del dispositivo.");
    });
    private void Failed(Exception error) => _main.Post(() =>
    { if (_lifetime.IsCancellationRequested) return; Render(); Busy(false, error is OperationCanceledException ? "Operación cancelada." : error.Message); });
    public override void _ExitTree() { _lifetime.Cancel(); _lifetime.Dispose(); }
}

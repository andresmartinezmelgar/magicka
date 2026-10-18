using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel : Control
{
    private Main _main = null!;
    private Label _profile = null!, _detailTitle = null!, _empty = null!, _sets = null!, _status = null!;
    private Label _combatPreview = null!;
    private Label _totalsEmpty = null!;
    private HBoxContainer _equipmentHeading = null!, _equipmentToolbar = null!;
    private HBoxContainer _summaryHeading = null!;
    private VBoxContainer _summaryContent = null!;
    private Label _summaryTitle = null!;
    private bool _showingTotals, _routineStatus = true;
    private GridContainer _details = null!, _totals = null!;
    private GridContainer? _selectorsGrid;
    private ScrollContainer? _setsScroll;
    private Label _salesHelp = null!;
    private ConfirmationDialog _confirm = null!;
    private AcceptDialog _setsInfo = null!;
    private readonly Button[] _slots = new Button[4];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Button> _actions = [];
    private EquipmentSlot _selectedSlot;
    private bool _busy;
    private bool _closing, _onlineInventory;
    private AccountProfile? _draft;
    private EquipmentLoadout _savedLoadout = new();
    private string _selling = "";
    public event Action? Closed;
    internal int VisibleItems => _draft?.Items.Length ?? 0;
    internal AccountProfile? PreviewProfile => _draft;
    internal bool HasChanges => _draft != null && _draft.Loadout != _savedLoadout;
    internal EquipmentSlot SelectedSlot => _selectedSlot;
    internal string Details => string.Join("\n", _details.GetChildren().OfType<Label>().Select(l => l.Text));
    internal string Totals => string.Join("\n", _totals.GetChildren().OfType<Label>().Select(l => l.Text));
    internal bool Saving => _busy;
    internal string Sets => _sets.Text;

    public void Build(Main main, string address)
    {
        _main = main; Name = "EquipmentPanel"; Theme = Ui.Theme(); Ui.Full(this);
        _draft = _defensePlayer == null ? main.Accounts.Profile : DefenseProfile(_defensePlayer);
        _savedLoadout = _draft?.Loadout ?? new();
        _onlineInventory = _defensePlayer == null && (main.Accounts.SignedIn || main.Accounts.HasDirectFirebaseSession);
        AddChild(new ColorRect { Color = new(0.01f, 0.025f, 0.045f, 0.98f), LayoutMode = 1, AnchorRight = 1, AnchorBottom = 1 });
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
        var panel = new PanelContainer(); margin.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); panel.AddChild(box);
        var heading = _equipmentHeading = new HBoxContainer(); box.AddChild(heading);
        var title = Ui.Label(_defensePlayer == null ? "MI MAGO · EQUIPACIÓN" : "DEFENSA · EQUIPO DE PARTIDA", 25, Ui.Gold);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart; title.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(title);
        _profile = Ui.Label("", 16); _profile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _profile.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _profile.HorizontalAlignment = HorizontalAlignment.Right; heading.AddChild(_profile);
        var connection = _equipmentToolbar = new HBoxContainer(); connection.AddThemeConstantOverride("separation", 8); box.AddChild(connection);
        Action(connection, "Actualizar", () => _ = Refresh()).Visible = _defensePlayer == null;
        _status = Ui.Label("", 18, Ui.Gold); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_status);
        _salesHelp = Ui.Label("", 15, Ui.Muted);
        _salesHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_salesHelp);
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 24); box.AddChild(columns);
        var inventoryScroll = new ContentScrollContainer { Name = "EquipmentItemsScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.1f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        columns.AddChild(inventoryScroll);
        Ui.VisibleVerticalScroll(inventoryScroll);
        var inventory = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; inventory.AddThemeConstantOverride("separation", 6); inventoryScroll.AddChild(inventory);
        _salesHelp.Reparent(inventory);
        var selectors = new GridContainer { Columns = 2 }; _selectorsGrid = selectors;
        selectors.AddThemeConstantOverride("h_separation", 10);
        selectors.AddThemeConstantOverride("v_separation", 6); inventory.AddChild(selectors);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var current = slot;
            var inspect = Action(selectors, "", () => OpenItemPicker(current));
            inspect.Name = "EquipmentSlot" + slot;
            inspect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            inspect.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            inspect.TooltipText = "Elegir " + EquipmentCatalogue.SlotName(slot).ToLowerInvariant() + " en el inventario ordenado por calidad";
            _slots[(int)slot] = inspect;
        }
        _detailTitle = Ui.Label("", 21, Ui.Gold); inventory.AddChild(_detailTitle);
        _details = Ui.Table(3, "EquipmentDetailsTable"); inventory.AddChild(_details);
        _empty = Ui.Label("", 16, Ui.Muted); _empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; inventory.AddChild(_empty);
        _combatPreview = Ui.Label("", Ui.IsMobile ? 16 : 17, Ui.Gold);
        _combatPreview.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        inventory.AddChild(_combatPreview);
        if (_defensePlayer != null) BuildDefenseActions(inventory);
        var totalsScroll = new ContentScrollContainer { Name = "EquipmentTotalsScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        columns.AddChild(totalsScroll);
        Ui.VisibleVerticalScroll(totalsScroll);
        var summary = _summaryContent = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; summary.AddThemeConstantOverride("separation", 8); totalsScroll.AddChild(summary);
        var summaryHeading = _summaryHeading = new HBoxContainer(); summaryHeading.AddThemeConstantOverride("separation", 10); summary.AddChild(summaryHeading);
        var summaryTitle = _summaryTitle = Ui.Label("DESGLOSE Y TOTAL DEL EQUIPO", 22, Ui.Gold); summaryTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        summaryTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summaryTitle.TooltipText = "Suma = botas + gorro + pecho + arma + conjunto. Aplicado incluye los límites de combate. Valores respecto al mago base.";
        summaryHeading.AddChild(summaryTitle);
        var setsButton = Action(summaryHeading, "Conjuntos", () => _setsInfo.PopupCentered());
        setsButton.CustomMinimumSize = new(0, 48);
        _sets = Ui.Label("", 16, Ui.Gold); _sets.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sets.SizeFlagsHorizontal = SizeFlags.ExpandFill; summary.AddChild(_sets);
        _totals = Ui.Table(8, "EquipmentTotalsTable"); summary.AddChild(_totals);
        _totalsEmpty = Ui.Label("No hay bonificaciones activas en el equipo actual.", 16, Ui.Muted);
        _totalsEmpty.Name = "EquipmentNoActiveBonuses";
        _totalsEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.AddChild(_totalsEmpty);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 14); box.AddChild(footer);
        var hint = Ui.Label("Suma = botas + gorro + pecho + arma + conjunto. Aplicado = valor real en combate.", 14, Ui.Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; hint.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(hint);
        var close = Action(footer, _defensePlayer == null ? "Volver" : "Volver a la tienda", () => Close()); _actions.Remove(close);
        _confirm = new ConfirmationDialog { Title = "Vender pieza", OkButtonText = "Vender y recibir oro", CancelButtonText = "Cancelar" };
        AddChild(_confirm); _confirm.Confirmed += () => _ = Sell();
        _confirm.Canceled += () => _selling = "";
        _setsInfo = new AcceptDialog { Title = "Conjuntos Míticos de los ocho elementos", OkButtonText = "Cerrar" }; AddChild(_setsInfo);
        var setsScroll = new ContentScrollContainer { CustomMinimumSize = new(600, 300),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _setsInfo.AddChild(setsScroll);
        _setsScroll = setsScroll;
        Ui.VisibleVerticalScroll(setsScroll);
        var descriptions = Ui.Label(string.Join("\n\n", EquipmentCatalogue.FocusedFamilies.Select(f => EquipmentCatalogue.FamilyName(f) + "\n"
            + "2 o 3 piezas: " + SetDescription(f, false) + "\n4 piezas: " + SetDescription(f, true)))
            + "\n\nEl bono de cuatro sustituye al de dos. Se pueden combinar dos conjuntos de dos piezas.", 17);
        descriptions.AutowrapMode = TextServer.AutowrapMode.WordSmart; descriptions.SizeFlagsHorizontal = SizeFlags.ExpandFill; setsScroll.AddChild(descriptions);
        ApplyOptionAEquipment(margin, panel, box, columns, inventoryScroll, totalsScroll, footer);
        BuildItemPicker();
        GetViewport().SizeChanged += UpdateEquipmentResponsive;
        UpdateEquipmentResponsive();
        Render();
        if (_defensePlayer == null && _main.Accounts.SignedIn)
        {
            _ = Refresh();
            if (_main.Accounts.IsDirectFirebaseSession) _ = ConnectInventoryServer();
        }
    }

    private void UpdateEquipmentResponsive()
    {
        float viewportWidth = GetViewport().GetVisibleRect().Size.X;
        float available = Math.Max(260, viewportWidth - (Ui.IsMobile ? 65 : 110));
        bool compact = viewportWidth < 970;
        if (_optionAItemsTab != null) _optionAItemsTab.Text = viewportWidth < 700 ? "OBJETOS Y EQUIPO" : "◇  OBJETOS Y EQUIPACIÓN";
        if (_optionAStatsTab != null) _optionAStatsTab.Text = viewportWidth < 700 ? "SUMA DE BONOS" : "✧  SUMA DE BONIFICACIONES";
        foreach (var tab in new[] { _optionAItemsTab, _optionAStatsTab })
            if (tab != null)
            {
                tab.AutowrapMode = TextServer.AutowrapMode.Off;
                tab.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? viewportWidth < 700 ? 18 : 21 : 17);
            }
        var profileParent = compact ? _equipmentToolbar : _equipmentHeading;
        if (_profile.GetParent() != profileParent)
        {
            _profile.Reparent(profileParent);
            if (compact) profileParent.MoveChild(_profile, 0);
        }
        _profile.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _equipmentHeading.Visible = _defensePlayer != null || !compact;
        _equipmentToolbar.Visible = !_showingTotals;
        _status.Visible = _status.Text.Length > 0 && (!_showingTotals || !_routineStatus) &&
            (_defensePlayer == null || !_routineStatus || HasChanges);
        _summaryTitle.Visible = !compact;
        var setsParent = compact ? (Node)_summaryHeading : _summaryContent;
        if (_sets.GetParent() != setsParent)
        {
            _sets.Reparent(setsParent);
            setsParent.MoveChild(_sets, compact ? 0 : 1);
        }
        if (_selectorsGrid != null)
            _selectorsGrid.Columns = ResponsiveLayout.Columns(available, Ui.IsMobile ? 365 : 340, 2, 10);
        // Tabla completa en escritorio; fichas de dos columnas en pantallas estrechas.
        // Su contenido se regenera solo si cambia realmente el formato.
        int columns = compact ? 2 : 8;
        if (_totals != null && _totals.Columns != columns)
        {
            _totals.Columns = columns;
            if (_slots.All(slot => slot != null)) Render();
        }
        if (_setsScroll != null)
            _setsScroll.CustomMinimumSize = new Vector2(
                Math.Max(240, Math.Min(920, viewportWidth - (Ui.IsMobile ? 85 : 140))),
                Math.Max(180, Math.Min(480, GetViewport().GetVisibleRect().Size.Y - 180)));
        UpdatePickerColumns();
    }

    private Button Action(Node parent, string text, Action action, bool primary = false)
    {
        var button = Ui.Button(text, action, primary); button.CustomMinimumSize = new(0, Ui.IsMobile ? 68 : 48);
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
    private static Label Cell(GridContainer grid, string text, bool number = false, Color? color = null, bool header = false)
        => Ui.TableCell(grid, text, number, color, header);
    private EquipmentItem[] Choices(EquipmentSlot slot) => (_draft?.Items ?? [])
        .Where(i => i.Slot == slot).OrderByDescending(i => i.Tier).ThenBy(i => i.Family).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
    private EquipmentItem? Selected()
    {
        var account = _draft;
        return account?.Items.FirstOrDefault(i => i.Id == account.Loadout.Get(_selectedSlot));
    }
    internal void PressInspect(EquipmentSlot slot) => _slots[(int)slot].EmitSignal(Button.SignalName.Pressed);
    private void Render()
    {
        var account = _draft;
        _profile.Text = account == null ? "Sin cuenta" : $"{account.Name} · {account.Gold} oro{(_defensePlayer == null ? "" : " temporal")} · {account.Items.Length}/{(_defensePlayer == null ? EquipmentCatalogue.InventoryLimit : DefenseRules.InventoryLimit)} objetos";
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var view = _slots[(int)slot]; var choices = Choices(slot);
            int index = Array.FindIndex(choices, i => i.Id == account?.Loadout.Get(slot));
            var item = index >= 0 ? choices[index] : null;
            view.Text = EquipmentCatalogue.SlotName(slot) + " · " + (item == null ? "Sin equipo" : EquipmentCatalogue.TierName(item.Tier))
                + "\n" + (item == null ? "" : EquipmentCatalogue.FamilyName(item.Family) + " · ") + $"{choices.Length} objetos · Elegir";
            view.AddThemeColorOverride("font_color", slot == _selectedSlot ? Ui.Gold : item == null ? Ui.Muted : TierColor(item.Tier));
            view.Disabled = _busy;
        }
        RenderDetails();
        var breakdown = PublicEquipment.Prepare(account ?? new("", "", 0, 0, 0, new(), []));
        _totalsEmpty.Visible = !RenderTotals(_totals, breakdown.View.Stats, breakdown.View.Items);
        var m = breakdown.Modifiers;
        _combatPreview.Text = account == null
            ? "Los porcentajes reales aparecen aquí cuando equipas un objeto."
            : "PRUEBA DE EFECTO DEL EQUIPO EQUIPADO: daño recibido por un ataque de 100: " +
              (100 * m.Incoming).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
              " (sin Defensa serían 100). Escudo básico al crearlo: " +
              (420 * m.ShieldCapacity).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
              " puntos (sin Absorción serían 420). La Defensa reduce daño, no aumenta los puntos que muestra el escudo.";
        var activeSets = EquipmentCatalogue.FocusedFamilies.Where(f => m.Pieces(f) >= 2).ToArray();
        _sets.Text = activeSets.Length == 0 ? "Sin conjuntos activos." : "Conjuntos activos · " +
            string.Join(" · ", activeSets.Select(f => EquipmentCatalogue.FamilyName(f) + " " + m.Pieces(f) + "/4"));
        _sets.TooltipText = "Solo cuentan las piezas Míticas de la misma familia. Con 3 piezas sigue activo el bonus de 2. El bonus de 4 sustituye al de 2.\n"
            + string.Join("\n", activeSets.Select(f => Set(EquipmentCatalogue.FamilyName(f), m.Pieces(f), SetDescription(f, false), SetDescription(f, true))));
        _salesHelp.Text = _defensePlayer != null
            ? "Piezas y oro de esta partida. Los cambios se aplican al volver a la tienda."
            : _main.Accounts.IsDirectFirebaseSession && _main.Accounts.ServerAddress.Length == 0
            ? "Sin conexión con el servidor de ventas. El inventario y la equipación siguen disponibles."
            : "Las bonificaciones equipadas afectan al combate. Consulta «Ayuda → Bonificaciones» para ver cómo actúa cada atributo.";
        if (!_busy && _status.Text.Length == 0) SetStatus(account == null ? "Inicia sesión con Google para recuperar tu inventario y conseguir objetos en PvP." :
            "Pulsa una posición para elegir una pieza. La configuración se guarda al salir del inventario.", routine: true);
        if (_defensePlayer != null) RenderDefenseActions();
    }
    public override void _Process(double delta)
    {
        if (_lifetime.IsCancellationRequested || _defensePlayer != null || _draft?.Uid == _main.Accounts.Profile?.Uid) return;
        RebaseDraft(); _onlineInventory = _main.Accounts.SignedIn || _main.Accounts.HasDirectFirebaseSession;
        _selling = ""; CloseItemPicker(); Render();
        SetStatus("La cuenta ha cambiado. Se ha cargado su inventario sin conservar la selección anterior.");
    }
    private static string Set(string name, int pieces, string two, string four) => name + " " + pieces + "/4 · " + (pieces == 4 ? four : pieces >= 2 ? two : "sin bonificación");
    private void RenderDetails()
    {
        Clear(_details); var item = Selected();
        _detailTitle.Text = EquipmentCatalogue.SlotName(_selectedSlot) + " · ATRIBUTOS INDIVIDUALES";
        foreach (string heading in new[] { "Actual", "Límites (mín. / máx.)", "Atributo" }) Cell(_details, heading, heading != "Atributo", header: true);
        if (item != null) foreach (var bonus in item.Bonuses)
        {
            Cell(_details, EquipmentCatalogue.Percent(bonus.Current), true, bonus.Current < 0 ? Ui.Red : Ui.Blue);
            Cell(_details, EquipmentCatalogue.Percent(bonus.Minimum) + " / " + EquipmentCatalogue.Percent(bonus.Maximum), true, Ui.Muted);
            Cell(_details, StatName(bonus.Stat)).TooltipText = EquipmentCatalogue.StatDescription(bonus.Stat);
        }
        _empty.Text = item == null ? "Sin pieza equipada. Pulsa una posición para abrir sus objetos ordenados por calidad." :
            "Pulsa una posición para comparar sus objetos. Los cambios se guardan al salir del inventario.";
    }
    public static Color TierColor(ItemTier tier) => tier switch
    { ItemTier.Silver => new("c4cbd7"), ItemTier.Gold => new("e8c574"), ItemTier.Platinum => new("82dfd6"), ItemTier.Diamond => new("73bfff"), _ => new("cf9cff") };
    private void SetStatus(string message, bool routine = false)
    {
        _status.Text = message; _routineStatus = routine;
        _status.Visible = message.Length > 0 && (!_showingTotals || !routine) &&
            (_defensePlayer == null || !routine || HasChanges);
    }
    private void Busy(bool value, string message = "")
    {
        _busy = value; foreach (var button in _actions) button.Disabled = value;
        if (message.Length > 0) SetStatus(message);
        if (_itemPicker != null && _itemPicker.Visible && message.Length > 0)
        { _pickerStatus.Text = message; _pickerStatus.Visible = true; }
        UpdatePickerActions();
        if (_defensePlayer != null) RenderDefenseActions();
    }
    private async Task ConnectInventoryServer()
    {
        try
        {
            await _main.EnsureShopServer(_lifetime.Token).ConfigureAwait(false);
            _main.Post(() => { if (!_lifetime.IsCancellationRequested) Render(); });
        }
        catch (Exception) { /* La consulta del inventario no depende del servidor de ventas. */ }
    }

    private async Task Refresh()
    {
        if (_defensePlayer != null) return;
        if (_busy) return;
        if (!_main.Accounts.SignedIn) { Busy(false, "Inicia sesión con Google para actualizar el inventario de tu cuenta."); return; }
        Busy(true, "Cargando inventario…");
        try { await _main.Accounts.Refresh(_lifetime.Token); Done("Inventario actualizado; se ha recuperado tu equipo guardado."); }
        catch (Exception error) { Failed(error); }
    }
    private void ChooseItem(string id)
    {
        if (_busy || _draft == null) return;
        try
        {
            _draft = AccountInventory.Equip(_draft, new InventoryRequest(id, _selectedSlot));
            Render();
            RefreshPickerItems();
            string message = HasChanges ? "Cambios pendientes. Se guardarán al salir del inventario." : "La configuración coincide con el equipo guardado.";
            SetStatus(message, routine: true);
            if (PickerVisible) { _pickerStatus.Text = message; _pickerStatus.Visible = true; }
        }
        catch (Exception error) { SetStatus(error.Message); }
    }
    private void ConfirmSale(string id)
    {
        var item = _draft?.Items.FirstOrDefault(i => i.Id == id && i.Slot == _selectedSlot);
        if (_busy || item == null || !CanSellInventory) return;
        _selling = item.Id;
        _confirm.DialogText = "PIEZA QUE VAS A ELIMINAR:\n" + EquipmentCatalogue.ItemName(item) +
            $"\n\nORO QUE RECIBIRÁS: +{item.SaleGold}\n\n" + (_defensePlayer == null
                ? "La pieza desaparecerá definitivamente de tu inventario y dejará de estar equipada. Esta acción no se puede deshacer."
                : "La pieza se venderá por oro de esta partida y dejará de estar equipada.") + "\n\n¿Confirmas la venta?";
        var viewport = GetViewport().GetVisibleRect().Size;
        _confirm.PopupCentered(new((int)Math.Min(640, viewport.X - 32), (int)Math.Min(300, viewport.Y - 32)));
    }
    private async Task Sell()
    {
        if (_defensePlayer != null) { SellDefenseItem(); return; }
        if (_busy || _selling.Length == 0) return; Busy(true, "Eliminando objeto y abonando oro…");
        try { var result = await _main.Accounts.Sell(_selling, _lifetime.Token); Done(result.Message); }
        catch (Exception error) { Failed(error); }
        finally { _selling = ""; }
    }
    private void Done(string message) => _main.Post(() =>
    {
        bool saved = _main.SaveAccountCache();
        if (_lifetime.IsCancellationRequested) return;
        RebaseDraft();
        Render(); Busy(false, saved ? message : message + " No se pudo actualizar la copia del dispositivo.");
        RefreshPickerItems();
        ContinueRequestedClose();
    });
    private void Failed(Exception error) => _main.Post(() =>
    {
        if (_lifetime.IsCancellationRequested) return;
        Render(); Busy(false, error is OperationCanceledException ? "Operación cancelada." : error.Message);
        RefreshPickerItems();
        ContinueRequestedClose();
    });
    public override void _ExitTree() { GetViewport().SizeChanged -= UpdateEquipmentResponsive; _lifetime.Cancel(); _lifetime.Dispose(); }
}

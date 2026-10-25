using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel : Control
{
    private Main _main = null!;
    private Label _profile = null!, _detailTitle = null!, _empty = null!, _sets = null!, _status = null!;
    private Label _combatPreview = null!;
    private Label _totalsEmpty = null!;
    private Label _totalsTitle = null!;
    private VBoxContainer _totalsSummary = null!;
    private HBoxContainer _equipmentToolbar = null!;
    private MenuButton _inventoryMenu = null!;
    private bool _showingTotals, _routineStatus = true;
    private GridContainer _details = null!, _totals = null!;
    private ScrollContainer? _setsScroll;
    private Label _salesHelp = null!;
    private ConfirmationDialog _confirm = null!;
    private AcceptDialog _setsInfo = null!;
    private readonly Button[] _slots = new Button[Enum.GetValues<EquipmentSlot>().Length];
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
    private EquipmentAvatar _avatar = null!;
    private PanelContainer _avatarPane = null!;
    private Control _rightHost = null!;
    private AcceptDialog? _savedEquipmentDialog;

    public void Build(Main main, string address)
    {
        _main = main; Name = "EquipmentPanel"; Theme = Ui.Theme(); Ui.Full(this);
        _draft = _defensePlayer == null ? main.Accounts.Profile : DefenseProfile(_defensePlayer);
        SyncDefensePreferences();
        _savedLoadout = _draft?.Loadout ?? new();
        _onlineInventory = _defensePlayer == null && (main.Accounts.SignedIn || main.Accounts.HasDirectFirebaseSession);
        AddChild(new ColorRect { Color = ArcanaMenuChrome.Backdrop, LayoutMode = 1, AnchorRight = 1, AnchorBottom = 1 });
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 8 : 16);
        if (_defensePlayer == null)
            _optionAChrome = ArcanaMenuChrome.Attach(this, margin, main, "Inventario", 4, main.NavigateOptionAMenu);
        var panel = new PanelContainer(); var frame = Ui.Panel(new("0a1d33"), 10, ArcanaMenuChrome.Border);
        frame.ContentMarginLeft = frame.ContentMarginRight = 8; frame.ContentMarginTop = frame.ContentMarginBottom = 6; panel.AddThemeStyleboxOverride("panel", frame);
        margin.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6); panel.AddChild(box);
        if (_defensePlayer != null)
        {
            var exits = new HBoxContainer { Name = "EquipmentExitBar" }; box.AddChild(exits);
            var back = Ui.ExitButton("Volver a tienda", () => Close(), "EquipmentBackToShop");
            var resume = Ui.ExitButton("Continuar partida", () => Close(_resumeDefense), "EquipmentResumeGame");
            back.SizeFlagsHorizontal = resume.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            exits.AddChild(back); exits.AddChild(resume);
        }
        _profile = Ui.Label("", 14, Ui.Gold); _profile.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _profile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _equipmentToolbar = new HBoxContainer(); box.AddChild(_equipmentToolbar);
        _inventoryMenu = new MenuButton { Name = "EquipmentActions", Text = "Opciones", CustomMinimumSize = new(72, 44) };
        _inventoryMenu.AddThemeFontSizeOverride("font_size", 13); _equipmentToolbar.AddChild(_inventoryMenu);
        var menu = _inventoryMenu.GetPopup();
        menu.AddItem(_defensePlayer == null ? "Actualizar inventario" : "Comprar pieza · 100 oro", 0);
        menu.AddItem(_defensePlayer == null ? "Equipos guardados" : "Volver a la tienda", 1);
        menu.AddItem("Venta múltiple", 2);
        menu.AddItem("Bonificaciones de conjuntos", 3);
        menu.AddItem("Descartar cambios y salir", 4);
        menu.AddItem("Venta automática", 5);
        menu.IdPressed += id =>
        {
            if (_defensePlayer != null && id == 1) { Close(); return; }
            if (InventoryLocked) return;
            if (id == 5) { OpenAutoSellRules(); return; }
            if (id == 4) { ConfirmDiscard(); return; }
            if (id == 3) { _setsInfo.PopupCentered(); return; }
            if (id == 2) { ToggleBulkMode(); return; }
            if (id == 0) { if (_defensePlayer == null) _ = Refresh(); else BuyDefenseItem(); }
            else _savedEquipmentDialog?.PopupCentered(new((int)Math.Min(600, GetViewport().GetVisibleRect().Size.X - 32), 260));
        };
        if (_defensePlayer == null)
        {
            _savedEquipmentDialog = new AcceptDialog { Name = "EquipmentSavedTeamsDialog", Title = "Equipos guardados", OkButtonText = "Cerrar" };
            _optionAChrome!.ContentHost.AddChild(_savedEquipmentDialog); Ui.ConfigureDialog(_savedEquipmentDialog);
            var teams = new VBoxContainer { CustomMinimumSize = new(360, 0) }; _savedEquipmentDialog.AddChild(teams); BuildSavedEquipment(teams);
        }
        _status = Ui.Label("", 15, Ui.Gold); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.MaxLinesVisible = 1; _status.Visible = false; box.AddChild(_status);
        var columns = new HBoxContainer { Name = "EquipmentSplit", SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", Ui.IsMobile ? 8 : 16); box.AddChild(columns);
        _avatarPane = new PanelContainer { Name = "EquipmentAvatarPane", SizeFlagsVertical = SizeFlags.ExpandFill };
        var avatarStyle = Ui.Panel(new("0c1a2e"), 9, new("29475e")); avatarStyle.ContentMarginLeft = avatarStyle.ContentMarginRight = avatarStyle.ContentMarginTop = avatarStyle.ContentMarginBottom = 4;
        _avatarPane.AddThemeStyleboxOverride("panel", avatarStyle); columns.AddChild(_avatarPane);
        var left = new VBoxContainer(); left.AddThemeConstantOverride("separation", 4); _avatarPane.AddChild(left);
        left.AddChild(_profile);
        _avatar = new EquipmentAvatar(); left.AddChild(_avatar);
        _avatar.Build(_defenseWizard?.WizardType ?? main.InventoryWizardType, OpenItemPicker);
        foreach (var slot in Enum.GetValues<EquipmentSlot>()) _slots[(int)slot] = _avatar.Buttons[slot];
        _salesHelp = Ui.Label("Los favoritos y las piezas equipadas están protegidos de la venta.", 13, Ui.Muted);
        _salesHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart; left.AddChild(_salesHelp);
        var right = new PanelContainer { Name = "EquipmentItemsPane", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var itemsStyle = Ui.Panel(new("0d2036"), 9, new("29475e")); itemsStyle.ContentMarginLeft = itemsStyle.ContentMarginRight = itemsStyle.ContentMarginTop = itemsStyle.ContentMarginBottom = 8;
        right.AddThemeStyleboxOverride("panel", itemsStyle); columns.AddChild(right);
        var rightBox = new VBoxContainer(); rightBox.AddThemeConstantOverride("separation", 6); right.AddChild(rightBox);
        var tabs = new HBoxContainer { Name = "OptionAEquipmentTabs" }; rightBox.AddChild(tabs);
        _optionAItemsTab = Ui.Button("Objetos", () => OpenItemPicker(_selectedSlot)); _optionAItemsTab.Name = "EquipmentItemsTab";
        _optionAStatsTab = Ui.Button("Ver estadísticas", () => SwitchOptionAEquipment(true)); _optionAStatsTab.Name = "EquipmentTotalsTab";
        foreach (var tab in new[] { _optionAItemsTab, _optionAStatsTab }) { tab.SizeFlagsHorizontal = SizeFlags.ExpandFill; tab.CustomMinimumSize = new(0, 44); tabs.AddChild(tab); }
        _equipmentToolbar.Reparent(tabs);
        _rightHost = new Control { Name = "EquipmentRightContent", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
        rightBox.AddChild(_rightHost);
        var totalsScroll = new ContentScrollContainer { Name = "EquipmentTotalsScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        _rightHost.AddChild(totalsScroll); Ui.Full(totalsScroll); Ui.VisibleVerticalScroll(totalsScroll); _optionAStats = totalsScroll;
        var summary = _totalsSummary = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; summary.AddThemeConstantOverride("separation", 8); totalsScroll.AddChild(summary);
        _totalsTitle = Ui.Label("ESTADÍSTICAS TOTALES", 18, Ui.Gold); summary.AddChild(_totalsTitle);
        _sets = Ui.Label("", 14, Ui.Gold); _sets.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.AddChild(_sets);
        _totals = Ui.Table(2, "EquipmentTotalsTable"); summary.AddChild(_totals);
        _totalsEmpty = Ui.Label("No hay bonificaciones activas.", 16, Ui.Muted); _totalsEmpty.Name = "EquipmentNoActiveBonuses"; summary.AddChild(_totalsEmpty);
        _combatPreview = Ui.Label("", 15, Ui.Muted); _combatPreview.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.AddChild(_combatPreview);
        _detailTitle = Ui.Label("", 17, Ui.Gold); summary.AddChild(_detailTitle);
        _details = Ui.Table(3, "EquipmentDetailsTable"); summary.AddChild(_details);
        _empty = Ui.Label("", 14, Ui.Muted); _empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.AddChild(_empty);
        BuildSaleModal();
        _setsInfo = new AcceptDialog { Name = "EquipmentSets", Title = "Conjuntos Míticos", OkButtonText = "Cerrar" }; AddChild(_setsInfo); Ui.ConfigureDialog(_setsInfo);
        var setsScroll = new ContentScrollContainer { CustomMinimumSize = new(400, 240), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _setsInfo.AddChild(setsScroll); _setsScroll = setsScroll; Ui.VisibleVerticalScroll(setsScroll);
        var setsBody = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; setsScroll.AddChild(setsBody);
        var descriptions = Ui.Label(string.Join("\n\n", EquipmentCatalogue.FocusedFamilies.Select(f => EquipmentCatalogue.FamilyName(f) + "\n2 o 3 piezas: " + SetDescription(f, false) + "\n4 piezas: " + SetDescription(f, true))) + "\n\nLos anillos no cuentan para conjuntos.", 16);
        descriptions.AutowrapMode = TextServer.AutowrapMode.WordSmart; descriptions.SizeFlagsHorizontal = SizeFlags.ExpandFill; setsBody.AddChild(descriptions);
        BuildItemPicker();
        GetViewport().SizeChanged += UpdateEquipmentResponsive; UpdateEquipmentResponsive();
        Render(); OpenItemPicker(EquipmentSlot.Hat); Busy(false);
        if (_defensePlayer == null && _main.Accounts.SignedIn && !_main.InventoryAutoSelling)
        {
            _ = Refresh();
            if (_main.Accounts.IsDirectFirebaseSession) _ = ConnectInventoryServer();
        }
    }

    private void UpdateEquipmentResponsive()
    {
        float width = GetViewport().GetVisibleRect().Size.X;
        _avatarPane.CustomMinimumSize = new(Math.Clamp(width * .32f, 178, 430), 0);
        bool shortView = GetViewport().GetVisibleRect().Size.Y <= 500;
        _salesHelp.Visible = !shortView;
        _totalsTitle.Visible = !shortView;
        _sets.Visible = !shortView || _sets.Text != "Sin conjuntos activos.";
        _totalsSummary.AddThemeConstantOverride("separation", shortView ? 4 : 8);
        foreach (var button in _equipmentToolbar.GetChildren().OfType<Button>())
        { button.CustomMinimumSize = new(0, 40); button.AddThemeFontSizeOverride("font_size", width < 800 ? 13 : 15); }
        foreach (var tab in new[] { _optionAItemsTab, _optionAStatsTab })
            if (tab != null)
            {
                tab.AddThemeFontSizeOverride("font_size", width < 800 ? 14 : 17);
                foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
                {
                    var style = (StyleBoxFlat)tab.GetThemeStylebox(state).Duplicate();
                    style.ContentMarginLeft = style.ContentMarginRight = 8;
                    style.ContentMarginTop = style.ContentMarginBottom = 4;
                    tab.AddThemeStyleboxOverride(state, style);
                }
            }
        _status.Visible = _status.Text.Length > 0 && !_routineStatus;
        if (_setsScroll != null) _setsScroll.CustomMinimumSize = new(Math.Max(240, Math.Min(800, width - 100)), Math.Max(150, Math.Min(400, GetViewport().GetVisibleRect().Size.Y - 180)));
        UpdatePickerColumns(); if (_confirm.Visible) ResizeSaleModal();
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
        _profile.Text = account == null ? "Sin cuenta" : $"{account.Gold} oro{(_defensePlayer == null ? "" : " temporal")} · {account.Items.Length}/{(_defensePlayer == null ? EquipmentCatalogue.InventoryLimit : DefenseRules.InventoryLimit)} objetos";
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var view = _slots[(int)slot]; var choices = Choices(slot);
            int index = Array.FindIndex(choices, i => i.Id == account?.Loadout.Get(slot));
            var item = index >= 0 ? choices[index] : null;
            _avatar.UpdateSlot(slot, item, slot == _selectedSlot, item == null ? null : ItemLevel(item));
            view.Disabled = _busy;
        }
        RenderDetails();
        RenderSavedEquipment();
        var breakdown = PublicEquipment.Prepare(account ?? new("", "", 0, 0, 0, new(), []), _itemLevels);
        _totalsEmpty.Visible = !RenderTotals(_totals, breakdown.View.Stats, breakdown.View.Items);
        var m = breakdown.Modifiers;
        _combatPreview.Text = account == null
            ? "Los porcentajes reales aparecen aquí cuando equipas un objeto."
            : "PRUEBA DE EFECTO DEL EQUIPO EQUIPADO: daño recibido por un ataque de 100: " +
              (100 * m.Incoming).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
              " (sin Defensa serían 100). Escudo básico al crearlo: " +
              (420 * m.ShieldCapacity).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
              " puntos (sin Absorción serían 420). La Defensa reduce daño, no aumenta los puntos que muestra el escudo.";
        string specialEffects = ItemAffixes.Summary(m);
        if (specialEffects.Length > 0) _combatPreview.Text += "\n\nEFECTOS ESPECIALES ACTIVOS\n" + specialEffects +
            "\nLos efectos iguales usan el valor más alto; no se suman entre piezas.";
        var activeSets = EquipmentCatalogue.FocusedFamilies.Where(f => m.Pieces(f) >= 2).ToArray();
        _sets.Text = activeSets.Length == 0 ? "Sin conjuntos activos." : "Conjuntos activos · " +
            string.Join(" · ", activeSets.Select(f => EquipmentCatalogue.FamilyName(f) + " " + m.Pieces(f) + "/4"));
        _sets.Visible = activeSets.Length > 0 || GetViewport().GetVisibleRect().Size.Y > 500;
        _sets.TooltipText = "Solo cuentan las piezas Míticas de la misma familia. Con 3 piezas sigue activo el bonus de 2. El bonus de 4 sustituye al de 2.\n"
            + string.Join("\n", activeSets.Select(f => Set(EquipmentCatalogue.FamilyName(f), m.Pieces(f), SetDescription(f, false), SetDescription(f, true))));
        _salesHelp.Text = _main.InventoryAutoSelling && !TemporaryInventory ? "Terminando la venta automática. Puedes salir de esta pantalla." :
            "Los favoritos y las piezas equipadas están protegidos.";
        if (!_busy && _status.Text.Length == 0) SetStatus(account == null ? "Inicia sesión con Google para recuperar tu inventario y conseguir objetos en PvP." :
            "Pulsa una posición para elegir una pieza. La configuración se guarda al salir del inventario.", routine: true);
        if (_defensePlayer != null) RenderDefenseActions();
    }
    public override void _Process(double delta)
    {
        if (_lifetime.IsCancellationRequested || _defensePlayer != null || _draft?.Uid == _main.Accounts.Profile?.Uid) return;
        RebaseDraft(); _onlineInventory = _main.Accounts.SignedIn || _main.Accounts.HasDirectFirebaseSession;
        _saleSelection.Clear(); _bulkMode = false;
        CancelSale(); CloseItemPicker(); Render();
        SetStatus("La cuenta ha cambiado. Se ha cargado su inventario sin conservar la selección anterior.");
    }
    private static string Set(string name, int pieces, string two, string four) => name + " " + pieces + "/4 · " + (pieces == 4 ? four : pieces >= 2 ? two : "sin bonificación");
    private int ItemLevel(EquipmentItem item) => _itemLevels.GetValueOrDefault(item.Id, item.UpgradeLevel);
    private void RenderDetails()
    {
        Clear(_details); var item = Selected();
        _detailTitle.Text = EquipmentCatalogue.SlotName(_selectedSlot) + " · ATRIBUTOS INDIVIDUALES";
        foreach (string heading in new[] { "Actual", "Límites (mín. / máx.)", "Atributo" }) Cell(_details, heading, heading != "Atributo", header: true);
        if (item != null) foreach (var bonus in item.Bonuses)
        {
            bool active = EquipmentCatalogue.BonusActive(item.Slot, bonus.Stat);
            Cell(_details, active ? EquipmentCatalogue.Percent(bonus.Current) : "Inactivo", true, active ? bonus.Current < 0 ? Ui.Red : Ui.Blue : Ui.Muted);
            Cell(_details, EquipmentCatalogue.Percent(bonus.Minimum) + " / " + EquipmentCatalogue.Percent(bonus.Maximum), true, Ui.Muted);
            Cell(_details, StatName(bonus.Stat)).TooltipText = active ? EquipmentCatalogue.StatDescription(bonus.Stat)
                : EquipmentCatalogue.BonusDescription(item.Slot, bonus) + ". El anillo mágico solo aporta capacidad de escudo y su habilidad elemental.";
        }
        if (item?.Affix is { } affix)
        {
            Cell(_details, "Especial", true, Ui.Gold);
            Cell(_details, ItemAffixes.Active(affix, item.Slot) == null ? "Inactivo"
                : affix.Kind == ItemAffixKind.CoinDividend ? $"Nivel {ItemLevel(item)}/10" : "Fijo", true, Ui.Muted);
            Cell(_details, ItemAffixes.Describe(affix, ItemLevel(item), item.Slot));
        }
        if (item != null && RingEffects.IsRing(item.Slot))
        {
            Cell(_details, "Anillo", true, Ui.Gold);
            Cell(_details, $"Nivel {ItemLevel(item)}/10", true, Ui.Muted);
            Cell(_details, RingEffects.Describe(item, ItemLevel(item), _draft == null ? null : EquipmentModifiers.Calculate(_draft, _itemLevels)));
        }
        _empty.Text = item == null ? "Sin pieza equipada. Pulsa una posición para abrir sus objetos ordenados por calidad." :
            "Pulsa una posición para comparar sus objetos. Los cambios se guardan al salir del inventario.";
    }
    public static Color TierColor(ItemTier tier) => tier switch
    { ItemTier.Silver => new("c4cbd7"), ItemTier.Gold => new("e8c574"), ItemTier.Platinum => new("82dfd6"), ItemTier.Diamond => new("73bfff"), _ => new("cf9cff") };
    private void SetStatus(string message, bool routine = false)
    {
        _status.Text = message; _status.TooltipText = message; _routineStatus = routine;
        _status.Visible = message.Length > 0 && !routine;
    }
    private void Busy(bool value, string message = "")
    {
        _busy = value; foreach (var button in _actions) button.Disabled = InventoryLocked;
        if (message.Length > 0) SetStatus(message);
        UpdatePickerActions();
        if (_itemPicker != null && _itemPicker.Visible && message.Length > 0)
            SetPickerMessage(message);
        RenderSavedEquipment();
        if (_defensePlayer != null) RenderDefenseActions();
        else { _inventoryMenu.GetPopup().SetItemDisabled(0, InventoryLocked); _inventoryMenu.GetPopup().SetItemDisabled(1, InventoryLocked); }
    }
    private async Task ConnectInventoryServer()
    {
        try
        {
            await _main.EnsureShopServer(_lifetime.Token).ConfigureAwait(false);
            _main.Post(() =>
            {
                if (_lifetime.IsCancellationRequested) return;
                Render(); RefreshPickerItems(); UpdatePickerActions();
            });
        }
        catch (Exception) { /* La consulta del inventario no depende del servidor de ventas. */ }
    }

    private async Task Refresh()
    {
        if (_defensePlayer != null) return;
        if (InventoryLocked) return;
        if (!_main.Accounts.SignedIn) { Busy(false, "Inicia sesión con Google para actualizar el inventario de tu cuenta."); return; }
        Busy(true, "Cargando inventario…");
        try { await _main.Accounts.Refresh(_lifetime.Token); Done("Inventario actualizado; se ha recuperado tu equipo guardado."); }
        catch (Exception error) { Failed(error); }
    }
    private void ChooseItem(string id)
    {
        if (InventoryLocked || _draft == null) return;
        ResetQuickActions();
        try
        {
            _draft = AccountInventory.Equip(_draft, new InventoryRequest(id, _selectedSlot));
            Render();
            RefreshPickerItems();
            if (id.Length > 0 && PickerVisible) _pickerScroll.ScrollVertical = 0;
            string message = HasChanges ? "Cambios pendientes. Se guardarán al salir del inventario." : "La configuración coincide con el equipo guardado.";
            SetStatus(message, routine: true);
            if (PickerVisible) { _pickerStatus.Text = message; _pickerStatus.Visible = true; }
        }
        catch (Exception error) { SetStatus(error.Message); }
    }
    private async Task Sell()
    {
        if (InventoryLocked || _selling.Length == 0) return;
        if (_confirmingBatch) { await SellBatch(); return; }
        if (SaleProtected(_selling) || !CanSellInventory || _saleOwner != _draft?.Uid ||
            (_defensePlayer == null && _saleOwner != _main.Accounts.Profile?.Uid)) { CancelSale(); return; }
        _confirm.Hide();
        if (_defensePlayer != null) { SellDefenseItem(); return; }
        string itemId = _selling, owner = _saleOwner;
        Busy(true, "Eliminando objeto y abonando oro…");
        try { await PreparePermanentSale(itemId); var result = await _main.Accounts.Sell(itemId, _lifetime.Token); Done(result.Message); }
        catch (Exception error)
        {
            if (!_main.Accounts.InventoryNeedsRefresh) Failed(error);
            else if (await RecoverSaleState(owner))
                Done(_main.Accounts.Profile!.Items.Any(i => i.Id == itemId)
                    ? "Inventario sincronizado. La pieza sigue disponible; puedes reintentar la venta."
                    : "Venta confirmada al sincronizar. El objeto y el oro están actualizados.");
            else Failed(new InvalidOperationException("Venta sin confirmar. Actualiza el inventario antes de volver a vender.", error));
        }
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
        RebaseDraft(); Render(); Busy(false, error is OperationCanceledException ? "Operación cancelada." : error.Message);
        RefreshPickerItems();
        ContinueRequestedClose();
    });
    public override void _ExitTree() { GetViewport().SizeChanged -= UpdateEquipmentResponsive; _lifetime.Cancel(); _lifetime.Dispose(); }
}

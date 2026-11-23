using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel : Control
{
    private Main _main = null!;
    private ElementTextLabel _profile = null!, _detailTitle = null!, _empty = null!, _sets = null!, _status = null!;
    private ElementTextLabel _combatPreview = null!;
    private ElementTextLabel _totalsEmpty = null!;
    private ElementTextLabel _totalsTitle = null!;
    private VBoxContainer _totalsSummary = null!;
    private HBoxContainer _equipmentToolbar = null!;
    private MenuButton _inventoryMenu = null!;
    private bool _showingTotals, _routineStatus = true;
    private GridContainer _details = null!, _totals = null!;
    private ScrollContainer? _setsScroll;
    private ElementTextLabel _salesHelp = null!;
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
    internal string Details => string.Join("\n", _details.GetChildren().Concat(_detailEffects.GetChildren()).OfType<ElementTextLabel>().Select(l => l.Text));
    internal string Totals => string.Join("\n", _totals.GetChildren().OfType<ElementTextLabel>().Select(l => l.Text));
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
            var back = Ui.ExitButton("Atrás", () => Close(), "EquipmentBackToShop");
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
        Ui.EnableTouchScrollPopup(_inventoryMenu, menu);
        if (!BotInventory)
        {
            menu.AddItem(_defensePlayer == null ? "Actualizar inventario" : $"Comprar pieza · {DefenseRules.SupplyCost} oro", 0);
            menu.AddItem(_defensePlayer == null ? "Equipos guardados" : "Atrás · menú de supervivencia", 1);
        }
        menu.AddItem("Venta múltiple", 2);
        menu.AddItem("Bonificaciones de conjuntos", 3);
        if (!BotInventory)
        {
            menu.AddItem("Descartar cambios y salir", 4);
            menu.AddItem("Venta automática", 5);
        }
        menu.IdPressed += id =>
        {
            if (BotInventory && id is not (2 or 3)) return;
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
        _progressStatsTitle = Ui.Label("MEJORAS DE PARTIDA · NIVEL, RELIQUIAS Y PODERES", 17, Ui.Gold);
        _progressStatsTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.AddChild(_progressStatsTitle);
        _progressStats = Ui.Table(2, "EquipmentProgressStatsTable"); summary.AddChild(_progressStats);
        _combatPreview = Ui.Label("", 15, Ui.Muted); _combatPreview.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.AddChild(_combatPreview);
        _detailTitle = Ui.Label("", 17, Ui.Gold); _detailTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.AddChild(_detailTitle);
        _details = Ui.Table(2, "EquipmentDetailsTable"); summary.AddChild(_details);
        _detailEffects = new VBoxContainer { Name = "EquipmentDetailEffects" }; summary.AddChild(_detailEffects);
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
    internal static string StatName(EquipmentStat stat) => EquipmentCatalogue.StatName(stat);
    internal static string SetDescription(EquipmentFamily family, bool four) => string.Join(", ", EquipmentCatalogue.SetBonuses(family)
        .Where(b => (four ? b.FourPieces : b.TwoPieces) != 0)
        .Select(b => StatName(b.Stat) + " " + EquipmentCatalogue.Percent(four ? b.FourPieces : b.TwoPieces)));
    private static void Clear(Node parent)
    { foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); } }
    private static ElementTextLabel Cell(GridContainer grid, string text, bool number = false, Color? color = null, bool header = false)
        => Ui.TableCell(grid, text, number, color, header);
    private EquipmentItem[] Choices(EquipmentSlot slot) => (_draft?.Items ?? [])
        .Where(i => i.Slot == slot).OrderByDescending(i => i.Tier).ThenBy(i => i.Family).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
    private EquipmentItem? Selected()
    {
        var account = _draft;
        return account?.Items.FirstOrDefault(i => i.Id == account.Loadout.Get(_selectedSlot));
    }
    internal void PressInspect(EquipmentSlot slot) => _slots[(int)slot].EmitSignal(Button.SignalName.Pressed);
}

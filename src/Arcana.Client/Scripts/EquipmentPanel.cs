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
        _automaticServer = PublicAccountServer.NeedsDiscovery(address, Ui.IsMobile);
        _main = main; Name = "EquipmentPanel"; Theme = Ui.Theme(); Ui.Full(this);
        AddChild(new ColorRect { Color = new(0.01f, 0.025f, 0.045f, 0.98f), LayoutMode = 1, AnchorRight = 1, AnchorBottom = 1 });
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
        var panel = new PanelContainer(); margin.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); panel.AddChild(box);
        var heading = _equipmentHeading = new HBoxContainer(); box.AddChild(heading);
        var title = Ui.Label("MI MAGO · EQUIPACIÓN", 25, Ui.Gold);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart; title.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(title);
        _profile = Ui.Label("", 16); _profile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _profile.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _profile.HorizontalAlignment = HorizontalAlignment.Right; heading.AddChild(_profile);
        var connection = _equipmentToolbar = new HBoxContainer(); connection.AddThemeConstantOverride("separation", 8); box.AddChild(connection);
        var operationsConfig = new ConfigFile();
        if (operationsConfig.Load("user://protected-account-service.cfg") == Error.Ok)
        {
            try { _main.Accounts.SetProtectedActionsServer(operationsConfig.GetValue("operations", "url", "").AsString()); }
            catch (ArgumentException) { /* El usuario puede corregir la dirección en esta pantalla. */ }
        }
        _address = new LineEdit { Text = _main.Accounts.ServerAddress, PlaceholderText = "https://servidor-seguro-para-ventas",
            CustomMinimumSize = new(0, 48), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _address.AddThemeFontSizeOverride("font_size", 18); connection.AddChild(_address);
        _address.Visible = false;
        Button configSales = null!;
        configSales = Action(connection, "Configurar ventas", () =>
        {
            if (!_address.Visible)
            {
                _address.Visible = true;
                configSales.Text = "Guardar configuración";
                SetStatus("Las ventas requieren un servicio HTTPS autorizado para intercambiar objetos por oro. El inventario y el equipamiento no dependen de él.");
            }
            else
            {
                ConfigureOperations();
                _address.Visible = false;
                configSales.Text = "Configurar ventas";
            }
        });
        configSales.Visible = _main.Accounts.IsDirectFirebaseSession;
        Action(connection, "Actualizar", () => _ = Refresh());
        _status = Ui.Label("", 18, Ui.Gold); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_status);
        _salesHelp = Ui.Label("", 15, Ui.Muted);
        _salesHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_salesHelp);
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 24); box.AddChild(columns);
        var inventoryScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
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
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 4); selectors.AddChild(row);
            var previous = Action(row, "‹", () => Cycle(current, -1)); previous.CustomMinimumSize = new(Ui.IsMobile ? 78 : 54, Ui.IsMobile ? 72 : 54);
            previous.TooltipText = "Pieza anterior: " + EquipmentCatalogue.SlotName(slot);
            var inspect = Action(row, "", () => SelectSlot(current)); inspect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            inspect.TooltipText = "Ver los atributos individuales de " + EquipmentCatalogue.SlotName(slot).ToLowerInvariant();
            var next = Action(row, "›", () => Cycle(current, 1)); next.CustomMinimumSize = new(Ui.IsMobile ? 78 : 54, Ui.IsMobile ? 72 : 54);
            next.TooltipText = "Pieza siguiente: " + EquipmentCatalogue.SlotName(slot);
            _slots[(int)slot] = new(previous, inspect, next);
        }
        _detailTitle = Ui.Label("", 21, Ui.Gold); inventory.AddChild(_detailTitle);
        _details = Ui.Table(3, "EquipmentDetailsTable"); inventory.AddChild(_details);
        _empty = Ui.Label("", 16, Ui.Muted); _empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; inventory.AddChild(_empty);
        _combatPreview = Ui.Label("", Ui.IsMobile ? 16 : 17, Ui.Gold);
        _combatPreview.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        inventory.AddChild(_combatPreview);
        _sell = Action(inventory, "Eliminar pieza por oro", ConfirmSale);
        var totalsScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        columns.AddChild(totalsScroll);
        Ui.VisibleVerticalScroll(totalsScroll);
        var summary = _summaryContent = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; summary.AddThemeConstantOverride("separation", 8); totalsScroll.AddChild(summary);
        var summaryHeading = _summaryHeading = new HBoxContainer(); summaryHeading.AddThemeConstantOverride("separation", 10); summary.AddChild(summaryHeading);
        var summaryTitle = _summaryTitle = Ui.Label("SUMA DE TODO EL EQUIPO", 22, Ui.Gold); summaryTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        summaryTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summaryTitle.TooltipText = "Suma = piezas + conjunto. Aplicado incluye los límites de combate. Valores respecto al mago base.";
        summaryHeading.AddChild(summaryTitle);
        var setsButton = Action(summaryHeading, "Conjuntos", () => _setsInfo.PopupCentered());
        setsButton.CustomMinimumSize = new(0, 48);
        _sets = Ui.Label("", 16, Ui.Gold); _sets.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sets.SizeFlagsHorizontal = SizeFlags.ExpandFill; summary.AddChild(_sets);
        _totals = Ui.Table(5, "EquipmentTotalsTable"); summary.AddChild(_totals);
        _totalsEmpty = Ui.Label("No hay bonificaciones activas en el equipo actual.", 16, Ui.Muted);
        _totalsEmpty.Name = "EquipmentNoActiveBonuses";
        _totalsEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.AddChild(_totalsEmpty);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 14); box.AddChild(footer);
        var hint = Ui.Label("Suma = piezas + conjunto. Aplicado = valor real en combate.", 14, Ui.Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; hint.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(hint);
        var close = Action(footer, "Volver", () => { _lifetime.Cancel(); QueueFree(); }); _actions.Remove(close);
        _confirm = new ConfirmationDialog { Title = "Eliminar objeto", OkButtonText = "Eliminar y recibir oro", CancelButtonText = "Cancelar" };
        AddChild(_confirm); _confirm.Confirmed += () => _ = Sell();
        _setsInfo = new AcceptDialog { Title = "Conjuntos Míticos de los ocho elementos", OkButtonText = "Cerrar" }; AddChild(_setsInfo);
        var setsScroll = new ScrollContainer { CustomMinimumSize = new(600, 300),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _setsInfo.AddChild(setsScroll);
        _setsScroll = setsScroll;
        Ui.VisibleVerticalScroll(setsScroll);
        var descriptions = Ui.Label(string.Join("\n\n", EquipmentCatalogue.FocusedFamilies.Select(f => EquipmentCatalogue.FamilyName(f) + "\n"
            + "2 o 3 piezas: " + SetDescription(f, false) + "\n4 piezas: " + SetDescription(f, true)))
            + "\n\nEl bono de cuatro sustituye al de dos. Se pueden combinar dos conjuntos de dos piezas.", 17);
        descriptions.AutowrapMode = TextServer.AutowrapMode.WordSmart; descriptions.SizeFlagsHorizontal = SizeFlags.ExpandFill; setsScroll.AddChild(descriptions);
        ApplyOptionAEquipment(margin, panel, box, columns, inventoryScroll, totalsScroll, footer);
        GetViewport().SizeChanged += UpdateEquipmentResponsive;
        UpdateEquipmentResponsive();
        Render();
        if (_main.Accounts.SignedIn) _ = Refresh();
    }

    private void UpdateEquipmentResponsive()
    {
        float viewportWidth = GetViewport().GetVisibleRect().Size.X;
        float available = Math.Max(260, viewportWidth - (Ui.IsMobile ? 65 : 110));
        bool compact = viewportWidth < 970;
        var profileParent = compact ? _equipmentToolbar : _equipmentHeading;
        if (_profile.GetParent() != profileParent)
        {
            _profile.Reparent(profileParent);
            if (compact) profileParent.MoveChild(_profile, 0);
        }
        _profile.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _equipmentHeading.Visible = !compact;
        _equipmentToolbar.Visible = !_showingTotals;
        _status.Visible = _status.Text.Length > 0 && (!_showingTotals || !_routineStatus);
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
        int columns = compact ? 2 : 5;
        if (_totals != null && _totals.Columns != columns)
        {
            _totals.Columns = columns;
            if (_slots.All(slot => slot != null)) Render();
        }
        if (_setsScroll != null)
            _setsScroll.CustomMinimumSize = new Vector2(
                Math.Max(240, Math.Min(920, viewportWidth - (Ui.IsMobile ? 85 : 140))),
                Math.Max(180, Math.Min(480, GetViewport().GetVisibleRect().Size.Y - 180)));
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
        var breakdown = EquipmentModifiers.Summarize(account ?? new("", "", 0, 0, 0, new(), []));
        _totalsEmpty.Visible = !RenderTotals(_totals, breakdown.Stats);
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
        _salesHelp.Text = _main.Accounts.IsDirectFirebaseSession && _main.Accounts.ServerAddress.Length == 0
            ? "Venta de objetos por oro: desactivada hasta configurar un servicio seguro. Equipar y consultar el inventario funcionan sin él."
            : "Las bonificaciones equipadas afectan al combate. Consulta «Ayuda → Bonificaciones» para ver cómo actúa cada atributo.";
        if (!_busy && _status.Text.Length == 0) SetStatus(account == null ? "Inicia sesión con Google para recuperar tu inventario y conseguir objetos en PvP." :
            _main.Accounts.SignedIn ? "Elige con las flechas: se guarda automáticamente en tu cuenta para PvP y contra IA." : "Elige con las flechas: se guarda en este dispositivo para jugar contra IA.", routine: true);
    }
    internal static bool RenderTotals(GridContainer table, IEnumerable<EquipmentStatTotal> stats)
    {
        Clear(table);
        var active = stats.Where(s => Math.Abs(s.Effective) > 0.00001f).ToArray();
        table.Visible = active.Length > 0;
        if (!table.Visible) return false;
        bool compactStats = table.Columns == 2;
        if (compactStats)
        {
            Cell(table, "Atributo", header: true);
            Cell(table, "Aplicado en combate", true, header: true);
        }
        else foreach (string heading in new[] { "Atributo", "Piezas", "Conjunto", "Suma", "Aplicado" })
            Cell(table, heading, heading != "Atributo", header: true);
        foreach (var stat in active)
        {
            Cell(table, StatName(stat.Stat)).TooltipText = EquipmentCatalogue.StatDescription(stat.Stat);
            if (!compactStats)
            {
                Cell(table, EquipmentCatalogue.Percent(stat.Items), true);
                Cell(table, EquipmentCatalogue.Percent(stat.Set), true, stat.Set > 0 ? Ui.Gold : Ui.Muted);
                Cell(table, EquipmentCatalogue.Percent(stat.Sum), true);
            }
            string appliedText = EquipmentCatalogue.Percent(stat.Effective);
            if (compactStats) appliedText += "\nPiezas: " + EquipmentCatalogue.Percent(stat.Items) +
                "\nConjunto: " + EquipmentCatalogue.Percent(stat.Set) + "\nSuma: " + EquipmentCatalogue.Percent(stat.Sum);
            var applied = Cell(table, appliedText, true,
                stat.Limited ? Ui.Gold : stat.Effective < 0 ? Ui.Red : Ui.Blue);
            applied.TooltipText = EquipmentCatalogue.StatDescription(stat.Stat) +
                (stat.Limited ? "\nLa suma está limitada: se aplica el valor de esta columna." : "");
        }
        return true;
    }
    private static string Set(string name, int pieces, string two, string four) => name + " " + pieces + "/4 · " + (pieces == 4 ? four : pieces >= 2 ? two : "sin bonificación");
    private void RenderDetails()
    {
        Clear(_details); var item = Selected();
        _detailTitle.Text = EquipmentCatalogue.SlotName(_selectedSlot) + " · ATRIBUTOS INDIVIDUALES";
        foreach (string heading in new[] { "Atributo", "Actual", "Límites (mín. / máx.)" }) Cell(_details, heading, heading != "Atributo", header: true);
        if (item != null) foreach (var bonus in item.Bonuses)
        {
            Cell(_details, StatName(bonus.Stat)).TooltipText = EquipmentCatalogue.StatDescription(bonus.Stat);
            Cell(_details, EquipmentCatalogue.Percent(bonus.Current), true, bonus.Current < 0 ? Ui.Red : Ui.Blue);
            Cell(_details, EquipmentCatalogue.Percent(bonus.Minimum) + " / " + EquipmentCatalogue.Percent(bonus.Maximum), true, Ui.Muted);
        }
        _empty.Text = item == null ? "Sin pieza equipada. Usa las flechas para recorrer los objetos de esta posición o pulsa otra pieza para ver sus atributos." :
            "Pulsa otra pieza para ver sus atributos. El oro se guarda para futuras mejoras.";
        _sell.Text = item == null ? "Eliminar pieza por oro" : $"Eliminar esta pieza · +{item.SaleGold} oro";
        _sell.Disabled = _busy || item == null || !_main.Accounts.SignedIn || (_main.Accounts.IsDirectFirebaseSession && _main.Accounts.ServerAddress.Length == 0);
    }
    public static Color TierColor(ItemTier tier) => tier switch
    { ItemTier.Silver => new("c4cbd7"), ItemTier.Gold => new("e8c574"), ItemTier.Platinum => new("82dfd6"), ItemTier.Diamond => new("73bfff"), _ => new("cf9cff") };
    private void SetStatus(string message, bool routine = false)
    {
        _status.Text = message; _routineStatus = routine;
        _status.Visible = message.Length > 0 && (!_showingTotals || !routine);
    }
    private void Busy(bool value, string message = "")
    {
        _busy = value; foreach (var button in _actions) button.Disabled = value;
        _address.Editable = !value; if (message.Length > 0) SetStatus(message);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var view = _slots[(int)slot]; view.Previous.Disabled = view.Next.Disabled = value || Choices(slot).Length == 0;
        }
        _sell.Disabled = value || Selected() == null || !_main.Accounts.SignedIn || (_main.Accounts.IsDirectFirebaseSession && _main.Accounts.ServerAddress.Length == 0);
    }
    private void ConfigureOperations()
    {
        try
        {
            _main.Accounts.SetProtectedActionsServer(_address.Text.Trim());
            var file = new ConfigFile();
            file.SetValue("operations", "url", _main.Accounts.ServerAddress);
            if (file.Save("user://protected-account-service.cfg") != Error.Ok)
                throw new InvalidOperationException("No se pudo guardar el servidor de operaciones.");
            SetStatus(_main.Accounts.ServerAddress.Length > 0
                ? "Servidor de operaciones protegido configurado. El login y las lecturas siguen siendo directos a Firebase."
                : "Las ventas requieren el servidor de operaciones; Google y equipación funcionan sin él.");
            Render();
        }
        catch (Exception error) { SetStatus(error.Message); }
    }

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
        var item = Selected(); if (_busy || item == null || !_main.Accounts.SignedIn || (_main.Accounts.IsDirectFirebaseSession && _main.Accounts.ServerAddress.Length == 0)) return;
        _selling = item.Id;
        _confirm.DialogText = "PIEZA QUE VAS A ELIMINAR:\n" + EquipmentCatalogue.ItemName(item) +
            $"\n\nORO QUE RECIBIRÁS: +{item.SaleGold}\n\nLa pieza desaparecerá definitivamente de tu inventario y dejará de estar equipada. Esta acción no se puede deshacer.\n\n¿Confirmas la venta?";
        _confirm.PopupCentered(new(640, 220));
    }
    private async Task Sell()
    {
        if (_busy || _selling.Length == 0) return; Busy(true, "Eliminando objeto y abonando oro…");
        try { var result = await _main.Accounts.Sell(_selling, _lifetime.Token); Done(result.Message); }
        catch (Exception error) { Failed(error); }
        finally { _selling = ""; }
    }
    private void Done(string message) => _main.Post(() =>
    {
        bool saved = _main.SaveAccountCache();
        if (_lifetime.IsCancellationRequested) return;
        Render(); Busy(false, saved ? message : message + " No se pudo actualizar la copia del dispositivo.");
    });
    private void Failed(Exception error) => _main.Post(() =>
    { if (_lifetime.IsCancellationRequested) return; Render(); Busy(false, error is OperationCanceledException ? "Operación cancelada." : error.Message); });
    public override void _ExitTree() { GetViewport().SizeChanged -= UpdateEquipmentResponsive; _lifetime.Cancel(); _lifetime.Dispose(); }
}

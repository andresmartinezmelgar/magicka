using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class PlayerEquipmentPanel : Control
{
    private Main _main = null!;
    private Func<WizardSnapshot[]> _players = null!;
    private Func<int, CancellationToken, Task<EquipmentInspection>> _load = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private OptionButton _choice = null!;
    private GridContainer? _headerGrid, _contentGrid;
    private VBoxContainer _pieces = null!, _summary = null!;
    private Label _status = null!, _name = null!;
    private EquipmentInspection? _view;
    private bool _compactStats;
    private int _selected;
    public event Action? Closed;
    internal string Details => string.Join("\n", Labels(_pieces).Select(l => l.Text));
    internal string Totals => string.Join("\n", Labels(_summary).Select(l => l.Text));
    internal int SelectedPlayer => _selected;
    private static IEnumerable<Label> Labels(Node parent) => parent.GetChildren().SelectMany(n => n is Label label ? new[] { label } : Labels(n));

    public void Build(Main main, Func<WizardSnapshot[]> players, int localId, Func<int, CancellationToken, Task<EquipmentInspection>> load)
    {
        _main = main; _players = players; _load = load; _selected = localId;
        Name = "PlayerEquipmentPanel"; Theme = Ui.Theme(); Ui.Full(this);
        AddChild(new ColorRect { Color = new(0.01f, 0.025f, 0.045f, 0.98f), LayoutMode = 1, AnchorRight = 1, AnchorBottom = 1 });
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
        var panel = new PanelContainer(); margin.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); panel.AddChild(box);
        box.AddChild(Ui.Label("BONOS DE LOS MAGOS", 25, Ui.Gold));
        var header = new GridContainer { Columns = 3 }; _headerGrid = header;
        header.AddThemeConstantOverride("h_separation", 12); box.AddChild(header);
        _choice = new OptionButton { Name = "InspectedPlayerChoice", CustomMinimumSize = new(240, Ui.IsMobile ? 66 : 48), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _choice.AddThemeFontSizeOverride("font_size", 20); header.AddChild(_choice);
        _choice.ItemSelected += index => { _selected = _choice.GetItemId((int)index); _ = Load(); };
        header.AddChild(Ui.Button("Actualizar", Refresh)); header.AddChild(Ui.Button("Volver", Close));
        _name = Ui.Label("", 20, Ui.Gold); box.AddChild(_name);
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var columns = new GridContainer { Columns = 2, SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill }; _contentGrid = columns;
        columns.AddThemeConstantOverride("h_separation", 24); columns.AddThemeConstantOverride("v_separation", 8); box.AddChild(columns);
        _pieces = Column(columns); _summary = Column(columns);
        _status = Ui.Label("", 16, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_status);
        GetViewport().SizeChanged += UpdateResponsivePlayerEquipment;
        UpdateResponsivePlayerEquipment();
        Refresh();
    }
    private void UpdateResponsivePlayerEquipment()
    {
        float width = GetViewport().GetVisibleRect().Size.X;
        if (_headerGrid != null) _headerGrid.Columns = ResponsiveLayout.Columns(Math.Max(250, width - 85), 250, 3, 12);
        if (_contentGrid != null) _contentGrid.Columns = ResponsiveLayout.Columns(Math.Max(250, width - 100), 480, 2, 24);
        int columns = _contentGrid?.Columns ?? 1;
        float summaryWidth = (width - 100 - 24 * (columns - 1)) / columns;
        bool compact = summaryWidth < 900;
        if (_compactStats != compact)
        {
            _compactStats = compact;
            if (_view != null) Render(_view);
        }
    }

    private static VBoxContainer Column(Node parent)
    {
        var scroll = new ContentScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; parent.AddChild(scroll);
        Ui.VisibleVerticalScroll(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", 8); scroll.AddChild(content);
        return content;
    }
    private static void Clear(Node parent)
    { foreach (var node in parent.GetChildren()) { parent.RemoveChild(node); node.QueueFree(); } }
    private void Refresh()
    {
        _choice.Clear(); var players = _players();
        foreach (var player in players.OrderBy(p => p.Team).ThenBy(p => p.Id))
            _choice.AddItem(player.Name + (player.Bot ? " · IA" : "") + (player.Team == 0 ? " · Azul" : " · Rojo"), player.Id);
        int selected = _choice.GetItemIndex(_selected);
        if (selected < 0 && _choice.ItemCount > 0) { selected = 0; _selected = _choice.GetItemId(0); }
        if (selected >= 0) { _choice.Select(selected); _ = Load(); }
        else { _view = null; Clear(_pieces); Clear(_summary); _name.Text = ""; _status.Text = "No quedan magos en esta sala."; }
    }
    internal void SelectPlayer(int id)
    {
        int index = _choice.GetItemIndex(id); if (index < 0) throw new ArgumentException("Mago desconocido.");
        _choice.Select(index); _choice.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }
    private async Task Load()
    {
        _request?.Cancel(); _request?.Dispose();
        var request = _request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        int id = _selected; _view = null; Clear(_pieces); Clear(_summary); _name.Text = ""; _status.Text = "Consultando el equipo de esta partida…";
        try
        {
            var view = await _load(id, request.Token).ConfigureAwait(false); PublicEquipment.Check(view);
            _main.Post(() => { if (!request.IsCancellationRequested && !_lifetime.IsCancellationRequested) Render(view); });
        }
        catch (Exception error)
        {
            _main.Post(() =>
            {
                if (request.IsCancellationRequested || _lifetime.IsCancellationRequested) return;
                _status.Text = error is OperationCanceledException ? "La consulta ha tardado demasiado. Pulsa Actualizar." : error.Message;
            });
        }
    }
    private void Render(EquipmentInspection view)
    {
        _view = view;
        Clear(_pieces); Clear(_summary);
        _name.Text = view.Name + " · " + WizardTypes.Name(view.WizardType) + (view.Bot ? " · IA" : "");
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var item = view.Equipment.Items.FirstOrDefault(i => i.Slot == slot);
            _pieces.AddChild(Ui.Label(EquipmentCatalogue.SlotName(slot) + " · " + (item == null ? "Sin equipo" : EquipmentCatalogue.TierName(item.Tier)
                + " · " + EquipmentCatalogue.FamilyName(item.Family)), 20, item == null ? Ui.Muted : EquipmentPanel.TierColor(item.Tier)));
            if (item == null) continue;
            var grid = Ui.Table(2, "PlayerPiece" + slot + "Table"); _pieces.AddChild(grid);
            Ui.TableCell(grid, "Atributo", header: true); Ui.TableCell(grid, "Actual", true, header: true);
            foreach (var bonus in item.Bonuses)
            { Ui.TableCell(grid, EquipmentPanel.StatName(bonus.Stat)); Ui.TableCell(grid, EquipmentCatalogue.Percent(bonus.Current), true, bonus.Current < 0 ? Ui.Red : Ui.Blue); }
        }
        _summary.AddChild(Ui.Label("TOTAL DEL EQUIPO", 22, Ui.Gold));
        var activeSets = view.Equipment.Sets.Where(s => s.Pieces >= 2).ToArray();
        string sets = activeSets.Length == 0 ? "Sin conjuntos activos." : string.Join("\n", activeSets.Select(s =>
            EquipmentCatalogue.FamilyName(s.Family) + " " + s.Pieces + "/4 · " + EquipmentPanel.SetDescription(s.Family, s.Pieces == 4)));
        var setLabel = Ui.Label(sets, 16, Ui.Gold); setLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; _summary.AddChild(setLabel);
        var totals = Ui.Table(_compactStats ? 2 : 8, "PlayerEquipmentTotalsTable"); _summary.AddChild(totals);
        if (!EquipmentPanel.RenderTotals(totals, view.Equipment.Stats, view.Equipment.Items))
        {
            var empty = Ui.Label("No hay bonificaciones activas en el equipo actual.", 16, Ui.Muted);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; _summary.AddChild(empty);
        }
        _status.Text = "Equipo de la ronda actual. Aplicado muestra el bono real después de los límites. La partida continúa mientras consultas.";
    }
    public void Close() { _lifetime.Cancel(); Closed?.Invoke(); QueueFree(); }
    public override void _ExitTree() { GetViewport().SizeChanged -= UpdateResponsivePlayerEquipment; _lifetime.Cancel(); _request?.Cancel(); _request?.Dispose(); _lifetime.Dispose(); }
}

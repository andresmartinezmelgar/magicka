using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class BestiaryPanel : Control
{
    private Main _main = null!;
    private VBoxContainer _entries = null!;
    private ElementTextLabel _status = null!;
    private Button _refresh = null!;
    private AccountProfile? _lastProfile;
    private bool _busy;
    private int _difficulty = 1, _defenders = 1;
    private readonly Dictionary<MinionKind, GridContainer> _stats = [];
    private GridContainer? _statsControls;
    public void Build(Main main)
    {
        _main = main; Name = "BestiaryPanel"; Theme = Ui.Theme(); Ui.Full(this);
        var shade = new ColorRect { Color = ArcanaMenuChrome.Backdrop }; AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 14 : 28);
        var layout = new VBoxContainer(); layout.AddThemeConstantOverride("separation", 12); margin.AddChild(layout);
        var heading = new HBoxContainer(); heading.AddThemeConstantOverride("separation", 12); layout.AddChild(heading);
        var title = Ui.Label("MI PERFIL · BESTIARIO", Ui.IsMobile ? 22 : 28, Ui.Gold);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart; title.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(title);
        _status = Ui.Label("", 16, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; layout.AddChild(_status);
        _refresh = Ui.Button("Actualizar bajas", () => _ = Refresh()); _refresh.Name = "BestiaryRefresh"; heading.AddChild(_refresh);
        var scroll = new ContentScrollContainer { Name = "BestiaryScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        layout.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        var contentMargin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        contentMargin.AddThemeConstantOverride("margin_right", Ui.IsMobile ? 20 : 16); scroll.AddChild(contentMargin);
        _entries = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _entries.AddThemeConstantOverride("separation", 12); contentMargin.AddChild(_entries);
        ArcanaMenuChrome.Attach(this, margin, main, "Bestiario", 3, main.NavigateOptionAMenu);
        GetViewport().SizeChanged += ResizeStats;
        Render();
    }
    private void Render()
    {
        _lastProfile = _main.Accounts.Profile;
        foreach (var child in _entries.GetChildren()) { _entries.RemoveChild(child); child.QueueFree(); }
        _stats.Clear();
        _status.Text = _lastProfile == null ? "Conecta tu cuenta para guardar las bajas. El catálogo se puede consultar sin iniciar sesión."
            : "Bajas guardadas de " + _lastProfile.Name + ". Solo cuentan los enemigos eliminados por ti.";
        int pending = _main.PendingBestiaryRuns;
        if (pending > 0) _status.Text += $" Hay {pending} partida(s) pendientes de sincronizar.";
        BuildStatsContext();
        foreach (var kind in Enum.GetValues<MinionKind>())
        {
            var card = new PanelContainer { Name = "Bestiary_" + kind, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("10263f"), 8, new Color("3375a6")));
            _entries.AddChild(card);
            var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 10); card.AddChild(content);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.IsMobile ? 12 : 18); content.AddChild(row);
            var portrait = new BestiaryPortrait { SizeFlagsVertical = SizeFlags.ShrinkCenter };
            row.AddChild(portrait); portrait.Build(kind);
            var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 6); row.AddChild(body);
            long kills = _lastProfile?.Bestiary.GetValueOrDefault(BestiaryCatalogue.Key(kind)) ?? 0;
            var heading = Ui.Label(DefenseRules.MinionName(kind) + $" · Bajas: {kills}", 20, Ui.Gold);
            heading.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(heading);
            var description = Ui.Label(BestiaryCatalogue.Description(kind), Ui.IsMobile ? 17 : 18);
            description.AutowrapMode = TextServer.AutowrapMode.WordSmart; description.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(description);
            var stats = Ui.Table(2, "BestiaryStats_" + kind); content.AddChild(stats); _stats[kind] = stats;
        }
        UpdateStats(); ResizeStats();
    }
    private void BuildStatsContext()
    {
        var context = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; context.AddThemeConstantOverride("separation", 8); _entries.AddChild(context);
        _statsControls = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _statsControls.AddThemeConstantOverride("h_separation", 14); _statsControls.AddThemeConstantOverride("v_separation", 8); context.AddChild(_statsControls);
        var stageBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _statsControls.AddChild(stageBox);
        var stage = new SpinBox { Name = "BestiaryDifficulty", MinValue = 1, MaxValue = 100, Step = 1, Rounded = true,
            Value = _difficulty, Prefix = "Dificultad ", CustomMinimumSize = new(0, 42), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "La dificultad aumenta cada 90 s de partida. Modifica vida, daño, velocidad y pausas entre ataques." };
        stageBox.AddChild(stage); Ui.TouchTarget(stage);
        stage.ValueChanged += value => { _difficulty = (int)value; UpdateStats(); };
        var partyBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _statsControls.AddChild(partyBox);
        var party = new OptionButton { Name = "BestiaryDefenders", CustomMinimumSize = new(0, 42), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        for (int i = 1; i <= DefenseRules.MaxDefenders; i++) party.AddItem(i == 1 ? "1 defensor" : i + " defensores", i);
        party.TooltipText = "Humanos y bots aliados. El número de defensores aumenta la vida de los monstruos.";
        party.Select(_defenders - 1); partyBox.AddChild(party); Ui.TouchTarget(party);
        party.ItemSelected += _ => { _defenders = party.GetSelectedId(); UpdateStats(); };
        var note = Ui.Label("Daño base sin defensas, estados ni potenciadores. La pausa se añade a la duración de cada ataque.", 15, Ui.Muted);
        note.Name = "BestiaryStatsNote"; note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        note.TooltipText = "Los haces muestran el intervalo de daño según su carga. Las pausas no incluyen la canalización ni la preparación del ataque.";
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; context.AddChild(note);
    }
    private void UpdateStats()
    {
        foreach (var (kind, table) in _stats)
        {
            var rows = BestiaryCatalogue.Stats(kind, _difficulty, _defenders);
            if (table.GetChildCount() == 0)
                foreach (var stat in rows) { Ui.TableCell(table, stat.Name); Ui.TableCell(table, stat.Value); }
            else for (int i = 0; i < rows.Length; i++) ((ElementTextLabel)table.GetChild(i * 2 + 1)).Text = rows[i].Value;
        }
    }
    private void ResizeStats()
    {
        if (_statsControls != null) _statsControls.Columns = ResponsiveLayout.Columns(
            Math.Max(240, GetViewport().GetVisibleRect().Size.X - (Ui.IsMobile ? 48 : 80)), 240, 2, 14);
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizeStats;
    private async Task Refresh()
    {
        if (_busy) return; _busy = true; _refresh.Disabled = true;
        string message = await _main.SynchronizeBestiary();
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        Render(); _status.Text = message; _busy = false; _refresh.Disabled = false;
    }
    public override void _Process(double delta)
    {
        if (!_busy && !ReferenceEquals(_lastProfile, _main.Accounts.Profile)) Render();
    }
}

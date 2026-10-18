using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class PracticePanel : PanelContainer
{
    private PracticeSession _session = null!;
    private Label _reading = null!, _spell = null!;
    private int _target;
    private ContentScrollContainer? _touchScroll;
    private SpellCombination? _lastSpell;
    private bool? _lastEnabled;
    public bool PointerOverPanel
    {
        get
        {
            var hovered = GetViewport().GuiGetHoveredControl();
            return hovered != null && (hovered == this || IsAncestorOf(hovered));
        }
    }
    public void Build(PracticeSession session)
    {
        _session = session; _target = session.Player.Id;
        Name = "PracticePanel"; Theme = Ui.Theme(); Position = new(24, Ui.IsMobile ? 200 : 82);
        CustomMinimumSize = new(320, 0); MouseFilter = MouseFilterEnum.Stop;
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; stack.AddThemeConstantOverride("separation", 6);
        if (Ui.IsMobile)
        {
            _touchScroll = new ContentScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            AddChild(_touchScroll); Ui.VisibleVerticalScroll(_touchScroll); _touchScroll.AddChild(stack);
        }
        else AddChild(stack);
        stack.AddChild(Ui.Label("LABORATORIO", 17, Ui.Gold));
        stack.AddChild(Ui.Label("Apunta a un objetivo para medirlo.", 12, Ui.Muted));
        _reading = Ui.Label("", 13); _reading.Name = "PracticeMeasurements"; stack.AddChild(_reading);
        BoxContainer row = Ui.IsMobile ? new VBoxContainer() : new HBoxContainer(); stack.AddChild(row);
        row.AddChild(Ui.Button("Restablecer objetivo", () => session.ResetTarget(_target)));
        row.AddChild(Ui.Button("Vida al 40 %", session.PrepareHealing));
        stack.AddChild(Ui.Label("Mago lanzador · dirección fija →", 14, Ui.Gold));
        var choice = new OptionButton { Name = "PracticeCasterChoice", CustomMinimumSize = new(Ui.IsMobile ? 250 : 310, 34),
            FitToLongestItem = !Ui.IsMobile, ClipText = Ui.IsMobile, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        foreach (var c in PracticeLayout.CasterSpells)
            choice.AddItem(c.Keys.Length == 0 ? "Empuje" : c.Keys + " · " + SpellCatalogue.Name(c.Elements));
        choice.Select(PracticeLayout.CasterSpells.ToList().FindIndex(c => c.Keys == session.CasterSpell.Keys));
        choice.ItemSelected += i => session.SetCasterSpell(PracticeLayout.CasterSpells[(int)i].Keys); stack.AddChild(choice);
        _spell = Ui.Label("", 12, Ui.Muted); _spell.Name = "PracticeCasterCadence"; stack.AddChild(_spell);
        var enabled = new CheckButton { Text = "Lanzador activo", ButtonPressed = true, Name = "PracticeCasterEnabled" };
        enabled.Toggled += session.SetCasterEnabled; stack.AddChild(enabled);
        if (Ui.IsMobile)
        {
            foreach (var label in stack.GetChildren().OfType<Label>()) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            foreach (var button in row.GetChildren().OfType<Button>())
            { button.AddThemeFontSizeOverride("font_size", 16); button.CustomMinimumSize = new(0, 44); }
        }
    }
    internal void FitTouchLayout(float top)
    {
        if (_touchScroll == null) return;
        var viewport = GetViewportRect().Size;
        float s = Math.Min(1, viewport.Y / 720f);
        Scale = Vector2.One * s; Position = new(22 * s, top);
        CustomMinimumSize = Vector2.Zero;
        Size = new(320, Math.Max(150, (viewport.Y - 207 * s - ScreenPlacement.MinimumGap - top) / s));
    }
    public void Update(V2 aim)
    {
        var target = PointerOverPanel ? _session.World.Players[_target]
            : _session.Targets.FirstOrDefault(t => V2.Distance(t.Wizard.Position, aim) < 2.8f)?.Wizard
                ?? (V2.Distance(_session.Caster.Position, aim) < 2.8f ? _session.Caster : _session.Player);
        _target = target.Id;
        var meter = _session.World.PracticeReading(target.Id);
        _reading.Text = $"{target.Name}\nVida {target.Health:0.0} / {target.MaxHealth:0}  ·  Escudo {target.Shield:0.0}\nDaño {meter.Damage:0.0}  ·  Absorbido {meter.Absorbed:0.0}\nCuración efectiva {meter.Healing:0.0}";
        if (_lastSpell != _session.CasterSpell || _lastEnabled != _session.CasterEnabled)
        {
            _lastSpell = _session.CasterSpell; _lastEnabled = _session.CasterEnabled;
            _spell.Text = _session.CasterEnabled ? _session.CasterDescription : "Lanzador pausado";
            _spell.TooltipText = SpellCatalogue.Describe(_session.CasterSpell.Recipe, CastMode.Forward);
        }
    }
}

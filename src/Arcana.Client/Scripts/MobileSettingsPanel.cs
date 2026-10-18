using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Ajustes locales con edición directa del HUD, sin enviar entradas al juego.</summary>
public partial class MobileSettingsPanel : Control
{
    private TouchSettings _settings = new();
    private TouchSettings _beforeEdit = new();
    private Control _form = null!, _editorBar = null!;
    private VBoxContainer _sliders = null!;
    private Label _status = null!;
    private MobileControls _preview = null!;
    private Hud _editorHud = null!;
    private Action<List<TouchHudArea>>? _modeAreas;
    private readonly List<TouchHudArea> _areas = [];
    private readonly List<Control> _modePreviews = [];
    public event Action<TouchSettings>? Saved;
    public event Action? Canceled;
    internal TouchSettings Configuration => _preview.Visible ? _preview.Settings : _settings;

    internal void Build(TouchSettings settings, WorldSnapshot? state = null, int playerId = 0, Action<List<TouchHudArea>>? modeAreas = null)
    {
        Name = "MobileSettings"; Theme = Ui.Theme(); Ui.Full(this); MouseFilter = MouseFilterEnum.Stop;
        _settings = settings.Normalize();
        var backdrop = new ColorRect { Color = new Color("081424") }; AddChild(backdrop); Ui.Full(backdrop);
        _modeAreas = modeAreas;
        _editorHud = new Hud { MobileLayout = true, Visible = false }; AddChild(_editorHud); _editorHud.Build();
        if (state == null)
        {
            var sample = new GameWorld(new WorldOptions { Mode = GameMode.Arena });
            playerId = sample.AddPlayer("Vista de controles", 0)!.Id; state = sample.Snapshot();
        }
        _editorHud.Update(state, playerId, "Vista de controles", 0); _editorHud.HideEditorPlayer();
        _preview = new MobileControls { EditorMode = true, Visible = false }; AddChild(_preview); _preview.Configure(_settings);
        var margin = new MarginContainer(); _form = margin; AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        var panel = new PanelContainer(); margin.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); panel.AddChild(box);
        box.AddChild(Ui.Label("CONTROLES TÁCTILES", 24, Ui.Gold));
        var help = Ui.Label("Ajustes de este dispositivo. Puedes cambiar cada posición arrastrando los botones y los dos joysticks.", 16, Ui.Muted);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(help);
        var scroll = new ContentScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        _sliders = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _sliders.AddThemeConstantOverride("separation", 8); scroll.AddChild(_sliders);
        BuildFields();
        var edit = Ui.Button("Mover botones y joysticks", StartEditing); edit.Name = "TouchEditPositions"; box.AddChild(edit);
        _status = Ui.Label("", 15, Ui.Gold); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_status);
        var footer = new HBoxContainer(); box.AddChild(footer);
        foreach (var button in new[] { Ui.Button("Restablecer", () => { _settings = new(); BuildFields(); }),
            Ui.Button("Cancelar", () => Canceled?.Invoke()), Ui.Button("Guardar", Save) })
        { button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new(0, 48); footer.AddChild(button); }
        var bar = new PanelContainer { Name = "TouchEditorToolbar", Position = new(12, 12), Size = new(550, 100), Visible = false }; _editorBar = bar; AddChild(bar);
        bar.MinimumSizeChanged += () => Callable.From(FitEditorToolbar).CallDeferred();
        var tools = new VBoxContainer(); bar.AddChild(tools);
        var instruction = Ui.Label("Arrastra los controles. Margen mínimo: 8 px. Las zonas de información están protegidas.", 16, Ui.Gold);
        instruction.AutowrapMode = TextServer.AutowrapMode.WordSmart; tools.AddChild(instruction);
        var actions = new HBoxContainer(); tools.AddChild(actions);
        var done = Ui.Button("Listo", FinishEditing); done.Name = "TouchEditorDone"; actions.AddChild(done);
        actions.AddChild(Ui.Button("Deshacer posiciones", () => { _settings = _beforeEdit; FinishEditing(false); }));
        foreach (var button in actions.GetChildren().OfType<Button>())
        {
            button.CustomMinimumSize = new(0, 44); button.AddThemeFontSizeOverride("font_size", 16);
            foreach (string stateName in new[] { "normal", "hover", "pressed" })
            {
                var style = Ui.Panel(new("19283d"), 8);
                style.ContentMarginLeft = style.ContentMarginRight = 8;
                style.ContentMarginTop = style.ContentMarginBottom = 8;
                button.AddThemeStyleboxOverride(stateName, style);
            }
        }
        _preview.PointerBlocked = point => _editorBar.Visible && _editorBar.GetGlobalRect().HasPoint(point);
    }
    private void BuildFields()
    {
        foreach (var child in _sliders.GetChildren()) { _sliders.RemoveChild(child); child.QueueFree(); }
        void Slider(string name, string title, double min, double max, double value, Action<float> set)
        {
            var label = Ui.Label(title + $" · {value:0.00}", 17); _sliders.AddChild(label);
            var slider = new HSlider { Name = name, MinValue = min, MaxValue = max, Step = .01, Value = value, CustomMinimumSize = new(0, 40) };
            slider.ValueChanged += next => { label.Text = title + $" · {next:0.00}"; set((float)next); }; _sliders.AddChild(slider);
        }
        Slider("TouchSize", "Tamaño de botones y joysticks", .75, 1.3, _settings.Size, v => _settings = _settings with { Size = v });
        Slider("TouchSpacing", "Separación entre botones", .8, 1.25, _settings.Spacing, v => _settings = _settings with { Spacing = v, Positions = new() });
        Slider("TouchDeadZone", "Zona muerta del joystick", .05, .35, _settings.DeadZone, v => _settings = _settings with { DeadZone = v });
        Slider("TouchSensitivity", "Sensibilidad del alcance de traslación y meteoro", .6, 1.8, _settings.Sensitivity, v => _settings = _settings with { Sensitivity = v });
        var handed = new CheckBox { Name = "TouchLeftHanded", Text = "Modo zurdo · movimiento a la derecha", ButtonPressed = _settings.LeftHanded, CustomMinimumSize = new(0, 46) };
        handed.Toggled += left => _settings = _settings with { LeftHanded = left, Positions = new() }; _sliders.AddChild(handed);
        var note = Ui.Label("Cambiar separación o modo zurdo restablece las posiciones personalizadas. Agua, hielo y rocas mantienen su marcador a distancia fija.", 14, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; _sliders.AddChild(note);
    }
    private void StartEditing()
    {
        _beforeEdit = _settings.Normalize(); _preview.Configure(_settings);
        float s = Math.Min(1, GetViewport().GetVisibleRect().Size.Y / 720f);
        _editorBar.Scale = Vector2.One * s; _editorBar.Size = new(310, 100);
        _editorBar.Position = new(22 * s, 18 * s);
        _form.Hide(); _editorHud.Show(); _preview.Show(); _editorBar.Show();
        UpdateEditorLayout();
    }
    private void FinishEditing() => FinishEditing(true);
    private void FitEditorToolbar()
    {
        if (!IsInsideTree() || !_editorBar.Visible) return;
        _editorBar.Size = new(_editorBar.Size.X, _editorBar.GetCombinedMinimumSize().Y);
    }
    private void FinishEditing(bool apply)
    {
        if (apply) _settings = _preview.Settings.Normalize();
        _preview.Hide(); _editorHud.Hide(); _editorBar.Hide(); _form.Show();
        foreach (var preview in _modePreviews) preview.Hide();
    }
    public override void _Process(double delta) { if (_preview.Visible) UpdateEditorLayout(); }
    private void UpdateEditorLayout()
    {
        _areas.Clear(); _modeAreas?.Invoke(_areas);
        int index = 0;
        foreach (var area in _areas)
        {
            if (index == _modePreviews.Count)
            {
                var panel = new Panel { MouseFilter = MouseFilterEnum.Ignore, ClipContents = true }; AddChild(panel); MoveChild(panel, _preview.GetIndex());
                panel.AddThemeStyleboxOverride("panel", Ui.Panel());
                var label = Ui.Label("", 13, Ui.Muted); label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;
                label.AutowrapMode = TextServer.AutowrapMode.WordSmart; panel.AddChild(label); Ui.Full(label); _modePreviews.Add(panel);
            }
            var placeholder = _modePreviews[index++]; placeholder.Show();
            placeholder.Position = area.Bounds.Position; placeholder.Size = area.Bounds.Size;
            var text = (Label)placeholder.GetChild(0);
            text.Text = area.Name;
            text.AddThemeFontSizeOverride("font_size", Math.Max(10, (int)(16 * Math.Min(1, Size.Y / 720f))));
        }
        // Las herramientas sustituyen a la ficha de vida en esta vista y también impiden arrastres por debajo.
        _areas.Add(new("Herramientas", _editorBar.GetGlobalRect()));
        TouchHudLayout.Apply(_preview, _editorHud, _areas);
    }
    private void Save()
    {
        _settings = _settings.Normalize();
        if (!DevicePreferences.Write("touch-controls", _settings)) { _status.Text = "No se pudieron guardar los controles. Vuelve a intentarlo."; return; }
        Saved?.Invoke(_settings);
    }
    public void HandleBack() { if (_editorBar.Visible) FinishEditing(); else Canceled?.Invoke(); }
}

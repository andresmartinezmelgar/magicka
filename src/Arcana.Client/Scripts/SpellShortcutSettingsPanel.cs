using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class SpellShortcutSettingsPanel : Control
{
    private sealed record Slot(ElementBuffer Selection, CheckButton Automatic, OptionButton LaunchMode)
    {
        public bool Valid => Automatic.ButtonPressed || Selection.Elements.Count > 0;
        public SpellShortcutPreset Read() => new() { Automatic = Automatic.ButtonPressed,
            Elements = Selection.Elements.ToArray(), Mode = (CastMode)LaunchMode.GetSelectedId() };
    }

    private readonly List<Slot> _slots = [];
    private Button _save = null!;

    internal void Build(SpellShortcutSettings settings, bool tutorial, Func<SpellShortcutSettings, bool> save, Action close)
    {
        Name = "SpellShortcutSettings";
        Theme = Ui.Theme(); Ui.Full(this);
        var shade = new ColorRect { Color = Ui.ModalDim, MouseFilter = MouseFilterEnum.Stop };
        AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); AddChild(center); Ui.Full(center);
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", Ui.ModalPanel()); center.AddChild(panel);
        void ResizePanel() => panel.CustomMinimumSize = new(Math.Max(0, Math.Min(650, Size.X - 24)),
            Math.Max(0, Math.Min(540, Size.Y - 24)));
        Resized += ResizePanel; ResizePanel();

        var frame = new VBoxContainer(); frame.AddThemeConstantOverride("separation", 8); panel.AddChild(frame);
        var header = new HBoxContainer(); frame.AddChild(header);
        var title = Ui.Label("Hechizos predefinidos", 21, Ui.Gold);
        title.ClipText = true; title.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(title);
        header.AddChild(Ui.ExitButton("Volver", close, "SpellShortcutsBack"));
        var hint = Ui.Label(tutorial ? "Practica con dos accesos temporales. Los ajustes de tus partidas se guardan aparte."
            : "Cada botón prepara una mezcla. Puedes dejarlo en automático o guardar la tuya.", 16, Ui.Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; frame.AddChild(hint);

        var scroll = new ContentScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        frame.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        var tabs = new TabContainer { Name = "SpellShortcutTabs", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(tabs);
        tabs.TabChanged += _ => scroll.ScrollVertical = 0;

        var footer = new HBoxContainer(); frame.AddChild(footer);
        var status = Ui.Label("", 16, Ui.Red);
        status.SizeFlagsHorizontal = SizeFlags.ExpandFill; status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        footer.AddChild(status);
        _save = Ui.Button("Guardar", () =>
        {
            if (_slots.Any(slot => !slot.Valid)) return;
            if (!save(new SpellShortcutSettings { Defense = _slots[0].Read(), Attack = _slots[1].Read() }.Normalize()))
                status.Text = "No se pudieron guardar los hechizos.";
        }, true);
        _save.Name = "SpellShortcutsSave";
        _save.CustomMinimumSize = new(0, 46); footer.AddChild(_save);

        AddSlot(tabs, "Defensa", "Defense", settings.Defense);
        AddSlot(tabs, "Ataque", "Attack", settings.Attack);
    }

    private void AddSlot(TabContainer tabs, string title, string id, SpellShortcutPreset preset)
    {
        var page = new VBoxContainer { Name = title };
        page.AddThemeConstantOverride("separation", 8); tabs.AddChild(page);
        var automatic = new CheckButton { Name = "SpellShortcutAutomatic" + id, Text = "Automático", ButtonPressed = preset.Automatic,
            CustomMinimumSize = new(0, 44) };
        page.AddChild(automatic);
        var automaticHelp = Ui.Label(id == "Defense" ? "Prepara protección si estás sin escudo y Vida si ya estás protegido."
            : "Busca un ataque útil según el objetivo y tus mejoras.", 16, Ui.Muted);
        automaticHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart; page.AddChild(automaticHelp);

        var custom = new VBoxContainer(); custom.AddThemeConstantOverride("separation", 7); page.AddChild(custom);
        var selection = new ElementBuffer();
        foreach (var element in preset.Elements) selection.Add(element);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); custom.AddChild(row);
        var preview = Ui.Label("", 17, Ui.Gold);
        preview.SizeFlagsHorizontal = SizeFlags.ExpandFill; preview.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(preview);
        var clear = Ui.Button("Limpiar", () => { selection.Clear(); Refresh(); });
        clear.Name = "SpellShortcutClear" + id; clear.CustomMinimumSize = new(0, 44); row.AddChild(clear);
        var elements = new GridContainer { Columns = 4 }; custom.AddChild(elements);
        elements.AddThemeConstantOverride("h_separation", 6); elements.AddThemeConstantOverride("v_separation", 6);
        foreach (var element in Enum.GetValues<Element>())
        {
            var button = Ui.Button(Ui.ElementName(element), () => { selection.Add(element); Refresh(); });
            button.Name = "SpellShortcut" + id + element;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new(0, 44);
            button.AddThemeColorOverride("font_color", Ui.ElementColor(element));
            elements.AddChild(button);
        }
        var launch = new HBoxContainer(); launch.AddThemeConstantOverride("separation", 8); custom.AddChild(launch);
        launch.AddChild(Ui.Label("Lanzamiento", 16));
        var mode = new ScrollOptionButton { Name = "SpellShortcutMode" + id, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new(0, 44) };
        foreach (var cast in Enum.GetValues<CastMode>()) mode.AddItem(Ui.CastName(cast), (int)cast);
        mode.Select(mode.GetItemIndex((int)preset.Mode)); launch.AddChild(mode);
        _slots.Add(new(selection, automatic, mode));
        automatic.Toggled += _ => Refresh();
        Refresh();

        void Refresh()
        {
            automaticHelp.Visible = automatic.ButtonPressed;
            custom.Visible = !automatic.ButtonPressed;
            preview.Text = selection.Elements.Count == 0 ? "Elige de uno a tres elementos" : SpellCatalogue.Name(selection.Elements);
            clear.Disabled = selection.Elements.Count == 0;
            _save.Disabled = _slots.Any(slot => !slot.Valid);
        }
    }
}

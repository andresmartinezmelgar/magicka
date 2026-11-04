using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Ensayo local de apariencia con los mismos renderizadores de combate e inventario.</summary>
public partial class EquipmentLaboratory : Control
{
    private readonly Dictionary<EquipmentSlot, OptionButton> _pieces = [];
    private OptionButton _type = null!, _facing = null!, _pose = null!, _team = null!;
    private GridContainer _choices = null!;
    private TabContainer _views = null!;
    private ContentScrollContainer _settings = null!;
    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private WizardVisual _base = null!, _equipped = null!;
    private EquipmentAvatar _avatar = null!;
    private WizardSnapshot _template = null!;
    private EquipmentAppearance? _appearance;
    private WizardType? _avatarType;
    private double _clock;
    private float _distance;
    private bool _built;
    internal WizardVisual Equipped => _equipped;
    internal EquipmentAvatar Avatar => _avatar;
    internal SubViewport PreviewViewport => _viewport;

    public void Build(Action close)
    {
        Name = "EquipmentLaboratory"; Theme = Ui.Theme(); Ui.Full(this);
        var shade = new ColorRect { Color = new("081629") }; AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 10 : 22);
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 6); margin.AddChild(stack);
        stack.AddChild(Ui.Label("LABORATORIO DE EQUIPO", Ui.IsMobile ? 20 : 25, Ui.Gold));
        var note = Ui.Label(Ui.IsMobile ? "Prueba visual; no modifica tu cuenta." :
            "Compara el aspecto base y el equipo elegido. Los cambios son solo para esta prueba.", 14, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; stack.AddChild(note);
        _views = new TabContainer { Name = "EquipmentLabViews", CustomMinimumSize = new(0, 160), SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddChild(_views);
        var arena = new SubViewportContainer { Name = "Combate", Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        _views.AddChild(arena);
        _viewport = new SubViewport { Size = new(800, 340), OwnWorld3D = true, HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenParentVisible };
        arena.AddChild(_viewport);
        var scene = new Node3D(); _viewport.AddChild(scene);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 4.8f, Position = new(0, 10, 10), Current = true };
        scene.AddChild(_camera); _camera.LookAt(new(0, 1.55f, 0));
        var world = new GameWorld(new() { Mode = GameMode.Arena, MaxPlayers = 2 });
        world.AddPlayer("Base", 0);
        _template = world.Snapshot().Players.Single() with { Health = 1000, MaxHealth = 1000, Shield = 0, InvulnerableSeconds = 0,
            Alive = true, Position = V2.Zero, Facing = new(0, 1), Selected = [], Equipment = null };
        _base = new WizardVisual(); scene.AddChild(_base); _base.Build(_template, false);
        _equipped = new WizardVisual(); scene.AddChild(_equipped); _equipped.Build(_template with { Id = 2, Name = "Equipo elegido" }, false);
        var inventory = new Control { Name = "Inventario", ClipContents = true }; _views.AddChild(inventory);
        _avatar = new EquipmentAvatar(); inventory.AddChild(_avatar); Ui.Full(_avatar);
        _avatar.Build(WizardType.Balanced, slot => _pieces[slot].GrabFocus());
        _settings = new ContentScrollContainer { Name = "EquipmentLabSettings", FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Ui.VisibleVerticalScroll(_settings); stack.AddChild(_settings);
        _choices = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _choices.AddThemeConstantOverride("h_separation", 10); _choices.AddThemeConstantOverride("v_separation", 6); _settings.AddChild(_choices);
        _type = Choice("Mago", "EquipmentLabWizard");
        foreach (var type in Enum.GetValues<WizardType>()) _type.AddItem(WizardTypes.Name(type), (int)type);
        _type.ItemSelected += _ => RefreshAvatar();
        _facing = Choice("Orientación", "EquipmentLabFacing");
        foreach (string name in new[] { "Frente", "Izquierda", "Espalda", "Derecha" }) _facing.AddItem(name);
        _pose = Choice("Animación", "EquipmentLabPose");
        foreach (string name in new[] { "Reposo", "Caminar", "Lanzar", "Congelado", "Muerte y reaparición", "Daño", "Automático" }) _pose.AddItem(name);
        _pose.Select(6);
        _pose.ItemSelected += _ => { _clock = 0; _distance = 0; };
        _facing.ItemSelected += _ => { _distance = 0; };
        _team = Choice("Color de equipo", "EquipmentLabTeam"); _team.AddItem("Azul", 0); _team.AddItem("Rojo", 1);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var choice = Choice(EquipmentCatalogue.SlotName(slot), "EquipmentLab" + slot);
            choice.AddItem("Vacío", -1);
            foreach (var tier in Enum.GetValues<ItemTier>()) choice.AddItem(EquipmentCatalogue.TierName(tier), (int)tier);
            choice.Select(choice.GetItemIndex((int)ItemTier.Mythic));
            choice.ItemSelected += _ => RefreshAvatar(); _pieces.Add(slot, choice);
        }
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 8); stack.AddChild(actions);
        foreach (var (text, action) in new (string, Action)[] {
            ("Todo mítico", () => SetAll(ItemTier.Mythic)), ("Vaciar equipo", () => SetAll(null)), ("Atrás", close) })
        {
            var button = Ui.Button(text, action); button.Name = "EquipmentLab" + (text == "Atrás" ? "Back" : text == "Todo mítico" ? "All" : "Clear");
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new(0, 46);
            button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 15 : 17); actions.AddChild(button);
        }
        Resized += Fit; _built = true; RefreshAvatar(); Fit(); Advance(1 / 60f);
    }

    private OptionButton Choice(string label, string name)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _choices.AddChild(column);
        column.AddChild(Ui.Label(label, 13, Ui.Muted));
        var choice = new OptionButton { Name = name, FitToLongestItem = false, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new(0, 44) };
        choice.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 15 : 16); column.AddChild(choice); return choice;
    }
    private void Fit()
    {
        if (!_built) return;
        _choices.Columns = Size.X < 800 ? 2 : 3;
        // Las seis zonas del avatar necesitan 164 px, además de la barra de pestañas.
        _views.CustomMinimumSize = new(0, 202);
        _settings.CustomMinimumSize = new(0, Size.Y < 520 ? 82 : 160);
    }
    internal void SelectPiece(EquipmentSlot slot, ItemTier? tier)
    { _pieces[slot].Select(_pieces[slot].GetItemIndex(tier.HasValue ? (int)tier.Value : -1)); RefreshAvatar(); }
    internal void SetAll(ItemTier? tier)
    {
        foreach (var choice in _pieces.Values) choice.Select(choice.GetItemIndex(tier.HasValue ? (int)tier.Value : -1));
        RefreshAvatar();
    }
    internal void SelectWizard(WizardType type) { _type.Select(_type.GetItemIndex((int)type)); RefreshAvatar(); }
    internal void SelectPose(int pose, int facing) { _pose.Select(pose); _facing.Select(facing); _clock = 0; _distance = 0; }

    private void RefreshAvatar()
    {
        if (!_built) return;
        var type = (WizardType)_type.GetSelectedId();
        if (_avatarType != type)
        {
            _avatar.Hide(); _avatar.QueueFree();
            var parent = (Control)_views.GetChild(1);
            _avatar = new EquipmentAvatar(); parent.AddChild(_avatar); Ui.Full(_avatar);
            _avatar.Build(type, slot => _pieces[slot].GrabFocus()); _avatarType = type;
        }
        ItemTier? Tier(EquipmentSlot slot)
        { int value = _pieces[slot].GetSelectedId(); return value >= 0 ? (ItemTier)value : null; }
        ulong mix = 0; bool full = true;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var tier = Tier(slot);
            EquipmentItem? item = tier.HasValue ? EquipmentCatalogue.Generate(new Random(700 + (int)slot), tier: tier, slot: slot, family: EquipmentFamily.Arcane) : null;
            _avatar.UpdateSlot(slot, item, false);
            if (tier != ItemTier.Mythic) full = false;
            else mix += 1UL << ((int)EquipmentFamily.Arcane * 4);
        }
        _appearance = new(Tier(EquipmentSlot.Boots), Tier(EquipmentSlot.Hat), Tier(EquipmentSlot.Chest), Tier(EquipmentSlot.Weapon), EquipmentFamily.Arcane)
            { GoldRing = Tier(EquipmentSlot.GoldRing), MagicRing = Tier(EquipmentSlot.MagicRing), MythicFamilies = full ? mix : 0 };
    }
    public override void _Process(double delta)
    { if (_built && IsVisibleInTree()) Advance((float)Math.Clamp(delta, 0, .1)); }
    internal void Advance(float dt)
    {
        _clock += dt;
        int pose = _pose.Selected, facingRow = _facing.Selected;
        if (pose == 6)
        {
            float cycle = (float)(_clock % 12);
            pose = cycle < 4 ? 1 : cycle < 7 ? 2 : cycle < 9 ? 3 : cycle < 11 ? 4 : 0;
            facingRow = (int)(_clock / 3) % 4;
        }
        V2 direction = facingRow switch { 1 => new(-1, 0), 2 => new(0, -1), 3 => new(1, 0), _ => new(0, 1) };
        if (pose == 1) _distance += dt * 2.5f;
        bool alive = pose != 4 || _pose.Selected == 4 && _clock % 6 >= 3;
        var player = _template with { WizardType = (WizardType)_type.GetSelectedId(), Team = _team.GetSelectedId(), Facing = direction,
            Position = direction * _distance, Alive = alive, Health = !alive ? 0 : pose == 5 && _clock % 1 < .3 ? 900 : 1000,
            Casting = pose == 2, Frozen = pose == 3 ? 1 : 0, Respawn = alive ? 0 : 2 };
        _base.Update(player, dt, (float)_clock, _camera, _clock, 0);
        _equipped.Update(player with { Id = 2, Name = "Equipo elegido", Equipment = _appearance }, dt, (float)_clock, _camera, _clock, 0);
        // Marcha en el sitio: la animación recibe desplazamiento real, pero las dos figuras siguen centradas.
        _base.Position = new(-1.45f, 0, 0); _equipped.Position = new(1.45f, 0, 0);
    }
}

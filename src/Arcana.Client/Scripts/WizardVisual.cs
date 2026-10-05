using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class WizardVisual : Node3D
{
    private Node3D _body = null!;
    private Sprite3D _sprite = null!;
    private WizardSprites.Atlas _atlas = null!;
    private ShaderMaterial _spriteMaterial = null!;
    private MeshInstance3D _shadow = null!;
    private WizardType _wizardType;
    private bool _local;
    private int _team, _facingRow;
    private float _castPose, _hitFlash, _previousHealth, _renderedFlash = -1;
    private readonly WizardMotion _motion = new();
    private bool _wasAlive;
    private int _deaths;
    internal Sprite3D Sprite => _sprite;
    private MeshInstance3D _shield = null!, _halo = null!, _ghost = null!;
    private Label3D _name = null!;
    private Node3D _healthRoot = null!;
    private MeshInstance3D _healthFill = null!;
    private MeshInstance3D _shieldFill = null!, _shieldTrack = null!;
    private Label3D _shieldAmount = null!;
    private MeshInstance3D _affinityRing = null!;
    private Color _color;
    private Node3D _elementsRoot = null!;
    private readonly List<MeshInstance3D> _elementFills = [];
    private readonly List<Label3D> _elementKeys = [];
    private Element[] _lastCastElements = [];
    internal Element[] DisplayedElements { get; private set; } = [];
    internal int ElementSlotCount => _elementFills.Count;
    internal bool ElementSlotsVisible => _elementsRoot.Visible;
    public override void _ExitTree()
    {
        if (_sprite != null)
        {
            _sprite.Texture = null;
            _sprite.MaterialOverride = null;
        }
        _spriteMaterial?.Dispose();
    }

    public void Build(WizardSnapshot p, bool local)
    {
        _color = p.Team == 0 ? Ui.Blue : Ui.Red;
        _team = p.Team; _wizardType = p.WizardType; _local = local;
        _previousHealth = p.Health;
        _wasAlive = p.Alive;
        Position = ArenaRenderer.World(p.Position);
        _body = new Node3D(); AddChild(_body);
        _atlas = WizardSprites.Load(p.WizardType);
        _spriteMaterial = WizardSprites.Material(_atlas, p.Team);
        _sprite = new Sprite3D { Name = "WizardSprite", Texture = _atlas.Texture, Hframes = WizardSprites.Columns,
            Vframes = WizardSprites.Rows, PixelSize = _atlas.Spec.PixelSize, Offset = _atlas.Offset(0),
            MaterialOverride = _spriteMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _body.AddChild(_sprite);
        _shadow = Mesh(this, new CylinderMesh { TopRadius = 0.48f, BottomRadius = 0.48f, Height = 0.01f, RadialSegments = 24 }, new(0, 0.015f, 0),
            new StandardMaterial3D { AlbedoColor = new Color(0, 0, 0, 0.28f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _shadow.Scale = new(1.25f, 1, 0.8f); _shadow.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Color affinityColor = Ui.WizardColor(p.WizardType);
        _halo = Mesh(this, new TorusMesh { InnerRadius = local ? 0.63f : 0.56f, OuterRadius = local ? 0.72f : 0.6f, Rings = 32, RingSegments = 8 }, new(0, 0.05f, 0),
            new StandardMaterial3D { AlbedoColor = _color, EmissionEnabled = true, Emission = _color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _affinityRing = Mesh(this, new TorusMesh { InnerRadius = 0.82f, OuterRadius = 0.86f, Rings = 24, RingSegments = 6 }, new(0, 0.055f, 0),
            new StandardMaterial3D { AlbedoColor = affinityColor, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        var shieldMat = new StandardMaterial3D { AlbedoColor = new Color(Ui.Shield.R, Ui.Shield.G, Ui.Shield.B, 0.22f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true, Emission = Ui.Shield, EmissionEnergyMultiplier = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _shield = Mesh(this, new SphereMesh { Radius = 1.05f, Height = 3.1f }, new(0, 1.45f, 0), shieldMat);
        BuildProtectionBands();
        _ghost = Mesh(this, new SphereMesh { Radius = 0.4f, Height = 1.5f }, new(0, 0.7f, 0),
            new StandardMaterial3D { AlbedoColor = new Color(_color.R, _color.G, _color.B, 0.14f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha });
        _name = new Label3D { Text = p.Name, FontSize = 34, PixelSize = 0.013f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = local ? Ui.Text : _color, OutlineModulate = new("060a12"), OutlineSize = 10, NoDepthTest = true };
        AddChild(_name);
        _healthRoot = new Node3D(); AddChild(_healthRoot);
        Mesh(_healthRoot, new BoxMesh { Size = new(1.42f, 0.13f, 0.025f) }, Vector3.Zero, new StandardMaterial3D { AlbedoColor = new("121c28"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _healthFill = Mesh(_healthRoot, new BoxMesh { Size = new(1.34f, 0.09f, 0.04f) }, new(0, 0, 0.03f),
            new StandardMaterial3D { AlbedoColor = local ? Ui.Health : _color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _shieldTrack = Mesh(_healthRoot, new BoxMesh { Size = new(1.42f, 0.14f, 0.025f) }, new(0, 0.19f, 0),
            new StandardMaterial3D { AlbedoColor = new("332b0b"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _shieldFill = Mesh(_healthRoot, new BoxMesh { Size = new(1.34f, 0.10f, 0.04f) }, new(0, 0.19f, 0.03f),
            new StandardMaterial3D { AlbedoColor = Ui.Shield, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _shieldAmount = new Label3D { Position = new(0, 0.48f, 0.06f), FontSize = 30, PixelSize = 0.012f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = Ui.Shield,
            OutlineModulate = new("060a12"), OutlineSize = 8, NoDepthTest = true };
        _healthRoot.AddChild(_shieldAmount);
        _elementsRoot = new Node3D { Name = "PreparedElements" }; AddChild(_elementsRoot);
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 0.4f;
            Mesh(_elementsRoot, new BoxMesh { Size = new(0.36f, 0.36f, 0.03f) }, new(x, 0, 0),
                new StandardMaterial3D { AlbedoColor = new("8b9bb0"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true });
            var fill = Mesh(_elementsRoot, new BoxMesh { Size = new(0.29f, 0.29f, 0.04f) }, new(x, 0, 0.035f),
                new StandardMaterial3D { AlbedoColor = new("111c2b"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true });
            _elementFills.Add(fill);
            var key = new Label3D { Position = new(x, 0, 0.075f), FontSize = 25, PixelSize = 0.008f,
                Modulate = new("09111e"), OutlineSize = 0, NoDepthTest = true };
            _elementsRoot.AddChild(key); _elementKeys.Add(key);
        }
    }
    private static MeshInstance3D Mesh(Node3D parent, Godot.Mesh mesh, Vector3 position, Material material)
    {
        var node = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; parent.AddChild(node); return node;
    }
    public void Update(WizardSnapshot p, float dt, float time, Camera3D camera, double sampleTime, double interpolationDelay)
    {
        _motion.Advance(p.Position, sampleTime, dt, interpolationDelay,
            p.Alive != _wasAlive || p.Deaths != _deaths || p.Frozen > 0);
        _wasAlive = p.Alive; _deaths = p.Deaths;
        Position = ArenaRenderer.World(_motion.Position);
        if (_wizardType != p.WizardType || _team != p.Team)
        {
            _wizardType = p.WizardType; _team = p.Team;
            _atlas = WizardSprites.Load(_wizardType); _sprite.Texture = _atlas.Texture; _sprite.PixelSize = _atlas.Spec.PixelSize;
            WizardSprites.SetAtlas(_spriteMaterial, _atlas);
            _spriteMaterial.SetShaderParameter("team_color", _team == 0 ? Ui.Blue : Ui.Red);
            _color = _team == 0 ? Ui.Blue : Ui.Red;
            var halo = (StandardMaterial3D)_halo.MaterialOverride; halo.AlbedoColor = halo.Emission = _color;
            ((StandardMaterial3D)_affinityRing.MaterialOverride).AlbedoColor = Ui.WizardColor(_wizardType);
            ((StandardMaterial3D)_healthFill.MaterialOverride).AlbedoColor = _local ? Ui.Health : _color;
            ((StandardMaterial3D)_ghost.MaterialOverride).AlbedoColor = new(_color.R, _color.G, _color.B, 0.14f);
            _name.Modulate = _local ? Ui.Text : _color;
        }
        _facingRow = WizardSprites.FacingRow(p.Facing, _facingRow);
        bool moving = p.Alive && p.Frozen <= 0 && _motion.Moving;
        _castPose = Math.Max(0, _castPose - dt);
        int pose = !p.Alive || p.Frozen > 0 ? 0 : p.Casting || _castPose > 0 ? 3 : moving ? (int)(_motion.WalkCycle * 2) : 0;
        int frame = _facingRow * WizardSprites.Columns + pose;
        if (_sprite.Frame != frame) _sprite.Frame = frame;
        Vector2 offset = _atlas.Offset(frame);
        if (_sprite.Offset != offset) _sprite.Offset = offset;
        _body.Basis = camera.GlobalBasis;
        _body.Position = camera.GlobalBasis.Y * (moving ? Math.Abs(Mathf.Sin(_motion.WalkCycle * Mathf.Tau)) * 0.025f : 0) + new Vector3(0, 0.03f, 0);
        _body.Visible = p.Alive; _ghost.Visible = !p.Alive; _halo.Visible = _shadow.Visible = p.Alive;
        _hitFlash = Math.Max(0, _hitFlash - dt);
        if (p.Alive && p.Health < _previousHealth - 0.5f) _hitFlash = 0.12f;
        _previousHealth = p.Health;
        float flash = _hitFlash / 0.12f * 0.5f;
        if (Math.Abs(flash - _renderedFlash) > 0.001f)
        { _spriteMaterial.SetShaderParameter("hit_flash", flash); _renderedFlash = flash; }
        _healthRoot.Position = camera.GlobalBasis.Y * 2.25f;
        _healthRoot.Basis = camera.GlobalBasis;
        _name.Position = camera.GlobalBasis.Y * (p.WizardType == WizardType.Balanced ? 3.22f : 3.54f);
        _elementsRoot.Position = camera.GlobalBasis.Y * (p.WizardType == WizardType.Balanced ? 3.65f : 4.02f);
        _elementsRoot.Basis = camera.GlobalBasis; _elementsRoot.Visible = p.Alive;
        var elements = p.DisplayElements.Length > 0 ? p.DisplayElements : _castPose > 0 ? _lastCastElements : [];
        DisplayedElements = p.Alive ? elements : [];
        for (int i = 0; i < 3; i++)
        {
            bool filled = p.Alive && i < elements.Length;
            ((StandardMaterial3D)_elementFills[i].MaterialOverride).AlbedoColor = filled ? Ui.ElementColor(elements[i]) : new("111c2b");
            _elementKeys[i].Text = filled ? Ui.ElementKey(elements[i]) : "";
        }
        _affinityRing.Visible = p.Alive && p.WizardType != WizardType.Balanced;
        _affinityRing.Scale = Vector3.One * (1 + Mathf.Sin(time * 2 + p.Id) * 0.04f);
        bool spawnProtected = p.Alive && p.InvulnerableSeconds > 0;
        _shield.Visible = p.Alive && (p.Shield > 0 || p.Frozen > 0 || spawnProtected);
        if (_shield.MaterialOverride is StandardMaterial3D shieldMat)
        {
            var c = p.Frozen > 0 ? Ui.ElementColor(Element.Cold) : p.Ward.Length > 0 ? Ui.SpellColor(p.Ward) : Ui.Shield;
            c.A = p.Frozen > 0 ? 0.42f : 0.12f + 0.10f * Math.Clamp(p.Shield / Ui.ShieldMaximum(p), 0, 1);
            if (p.Shield > 0 && p.Frozen <= 0) c.A += (p.ShieldCharges - 1) * 0.035f * Math.Clamp(p.Shield / Ui.ShieldMaximum(p), 0, 1);
            if (spawnProtected) c = new Color(new Color("fff2a3"), 0.20f + 0.035f * Mathf.Sin(time * 12));
            shieldMat.AlbedoColor = c; shieldMat.Emission = c;
        }
        _shield.Scale = Vector3.One * (1 + Mathf.Sin(time * 3) * 0.03f);
        UpdateProtectionBands(p, time);
        _name.Text = p.Name + (!p.Alive ? $"  {Mathf.CeilToInt(p.Respawn)}s" : spawnProtected ? "  INMUNE" : p.Frozen > 0 ? "  CONGELADO" : p.Burning > 0 ? "  FUEGO" : p.Wet > 0 ? "  MOJADO" : "")
            + (p.WizardType != WizardType.Balanced ? "\n" + WizardTypes.Name(p.WizardType) : "");
        _healthRoot.Visible = p.Alive;
        float hp = Math.Clamp(p.Health / p.MaxHealth, 0, 1);
        _healthFill.Visible = p.Health > 0;
        _healthFill.Scale = new(hp, 1, 1); _healthFill.Position = new(-(1 - hp) * 0.67f, 0, 0.03f);
        bool shielded = p.Alive && p.Shield > 0;
        _shieldTrack.Visible = _shieldFill.Visible = _shieldAmount.Visible = shielded;
        if (shielded)
        {
            float fraction = Math.Clamp(p.Shield / Ui.ShieldMaximum(p), 0, 1);
            _shieldFill.Scale = new(fraction, 1, 1); _shieldFill.Position = new(-(1 - fraction) * 0.67f, 0.19f, 0.03f);
            _shieldAmount.Text = $"{Math.Ceiling(p.Shield):0}/{Ui.ShieldMaximum(p):0}";
        }
    }
    public void PlayCastPose(Element[]? elements = null)
    { _castPose = 0.28f; _lastCastElements = elements?.Take(3).ToArray() ?? []; }
}

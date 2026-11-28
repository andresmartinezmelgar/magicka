using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Rayos con núcleo luminoso, cinco placas frontales y alcance de detonación visible.</summary>
public partial class SpecialMinionVisual : Node3D
{
    private MeshInstance3D _electricity = null!, _deathRadius = null!;
    private Node3D _shield = null!;
    private DefenseSpecialAbility _lastAbility;
    private static ArrayMesh? _bolts;
    private static BoxMesh? _pane;
    private static PlaneMesh? _floor;
    private static Shader? _electricShader, _shieldShader, _warningShader;
    private static ShaderMaterial? _shieldSkin, _previewSkin;
    private static readonly Dictionary<DefenseSpecialAbility, ShaderMaterial> ElectricSkins = [];
    internal const int ElectricBoltCount = 6, FrontalPaneCount = 5;
    internal bool ShieldVisible => _shield.Visible;
    internal bool DeathRadiusVisible => _deathRadius.Visible && Visible;
    internal static Color BodyColor(DefenseSpecialAbility ability, Element? weakness = null)
    {
        var color = new Color(ability == DefenseSpecialAbility.FrontalShield ? "202532" : "3f234f");
        return weakness.HasValue ? color.Lerp(Ui.ElementColor(weakness.Value), .18f) : color;
    }
    private static Color AuraColor(DefenseSpecialAbility ability) =>
        new(ability == DefenseSpecialAbility.FrontalShield ? "55efff" : "e4a0ff");

    internal static ShaderMaterial WarningMaterial(bool preview = false)
    {
        _warningShader ??= GD.Load<Shader>("res://Assets/VFX/special-death-warning.gdshader");
        if (preview && _previewSkin != null) return _previewSkin;
        var material = new ShaderMaterial { Shader = _warningShader, RenderPriority = 8 };
        material.SetShaderParameter("preview", preview);
        if (preview) _previewSkin = material;
        return material;
    }

    private static ShaderMaterial Electricity(DefenseSpecialAbility ability)
    {
        if (ElectricSkins.TryGetValue(ability, out var cached)) return cached;
        _electricShader ??= GD.Load<Shader>("res://Assets/VFX/special-electricity.gdshader");
        var material = new ShaderMaterial { Shader = _electricShader, RenderPriority = 3 };
        material.SetShaderParameter("electric_color", AuraColor(ability));
        ElectricSkins.Add(ability, material); return material;
    }

    private static ArrayMesh Bolts()
    {
        if (_bolts != null) return _bolts;
        using var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        const int segments = 9;
        for (int bolt = 0; bolt < ElectricBoltCount; bolt++)
        {
            float angle = bolt * MathF.Tau / ElectricBoltCount;
            Vector3 Point(int point)
            {
                float jitter = ((point * 5 + bolt * 3) % 7 - 3) * .045f;
                float radius = 1.02f + MathF.Sin(point * 1.7f + bolt) * .09f;
                return new(MathF.Sin(angle) * radius + MathF.Cos(angle) * jitter,
                    .035f + point / (float)segments * (1.16f + bolt % 3 * .055f),
                    MathF.Cos(angle) * radius - MathF.Sin(angle) * jitter);
            }
            void Vertex(Vector3 point, float side, float along)
            {
                surface.SetNormal(Vector3.Back); surface.SetColor(new Color(bolt / (float)ElectricBoltCount, 0, 0));
                surface.SetUV(new(side, along)); surface.AddVertex(point);
            }
            for (int segment = 0; segment < segments; segment++)
            {
                var a = Point(segment); var b = Point(segment + 1);
                float start = segment / (float)segments, end = (segment + 1f) / segments;
                Vertex(a, 0, start); Vertex(b, 0, end); Vertex(b, 1, end);
                Vertex(a, 0, start); Vertex(b, 1, end); Vertex(a, 1, start);
            }
        }
        return _bolts = surface.Commit();
    }

    internal void Build(MinionKind kind)
    {
        Name = "SpecialMinionEffects";
        float radius = kind == MinionKind.Boss ? 1.6f : kind is MinionKind.Brute or MinionKind.Tank ? 1.2f : .9f;
        float height = MinionArt.CasterType(kind) != null ? 2.5f : Math.Max(1.3f, MinionArt.BodyHeight(kind) * 2);
        _electricity = new MeshInstance3D { Name = "SpecialElectricity", Mesh = Bolts(), Scale = new(radius, height, radius),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = .25f };
        AddChild(_electricity);
        _shield = new Node3D { Name = "SpecialFrontalShield", Visible = false }; AddChild(_shield);
        _pane ??= new BoxMesh { Size = Vector3.One };
        _shieldShader ??= GD.Load<Shader>("res://Assets/VFX/special-frontal-shield.gdshader");
        _shieldSkin ??= new ShaderMaterial { Shader = _shieldShader, RenderPriority = 2 };
        for (int i = 0; i < FrontalPaneCount; i++)
        {
            float angle = (i - 2) * MathF.PI * 2 / 15;
            float width = 2 * (radius + .22f) * MathF.Tan(MathF.PI / 15) * .88f;
            _shield.AddChild(new MeshInstance3D
            {
                Name = "FrontalShieldPane" + i, Mesh = _pane,
                Scale = new(width, height * .82f, .055f),
                Position = new(MathF.Sin(angle) * (radius + .22f), height * .49f, MathF.Cos(angle) * (radius + .22f)),
                Rotation = new(0, angle, 0), MaterialOverride = _shieldSkin,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }
        float range = DefenseRules.SpecialExplosionRadius * (kind == MinionKind.Boss ? 1.35f : 1) / .98f;
        _floor ??= new PlaneMesh { Size = Vector2.One * 2 };
        _deathRadius = new MeshInstance3D { Name = "SpecialDetonationRadius", Mesh = _floor,
            Scale = new(range, 1, range), Position = new(0, .065f, 0), MaterialOverride = WarningMaterial(preview: true),
            Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_deathRadius);
        Visible = false;
    }

    internal void Update(MinionSnapshot minion, float time)
    {
        Visible = minion.SpecialAbility != DefenseSpecialAbility.None;
        if (!Visible) { _shield.Hide(); _deathRadius.Hide(); return; }
        if (_lastAbility != minion.SpecialAbility)
        {
            _electricity.MaterialOverride = Electricity(minion.SpecialAbility);
            _lastAbility = minion.SpecialAbility;
        }
        _electricity.Rotation = new(0, time * .38f, 0);
        _shield.Visible = minion.SpecialAbility == DefenseSpecialAbility.FrontalShield &&
            minion.FrontalShieldSeconds > 0 && minion.Frozen <= 0 && !minion.Stunned;
        _shield.Rotation = new(0, MathF.Atan2(minion.Facing.X, minion.Facing.Y), 0);
        _deathRadius.Visible = minion.SpecialAbility == DefenseSpecialAbility.DeathExplosion;
    }

    internal static void ClearCache()
    {
        foreach (var material in ElectricSkins.Values) material.Dispose(); ElectricSkins.Clear();
        _shieldSkin?.Dispose(); _shieldSkin = null; _previewSkin?.Dispose(); _previewSkin = null;
        _bolts?.Dispose(); _bolts = null; _pane?.Dispose(); _pane = null; _floor?.Dispose(); _floor = null;
        _electricShader?.Dispose(); _electricShader = null; _shieldShader?.Dispose(); _shieldShader = null;
        _warningShader?.Dispose(); _warningShader = null;
    }
}

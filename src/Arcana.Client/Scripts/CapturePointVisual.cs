using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>El área representa el progreso firmado del servidor, también durante la neutralización.</summary>
public partial class CapturePointVisual : Node3D
{
    private readonly ShaderMaterial _fill = new();
    private readonly StandardMaterial3D _outline = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    private Label3D _label = null!;
    private MeshInstance3D _crystal = null!;
    private float _progress;
    public float DisplayedProgress => _progress;
    public bool Contested { get; private set; }

    public void Build(int id, V2 position)
    {
        Position = ArenaRenderer.World(position);
        _fill.Shader = new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix;
            uniform float progress : hint_range(0.0, 1.0) = 0.0;
            uniform vec4 team_color : source_color = vec4(0.22, 0.73, 1.0, 1.0);
            void fragment() {
                vec2 p = UV * 2.0 - 1.0;
                float circle = 1.0 - smoothstep(0.985, 1.0, length(p));
                float angle = mod(atan(p.x, -p.y) + 6.283185, 6.283185) / 6.283185;
                float occupied = step(angle, progress) * step(0.0001, progress);
                ALBEDO = mix(vec3(0.035, 0.06, 0.10), team_color.rgb, occupied);
                EMISSION = team_color.rgb * occupied * 0.03;
                ALPHA = circle * mix(0.58, 0.84, occupied);
            }
            """ };
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new(6, 6) },
            Position = new(0, 0.045f, 0), MaterialOverride = _fill, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var contrast = new StandardMaterial3D { AlbedoColor = new("09111e"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D { Name = "CaptureContrast", Mesh = new TorusMesh { InnerRadius = 2.72f, OuterRadius = 3.12f, Rings = 64, RingSegments = 8 },
            Position = new(0, 0.075f, 0), Scale = new(1, 0.20f, 1), MaterialOverride = contrast, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 2.81f, OuterRadius = 3.03f, Rings = 64, RingSegments = 8 },
            Position = new(0, 0.15f, 0), Scale = new(1, 0.35f, 1), MaterialOverride = _outline, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _crystal = new MeshInstance3D { Mesh = new PrismMesh { Size = new(0.38f, 0.6f, 0.38f) }, MaterialOverride = _outline };
        AddChild(_crystal);
        _label = new Label3D { Text = ((char)('A' + id)).ToString(), Position = new(0, 0.32f, -2.3f), FontSize = 34,
            PixelSize = 0.010f, OutlineSize = 10, OutlineModulate = new("060a12"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _label.Name = "CaptureLabel"; AddChild(_label);
    }

    public void Update(PointSnapshot point, float dt, float time)
    {
        float desired = point.Progress;
        if (Math.Abs(desired) < 0.0001f && point.Owner >= 0) desired = point.Owner == 0 ? -1 : 1;
        _progress = Mathf.Lerp(_progress, desired, 1 - Mathf.Exp(-12 * dt));
        Contested = point.Contested;
        Color color = _progress < -0.001f ? Ui.Blue : _progress > 0.001f ? Ui.Red : Ui.Muted;
        _fill.SetShaderParameter("progress", Math.Abs(_progress));
        _fill.SetShaderParameter("team_color", color);
        _outline.AlbedoColor = point.Contested && Math.Sin(time * 7) > 0 ? Ui.Text : color;
        _crystal.Rotation = new(0, time * 0.7f, 0);
        _crystal.Position = new(0, 0.6f + Mathf.Sin(time * 2 + point.Id) * 0.07f, 0);
        string team = _progress < -0.001f ? "AZUL" : _progress > 0.001f ? "ROJO" : "NEUTRAL";
        _label.Text = $"{(char)('A' + point.Id)} · " + (point.Contested ? "DISPUTADO" : $"{team} {Math.Round(Math.Abs(_progress) * 100):0}%");
        _label.Modulate = color;
    }
    public void Reset() => _progress = 0;
}

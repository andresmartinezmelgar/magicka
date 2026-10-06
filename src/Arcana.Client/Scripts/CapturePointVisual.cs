using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Círculo mágico original y progreso firmado por el servidor, también al neutralizar.</summary>
public partial class CapturePointVisual : Node3D
{
    private static readonly string[] Assets =
        ["fire.png", "ice.png", "nature.png", "arcane.png", "solar.png", "crystal.png"];
    private readonly ShaderMaterial _progressMaterial = new();
    private float _progress;
    public float DisplayedProgress => _progress;
    public bool Contested { get; private set; }

    public void Build(int id, V2 position, ArenaMapId map = ArenaMapId.Classic)
    {
        Position = ArenaRenderer.World(position);
        // Los mapas alternan las dos láminas de tres círculos aportadas.
        string path = $"res://Assets/CapturePoints/{Assets[(id + ((int)map % 2) * 3) % Assets.Length]}";
        var texture = ResourceLoader.Load<Texture2D>(path)
            ?? throw new InvalidDataException($"Falta el círculo mágico {path}.");
        var material = new StandardMaterial3D
        {
            AlbedoTexture = texture,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        AddChild(new MeshInstance3D
        {
            Name = "CaptureMagicCircle", Mesh = new PlaneMesh { Size = new(6, 6) },
            Position = new(0, 0.06f, 0), MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });

        _progressMaterial.Shader = ResourceLoader.Load<Shader>("res://Assets/CapturePoints/progress.gdshader")
            ?? throw new InvalidDataException("Falta el shader de progreso de captura.");
        AddChild(new MeshInstance3D
        {
            Name = "CaptureProgress", Mesh = new PlaneMesh { Size = new(6.4f, 6.4f) },
            Position = new(0, 0.09f, 0), MaterialOverride = _progressMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
        Reset();
    }

    public void Update(PointSnapshot point, float dt, float time)
    {
        float desired = point.Progress;
        if (Math.Abs(desired) < 0.0001f && point.Owner >= 0) desired = point.Owner == 0 ? -1 : 1;
        _progress = Mathf.Lerp(_progress, desired, 1 - Mathf.Exp(-12 * dt));
        Contested = point.Contested;
        Color color = _progress < -0.001f ? Ui.Blue : _progress > 0.001f ? Ui.Red : Ui.Muted;
        // Una disputa pulsa en brillo; nunca inventa ni modifica el porcentaje capturado.
        _progressMaterial.SetShaderParameter("progress", Math.Abs(_progress));
        _progressMaterial.SetShaderParameter("team_color", color);
        _progressMaterial.SetShaderParameter("brightness", point.Contested ? 0.65f + 0.35f * Mathf.Sin(time * 7) : 1f);
    }

    public void Reset()
    {
        _progress = 0; Contested = false;
        _progressMaterial.SetShaderParameter("progress", 0f);
        _progressMaterial.SetShaderParameter("brightness", 1f);
    }
}

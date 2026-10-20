using Godot;

namespace Arcana.Client;

/// <summary>Glifos visibles en los segmentos de muro y los anillos defensivos.</summary>
public partial class BarrierVisual
{
    private Node3D? _styleBCrests;

    private void BuildStyleBCrests(float length, bool stone)
    {
        _styleBCrests = new Node3D { Name = "StyleBBarrierCrests" };
        _geometry.AddChild(_styleBCrests);
        int count = Charges > 2 ? 3 : 2;
        // Geometría pequeña compartida por las crestas, sin nuevas luces.
        var shard = new PrismMesh { Size = new(0.14f, 0.34f, 0.18f) };
        var ring = new TorusMesh { InnerRadius = 0.21f, OuterRadius = 0.235f,
            Rings = 24, RingSegments = 4 };
        for (int i = 0; i < count; i++)
        {
            float u = (i + 1.0f) / (count + 1.0f);
            float z = (u - 0.5f) * length;
            float y = stone ? 1.18f + _reinforcement * 0.38f
                : 1.18f + _reinforcement * 0.14f + MathF.Sin(u * Mathf.Pi) * 0.18f;
            float x = stone ? StoneThickness * 0.5f + 0.07f : 0.105f;
            var at = new Vector3(x, y, z);
            var crystal = new MeshInstance3D
            {
                Name = "StyleBCrest" + i,
                Mesh = shard,
                MaterialOverride = _rim,
                Position = at,
                Rotation = new(0, 0, Mathf.Pi / 4),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            _styleBCrests.AddChild(crystal);
            var halo = new MeshInstance3D
            {
                Name = "StyleBRuneRing" + i,
                Mesh = ring,
                MaterialOverride = _rim,
                Position = at - Vector3.Right * 0.025f,
                Rotation = new(0, 0, Mathf.Pi / 2),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            _styleBCrests.AddChild(halo);
        }
    }

    private void UpdateStyleBCrests(float time)
    {
        if (_styleBCrests == null) return;
        _styleBCrests.Scale = new Vector3(1, 1.0f + MathF.Sin(time * 2.2f) * 0.028f, 1);
    }
}

using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private readonly Dictionary<Color, InstanceDrawBatch> _beamCoreBatches = [];
    private Shader? _beamCoreShader;

    private void BatchBeamCore(Vector3 from, Vector3 to, float length, Color color, float radius, float endRadius)
    {
        // La carga cambia el alfa continuamente. Agrupar por RGB evita crear
        // un material/lote por cada valor de potencia de un canal.
        var tint = new Color(color.R, color.G, color.B, 1);
        if (!_beamCoreBatches.TryGetValue(tint, out var batch))
        {
            _beamCoreShader ??= new Shader { Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
                uniform vec4 tint : source_color;
                varying float opacity;
                void vertex() {
                    VERTEX.xz *= mix(INSTANCE_CUSTOM.y, INSTANCE_CUSTOM.x, VERTEX.y + 0.5);
                    opacity = INSTANCE_CUSTOM.z;
                }
                void fragment() {
                    float ends = smoothstep(0.0, 0.055, UV.y) * smoothstep(0.0, 0.11, 1.0 - UV.y);
                    ALBEDO = tint.rgb;
                    EMISSION = tint.rgb * 0.2;
                    ALPHA = opacity * ends;
                }
                """ };
            var material = new ShaderMaterial { Shader = _beamCoreShader }; material.SetShaderParameter("tint", tint);
            _simpleBeamShape ??= new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 10, Rings = 0 };
            _beamCoreBatches[tint] = batch = new(this, "BeamCoreBatch" + _beamCoreBatches.Count,
                _simpleBeamShape, material, 512, custom: true);
        }
        float bottom = endRadius < 0 ? radius : endRadius;
        float widest = Math.Max(radius, bottom);
        var orientation = Basis.LookingAt(to - from) * CylinderFacing;
        var basis = new Basis(orientation.X * widest, orientation.Y * length, orientation.Z * widest);
        batch.Add(new(basis, (from + to) * 0.5f), new(radius / widest, bottom / widest, color.A, 0));
    }

    internal bool VerifyBeamCoreBatch()
    {
        Clear();
        var color = new Color(0.18f, 0.34f, 0.73f, 0.31f);
        Vector3 from = new(-3, 1.4f, -2), to = new(4, 1.4f, 5);
        BeamSegment(from, to, color, 0.12f, 0.4f, profile: 6);
        BeamSegment(to, from, new Color(color, 0.79f), 0.4f, 0.12f, profile: 6);
        foreach (var batch in _beamCoreBatches.Values) batch.Commit();
        var view = GetChildren().OfType<MultiMeshInstance3D>().Single(v =>
            v.Name.ToString().StartsWith("BeamCoreBatch") && v.Visible);
        var t = view.Multimesh.GetInstanceTransform(0); var data = view.Multimesh.GetInstanceCustomData(0);
        bool valid = view.Multimesh.VisibleInstanceCount == 2 && view.Multimesh.InstanceCount == 16 && ActiveBeamSegments == 2 && BeamGeometryUpdates == 0 &&
            (t * new Vector3(0, 0.5f, 0)).IsEqualApprox(from) && (t * new Vector3(0, -0.5f, 0)).IsEqualApprox(to) &&
            Math.Abs(t.Basis.X.Length() * data.R - 0.12f) < 0.001f && Math.Abs(t.Basis.Z.Length() * data.G - 0.4f) < 0.001f &&
            Math.Abs(data.B - 0.31f) < 0.001f && Math.Abs(view.Multimesh.GetInstanceCustomData(1).B - 0.79f) < 0.001f;
        int children = GetChildCount(); Clear();
        valid &= !view.Visible && view.Multimesh.VisibleInstanceCount == 0;
        // Un mismo color cargándose no multiplica materiales ni nodos.
        for (int i = 0; i < 512; i++) BeamSegment(from, to, new Color(color, i / 511f), 0.1f, profile: 6);
        BeamSegment(from, to, color, 0.1f, profile: 6);
        foreach (var batch in _beamCoreBatches.Values) batch.Commit();
        valid &= GetChildCount() == children && ActiveBeamSegments == 512 && view.Multimesh.VisibleInstanceCount == 512 &&
            view.Multimesh.InstanceCount == 512 && view.Multimesh.GetInstanceCustomData(0).B == 0 &&
            view.Multimesh.GetInstanceCustomData(511).B == 1 &&
            view.Multimesh.GetInstanceTransform(0).Origin.IsEqualApprox((from + to) * 0.5f) &&
            view.Multimesh.GetInstanceTransform(511).Origin.IsEqualApprox((from + to) * 0.5f);
        Clear(); valid &= view.Multimesh.InstanceCount == 16 && view.Multimesh.VisibleInstanceCount == 0;
        BeamSegment(from, to, color, 0.2f, profile: 6);
        foreach (var batch in _beamCoreBatches.Values) batch.Commit();
        valid &= view.Multimesh.InstanceCount == 16 && view.Multimesh.VisibleInstanceCount == 1 &&
            Math.Abs(view.Multimesh.GetInstanceCustomData(0).B - color.A) < 0.001f;
        Clear(); return valid;
    }
}

using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private readonly Dictionary<Color, InstanceDrawBatch> _simpleBeamBatches = [];
    private CylinderMesh? _simpleBeamShape;
    private Shader? _simpleBeamOpaque, _simpleBeamTransparent;
    private static readonly Basis CylinderFacing = new(Vector3.Right, Mathf.Pi / 2);

    private void BatchSimpleBeam(Vector3 from, Vector3 to, float length, Color color, float radius, float endRadius)
    {
        if (!_simpleBeamBatches.TryGetValue(color, out var batch))
        {
            bool transparent = color.A < 1;
            var shader = transparent ? _simpleBeamTransparent : _simpleBeamOpaque;
            if (shader == null)
            {
                shader = new Shader { Code = """
                    shader_type spatial;
                    render_mode unshaded, cull_disabled;
                    uniform vec4 tint : source_color;
                    void vertex() {
                        float radius = mix(INSTANCE_CUSTOM.y, INSTANCE_CUSTOM.x, VERTEX.y + 0.5);
                        VERTEX.xz *= radius;
                    }
                    void fragment() {
                        ALBEDO = tint.rgb;
                        EMISSION = tint.rgb * 0.25;
                    """ + (transparent ? "ALPHA = tint.a;\n}" : "}") };
                if (transparent) _simpleBeamTransparent = shader; else _simpleBeamOpaque = shader;
            }
            var material = new ShaderMaterial { Shader = shader }; material.SetShaderParameter("tint", color);
            _simpleBeamShape ??= new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 10, Rings = 0 };
            _simpleBeamBatches[color] = batch = new(this, "SimpleBeamBatch" + _simpleBeamBatches.Count,
                _simpleBeamShape, material, 512, custom: true);
        }
        float bottom = endRadius < 0 ? radius : endRadius;
        float widest = Math.Max(radius, bottom);
        var orientation = Basis.LookingAt(to - from) * CylinderFacing;
        var basis = new Basis(orientation.X * widest, orientation.Y * length, orientation.Z * widest);
        batch.Add(new(basis, (from + to) * 0.5f), new(radius / widest, bottom / widest, 0, 0));
    }

    internal bool VerifySimpleBeamBatch()
    {
        Clear();
        var color = new Color(0.125f, 0.375f, 0.625f);
        Vector3[] starts = [new(1, 2, 3), new(-4, 1, -2), new(3, 0.5f, -7)];
        Vector3[] ends = [new(9, 2, 9), new(-4, 1, -10), new(-5, 0.5f, -7)];
        for (int i = 0; i < starts.Length; i++) BeamSegment(starts[i], ends[i], color, 0.2f, 0.7f);
        foreach (var batch in _simpleBeamBatches.Values) batch.Commit();
        var view = GetChildren().OfType<MultiMeshInstance3D>().Single(v => v.MaterialOverride is ShaderMaterial material &&
            material.GetShaderParameter("tint").AsColor().IsEqualApprox(color));
        bool valid = view.Visible && view.Multimesh.VisibleInstanceCount == 3 && ActiveBeamSegments == 3;
        for (int i = 0; i < starts.Length; i++)
        {
            var t = view.Multimesh.GetInstanceTransform(i); var data = view.Multimesh.GetInstanceCustomData(i);
            valid &= (t * new Vector3(0, 0.5f, 0)).DistanceTo(starts[i]) < 0.001f &&
                (t * new Vector3(0, -0.5f, 0)).DistanceTo(ends[i]) < 0.001f &&
                Math.Abs(t.Basis.X.Length() * data.R - 0.2f) < 0.001f &&
                Math.Abs(t.Basis.Z.Length() * data.G - 0.7f) < 0.001f &&
                view.Multimesh.CustomAabb.HasPoint(starts[i]) && view.Multimesh.CustomAabb.HasPoint(ends[i]);
        }
        int children = GetChildCount(); Clear();
        valid &= !view.Visible && view.Multimesh.VisibleInstanceCount == 0 && ActiveBeamSegments == 0;
        BeamSegment(starts[0], ends[0], color, 0.7f, 0.2f);
        foreach (var batch in _simpleBeamBatches.Values) batch.Commit();
        valid &= GetChildCount() == children && view.Visible && view.Multimesh.VisibleInstanceCount == 1;
        Clear(); return valid;
    }
}

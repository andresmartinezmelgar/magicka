using Godot;

namespace Arcana.Client;

public partial class BarrierVisual
{
    internal int DetailSourceMeshes { get; private set; }
    internal int DetailDrawMeshes { get; private set; }
    internal int DetailSourceVertices { get; private set; }
    internal int DetailBatchedVertices { get; private set; }

    private void BatchDetails()
    {
        // Cada grupo comparte material, sombras y padre animado. No se juntan
        // muros distintos ni las superficies con shaders, sprites o piedra.
        MergeDetails(_geometry, _rim, "BarrierRuneBatch", preserveArch: true);
        if (_iceSkin != null) MergeDetails(_geometry, _iceSkin, "BarrierIceBatch");
        if (_styleBCrests != null) MergeDetails(_styleBCrests, _rim, "BarrierCrestBatch");
    }

    private void MergeDetails(Node3D parent, StandardMaterial3D material, string name, bool preserveArch = false)
    {
        var parts = parent.GetChildren().OfType<MeshInstance3D>().Where(p =>
            ReferenceEquals(p.MaterialOverride, material) && p.Mesh != null && p.Mesh.GetSurfaceCount() == 1 &&
            p.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off &&
            (!preserveArch || p.Name != "BarrierArchColor")).ToArray();
        DetailSourceMeshes += parts.Length;
        if (parts.Length < 2) { DetailDrawMeshes += parts.Length; return; }
        using var tool = new SurfaceTool(); tool.Begin(Godot.Mesh.PrimitiveType.Triangles);
        foreach (var part in parts)
        {
            using var arrays = part.Mesh.SurfaceGetArrays(0);
            DetailSourceVertices += arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array().Length;
            tool.AppendFrom(part.Mesh, 0, part.Transform);
        }
        var combined = tool.Commit();
        using var combinedArrays = combined.SurfaceGetArrays(0);
        DetailBatchedVertices += combinedArrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array().Length;
        parent.AddChild(new MeshInstance3D { Name = name, Mesh = combined, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        DetailDrawMeshes++;
        foreach (var part in parts)
        {
            // Algunas crestas comparten su primitiva: se liberan los nodos,
            // dejando que el recuento de referencias gestione esas mallas.
            parent.RemoveChild(part); part.Free();
        }
    }
}

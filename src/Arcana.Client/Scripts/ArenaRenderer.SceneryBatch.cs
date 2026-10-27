using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    internal int ScenerySourceMeshes { get; private set; }
    internal int SceneryDrawMeshes { get; private set; }
    internal int ScenerySourceVertices { get; private set; }
    internal int SceneryBatchedVertices { get; private set; }
    private const float SceneryChunkSize = 12;

    // Agrupa solo decoración opaca e inmóvil. Las zonas siguen teniendo su propia
    // caja de visibilidad; nunca se convierte todo el mapa en una sola malla.
    private void BatchScenery(Node3D root, bool append = false)
    {
        if (!append) ScenerySourceMeshes = SceneryDrawMeshes = ScenerySourceVertices = SceneryBatchedVertices = 0;
        var meshes = new List<MeshInstance3D>();
        void Collect(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is MeshInstance3D mesh) meshes.Add(mesh);
                Collect(child);
            }
        }
        Collect(root); ScenerySourceMeshes += meshes.Count;
        var inverse = root.GlobalTransform.AffineInverse();
        var groups = new Dictionary<(ulong Material, GeometryInstance3D.ShadowCastingSetting Shadow, int X, int Z), List<(MeshInstance3D Node, Transform3D Transform)>>();
        foreach (var node in meshes)
        {
            // El suelo extenso, su shader y los MultiMesh de plantas mantienen su
            // renderizado original. No se mezclan materiales ni transparencias.
            if (node.Mesh is not PrimitiveMesh primitive || primitive is PlaneMesh ||
                primitive.GetAabb().Size.X > SceneryChunkSize || primitive.GetAabb().Size.Z > SceneryChunkSize ||
                node.MaterialOverride is not StandardMaterial3D material || material.Transparency != BaseMaterial3D.TransparencyEnum.Disabled) continue;
            var transform = inverse * node.GlobalTransform;
            var key = (material.GetInstanceId(), node.CastShadow, (int)MathF.Floor(transform.Origin.X / SceneryChunkSize), (int)MathF.Floor(transform.Origin.Z / SceneryChunkSize));
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = [];
            group.Add((node, transform));
        }
        int removed = 0, serial = 0;
        foreach (var group in groups.Values)
        {
            if (group.Count < 2) continue;
            using var tool = new SurfaceTool(); tool.Begin(Godot.Mesh.PrimitiveType.Triangles);
            foreach (var entry in group)
            {
                using var arrays = entry.Node.Mesh.SurfaceGetArrays(0);
                ScenerySourceVertices += arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array().Length;
                tool.AppendFrom(entry.Node.Mesh, 0, entry.Transform);
            }
            var combined = tool.Commit();
            using var combinedArrays = combined.SurfaceGetArrays(0);
            SceneryBatchedVertices += combinedArrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array().Length;
            root.AddChild(new MeshInstance3D { Name = "SceneryBatch" + serial++, Mesh = combined,
                MaterialOverride = group[0].Node.MaterialOverride, CastShadow = group[0].Node.CastShadow });
            foreach (var entry in group)
            {
                // Estas primitivas se crean exclusivamente para cada nodo de
                // decoración. Una vez copiadas, libera también su enlace C#.
                var original = entry.Node.Mesh!;
                entry.Node.Mesh = null; entry.Node.MaterialOverride = null;
                original.Dispose();
                entry.Node.GetParent().RemoveChild(entry.Node); entry.Node.QueueFree();
            }
            removed += group.Count - 1;
        }
        SceneryDrawMeshes += meshes.Count - removed;
        void Prune(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                Prune(child);
                if (child is Node3D && child is not VisualInstance3D && child.GetChildCount() == 0)
                { node.RemoveChild(child); child.QueueFree(); }
            }
        }
        Prune(root);
    }
}

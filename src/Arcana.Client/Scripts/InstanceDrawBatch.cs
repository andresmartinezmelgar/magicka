using Godot;

namespace Arcana.Client;

/// <summary>Instancias con una sola malla/material y una subida contigua por fotograma.</summary>
internal sealed class InstanceDrawBatch
{
    private readonly MultiMesh _instances;
    private readonly MultiMeshInstance3D _visual;
    private readonly float[] _buffer;
    private readonly Aabb _meshBounds;
    private readonly int _stride;
    private Aabb _bounds;
    private int _visible;
    internal int Count { get; private set; }

    internal InstanceDrawBatch(Node3D parent, string name, Godot.Mesh mesh, Material material, int capacity, bool custom = false)
    {
        _stride = custom ? 16 : 12; _buffer = new float[capacity * _stride];
        _meshBounds = mesh.GetAabb();
        _instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = custom, Mesh = mesh, InstanceCount = capacity, VisibleInstanceCount = 0 };
        _visual = new MultiMeshInstance3D { Name = name, Multimesh = _instances, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        parent.AddChild(_visual);
    }

    internal void Begin() => Count = 0;
    internal void Add(Transform3D transform, Vector4 custom = default)
    {
        int offset = Count * _stride;
        if (offset + _stride > _buffer.Length) throw new InvalidOperationException("Se agotó el lote de instancias visuales.");
        var b = transform.Basis; var p = transform.Origin;
        // MultiMesh almacena tres filas de cuatro floats; Basis expone sus columnas.
        _buffer[offset] = b.X.X; _buffer[offset + 1] = b.Y.X; _buffer[offset + 2] = b.Z.X; _buffer[offset + 3] = p.X;
        _buffer[offset + 4] = b.X.Y; _buffer[offset + 5] = b.Y.Y; _buffer[offset + 6] = b.Z.Y; _buffer[offset + 7] = p.Y;
        _buffer[offset + 8] = b.X.Z; _buffer[offset + 9] = b.Y.Z; _buffer[offset + 10] = b.Z.Z; _buffer[offset + 11] = p.Z;
        if (_stride == 16)
        { _buffer[offset + 12] = custom.X; _buffer[offset + 13] = custom.Y; _buffer[offset + 14] = custom.Z; _buffer[offset + 15] = custom.W; }
        var half = _meshBounds.Size * 0.5f;
        var extent = b.X.Abs() * half.X + b.Y.Abs() * half.Y + b.Z.Abs() * half.Z;
        var bounds = new Aabb(transform * _meshBounds.GetCenter() - extent, extent * 2);
        _bounds = Count == 0 ? bounds : _bounds.Merge(bounds);
        Count++;
    }

    internal void Commit()
    {
        if (Count > 0)
        { _instances.Buffer = _buffer; _instances.CustomAabb = _bounds.Grow(0.03f); }
        if (_visible == Count) return;
        _instances.VisibleInstanceCount = Count; _visual.Visible = Count > 0; _visible = Count;
    }
    internal void Clear() { Begin(); Commit(); }
}

using Godot;

namespace Arcana.Client;

/// <summary>Instancias con una sola malla/material y una subida contigua por fotograma.</summary>
internal sealed class InstanceDrawBatch
{
    private readonly MultiMesh _instances;
    private readonly MultiMeshInstance3D _visual;
    private float[] _buffer;
    private readonly Aabb _meshBounds;
    private readonly int _stride;
    private readonly int _maximum, _initialCapacity;
    private Aabb _bounds;
    private int _visible;
    internal int Count { get; private set; }

    internal InstanceDrawBatch(Node3D parent, string name, Godot.Mesh mesh, Material material, int capacity, bool custom = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _maximum = capacity; _initialCapacity = Math.Min(16, capacity);
        _stride = custom ? 16 : 12; _buffer = new float[_initialCapacity * _stride];
        _meshBounds = mesh.GetAabb();
        _instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = custom, Mesh = mesh, InstanceCount = _initialCapacity, VisibleInstanceCount = 0 };
        _visual = new MultiMeshInstance3D { Name = name, Multimesh = _instances, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        parent.AddChild(_visual);
    }

    internal void Begin() => Count = 0;
    internal void Add(Transform3D transform, Vector4 custom = default)
    {
        int offset = Count * _stride;
        if (Count >= _maximum) throw new InvalidOperationException("Se agotó el lote de instancias visuales.");
        if (offset + _stride > _buffer.Length)
        {
            // Crece solo al superar el máximo usado. Los fotogramas normales
            // reutilizan el buffer y no suben cientos de matrices vacías.
            int capacity = Math.Min(_maximum, _buffer.Length / _stride * 2);
            Array.Resize(ref _buffer, capacity * _stride);
            _instances.InstanceCount = capacity; _visible = -1;
        }
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
    internal void Clear()
    {
        Begin(); Commit();
        // Al terminar/cambiar de escena libera la reserva de un pico anterior.
        // Nunca se reduce el lote durante una animación activa.
        if (_buffer.Length == _initialCapacity * _stride) return;
        _buffer = new float[_initialCapacity * _stride];
        _instances.InstanceCount = _initialCapacity; _instances.VisibleInstanceCount = 0;
    }
}

using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckInstanceBatches()
    {
        bool gpu = DisplayServer.GetName() != "headless";
        bool valid = true;
        // El renderer Dummy devuelve identidad/cero al leer buffers. CI exige además
        // esta misma prueba con viewport OpenGL en escritorio y móvil.
        if (gpu)
        {
            var root = new Node3D(); AddChild(root);
            var batch = new InstanceDrawBatch(root, "BufferProbe", new BoxMesh(), new StandardMaterial3D(), 3, custom: true);
            var expected = new Transform3D((new Basis(Vector3.Up, 0.7f) * new Basis(Vector3.Right, 0.4f)).Scaled(new(2, 3, 4)), new(11, 2, -7));
            var custom = new Color(0.25f, 0.5f, 0.75f, 1);
            batch.Begin(); batch.Add(expected, new(custom.R, custom.G, custom.B, custom.A)); batch.Commit();
            var view = root.GetNode<MultiMeshInstance3D>("BufferProbe");
            valid = view.Visible && view.Multimesh.VisibleInstanceCount == 1 &&
                view.Multimesh.GetInstanceTransform(0).IsEqualApprox(expected) && view.Multimesh.GetInstanceCustomData(0).IsEqualApprox(custom);
            if (!valid) throw new InvalidDataException("INSTANCE_BATCH_FAILED: buffer: " + DisplayServer.GetName() + ", transform=" + view.Multimesh.GetInstanceTransform(0) + ", custom=" + view.Multimesh.GetInstanceCustomData(0));
            batch.Clear(); valid &= !view.Visible && view.Multimesh.VisibleInstanceCount == 0;
            batch.Add(Transform3D.Identity.Translated(new(100, 0, 0))); batch.Commit();
            valid &= view.Multimesh.VisibleInstanceCount == 1 && root.GetChildCount() == 1 &&
                view.Multimesh.GetInstanceTransform(0).Origin.IsEqualApprox(new(100, 0, 0)) &&
                view.Multimesh.CustomAabb.HasPoint(new(100, 0, 0)) && !view.Multimesh.CustomAabb.HasPoint(expected.Origin);
            root.QueueFree();
            if (!valid) throw new InvalidDataException("INSTANCE_BATCH_FAILED: limpieza o límites del lote.");
            valid &= _runtime.Renderer.VerifySimpleBeamBatch();
            if (!valid) throw new InvalidDataException("INSTANCE_BATCH_FAILED: extremos o radios de los rayos.");
            valid &= _runtime.Renderer.VerifyBeamCoreBatch();
            if (!valid) throw new InvalidDataException("INSTANCE_BATCH_FAILED: núcleos, radios, alfa variable o presupuesto de canales.");
        }

        var probe = new CombatEffects(); AddChild(probe);
        var empty = VisualFixture.Create("none") with { Players = [], Projectiles = [], Beams = [], Fields = [], Walls = [], Events = [] };
        void Burst()
        {
            probe.Play(new(1, "impact", V2.Zero, [Element.Fire], ""));
            probe.Play(new(2, "impact", new(2, 0), [Element.Cold], ""));
            probe.Play(new(3, "impact", new(-2, 0), [Element.Water], ""));
            probe.Advance(empty, 0, false);
        }
        Burst();
        var draws = probe.GetChildren().OfType<MultiMeshInstance3D>().ToArray();
        int children = probe.GetChildCount();
        valid &= draws.Length == 3 && draws.All(d => d.Multimesh.InstanceCount == 16) && draws.Sum(d => d.Multimesh.VisibleInstanceCount) == probe.ActiveParticles &&
            probe.ActiveParticles == 18 && draws.All(d => d.Visible && d.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off);
        if (!valid) throw new InvalidDataException($"INSTANCE_BATCH_FAILED: partículas: {draws.Length} lotes, {probe.ActiveParticles} partículas.");
        for (int i = 0; i < 20; i++) probe.Advance(empty, 0.05f, false);
        valid &= probe.ActiveCount == 0 && draws.All(d => !d.Visible && d.Multimesh.VisibleInstanceCount == 0);
        Burst(); valid &= probe.GetChildCount() == children && draws.All(d => d.Visible);
        probe.Clear(); valid &= draws.All(d => !d.Visible && d.Multimesh.VisibleInstanceCount == 0);
        probe.QueueFree();
        if (valid) GD.Print(gpu
            ? "INSTANCE_BATCHES_OK: buffers reales, extremos y radios, alfa por instancia, crecimiento 16→512, liberación de reservas y reutilización, colores y expiración."
            : "INSTANCE_BATCHES_POOL_OK: colores, cantidades, límites, caducidad y reutilización; lectura GPU verificada por CI con viewport real.");
        return valid;
    }
}

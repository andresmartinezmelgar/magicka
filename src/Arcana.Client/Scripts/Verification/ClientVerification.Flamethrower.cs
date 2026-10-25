using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckFlamethrowerVisuals()
    {
        bool gpu = DisplayServer.GetName() != "headless";
        var renderer = _runtime.Renderer;
        var empty = _runtime.Snapshot! with { Players = [], Beams = [], Projectiles = [], Walls = [],
            Fields = [], Points = [], Events = [], Pickups = [], Defense = null };
        var probe = new CombatEffects(); AddChild(probe);
        int[] counts = new int[3]; bool valid = true;
        for (int charges = 1; charges <= 3; charges++)
        {
            probe.Clear();
            var fire = new BeamSnapshot(1, V2.Zero, new(6, 0),
                Enumerable.Repeat(Element.Fire, charges).ToArray(), SpellShape.Spray);
            var scene = empty with { Beams = [fire] };
            for (int i = 0; i < 32; i++) probe.Advance(scene, 1 / 60f, true);
            counts[charges - 1] = probe.ActiveFlames;
            valid &= probe.ActiveFlames > 0 && probe.ActiveFlames == probe.ActiveParticles && probe.FlameBoundsValid();
        }
        valid &= counts[1] > counts[0] && counts[2] > counts[1];
        var batch = probe.GetNode<MultiMeshInstance3D>("FlamethrowerBatch");
        valid &= batch.Multimesh.UseCustomData && batch.Multimesh.VisibleInstanceCount == probe.ActiveFlames;
        int children = probe.GetChildCount();
        // Girar afecta a las nuevas llamas; las anteriores no pivotan con el lanzador.
        probe.Advance(empty with { Beams = [new(1, V2.Zero, new(0, 6), [Element.Fire], SpellShape.Spray)] }, 1 / 60f, true);
        // El servidor Dummy devuelve identidad al leer buffers. Estas lecturas se
        // verifican en CI con viewport real, como las demás pruebas de MultiMesh.
        if (gpu) valid &= Enumerable.Range(0, batch.Multimesh.VisibleInstanceCount)
            .Any(i => batch.Multimesh.GetInstanceTransform(i).Origin.X > 3);
        int afterTurn = probe.ActiveFlames;
        probe.Advance(empty, 1 / 60f, true);
        valid &= probe.ActiveFlames > 0 && probe.ActiveFlames <= afterTurn;
        for (int i = 0; i < 40; i++) probe.Advance(empty, 1 / 60f, false);
        valid &= probe.ActiveCount == 0 && !batch.Visible && batch.Multimesh.VisibleInstanceCount == 0;

        foreach (float scale in new[] { 1f, .35f })
        {
            probe.Clear(); probe.DetailScale = scale;
            var crowded = empty with { Beams = Enumerable.Range(0, 128).Select(i =>
                new BeamSnapshot(i + 1, new(i * 10, 0), new(i * 10 + 6, 0),
                    [Element.Fire, Element.Fire, Element.Fire], SpellShape.Spray)).ToArray() };
            var visibleCasters = new HashSet<int>();
            for (int i = 0; i < 180; i++)
            {
                probe.Advance(crowded, 1 / 60f, true);
                valid &= probe.ActiveFlames <= Math.Max(48, (int)(FlameVisuals.MaximumParticles * scale)) &&
                    probe.ActiveParticles <= CombatEffects.ParticleLimit &&
                    probe.EmittedLastFrame <= CombatEffects.EmissionBudget && probe.FlameBoundsValid();
                for (int j = 0; gpu && j < batch.Multimesh.VisibleInstanceCount; j++)
                    visibleCasters.Add(Mathf.FloorToInt(batch.Multimesh.GetInstanceTransform(j).Origin.X / 10));
            }
            valid &= (!gpu || visibleCasters.Count == 128) && probe.GetChildCount() == children;
        }
        probe.Clear(); probe.DetailScale = 1;
        var steam = new BeamSnapshot(1, V2.Zero, new(6, 0), [Element.Water, Element.Fire], SpellShape.Spray);
        var water = steam with { Elements = [Element.Water] };
        var frost = steam with { Elements = [Element.Cold] };
        valid &= !FlameVisuals.Matches(steam) && !FlameVisuals.Matches(water) && !FlameVisuals.Matches(frost) &&
            !FlameVisuals.Matches(steam with { Elements = [Element.Fire], Shape = SpellShape.Beam });
        for (int i = 0; i < 10; i++) probe.Advance(empty with { Beams = [steam, water, frost] }, 1 / 60f, true);
        valid &= probe.ActiveFlames == 0 && probe.ActiveParticles > 0;
        probe.Clear(); probe.QueueFree();

        var player = _runtime.Snapshot.Players[0];
        V2[] directions = [new(0, 1), new(-1, 0), new(0, -1), new(1, 0)];
        foreach (var type in Enum.GetValues<WizardType>())
        foreach (var direction in directions)
        {
            renderer.Clear();
            var caster = player with { WizardType = type, Position = V2.Zero, Facing = direction,
                Casting = true, CastingElements = [Element.Fire], Alive = true, Frozen = 0,
                Burning = 0, Wet = 0, Chill = 0, Haste = 0, RegenerationRate = 0 };
            var fire = new BeamSnapshot(caster.Id, direction * .7f, direction * 6, [Element.Fire], SpellShape.Spray);
            renderer.Render(empty with { Players = [caster], Beams = [fire] }, caster.Id, 1 / 60.0, true);
            var wizard = renderer.Wizards[caster.Id];
            valid &= wizard.CastingOrigin is { } origin && origin.IsFinite() &&
                origin.DistanceTo(wizard.Position) is > .5f and < 3.5f &&
                renderer.Effects.ActiveFlames > 0 && renderer.ActiveBeamSegments == 0;
            renderer.Render(empty with { Players = [caster], Beams = [fire, water] }, caster.Id, 1 / 60.0, true);
            valid &= renderer.ActiveBeamSegments > 0;
        }
        renderer.Clear();
        valid &= renderer.Effects.ActiveFlames == 0 && renderer.Effects.ActiveCount == 0;
        if (valid) GD.Print(gpu
            ? $"FLAMETHROWER_OK: 20 anclajes, cargas {string.Join('/', counts)}, giro, corte, límites, 128 emisores, detalle bajo y mezclas independientes"
            : $"FLAMETHROWER_POOL_OK: 20 anclajes, cargas {string.Join('/', counts)}, corte, límites, caducidad y limpieza; lectura GPU reservada al viewport real");
        return valid;
    }
}

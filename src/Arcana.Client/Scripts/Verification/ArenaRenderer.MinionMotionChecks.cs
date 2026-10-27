using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    internal void VerifyEnemyCasterMotion(WorldSnapshot original, int localId)
    {
        var origin = original.Players.Single(p => p.Id == localId).Position;
        double originalDelay = InterpolationDelay;
        try
        {
            foreach (int fps in new[] { 30, 60, 120 })
            foreach (double delay in new[] { 0, .075 })
            foreach (var kind in Enum.GetValues<MinionKind>().Where(k => MinionArt.CasterType(k) != null))
            {
                InterpolationDelay = delay;
                int id = 900000 + (int)kind;
                var sample = new MinionSnapshot(id, kind, origin, new(1, 0), 1000, 1000,
                    0, 0, 0, 0, false, [], false);
                var state = original with { Projectiles = [], Walls = [], Fields = [], Beams = [], Events = [],
                    Defense = original.Defense! with { Minions = [sample], Drops = [] } };
                // Una ID nueva fuerza la misma inicialización que al entrar en pantalla.
                Render(state with { Defense = state.Defense! with { Minions = [] } }, localId, 0, true);
                Render(state, localId, 1.0 / fps, true);
                var target = origin + new V2(6, 0);
                state = state with { Time = state.Time + 1.0 / fps,
                    Defense = state.Defense! with { Minions = [sample with { Position = target }] } };
                Render(state, localId, 1.0 / fps, true);
                var visual = _minionVisuals[id].Mage!;
                if (visual.GlobalPosition.DistanceTo(World(target)) > .001f)
                    throw new InvalidOperationException($"El sprite de {kind} arrastra una interpolación adicional tras teletransportarse a {fps} FPS.");
                for (int tick = 1; tick <= fps / 2; tick++)
                {
                    state = state with { Time = state.Time + 1.0 / fps,
                        Defense = state.Defense! with { Minions = [sample with { Position = target + new V2(tick * 3f / fps, 0) }] } };
                    Render(state, localId, 1.0 / fps, true);
                    if (visual.GlobalPosition.DistanceTo(_minionVisuals[id].Root.GlobalPosition) > .001f)
                        throw new InvalidOperationException("Las auras o los avisos se separan del sprite durante la marcha: " + kind);
                }
            }
        }
        finally { InterpolationDelay = originalDelay; Render(original, localId, 0, true); }
        GD.Print("ENEMY_CASTER_MOTION_OK: cuatro magos, 30/60/120 FPS, práctica y red, reaparición y teletransporte sin doble interpolación.");
    }
}

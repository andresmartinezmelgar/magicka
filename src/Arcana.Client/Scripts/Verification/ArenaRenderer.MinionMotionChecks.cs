using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    internal void VerifyEnemyCasterMotion(WorldSnapshot original, int localId)
    {
        VerifySurvivalVisibilityCache(original, localId);
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

    private void VerifySurvivalVisibilityCache(WorldSnapshot original, int localId)
    {
        var savedSource = _visibleSource; var savedState = _visibleState; var savedBounds = _visibleBounds;
        var savedCenter = _survivalCenter; float savedHalfX = _survivalHalfX, savedHalfY = _survivalHalfY;
        try
        {
            _visibleSource = _visibleState = null;
            _survivalCenter = V2.Zero; _survivalHalfX = _survivalHalfY = 8;
            var far = new V2(40, 0);
            var player = original.Players.Single(p => p.Id == localId);
            var sample = original with
            {
                Players = [player with { Position = V2.Zero }, player with { Id = 900001, Position = far }],
                Projectiles = [new(80001, localId, V2.Zero, new(1, 0), [Element.Fire], .2f),
                    new(80002, localId, far, new(1, 0), [Element.Fire], .2f)],
                Walls = [new(80001, localId, V2.Zero, new(1, 0), 100, 1, [Element.Shield]),
                    new(80002, localId, far, far + new V2(1, 0), 100, 1, [Element.Shield])],
                Fields = [new(80001, localId, V2.Zero, 1, 1, [Element.Fire], "spell"),
                    new(80002, localId, far, 1, 1, [Element.Fire], "spell")],
                Beams = [new(localId, V2.Zero, new(1, 0), [Element.Arcane], SpellShape.Beam),
                    new(localId, far, far + new V2(1, 0), [Element.Arcane], SpellShape.Beam)]
            };
            var first = VisibleState(sample);
            if (first.Players.Length != 1 || first.Projectiles.Length != 1 || first.Walls.Length != 1 ||
                first.Fields.Length != 1 || first.Beams.Length != 1)
                throw new InvalidOperationException("La vista de Supervivencia pierde sus límites de visibilidad.");
            string untouched = Protocol.Serialize(first);
            _survivalCenter = new(.1f, 0);
            if (!ReferenceEquals(first, VisibleState(sample)))
                throw new InvalidOperationException("Mover la vista sin cambiar entidades vuelve a construir el estado visible.");
            _survivalCenter = far;
            var shifted = VisibleState(sample);
            if (shifted.Players.Length != 1 || shifted.Players[0].Id != 900001 ||
                shifted.Projectiles.Length != 1 || shifted.Projectiles[0].Id != 80002 ||
                shifted.Walls.Length != 1 || shifted.Walls[0].Id != 80002 ||
                shifted.Fields.Length != 1 || shifted.Fields[0].Id != 80002 ||
                shifted.Beams.Length != 1 || !ReferenceEquals(shifted.Beams[0], sample.Beams[1]) ||
                Protocol.Serialize(first) != untouched)
                throw new InvalidOperationException("Cambiar de zona conserva entidades antiguas o modifica una vista anterior.");
            var fresh = sample with { Tick = sample.Tick + 1, Time = sample.Time + .05 };
            var updated = VisibleState(fresh);
            if (updated.Tick != fresh.Tick || updated.Time != fresh.Time)
                throw new InvalidOperationException("Una selección idéntica conserva el reloj de un snapshot anterior.");
            _survivalHalfX = _survivalHalfY = 100;
            if (!ReferenceEquals(VisibleState(fresh), fresh))
                throw new InvalidOperationException("La vista completa vuelve a copiar todos los arrays del snapshot.");
        }
        finally
        {
            _visibleSource = savedSource; _visibleState = savedState; _visibleBounds = savedBounds;
            _survivalCenter = savedCenter; _survivalHalfX = savedHalfX; _survivalHalfY = savedHalfY;
        }
        GD.Print("SURVIVAL_VISIBILITY_CACHE_OK: entrada y salida de entidades, relojes nuevos y vistas anteriores independientes.");
    }
}

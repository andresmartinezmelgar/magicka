using Arcana.Core;

namespace Arcana.Client;

public partial class Main
{
    private (V2 Target, bool Valid) MagickPreview(WorldSnapshot world, WizardSnapshot player, int slot)
    {
        var map = ArenaMap.Get(world.Map);
        if (slot == 1)
        {
            bool Clear(V2 from, V2 to) => map.ClearPath(from, to) && !world.Walls.Any(w =>
                w.Health > 0 && Geometry.SegmentCapsule(from, to, w.A, w.B, ArenaMap.WizardRadius + 0.18f) != null);
            var arrival = MagickRules.BlinkDestination(player.Position, _aim, Clear);
            return (arrival, V2.Distance(arrival, player.Position) >= 0.4f);
        }
        var target = MagickRules.MeteorTarget(player.Position, _aim);
        return (target, map.Walkable(target) && world.Fields.Length < 40);
    }

    private bool CheckMagickAvailable(int slot, bool checkTarget)
    {
        var world = _connection?.Latest ?? _snapshot;
        var player = world?.Players.FirstOrDefault(p => p.Id == _localId);
        if (player == null || slot is < 0 or > 3) return false;
        float cost = slot == 3 ? player.ReanimationCost : SpellBalance.Magicks[slot].Cost;
        string reason = !player.Alive ? "No puedes lanzar hechizos mientras estás caído." :
            world!.MatchEnded ? "La partida ha terminado." :
            (world.Defense?.Paused == true || DefenseInterfaceOpen) ? "Cierra esta pantalla para volver al combate." :
            player.Frozen > 0 ? "No puedes lanzar hechizos mientras estás congelado." :
            player.Cooldowns[slot] > 0 ? $"{SpellBalance.Magicks[slot].Name}: quedan {Math.Ceiling(player.Cooldowns[slot])} s de recarga." :
            player.Energy < cost ? $"Necesitas {cost:0} de energía para {SpellBalance.Magicks[slot].Name}." :
            slot == 3 && MagickRules.ReanimationTarget(world, player) == null
                ? "Reanimar: necesitas un aliado caído a menos de 8 m con vidas disponibles." :
            checkTarget && slot is 1 or 2 && !MagickPreview(world, player, slot).Valid
                ? slot == 1 ? "Traslación: no hay espacio libre en esa dirección." : "Meteoro: elige un punto sobre el suelo." : "";
        if (reason.Length == 0) return true;
        _hud?.ActionNotice(reason); return false;
    }
}

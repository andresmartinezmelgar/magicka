namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void CreateEquipmentWaterPool(Wizard caster)
    {
        var previous = _fields.FirstOrDefault(f => f.Owner == caster.Id && f.Kind == "equipment_water_pool");
        if (previous != null)
        {
            previous.Position = caster.Position; previous.Lifetime = 5;
            return;
        }
        if (_fields.Count >= 40) return;
        _fields.Add(new Field { Id = _nextEntity++, Owner = caster.Id, Position = caster.Position,
            Radius = 3, Lifetime = 5, Kind = "equipment_water_pool", Recipe = new([Element.Water]) });
        Emit("nova", caster.Position, [Element.Water], "Charco", 3);
    }
    private void ChainEquipmentLightning(Wizard caster, SpellRecipe spell, Wizard[] hit, float dt)
    {
        if (hit.Length == 0) return;
        var visited = new HashSet<int>(hit.Select(p => p.Id)) { caster.Id };
        var previous = hit[^1];
        for (int jump = 0; jump < Math.Min(2, caster.Equipment.LightningChains); jump++)
        {
            Wizard? nearest = null; float distance = 5;
            foreach (var target in _combatants.Values)
            {
                if (!target.Alive || visited.Contains(target.Id) || !Hostile(caster, target)) continue;
                float separation = V2.Distance(previous.Position, target.Position);
                if (separation > distance || separation == distance && nearest != null && target.Id > nearest.Id ||
                    !Visible(previous.Position, target.Position, caster.Id)) continue;
                nearest = target; distance = separation;
            }
            if (nearest == null) break;
            visited.Add(nearest.Id);
            Hit(nearest, spell, dt, caster.Id, (nearest.Position - previous.Position).Normalized);
            if (_beams.Count < 64) _beams.Add(new(caster.Id, previous.Position, nearest.Position, spell.Elements, spell.Shape));
            previous = nearest;
        }
    }
}

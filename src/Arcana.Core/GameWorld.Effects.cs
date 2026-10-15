namespace Arcana.Core;

public sealed partial class GameWorld
{
    internal Field? DemonstrationMine => _fields.FirstOrDefault(f => f.Kind is "mine" or "healing_mine");

    private void AddMine(Wizard owner, SpellRecipe spell, V2 position)
    {
        if (_fields.Count >= 40 || !Walkable(position)) return;
        _fields.Add(new Field { Id = _nextEntity++, Owner = owner.Id, Position = position,
            Radius = 2.4f, Lifetime = 12, Delay = 0.4f, Recipe = spell,
            Kind = spell.Healing ? "healing_mine" : "mine" });
    }

    private bool EligibleForMine(Wizard player, Field mine)
    {
        if (!player.Alive || !_combatants.TryGetValue(mine.Owner, out var owner)) return false;
        return mine.Kind == "healing_mine" ? Allied(owner, player) && player.Health < player.MaxHealth - 0.01f
            : Hostile(owner, player);
    }

    private void ProjectileSplash(Projectile projectile, int directTarget)
    {
        var recipe = projectile.Recipe;
        float radius = recipe.SplashRadius;
        if (radius <= 0) return;
        foreach (var target in _combatants.Values)
        {
            if (!target.Alive || target.Id == directTarget) continue;
            float distance = V2.Distance(target.Position, projectile.Position);
            if (distance >= radius || !Visible(projectile.Position, target.Position, projectile.Owner)) continue;
            float falloff = 0.65f * (1 - distance / radius);
            Hit(target, recipe, projectile.Power * falloff, projectile.Owner, (target.Position - projectile.Position).Normalized);
        }
        Emit("splash", projectile.Position, recipe.Elements, "", radius);
        if (recipe.GroundKind is not { } kind || _fields.Count >= 40) return;
        // La roca ya aplicó su golpe: el suelo conserva solo los elementos secundarios.
        var ground = new SpellRecipe(recipe.Elements.Where(e => e != Element.Earth));
        _fields.Add(new Field { Id = _nextEntity++, Owner = projectile.Owner, Position = projectile.Position,
            Radius = radius * 0.85f, Lifetime = 4, Recipe = ground, Kind = kind });
    }
}

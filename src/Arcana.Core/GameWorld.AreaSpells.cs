namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void SelfCast(Wizard p)
    {
        if (!Ready(p)) return;
        if (p.Channel != null) RecordCastInterruption(p, SpellInterruptionCause.SelfCast);
        Release(p);
        var spell = new SpellRecipe(p.Selection.Consume());
        p.ActionCooldown = 0.45f;
        _statistics.Cast(p.Id, StatisticAttack.Self, spell.Elements);
        float shieldBefore = p.Shield;
        if (spell.Has(Element.Shield))
        {
            p.Ward = spell.Elements.Where(e => e != Element.Shield).Distinct().ToArray();
            p.WardTime = 12 * p.Equipment.ShieldDuration;
            p.Shield = p.ShieldMax = (spell.Has(Element.Earth) ? 650 : p.Ward.Length == 0 ? 420 : 180) * p.Equipment.ShieldCapacity;
            p.ShieldTime = p.WardTime;
            _statistics.Shield(p.Id, p.Shield - shieldBefore);
            p.ShieldCharges = spell.Elements.Length;
            p.RegenerationRate = 38 * spell.Count(Element.Life);
            if (spell.Has(Element.Water) && p.Equipment.WaterShieldPool) CreateEquipmentWaterPool(p);
        }
        else if (spell.Healing)
        {
            HealAs(StatisticAttack.Self, p, SpellBalance.SelfHeal * spell.Count(Element.Life) * HealingPower(p.Id), p.Id);
        }
        else if (spell.Has(Element.Fire) && (p.Wet > 0 || p.Frozen > 0 || p.Chill > 0))
        {
            p.Wet = 0; p.Frozen = 0; p.Chill = 0;
        }
        else if (spell.Has(Element.Water))
        {
            p.Wet = 6 * p.Equipment.WetDuration; p.Burning = 0;
        }
        else if (spell.Elements.Length > 0) HitAs(StatisticAttack.Self, p, spell, 1, p.Id, V2.Zero);
        Emit("self", p.Position, spell.Elements, "");
    }

    private void Nova(Wizard p)
    {
        if (!Ready(p)) return;
        if (p.Channel != null) RecordCastInterruption(p, SpellInterruptionCause.AreaCast);
        Release(p);
        var spell = new SpellRecipe(p.Selection.Consume());
        p.ActionCooldown = 0.8f;
        _statistics.Cast(p.Id, StatisticAttack.Nova, spell.Elements);
        if (spell.Has(Element.Shield))
        {
            if (spell.Healing || spell.Has(Element.Arcane))
            {
                for (int i = 0; i < 6; i++) AddMine(p, spell, p.Position + new V2(1, 0).Rotated(i * MathF.Tau / 6) * 2.5f);
                Emit("mine_place", p.Position, spell.Elements, "");
                return;
            }
            for (int i = 0; i < 6; i++)
            {
                var a = new V2(1, 0).Rotated(i * MathF.Tau / 6);
                var b = new V2(1, 0).Rotated((i + 1) * MathF.Tau / 6);
                AddWall(p, spell, p.Position + a * 2.5f, p.Position + b * 2.5f);
            }
        }
        else
        {
            foreach (var target in _combatants.Values.Where(q => q.Id != p.Id && q.Alive && V2.Distance(q.Position, p.Position) < 4 * p.Equipment.Range))
                if (Visible(p.Position, target.Position, p.Id)) HitAs(StatisticAttack.Nova, target, spell, 1.0f, p.Id, (target.Position - p.Position).Normalized);
        }
        Emit("nova", p.Position, spell.Elements, "", 4 * p.Equipment.Range, IsDefense ? p.Id : 0);
    }

    private void CreateBarrier(Wizard p, SpellRecipe spell)
    {
        var center = p.Position + p.Facing * (3.2f * p.Equipment.Range);
        if (!Map.Inside(center)) return;
        if (spell.Has(Element.Life) || spell.Has(Element.Arcane))
        {
            AddMine(p, spell, center);
        }
        else AddWall(p, spell, center - p.Facing.Perpendicular * 2.2f, center + p.Facing.Perpendicular * 2.2f);
        Emit("barrier", center, spell.Elements, "");
    }

    private void AddWall(Wizard p, SpellRecipe recipe, V2 a, V2 b)
    {
        if (_walls.Count >= 40 || !Map.Inside(a) || !Map.Inside(b) || Map.BoundaryHit(a, b) != null) return;
        // Evita encerrar a alguien dentro de una pared recién creada.
        if (_combatants.Values.Any(q => q.Alive && Geometry.DistanceToSegment(q.Position, a, b) < 0.7f)) return;
        _statistics.Shield(p.Id, (recipe.Has(Element.Earth) ? 1300 : 650) * p.Equipment.ShieldCapacity);
        _walls.Add(new Wall { Id = _nextEntity++, Owner = p.Id, A = a, B = b, Recipe = recipe,
            Health = (recipe.Has(Element.Earth) ? 1300 : 650) * p.Equipment.ShieldCapacity,
            Lifetime = (recipe.Has(Element.Earth) ? 10 : 7) * p.Equipment.ShieldDuration });
    }

    private void Melee(Wizard p)
    {
        if (!Ready(p)) return;
        p.ActionCooldown = 0.5f;
        _statistics.Cast(p.Id, StatisticAttack.Melee);
        foreach (var q in _combatants.Values.Where(q => q.Id != p.Id && q.Alive && V2.Distance(q.Position, p.Position) < 1.8f))
            if (V2.Dot((q.Position - p.Position).Normalized, p.Facing) > 0.2f &&
                Visible(p.Position, q.Position) &&
                (Options.FriendlyFire && !IsDefense || !Allied(p, q))) DamageAs(StatisticAttack.Melee, q, 90, p.Id);
        Emit("melee", p.Position + p.Facing, [], "");
    }

    private void Magick(Wizard p, int slot)
    {
        // Las magias tienen energía y recarga propias: un ataque recién soltado
        // no debe perder el lanzamiento especial al finalizar el gesto móvil.
        if (!p.Alive || p.Frozen > 0 || p.StunTime > 0 || MatchEnded || slot is < 0 or > 3 || p.MagickCooldowns[slot] > 0) return;
        var magick = SpellBalance.Magicks[slot];
        float cost = magick.Cost * (slot == 3 ? p.Equipment.LifeCost : 1);
        if (p.Energy < cost) return;
        var target = new V2(p.Input.AimX, p.Input.AimY);
        if (slot == 0) p.Haste = 4;
        if (slot == 1)
        {
            var direction = (target - p.Position).Normalized;
            var last = MagickRules.BlinkDestination(p.Position, target, ClearMovement);
            if (V2.Distance(last, p.Position) < 0.4f) return;
            Emit("blink", p.Position, [], "");
            p.Position = last; p.Facing = direction; p.Input.MoveTo = false;
            p.Knockback = V2.Zero;
            ResetSlide(p);
            _routes.Remove(p.Id);
            Emit("blink_arrive", p.Position, [], "");
        }
        if (slot == 2)
        {
            target = MagickRules.MeteorTarget(p.Position, target);
            if (!Map.Walkable(target) || _fields.Count >= 40) return;
            _fields.Add(new Field { Id = _nextEntity++, Owner = p.Id, Position = target, Radius = SpellBalance.MeteorRadius,
                Lifetime = SpellBalance.MeteorLifetime, Delay = SpellBalance.MeteorDelay, Kind = "meteor",
                Recipe = new SpellRecipe([Element.Earth, Element.Fire, Element.Fire]) });
        }
        if (slot == 3)
        {
            var ally = _players.Values.Where(q => q.Id != p.Id && MagickRules.CanReanimate(Options.Mode, p.Team, q.Team, q.Alive, q.Deaths, V2.Distance(q.Position, p.Position)))
                .OrderBy(q => V2.Distance(q.Position, p.Position)).ThenBy(q => q.Id).FirstOrDefault();
            if (ally == null) return;
            var location = ally.Position;
            _statistics.Actor(p.Id, StatisticMeasure.Revives); _statistics.Actor(ally.Id, StatisticMeasure.Revived);
            _statistics.Total(StatisticMeasure.Revives);
            Respawn(ally); if (Walkable(location)) ally.Position = location; ally.Health = Math.Min(600, ally.MaxHealth);
        }
        if (slot != 0) InterruptCast(p, SpellInterruptionCause.Magick);
        _statistics.Cast(p.Id, slot switch { 0 => StatisticAttack.Haste, 1 => StatisticAttack.Teleport, 2 => StatisticAttack.Meteor, _ => StatisticAttack.Revive },
            slot == 2 ? [Element.Earth, Element.Fire, Element.Fire] : slot == 3 ? [Element.Life] : null);
        p.Energy -= cost; p.MagickCooldowns[slot] = magick.Cooldown;
        p.ActionCooldown = 0.2f;
        Emit("magick", p.Position, [], magick.Name);
    }
}

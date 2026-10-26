namespace Arcana.Core;

public sealed partial class GameWorld
{
    // La asistencia usa movimiento y hechizos normales. No compra equipo,
    // no concede inmunidad ni cambia la identidad o la participación del humano.
    private void ThinkDefenseAssistance(Wizard player, float dt)
    {
        var progress = _defenders[player.Id];
        progress.AssistanceCooldown = Math.Max(0, progress.AssistanceCooldown - dt);
        progress.AssistanceNavigationCooldown -= dt;
        progress.AssistanceAttackSeconds = Math.Max(0, progress.AssistanceAttackSeconds - dt);
        Wizard? closest = null; float nearest = float.MaxValue;
        foreach (var minion in _minions.Values)
        {
            if (!minion.Body.Alive) continue;
            float distance = V2.Distance(player.Position, minion.Body.Position);
            if (distance < nearest) { closest = minion.Body; nearest = distance; }
        }
        bool hazard = AssistanceHazard(player, player.Position) > 0;
        var movement = V2.Zero;
        if (nearest < 14 || hazard)
        {
            if (progress.AssistanceNavigationCooldown <= 0 || V2.Distance(player.Position, progress.AssistanceGoal) < 1)
            {
                progress.AssistanceGoal = AssistanceRetreatGoal(player);
                progress.AssistanceNavigationCooldown = .35f;
            }
            movement = NavigationDirection(player, progress.AssistanceGoal, dt);
        }
        else progress.AssistanceNavigationCooldown = 0;
        player.Input.MoveTo = false; player.Input.MoveX = movement.X; player.Input.MoveY = movement.Y;
        player.LastInput = Time;

        float shieldCapacity = 650 * player.Equipment.ShieldCapacity;
        bool shieldNeeded = player.Shield <= 0 || player.ShieldTime < 2 && player.Shield <= shieldCapacity ||
            player.Shield < player.ShieldMax * .6f && player.Shield < shieldCapacity;
        bool safeToAttack = !shieldNeeded && player.ShieldTime >= 2 && !hazard && nearest >= 14 && player.Burning <= 0 && player.Health >= player.MaxHealth * .9f;
        if (player.Channel != null && (!safeToAttack || progress.AssistanceAttackSeconds <= 0)) CancelCast(player);
        if (!Ready(player)) return;
        // El escudo se repone incluso sin monstruos cerca y puede interrumpir un ataque.
        if (shieldNeeded)
        {
            CastDefenseAssistance(player, [Element.Shield, Element.Earth]);
            progress.AssistanceCooldown = .45f; return;
        }
        if (progress.AssistanceCooldown > 0) return;
        if (player.Burning > 0)
        {
            CastDefenseAssistance(player, [Element.Water]);
            progress.AssistanceCooldown = .5f; return;
        }
        if (player.Health < player.MaxHealth * .9f)
        {
            CastDefenseAssistance(player, [Element.Life, Element.Life, Element.Life]);
            progress.AssistanceCooldown = .7f; return;
        }
        // Solo canaliza brevemente desde una distancia segura y con la línea libre de aliados.
        if (!safeToAttack || closest == null || nearest > 18 || closest.Invulnerable > 0 || closest.VulnerableElement != null ||
            !Visible(player.Position, closest.Position, player.Id) || AssistanceAllyInLine(player, closest)) return;
        player.Input.AimX = closest.Position.X; player.Input.AimY = closest.Position.Y;
        UpdateFacing(player); CastDefenseAssistance(player, [Element.Arcane, Element.Arcane], attack: true);
        progress.AssistanceAttackSeconds = .3f; progress.AssistanceCooldown = 1.5f;
    }

    private void CastDefenseAssistance(Wizard player, Element[] elements, bool attack = false)
    {
        var selection = player.Selection.Elements.ToArray();
        CancelCast(player); player.Selection.Clear();
        try
        {
            foreach (var element in elements) player.Selection.Add(element);
            if (attack) BeginCast(player); else SelfCast(player);
        }
        finally
        {
            player.Selection.Clear();
            foreach (var element in selection) player.Selection.Add(element);
        }
    }

    private V2 AssistanceRetreatGoal(Wizard player)
    {
        float Safety(V2 point)
        {
            float closest = 24, crowd = 0, crossing = 0;
            var travel = point - player.Position;
            foreach (var minion in _minions.Values)
            {
                if (!minion.Body.Alive) continue;
                float distance = V2.Distance(point, minion.Body.Position);
                closest = Math.Min(closest, distance);
                crowd += MathF.Pow(Math.Max(0, 12 - distance), 2);
                if (travel.LengthSquared <= .01f) continue;
                float along = Math.Clamp(V2.Dot(minion.Body.Position - player.Position, travel) / travel.LengthSquared, 0, 1);
                crossing += Math.Max(0, 2 - V2.Distance(minion.Body.Position, player.Position + travel * along)) * 8;
            }
            return closest * 4 - crowd * .15f - crossing - AssistanceHazard(player, point) * 30 - travel.Length * .1f;
        }
        V2 best = player.Position; float score = Safety(best);
        foreach (float radius in new[] { 4f, 8f })
            for (int i = 0; i < 16; i++)
            {
                float angle = i * MathF.Tau / 16;
                var candidate = player.Position + new V2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                if (!Walkable(candidate) || !ClearMovement(player.Position, candidate)) continue;
                float safety = Safety(candidate);
                if (safety <= score) continue;
                best = candidate; score = safety;
            }
        return best;
    }

    private float AssistanceHazard(Wizard player, V2 at)
    {
        float danger = 0;
        foreach (var field in _fields)
            if (field.Lifetime > 0 && !field.Recipe.Healing && _combatants.TryGetValue(field.Owner, out var owner) && Hostile(player, owner) &&
                V2.Distance(at, field.Position) < field.Radius + 1) danger++;
        return danger;
    }

    private bool AssistanceAllyInLine(Wizard player, Wizard target)
    {
        var direction = target.Position - player.Position;
        if (direction.LengthSquared < .01f) return true;
        foreach (var ally in _players.Values)
        {
            if (ally.Id == player.Id || !ally.Alive || !Allied(player, ally)) continue;
            float along = V2.Dot(ally.Position - player.Position, direction) / direction.LengthSquared;
            if (along > 0 && V2.Distance(ally.Position, player.Position + direction * along) < 1.5f) return true;
        }
        return false;
    }
}

namespace Arcana.Core;

public sealed partial class GameWorld
{
    // Los humanos conservan su identidad, sus defensas y las reglas normales de daño.
    // La ayuda no compra equipo, no gasta oro ni lanza ataques ofensivos.
    private void ThinkDefenseAssistance(Wizard player, float dt)
    {
        var progress = _defenders[player.Id];
        progress.AssistanceCooldown = Math.Max(0, progress.AssistanceCooldown - dt);
        V2 escape = V2.Zero; float nearest = 12;
        foreach (var minion in _minions.Values)
        {
            if (!minion.Body.Alive) continue;
            var away = player.Position - minion.Body.Position; float distance = away.Length;
            if (distance >= 12) continue;
            nearest = Math.Min(nearest, distance);
            if (distance < .01f) away = new(1, 0);
            escape += away.Normalized * (12 - distance) / Math.Max(1, distance);
        }
        var movement = V2.Zero;
        if (escape.LengthSquared > .01f && nearest < 9)
        {
            var goal = SafeDefenseBotGoal(player.Position + escape.Normalized * 6, player.Position);
            movement = NavigationDirection(player, goal, dt);
        }
        player.Input.MoveTo = false; player.Input.MoveX = movement.X; player.Input.MoveY = movement.Y;
        player.LastInput = Time;
        if (progress.AssistanceCooldown > 0 || !Ready(player)) return;
        Element[] spell;
        if (player.Burning > 0) spell = [Element.Water];
        else if (nearest < 10 && (player.Shield <= 0 || player.ShieldTime < 1)) spell = [Element.Shield];
        else if (player.Health < player.MaxHealth * .75f) spell = [Element.Life, Element.Life, Element.Life];
        else return;
        var selection = player.Selection.Elements.ToArray();
        CancelCast(player); player.Selection.Clear();
        foreach (var element in spell) player.Selection.Add(element);
        SelfCast(player);
        player.Selection.Clear();
        foreach (var element in selection) player.Selection.Add(element);
        progress.AssistanceCooldown = 1;
    }
}

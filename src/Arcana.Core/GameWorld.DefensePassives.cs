namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void TickDefensePassives(float dt)
    {
        foreach (var player in _players.Values)
        {
            var progress = _defenders[player.Id];
            progress.BerserkerRemaining = Math.Max(0, progress.BerserkerRemaining - dt);
            if (!player.Alive) continue;
            for (int i = 0; i < progress.Passives.Length; i++)
            {
                int level = progress.Passives[i];
                if (level <= 0) continue;
                progress.PassiveCooldowns[i] = Math.Max(0, progress.PassiveCooldowns[i] - dt);
                if (progress.PassiveCooldowns[i] > 0) continue;
                float range = i switch { 0 => 16, 1 => 18, 2 => 5, _ => 10 };
                var targets = _minions.Values.Where(m => m.Body.Alive && V2.Distance(m.Body.Position, player.Position) < range &&
                    Visible(player.Position, m.Body.Position, player.Id)).OrderBy(m => V2.Distance(m.Body.Position, _castle!.Position)).ToArray();
                if (targets.Length == 0) continue; // Un poder listo espera al combate; nunca cuenta como actividad humana.
                switch ((DefensePassive)i)
                {
                    case DefensePassive.Meteor:
                        if (_fields.Count >= 40) continue;
                        _fields.Add(new Field { Id = _nextEntity++, Owner = player.Id, Position = targets[0].Body.Position,
                            Radius = 3, Lifetime = 1.5f, Delay = 1.4f, Kind = "defense_meteor", Power = 260 * level,
                            Recipe = new SpellRecipe([Element.Earth, Element.Fire]) });
                        progress.PassiveCooldowns[i] = 3;
                        break;
                    case DefensePassive.Berserker:
                        progress.BerserkerRemaining = DefenseRules.BerserkerDuration(level);
                        progress.PassiveCooldowns[i] = 16;
                        Emit("nova", player.Position, [Element.Fire, Element.Arcane], "Berserker", 1.8f);
                        break;
                    case DefensePassive.FrostNova:
                        foreach (var target in targets)
                        {
                            Damage(target.Body, 65 * level * player.Equipment.ElementPower(Element.Cold), player.Id, [Element.Cold]);
                            target.Body.Chill = Math.Min(95, target.Body.Chill + 35 + 8 * level);
                        }
                        progress.PassiveCooldowns[i] = 5;
                        Emit("nova", player.Position, [Element.Cold], "Pulso glacial", 5);
                        break;
                    case DefensePassive.ChainLightning:
                        foreach (var target in targets.Take(Math.Min(6, 2 + level)))
                        {
                            Damage(target.Body, 140 * level * player.Equipment.ElementPower(Element.Lightning), player.Id, [Element.Lightning]);
                            Emit("hurt", target.Body.Position, [Element.Lightning], "Relámpago");
                        }
                        progress.PassiveCooldowns[i] = 4;
                        break;
                }
            }
        }
    }
}

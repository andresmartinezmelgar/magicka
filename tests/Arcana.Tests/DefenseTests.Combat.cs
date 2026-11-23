using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    private static void RunCombat(Action<string, Action> test)
    {
        test("Defensa: mapa exclusivo, un equipo y enemigos fuera de plazas de jugadores", () =>
        {
            var w = World(bots: 3); var p = w.AddPlayer("Humano", 1)!;
            Check(w.Players.Count == 4 && w.Players.Values.All(p => p.Team == 0), "Equipos cooperativos incorrectos.");
            for (int i = 0; i < 20; i++)
            {
                var enemy = Enemy(w);
                Check(w.Map.Walkable(enemy.Position) && w.Players.Values.All(p => V2.Distance(enemy.Position, p.Position) >= DefenseRules.SafeRadius), "Aparición insegura.");
                Check(State(w).Minions.Last().Kind == MinionKind.Melee, "Enemigo avanzado en primera oleada.");
            }
            Check(w.Players.Count == 4 && w.AddPlayer("Quinto") == null, "Los minions consumieron o ampliaron plazas.");
            Check(!w.ApplyMessage(p.Id, new ClientMessage { Type = null!, Sequence = p.LastSequence + 1 }), "Un tipo de acción nulo fue aceptado.");
            Check(State(w).Minions.Select(m => m.Id).Concat(w.Players.Keys).Distinct().Count() == 24, "Los minions y defensores comparten un ID.");
            SnapshotValidation.Check(w.Snapshot());
            foreach (var mode in new[] { GameMode.Arena, GameMode.Duel, GameMode.Warfare })
                Check(!ArenaMap.Get(ArenaMapId.Bastion).SupportsMode(mode), "Bastión admitió un modo competitivo.");
            Check(!ArenaMap.Get(ArenaMapId.Classic).SupportsMode(GameMode.Defense), "Defensa admitió geometría sin castillo.");
        });
        test("Supervivencia: flujo continuo único y dificultad gradual", () =>
        {
            Check(Enum.GetValues<DefenseFlow>().SequenceEqual(new[] { DefenseFlow.Continuous }), "Sigue disponible la modalidad por oleadas.");
            var w = World(); var p = w.AddPlayer("Humano")!; p.Invulnerable = 10000; Advance(w, 120);
            Check(State(w).Minions.Length > 0 && State(w).Difficulty == 1, "No comenzó el flujo continuo.");
            Set(w, "_defenseElapsed", 91.0); Set(w, "<Tick>k__BackingField", 5460L); Advance(w, 2); p.Invulnerable = 1.2f;
            Check(State(w).Difficulty == 2 && DefenseRules.AttackDelay(100) >= 0.4f, "Escalado o delay sin límites.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Defensa: proyectiles, áreas y rayos dañan minions usando el combate existente", () =>
        {
            foreach (var elements in new[] { new[] { Element.Fire, Element.Fire, Element.Fire }, new[] { Element.Arcane, Element.Arcane, Element.Fire }, new[] { Element.Earth, Element.Earth, Element.Earth } })
            {
                var w = World(); var p = w.AddPlayer("Mago")!; p.Position = new(0, 0);
                var enemy = Enemy(w); enemy.Position = new(3, 0); enemy.Health = enemy.MaxHealth = 2000;
                p.Facing = new(1, 0); p.Input.AimX = 10; p.LastInput = w.Time;
                foreach (var e in elements) p.Selection.Add(e);
                Invoke(w, "BeginCast", p);
                if (elements[0] == Element.Earth) { p.ChargeTime = 1; Invoke(w, "Release", p); }
                for (int i = 0; i < 25; i++) { p.LastInput = w.Time; w.Step(); }
                Check(enemy.Health < 2000, "El hechizo no alcanzó entidades de minion: " + elements[0]);
            }
        });
        test("Defensa: experiencia compartida, equipo y reliquias sobreviven a la muerte", () =>
        {
            var w = World(); var p = w.AddPlayer("Mago")!; var ally = w.AddPlayer("Apoyo")!;
            for (int i = 0; i < 10; i++) { Damage(w, Enemy(w), 100000, p.Id); Invoke(w, "CleanupDefense"); }
            Check(Player(w, p).Level == 2 && Player(w, ally).Level == 2 && p.Kills == 10, "No se compartió experiencia o bajas.");
            var item = EquipmentCatalogue.Generate(new Random(1)); Invoke(w, "GrantDefenseItem", p, item);
            Invoke(w, "AddDefenseDrop", p.Position, p.Id, DefenseDropKind.Relic); Invoke(w, "TickDefenseDrops", 0.01f);
            Gold(w, p, 1000); Check(Command(w, p, "defense_aura"), "No compró aura.");
            var before = Player(w, p); int buffs = p.BuffStacks.Sum();
            Damage(w, p, 1000000, Enemy(w).Id);
            Check(!p.Alive && Math.Abs(p.Respawn - 4.5f) < 0.01 && p.BuffStacks.Sum() == buffs, "Muerte borró reliquias o penalización errónea.");
            var after = Player(w, p);
            Check(after.Level == before.Level && after.Items.Length == before.Items.Length && after.Gold == before.Gold && after.Auras.SequenceEqual(before.Auras), "Se perdió progreso temporal.");
            SnapshotValidation.Check(w.Snapshot());
            Advance(w, 271); Check(p.Alive, "No reapareció.");
            Damage(w, p, 1000000, Enemy(w).Id); Check(p.Deaths == 2 && p.Respawn == 0 && !p.Alive && !w.MatchEnded, "No agotó la segunda vida mientras el aliado continúa.");
        });
        test("Defensa: bot aliado apunta hacia amenazas cercanas al defensor", () =>
        {
            var w = World(bots: 1); w.AddPlayer("Humano")!.Position = new(-15, 0);
            var bot = w.Players.Values.Single(p => p.Bot); bot.Position = new(-24, 0);
            var enemy = Enemy(w); enemy.Position = new(-28.7f, 0); float health = enemy.Health;
            Advance(w, 45);
            Check(bot.Facing.X < -0.9f && enemy.Health < health, $"El bot disparó al lado opuesto de la amenaza: facing={bot.Facing}, salud={enemy.Health}/{health}, posición={bot.Position}.");
        });
    }
}

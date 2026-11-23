using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class DefenseBotTargetTests
{
    private static MethodInfo Method(string name) => typeof(GameWorld).GetMethod(name,
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;
    private static Wizard Body(object minion) => (Wizard)minion.GetType().GetField("Body")!.GetValue(minion)!;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static GameWorld World() => new(new WorldOptions { Mode = GameMode.Defense,
        Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4, Seed = 71 });

    public static void Run(Action<string, Action> test)
    {
        test("Bots: el humano más próximo conserva el primer empate y los extremos de distancia", () =>
        {
            var bot = new Wizard { Id = 20, Name = "Médico", Bot = true, Position = V2.Zero };
            var a = new Wizard { Id = 2, Name = "A", Position = new(3, 0) };
            var b = new Wizard { Id = 1, Name = "B", Position = new(-3, 0) };
            var values = new List<Wizard> { a, b };
            foreach (var position in new[] { new V2(3, 0), new V2(float.NaN, 0), new V2(float.PositiveInfinity, 0) })
            {
                a.Position = position;
                var expected = values.MinBy(human => (human.Position - bot.Position).LengthSquared);
                var actual = Method("ClosestDefenseHuman").Invoke(null, new object[] { values, bot });
                Check(ReferenceEquals(actual, expected), "Cambió el empate o la comparación de distancias.");
            }
            values.Clear();
            Check(Method("ClosestDefenseHuman").Invoke(null, new object[] { values, bot }) == null,
                "Una lista vacía produjo un ancla inventada.");
        });

        test("Bots: sanación conserva umbrales, continuidad, alcance y exclusión de objetivos asignados", () =>
        {
            var world = World(); var bot = world.AddPlayer("Médico", bot: true)!;
            var a = world.AddPlayer("Herido A")!; var b = world.AddPlayer("Herido B")!;
            bot.Position = V2.Zero; a.Position = new(5, 0); b.Position = new(6, 0);
            bot.Health = bot.MaxHealth; a.Health = 500; b.Health = 500;
            Wizard? Select(int previous, HashSet<int> assigned) =>
                (Wizard?)Method("SelectDefenseHealingTarget").Invoke(world, new object[] { bot, previous, assigned });
            Check(Select(0, []) == a, "La misma proporción de vida no desempató por ID.");
            Check(Select(0, [a.Id]) == b, "Se asignaron dos sanadores al mismo aliado.");
            b.Health = b.MaxHealth; a.Health = 700;
            Check(Select(0, []) == null && Select(a.Id, []) == a, "Cambió la continuidad del objetivo herido.");
            a.Health = 820; Check(Select(a.Id, []) == null, "Se admitió el umbral superior exacto.");
            a.Health = 549; a.Position = new(12, 0);
            Check(Select(0, []) == null, "Se incluyó un objetivo en el límite de alcance.");
            a.Position = new(float.NaN, 0);
            Check(Select(0, []) == null, "Una distancia no finita entró en la selección.");
        });

        test("Bots: selección de amenazas equivale al orden previo con reservas, sesgo y empates", () =>
        {
            var world = World(); var bot = new Wizard { Id = 1, Name = "Defensor", Position = V2.Zero };
            _ = world.AddPlayer("Ancla");
            for (int i = 0; i < 8; i++) Method("SpawnMinion").Invoke(world, null);
            var dictionary = (IDictionary)typeof(GameWorld).GetField("_minions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            var values = dictionary.Values.Cast<object>().ToArray();
            Check(values.Length > 0, "No se prepararon enemigos de prueba.");
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(values[0].GetType()))!;
            foreach (bool reverse in new[] { false, true })
            foreach (int reserved in new[] { 0, 1, 2 })
            foreach (int previous in new[] { 0, Body(values[0]).Id })
            foreach (bool nonFinite in new[] { false, true })
            {
                list.Clear(); foreach (var value in reverse ? values.Reverse() : values) list.Add(value);
                foreach (var value in values) Body(value).Position = new(10, 0);
                if (nonFinite) Body(values[0]).Position = new(float.NaN, 0);
                var assigned = values.Where((_, index) => reserved == 2 || reserved == 1 && index % 2 == 0)
                    .Select(value => Body(value).Id).ToHashSet();
                bool uncovered = values.Any(value => !assigned.Contains(Body(value).Id));
                var expected = list.Cast<object>().Where(value => !uncovered || !assigned.Contains(Body(value).Id))
                    .OrderBy(value => (float)Method("DefenseThreatScore").Invoke(world, new object[] { value, bot, V2.Zero, previous })!)
                    .ThenBy(value => Body(value).Id).FirstOrDefault();
                var actual = Method("SelectDefenseThreat").Invoke(world, new object[] { list, assigned, bot, V2.Zero, previous });
                Check(ReferenceEquals(actual, expected), "La selección cambió respecto al orden por prioridad e ID.");
            }
            list.Clear();
            Check(Method("SelectDefenseThreat").Invoke(world, new object[] { list, new HashSet<int>(), bot, V2.Zero, 0 }) == null,
                "Una lista sin amenazas devolvió un enemigo.");
        });
    }
}

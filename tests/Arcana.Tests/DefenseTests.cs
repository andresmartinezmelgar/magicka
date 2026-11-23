using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static GameWorld World(DefenseFlow flow = DefenseFlow.Continuous, int bots = 0) => new(new WorldOptions
        { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4, DefenseFlow = flow, Seed = 37, Bots = bots });
    private static object Field(GameWorld world, string name) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
    private static void Set(GameWorld world, string name, object value) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, value);
    private static object? Invoke(GameWorld world, string name, params object?[] args) => typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, args);
    private static DefenseSnapshot State(GameWorld world) => world.Snapshot().Defense!;
    private static DefensePlayerSnapshot Player(GameWorld world, Wizard p) => State(world).Defenders.Single(d => d.PlayerId == p.Id);
    private static void Gold(GameWorld world, Wizard p, int value) => ((IDictionary)Field(world, "_defenders"))[p.Id]!.GetType().GetField("Gold")!.SetValue(((IDictionary)Field(world, "_defenders"))[p.Id], value);
    private static void End(GameWorld world, int owner)
    {
        foreach (var human in world.Players.Values.Where(p => !p.Bot))
        { human.Health = human.MaxHealth; human.Deaths = 1; Damage(world, human, 1000000, owner); }
    }
    private static Wizard Enemy(GameWorld world)
    {
        Invoke(world, "SpawnMinion");
        int id = State(world).Minions.Last().Id;
        return ((Dictionary<int, Wizard>)Field(world, "_combatants"))[id];
    }
    private static bool Command(GameWorld world, Wizard p, string type, int slot = 0, string itemId = "") =>
        world.ApplyMessage(p.Id, new() { Type = type, Slot = slot, ItemId = itemId, Sequence = p.LastSequence + 1 });
    private static void Damage(GameWorld world, Wizard target, float amount, int owner)
    { target.Invulnerable = 0; Invoke(world, "Damage", target, amount, owner, null); }
    private static void Advance(GameWorld world, int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    private sealed class FixedTierRoll(int roll) : Random(73)
    {
        private bool _first = true;
        public override int Next(int maxValue) { if (_first) { _first = false; return roll; } return base.Next(maxValue); }
    }

    public static void Run(Action<string, Action> test)
    {
        SurvivalEquipmentSetupTests.Run(test);
        test("Defensa: recalcular equipo o nivel no cura sin un efecto curativo", () =>
        {
            var world = World(); var player = world.AddPlayer("Mago", 0)!;
            player.Health = 500;
            var state = ((IDictionary)Field(world, "_defenders"))[player.Id]!;
            state.GetType().GetField("Level")!.SetValue(state, 2);
            Invoke(world, "RecalculateDefender", player);
            Check(player.MaxHealth == 1050 && player.Health == 500, "Subir el máximo concedió curación implícita.");
            player.Health = .25f; Invoke(world, "RecalculateDefender", player);
            Check(player.Health == .25f, "Recalcular elevó la vida mínima a 1.");
            Invoke(world, "TickStatus", player, 1f);
            Check(player.Health == .25f, "Se cura sin escudo de Vida, reliquia ni otro efecto.");
        });
        DefenseBalanceTests.Run(test);
        DefenseHordeTests.Run(test);
        SurvivalTests.Run(test);
        RunCatalogue(test);
        RunCombat(test);
        RunEconomy(test);
        RunContinuity(test);
        RunBotInventory(test);
        RunAssistance(test);
        RunProgression(test);
        RunPowers(test);
        DefenseBotTargetTests.Run(test);
    }

}

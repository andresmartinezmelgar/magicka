namespace Arcana.Core;

/// <summary>Escenario local reproducible: simulación real, cuatro defensores, inventario, poderes y portales.</summary>
public sealed class SurvivalPerformanceSession
{
    public GameWorld World { get; } = GameWorld.CreateSurvivalPerformanceWorld();
    private double _accumulator;
    private int _portal;
    public long SimulatedTicks { get; private set; }
    public int PortalCrossings { get; private set; }
    public WorldSnapshot Frame(double delta)
    {
        _accumulator += Math.Clamp(delta, 0, 0.25);
        while (_accumulator >= GameWorld.StepSeconds)
        {
            foreach (var p in World.Players.Values) p.Invulnerable = 1.2f;
            var human = World.Players.Values.First(p => !p.Bot);
            if (SimulatedTicks % 120 == 0)
            { human.Position = _portal++ % 2 == 0 ? new(-76, -52) : new(76, -52); PortalCrossings++; }
            if (SimulatedTicks % 60 == 0)
            {
                World.ApplyMessage(human.Id, new() { Type = "input", Sequence = human.LastSequence + 1,
                    AimX = human.Position.X + 8, AimY = human.Position.Y });
                foreach (var element in new[] { Element.Arcane, Element.Arcane, Element.Fire })
                    World.ApplyMessage(human.Id, new() { Type = "element", Sequence = human.LastSequence + 1, Element = element });
                World.ApplyMessage(human.Id, new() { Type = "cast", Sequence = human.LastSequence + 1 });
            }
            human.LastInput = World.Time;
            World.Step(); SimulatedTicks++; _accumulator -= GameWorld.StepSeconds;
        }
        return World.Snapshot();
    }
}

public sealed partial class GameWorld
{
    internal static GameWorld CreateSurvivalPerformanceWorld()
    {
        var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Bots = 3, Seed = 271 });
        w.AddPlayer("Medición"); w._defensePartySize = 4; w._defenseElapsed = 810; w._defenseDifficulty = 10; w.Tick = 48600;
        foreach (var player in w.Players.Values)
        {
            var progress = w._defenders[player.Id]; progress.Performance = new(DefenseRules.MaxMinions);
            Array.Fill(progress.Auras, 3); Array.Fill(progress.Passives, 1);
            for (int i = 0; i < DefenseRules.InventoryLimit; i++)
                w.GrantDefenseItem(player, EquipmentCatalogue.Generate(new Random(player.Id * 100 + i), tier: ItemTier.Mythic,
                    slot: (EquipmentSlot)(i % 4)) with { Id = $"{player.Id * 100 + i + 1:x32}", CreatedAt = DateTimeOffset.UnixEpoch });
        }
        var kinds = Enum.GetValues<MinionKind>().Where(k => k != MinionKind.Boss).ToArray();
        w.TrySpawnMinion(MinionKind.Boss);
        for (int i = 1; i < DefenseRules.MaxMinions; i++) w.TrySpawnMinion(kinds[i % kinds.Length]);
        foreach (var kind in Enum.GetValues<BuffKind>())
            w._defenseDrops.Add(new() { Id = w._nextEntity++, Kind = DefenseDropKind.Relic, Buff = kind, Position = new(-70, -52) });
        return w;
    }
}

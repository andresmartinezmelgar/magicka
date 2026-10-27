using Arcana.Core;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ComprehensiveAuditTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Get(object owner, string name) => owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Fields)!.SetValue(owner, value);
    private static object? Call(GameWorld world, string name, params object?[] values) =>
        typeof(GameWorld).GetMethod(name, Fields)!.Invoke(world, values);
    private static void Check(bool valid, string reason) { if (!valid) throw new Exception(reason); }
    private static (GameWorld World, Wizard Player) World()
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Seed = 381 });
        Set(world, "_spawnClock", 3600f); Set(world, "_bossClock", 120f);
        var player = world.AddPlayer("Revisión")!; player.Position = new(-8, 0); player.Invulnerable = 0;
        return (world, player);
    }
    private static Wizard Enemy(GameWorld world, MinionKind kind)
    {
        Check((bool)Call(world, "TrySpawnMinion", kind)!, "No se pudo crear el enemigo.");
        return ((Dictionary<int, Wizard>)Get(world, "_combatants"))[world.Snapshot().Defense!.Minions.Last().Id];
    }
    private static void Equipment(Wizard player, EquipmentModifiers equipment) =>
        typeof(Wizard).GetProperty(nameof(Wizard.Equipment))!.SetValue(player, equipment);
    private static void Ground(GameWorld world, Wizard owner, V2 position, Element element)
    {
        var type = typeof(GameWorld).Assembly.GetType("Arcana.Core.Field")!;
        object field = Activator.CreateInstance(type)!;
        int id = (int)Get(world, "_nextEntity"); Set(world, "_nextEntity", id + 1);
        foreach (var (name, value) in new (string, object)[] { ("Id", id), ("Owner", owner.Id), ("Position", position),
            ("Radius", 3f), ("Lifetime", 4f), ("Recipe", new SpellRecipe([element])),
            ("Kind", element == Element.Fire ? "burning_ground" : "frost_ground") }) Set(field, name, value);
        ((IList)Get(world, "_fields")).Add(field);
    }

    public static void Run(Action<string, Action> test)
    {
        test("Revisión integral: una barrera de nivel 100 con equipo máximo se replica sin desconectar", StrongBarrier);
        test("Revisión integral: una roca con Vida cura al aliado y una roca ofensiva lo atraviesa", HealingProjectile);
        foreach (var element in new[] { Element.Fire, Element.Cold })
        {
            test($"Revisión integral: el suelo de {element} conserva potencia y bando tras morir su creador", () => PersistentGround(element, false));
            test($"Revisión integral: guardar y continuar conserva el origen del suelo de {element}", () => PersistentGround(element, true));
            test($"Revisión integral: la asistencia sale de su propio suelo dañino de {element}", () => OwnGround(element));
        }
        test("Revisión integral: un guardado con oro de botín inválido se rechaza antes de jugar", InvalidSavedGold);
        foreach (bool restore in new[] { false, true })
        {
            test($"Revisión integral: el proyectil póstumo conserva daño y bando; continuación={restore}", () => PersistentProjectile(restore));
            test($"Revisión integral: la quemadura conserva potencia sin otros efectos; continuación={restore}", () => PersistentBurn(restore));
        }
        foreach (var element in new[] { Element.Water, Element.Fire, Element.Cold, Element.Lightning, Element.Arcane })
            test($"Revisión integral: el haz aporta una vez el componente {element} al escudo de su prisma", () => PrismBeamShield(element));
    }

    private static void StrongBarrier()
    {
        var (world, player) = World();
        Set(((IDictionary)Get(world, "_defenders"))[player.Id]!, "Level", 100);
        foreach (var slot in new[] { EquipmentSlot.Boots, EquipmentSlot.Hat, EquipmentSlot.Chest, EquipmentSlot.Weapon })
        {
            var item = EquipmentCatalogue.Generate(new(11 + (int)slot), tier: ItemTier.Mythic, slot: slot)
                with { Bonuses = [new(EquipmentStat.ShieldCapacity, 0, .3f, .2f)] };
            Call(world, "GrantDefenseItem", player, item);
        }
        player.Selection.Add(Element.Shield); player.Selection.Add(Element.Earth);
        Check(world.ApplyMessage(player.Id, new() { Type = "cast", Sequence = 1 }), "No pudo crear la barrera.");
        var state = world.Snapshot();
        Check(state.Walls.Length == 1 && state.Walls[0].Health > 2080.01f, "La prueba no alcanza la capacidad de nivel y equipo.");
        SnapshotValidation.Check(DefenseVisibility.ForPlayer(state, player.Id));
        var wire = Protocol.SerializeSnapshot(new() { Type = "snapshot", Snapshot = state });
        var received = Protocol.DeserializeFrame(wire, wire[0] == 0xA1)!.Snapshot!;
        SnapshotValidation.Check(received);
        Check(Math.Abs(received.Walls[0].Health - state.Walls[0].Health) < .001f, "La red cambió la capacidad.");
    }

    private static void HealingProjectile()
    {
        foreach (bool healing in new[] { true, false })
        {
            var (world, player) = World();
            var ally = world.AddPlayer("Aliado")!; ally.Position = player.Position + new V2(2, 0); ally.Invulnerable = 0; ally.Health = 300;
            var recipe = new SpellRecipe(healing ? [Element.Earth, Element.Life] : [Element.Earth]);
            Call(world, "SpawnProjectile", player, recipe, new V2(1, 0), 1.1f, 13f, .37f);
            for (int i = 0; i < 14; i++) Call(world, "TickProjectiles", GameWorld.StepSeconds);
            Check(healing ? ally.Health > 300 : ally.Health == 300 && world.Snapshot().Projectiles.Length == 1,
                healing ? "La roca de Vida no alcanza al aliado herido." : "La roca ofensiva choca con el aliado.");
        }
    }

    private static void PersistentGround(Element element, bool restore)
    {
        var (world, player) = World();
        var owner = Enemy(world, MinionKind.Warlock); owner.Position = new(7, 0);
        Equipment(owner, owner.Equipment with { Power = 2.5f });
        var ally = Enemy(world, MinionKind.Melee); ally.Position = player.Position;
        Ground(world, owner, player.Position, element);
        float before = player.Health, allyHealth = ally.Health;
        Call(world, "TickWallsAndFields", GameWorld.StepSeconds);
        float damage = before - player.Health;
        Check(damage > 0 && ally.Health == allyHealth, "El campo inicial no respeta potencia y bando.");
        Call(world, "Damage", owner, 100000f, player.Id, null); Call(world, "CleanupDefense");
        Check(!((IDictionary)Get(world, "_combatants")).Contains(owner.Id), "El cadáver sigue entre los combatientes activos.");
        if (restore)
        {
            world = GameWorld.RestoreSurvival(world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N")));
            player = world.Players[player.Id]; ally = ((Dictionary<int, Wizard>)Get(world, "_combatants"))[ally.Id];
        }
        before = player.Health;
        Call(world, "TickWallsAndFields", GameWorld.StepSeconds);
        Check(ally.Health == allyHealth, "El campo enemigo empieza a dañar a sus propios aliados.");
        Check(Math.Abs((before - player.Health) - damage) < .001f, "La muerte o la continuación cambian la potencia del campo.");
        ((IList)Get(world, "_fields")).Clear(); player.Burning = ally.Burning = 0;
        Call(world, "CleanupDefense");
        Check(((IDictionary)Get(world, "_lingeringSources")).Count == 0, "El origen se conserva después de extinguirse todos sus efectos.");
    }

    private static void InvalidSavedGold()
    {
        var (world, player) = World();
        Call(world, "AddDefenseDrop", player.Position, 0, DefenseDropKind.Gold);
        var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
        foreach (int amount in new[] { -1, int.MaxValue })
        {
            var state = JsonNode.Parse(save.State.GetRawText())!;
            state["Drops"]![0]!["Gold"] = amount;
            bool rejected = false;
            try { _ = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(state) }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "El guardado admite oro que hará fallar la recogida o el estado de red.");
        }
    }

    private static void PrismBeamShield(Element weakness)
    {
        var (world, player) = World();
        var target = Enemy(world, MinionKind.Prism); target.Position = player.Position + new V2(4, 0);
        typeof(Wizard).GetProperty("VulnerableElement", Fields)!.SetValue(target, weakness);
        target.Shield = target.ShieldMax = 100; target.ShieldTime = 10;
        var spell = new SpellRecipe(weakness == Element.Arcane ? [Element.Arcane, Element.Fire] : [Element.Arcane, weakness]);
        player.Channel = spell; player.Facing = new(1, 0);
        float component = weakness == Element.Water ? DefenseRules.PrismWaterDamage : SpellBalance.Component(spell, weakness);
        float expected = component * ElementInteractions.ProtectionMultiplier(weakness, ElementInteractions.ShieldProtection) *
            SpellBalance.BeamDamageMultiplier(GameWorld.StepSeconds) * .2f * GameWorld.StepSeconds;
        Call(world, "TickChannels", GameWorld.StepSeconds);
        Check(Math.Abs(100 - target.Shield - expected) < .00005f,
            $"El escudo absorbió {100 - target.Shield} en lugar de {expected}; el componente se perdió o se filtró dos veces.");
        var received = world.MatchStatistics().Metrics.Where(m => m.Group == StatisticGroup.EnemyElement &&
            m.Key / 9 == (int)MinionKind.Prism && m.Measure == StatisticMeasure.ShieldAbsorbed).ToArray();
        Check(received.Length == 1 && received[0].Key % 9 == (int)weakness && Math.Abs(received[0].Value - expected) < .00005,
            "Las estadísticas reparten el daño del escudo entre elementos inmunes.");
    }

    private static void PersistentProjectile(bool restore)
    {
        var (world, player) = World();
        var owner = Enemy(world, MinionKind.Warlock); owner.Position = new(-12, 0);
        Equipment(owner, owner.Equipment with { Power = 2.5f });
        var ally = Enemy(world, MinionKind.Melee); ally.Position = new(-10, 0);
        float allyHealth = ally.Health;
        float expected = SpellBalance.Earth * .7f * 2.5f * WizardTypes.Outgoing(owner.WizardType, Element.Earth);
        Call(world, "SpawnProjectile", owner, new SpellRecipe([Element.Earth]), new V2(1, 0), .7f, 13f, .32f);
        Call(world, "Damage", owner, 100000f, player.Id, null); Call(world, "CleanupDefense");
        if (restore)
        {
            world = GameWorld.RestoreSurvival(world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N")));
            player = world.Players[player.Id]; ally = ((Dictionary<int, Wizard>)Get(world, "_combatants"))[ally.Id];
        }
        float before = player.Health;
        for (int i = 0; i < 30; i++) Call(world, "TickProjectiles", GameWorld.StepSeconds);
        Check(ally.Health == allyHealth && Math.Abs(before - player.Health - expected) < .001f,
            "El proyectil pierde su potencia o impacta a un aliado del lanzador fallecido.");
        Call(world, "CleanupDefense");
        Check(((IDictionary)Get(world, "_lingeringSources")).Count == 0, "La fuente del proyectil no se libera.");
    }

    private static void PersistentBurn(bool restore)
    {
        var (world, player) = World();
        var owner = Enemy(world, MinionKind.Warlock); owner.Position = new(7, 0);
        Equipment(owner, owner.Equipment with { Power = 2.5f });
        Call(world, "Hit", player, new SpellRecipe([Element.Fire]), .1f, owner.Id, V2.Zero, 1f);
        float expected = SpellBalance.BurnPerSecond * GameWorld.StepSeconds * player.BurnMultiplier * 2.5f;
        Call(world, "Damage", owner, 100000f, player.Id, null); Call(world, "CleanupDefense");
        if (restore)
        {
            world = GameWorld.RestoreSurvival(world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N")));
            player = world.Players[player.Id];
        }
        float before = player.Health;
        Call(world, "TickStatus", player, GameWorld.StepSeconds);
        Check(Math.Abs(before - player.Health - expected) < .001f, "La quemadura cambia al retirar el último efecto del lanzador.");
        player.Burning = 0; Call(world, "CleanupDefense");
        Check(((IDictionary)Get(world, "_lingeringSources")).Count == 0, "La fuente de la quemadura no se libera.");
    }

    private static void OwnGround(Element element)
    {
        var (world, player) = World();
        var ally = world.AddPlayer("Aliado")!; ally.Position = new(-8, 5); ally.Invulnerable = 0;
        Ground(world, player, player.Position, element);
        float health = player.Health;
        Call(world, "TickWallsAndFields", GameWorld.StepSeconds);
        Check(player.Health < health, "El área propia no causa daño en el escenario de prueba.");
        Check((float)Call(world, "AssistanceHazard", player, player.Position)! > 0, "La asistencia considera seguro un área propia dañina.");
        Check(world.ApplyMessage(player.Id, new() { Type = "defense_shop", Slot = 1, Sequence = 1 }), "No pudo activar la asistencia.");
        var before = player.Position; world.Step();
        Check(V2.Distance(before, player.Position) > .01f, "La asistencia no abandona su propio campo.");
    }
}

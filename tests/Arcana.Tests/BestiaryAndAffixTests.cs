using Arcana.Core;
using System.Reflection;

internal static class BestiaryAndAffixTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object? Call(GameWorld world, string method, params object?[] args) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, args);
    private static (GameWorld World, Wizard Caster, Wizard[] Targets) Line(EquipmentModifiers equipment)
    {
        var world = new GameWorld(new() { Mode = GameMode.Arena, MaxPlayers = 8, SpawnSeed = 7 });
        var caster = world.AddPlayer("Caster", 0)!;
        caster.Position = new(-4, 0); caster.Invulnerable = 0; caster.Facing = new(1, 0);
        world.EquipPlayer(caster.Id, equipment);
        var targets = Enumerable.Range(0, 4).Select(i =>
        {
            var p = world.AddPlayer("Target " + i, 1)!;
            p.Position = new(-1 + i * 2, 0); p.Invulnerable = 0;
            return p;
        }).ToArray();
        return (world, caster, targets);
    }
    public static void Run(Action<string, Action> test)
    {
        test("Afijos: calidades, rangos, serialización y objetos antiguos", () =>
        {
            foreach (var tier in Enum.GetValues<ItemTier>())
            for (int seed = 0; seed < 80; seed++)
            {
                var item = EquipmentCatalogue.Generate(new(seed), tier: tier);
                Check(EquipmentCatalogue.Valid(item), "Objeto nuevo inválido.");
                Check((item.Affix != null) == (tier >= ItemTier.Diamond), "Afijo en una calidad incorrecta.");
                Check(Protocol.Deserialize<EquipmentItem>(Protocol.Serialize(item))!.Affix == item.Affix, "Se perdió el afijo al guardar.");
                Check(EquipmentCatalogue.Valid(item with { Affix = null, GenerationVersion = 1 }), "Se rechazó un objeto antiguo.");
            }
            Check(!ItemAffixes.Valid(new(ItemAffixKind.BeamPierce, 4), ItemTier.Mythic), "Penetración sin límite.");
            Check(!ItemAffixes.Valid(new(ItemAffixKind.DoubleBurn), ItemTier.Silver), "Afijo en plata.");
            Check(!ItemAffixes.Valid(new((ItemAffixKind)99), ItemTier.Mythic), "Afijo desconocido.");
        });
        test("Afijos: varias piezas usan el máximo y no suman saltos", () =>
        {
            var items = new[]
            {
                EquipmentCatalogue.Generate(new(1), tier: ItemTier.Mythic, slot: EquipmentSlot.Boots) with { Affix = new(ItemAffixKind.BeamPierce, 2) },
                EquipmentCatalogue.Generate(new(2), tier: ItemTier.Mythic, slot: EquipmentSlot.Hat) with { Affix = new(ItemAffixKind.BeamPierce, 3) }
            };
            var profile = new AccountProfile("fixture", "Mago", 0, 0, 0, new(items[0].Id, items[1].Id), items);
            var prepared = PublicEquipment.Prepare(profile);
            Check(prepared.Modifiers.BeamPierce == 3, "Se sumó la penetración.");
            PublicEquipment.Check(new(1, "Mago", WizardType.Balanced, false, prepared.View));
        });
        test("Afijos: el haz atraviesa exactamente un enemigo adicional", () =>
        {
            var (world, caster, targets) = Line(new() { BeamPierce = 1 });
            caster.Channel = new([Element.Arcane, Element.Arcane, Element.Arcane]);
            world.Step();
            Check(targets[0].Health < 1000 && targets[1].Health < 1000 && targets[2].Health == 1000,
                "El haz perdió la penetración o superó su límite.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Afijos: los escudos detienen la penetración", () =>
        {
            var (world, caster, targets) = Line(new() { BeamPierce = 3 });
            targets[0].Shield = targets[0].ShieldMax = 1000; targets[0].ShieldTime = 10;
            caster.Channel = new([Element.Arcane]);
            world.Step();
            Check(targets[1].Health == 1000, "El haz atravesó un escudo.");
        });
        test("Afijos: el relámpago busca dos saltos nuevos fuera del cono", () =>
        {
            var (world, caster, targets) = Line(new() { LightningChains = 2 });
            targets[0].Position = new(-1, 0); targets[1].Position = new(-5, 0);
            targets[2].Position = new(-7, 0); targets[3].Position = new(-9, 0);
            caster.Channel = new([Element.Lightning]); world.Step();
            Check(targets[0].Health < 1000 && targets[1].Health < 1000 && targets[2].Health < 1000 && targets[3].Health == 1000,
                "Los saltos no respetaron el límite o repitieron objetivos.");
        });
        test("Afijos: doble quemadura y charco de agua real", () =>
        {
            var (world, caster, targets) = Line(new() { BurnPotency = 2, WaterShieldPool = true });
            Call(world, "Hit", targets[0], new SpellRecipe([Element.Fire]), 1f, caster.Id, V2.Zero, 1f);
            Check(targets[0].BurnMultiplier == 2, "No se duplicó el daño periódico.");
            targets[0].Position = caster.Position + new V2(1, 0);
            caster.Selection.Add(Element.Shield); caster.Selection.Add(Element.Water);
            Call(world, "SelfCast", caster); world.Step();
            Check(world.Snapshot().Fields.Any(f => f.Kind == "equipment_water_pool" && f.Radius == 3), "Falta el charco.");
            Check(caster.Wet > 0 && targets[0].Wet > 0 && targets[0].Burning == 0, "El charco no moja a todos o no apaga fuego.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Bestiario: baja real, tipo correcto y crédito exclusivo al autor", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 });
            var a = world.AddPlayer("A", 0)!; var b = world.AddPlayer("B", 0)!;
            Check((bool)Call(world, "TrySpawnMinion", MinionKind.Runner)!, "No se pudo crear el enemigo.");
            int id = world.Snapshot().Defense!.Minions.Single().Id;
            var field = typeof(GameWorld).GetField("_combatants", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var combatants = (Dictionary<int, Wizard>)field.GetValue(world)!;
            combatants[id].Invulnerable = 0;
            Call(world, "Damage", combatants[id], 100000f, a.Id, null);
            string run = Guid.NewGuid().ToString("N");
            var progress = world.BestiaryProgress(a.Id, run, "a");
            Check(progress?.Kills.GetValueOrDefault("runner") == 1 && world.BestiaryProgress(b.Id, run, "b") == null,
                "Se atribuyó la baja a todos o a un tipo incorrecto.");
            Check(BestiaryCatalogue.Valid(progress), "Registro válido rechazado.");
            foreach (var kind in Enum.GetValues<MinionKind>()) Check(BestiaryCatalogue.Description(kind).Length > 20, "Enemigo sin descripción.");
        });
        test("Bestiario: la cola sobrevive a reinicios y no borra una versión posterior", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), "arcana-bestiary-" + Guid.NewGuid().ToString("N"));
            try
            {
                var queue = new BestiaryOutbox(path);
                var first = new BestiaryProgress(Guid.NewGuid().ToString("N"), "alice", 20, new() { ["runner"] = 3 });
                queue.Enqueue(first);
                queue = new(path);
                Check(queue.Pending("alice").Single().Kills["runner"] == 3 && queue.Pending("bob").Length == 0, "Se perdió o mezcló la cola.");
                var latest = first with { Kills = new() { ["runner"] = 5 } };
                queue.Enqueue(latest); queue.Acknowledge(first);
                Check(queue.Pending().Single().Kills["runner"] == 5, "La confirmación antigua borró datos nuevos.");
                queue.Acknowledge(latest); Check(queue.Pending().Length == 0, "No se retiró lo confirmado.");
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        });
    }
}

using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    private static void RunCatalogue(Action<string, Action> test)
    {
        test("Defensa: bestiario descubre, conserva muertos, actualiza la última versión y reinicia", () =>
        {
            var w = World(); var p = w.AddPlayer("Mago")!;
            Check(State(w).Bestiary.Length == 0, "Se revelaron enemigos antes de aparecer.");
            var first = Enemy(w); var initial = State(w).Bestiary.Single();
            Check(initial.Kind == MinionKind.Melee && initial.MaxHealth == first.MaxHealth && initial.Attack == 48 &&
                initial.Spawned == 1 && initial.Killed == 0, "La ficha no coincide con el primer ejemplar.");
            Damage(w, first, 100000, p.Id); Invoke(w, "CleanupDefense");
            Check(State(w).Minions.Length == 0 && State(w).Bestiary.Single().Killed == 1, "Se perdió la ficha del muerto.");
            Set(w, "_defenseDifficulty", 3); Invoke(w, "TrySpawnMinion", MinionKind.Melee);
            var stronger = ((Dictionary<int, Wizard>)Field(w, "_combatants"))[State(w).Minions.Last().Id]; var latest = State(w).Bestiary.Single();
            Check(latest.Difficulty == 3 && latest.MaxHealth == stronger.MaxHealth && latest.MaxHealth > initial.MaxHealth &&
                latest.Attack > initial.Attack && latest.AttackInterval < initial.AttackInterval && latest.Spawned == 2 && latest.Killed == 1,
                "No muestra la versión más reciente.");
            Check(initial.Difficulty == 1 && initial.Killed == 0, "Se mutó una instantánea anterior.");
            var snapshot = DefenseVisibility.ForPlayer(w.Snapshot(), p.Id);
            var decoded = Protocol.Deserialize<ServerMessage>(Protocol.Serialize(new ServerMessage { Type = "snapshot", Snapshot = snapshot }))!.Snapshot!;
            SnapshotValidation.Check(decoded);
            Check(decoded.Defense!.Bestiary.Single() == latest, "La guía no llegó intacta por red.");
            End(w, stronger.Id); Check(w.RestartDefense(), "No se reinició.");
            Check(State(w).Bestiary.Length == 0, "El bestiario se conservó entre partidas.");
        });
        test("Defensa: bestiario rechaza duplicados, valores no finitos y bajas imposibles", () =>
        {
            var w = World(); w.AddPlayer("Mago"); Enemy(w); var snapshot = w.Snapshot(); var e = snapshot.Defense!.Bestiary.Single();
            foreach (var entries in new[] { new[] { e, e }, new[] { e with { Attack = float.NaN } }, new[] { e with { Killed = 2 } }, Array.Empty<DefenseEnemySnapshot>() })
            {
                bool rejected = false;
                try { SnapshotValidation.Check(snapshot with { Defense = snapshot.Defense with { Bestiary = entries } }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Ficha inválida aceptada.");
            }
        });
        test("Defensa: porcentajes de calidad coinciden con las cien tiradas del generador", () =>
        {
            foreach (var tier in Enum.GetValues<ItemTier>())
            {
                int count = Enumerable.Range(0, 100).Count(roll => EquipmentCatalogue.Generate(new FixedTierRoll(roll)).Tier == tier);
                Check(count == EquipmentCatalogue.TierChance(tier), "Guía y generador difieren: " + tier);
            }
            Check(Enum.GetValues<ItemTier>().Sum(EquipmentCatalogue.TierChance) == 100, "Las calidades no suman 100%.");
        });
        test("Defensa: inventario compartido aplica seis ranuras de forma atómica sin importar equipo permanente", () =>
        {
            var world = World(); var owner = world.AddPlayer("Defensor")!; var other = world.AddPlayer("Aliado")!;
            var random = new Random(173);
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                Invoke(world, "GrantDefenseItem", owner, EquipmentCatalogue.Generate(random, tier: ItemTier.Gold, slot: slot));
                Invoke(world, "GrantDefenseItem", owner, EquipmentCatalogue.Generate(random, tier: ItemTier.Diamond, slot: slot));
            }
            var items = Player(world, owner).Items;
            var desired = new EquipmentLoadout();
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
                desired = desired.With(slot, items.Single(i => i.Item.Slot == slot && i.Item.Tier == ItemTier.Diamond).Item.Id);
            bool Apply(EquipmentLoadout? loadout)
            {
                var command = new ClientMessage { Type = "defense_loadout", Sequence = owner.LastSequence + 1, Loadout = loadout };
                var wire = Protocol.Deserialize<ClientMessage>(Protocol.SerializeBinaryAction(command))!;
                return world.ApplyMessage(owner.Id, wire);
            }
            Command(world, owner, "defense_shop", 1);
            Check(Apply(desired) && Player(world, owner).Items.Count(i => i.Equipped && i.Item.Tier == ItemTier.Diamond) == 6,
                "No aplicó las seis posiciones desde el mensaje de red.");
            int gold = Player(world, owner).Gold;
            var invalid = desired.With(EquipmentSlot.Boots, items.First(i => i.Item.Slot == EquipmentSlot.Boots && i.Item.Tier == ItemTier.Gold).Item.Id)
                .With(EquipmentSlot.Weapon, new string('a', 32));
            Check(!Apply(invalid) && Player(world, owner).Items.Where(i => i.Equipped).All(i => i.Item.Tier == ItemTier.Diamond),
                "Un ID desconocido aplicó parcialmente el equipo.");
            Check(!Apply(desired.With(EquipmentSlot.Hat, desired.Boots)) && !Apply(null) &&
                !Apply(desired.With(EquipmentSlot.Hat, null!)), "Aceptó ranuras intercambiadas o selecciones nulas.");
            Check(!world.ApplyMessage(other.Id, new() { Type = "defense_loadout", Sequence = other.LastSequence + 1, Loadout = desired }),
                "El aliado pudo equipar piezas ajenas.");
            owner.Health = 0;
            Check(Apply(new()) && Player(world, owner).Items.All(i => !i.Equipped) && Player(world, owner).Gold == gold &&
                Protocol.Serialize(Player(world, owner).Items.Select(i => i.Item)) == Protocol.Serialize(items.Select(i => i.Item)),
                "Quitar equipo durante la reaparición alteró las piezas o el oro.");
            owner.Health = owner.MaxHealth; Check(Apply(desired), "No recuperó la selección.");
            Invoke(world, "ResetMatch");
            Check(Player(world, owner).Items.Length == 0 && Player(world, owner).Gold == 0, "El equipo se conservó en otra partida.");
            SnapshotValidation.Check(world.Snapshot());
        });
    }
}

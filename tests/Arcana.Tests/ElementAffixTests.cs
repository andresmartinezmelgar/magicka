using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class ElementAffixTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float actual, float expected, string message) =>
        Check(Math.Abs(actual - expected) < .005f, $"{message}: {actual} != {expected}");
    private static object? Call(GameWorld world, string method, params object[] args) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static T Status<T>(Wizard player, string name) =>
        (T)typeof(Wizard).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
    private static void Status(Wizard player, string name, object value) =>
        typeof(Wizard).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);
    private static (GameWorld World, Wizard Source, Wizard Target) Duel(EquipmentModifiers equipment, bool allies = false)
    {
        var world = new GameWorld(new() { Mode = GameMode.Warfare, MaxPlayers = 8, FriendlyFire = true });
        var source = world.AddPlayer("Mago", 0)!; var target = world.AddPlayer("Objetivo", allies ? 0 : 1)!;
        source.Position = new(-3, 0); target.Position = new(-1, 0);
        source.Invulnerable = target.Invulnerable = 0; source.LastInput = target.LastInput = 1000;
        source.Input.AimX = target.Position.X; source.Input.AimY = target.Position.Y;
        world.EquipPlayer(source.Id, equipment);
        return (world, source, target);
    }
    private static void Hit(GameWorld world, Wizard source, Wizard target, params Element[] elements) =>
        Call(world, "Hit", target, new SpellRecipe(elements), GameWorld.StepSeconds, source.Id, V2.Zero, 1f);
    private static void Steps(GameWorld world, int count) { for (int i = 0; i < count; i++) world.Step(); }

    internal static void Run(Action<string, Action> test)
    {
        test("Aturdimiento eléctrico: exige un segundo, no añade daño ni frío e interrumpe movimiento y hechizos", () =>
        {
            foreach (int rank in new[] { 1, 2 })
            {
                var (world, source, target) = Duel(new() { LightningStun = rank });
                var (baseline, plain, normal) = Duel(EquipmentModifiers.None);
                source.Channel = plain.Channel = new([Element.Lightning]);
                Steps(world, 59); Steps(baseline, 59);
                Check(Status<float>(target, "StunTime") == 0, "Aturde antes del segundo.");
                target.Channel = new([Element.Life]); world.Step(); baseline.Step();
                Near(Status<float>(target, "StunTime"), rank == 1 ? .1f : .2f, "Duración incorrecta");
                Near(target.Health, normal.Health, "Añade un bono de daño ya cubierto por atributos");
                Check(target.Channel == null && target.Frozen == 0 && target.Chill == 0 &&
                    world.Snapshot().Players.Single(p => p.Id == target.Id).Stunned, "No interrumpe el canal o simula frío.");
                Check(!world.ApplyMessage(target.Id, new() { Type = "melee", Sequence = 1 }), "Permite atacar durante el aturdimiento.");
                var before = target.Position; target.Input.MoveX = 1;
                Call(world, "Move", target, .1f); Check(target.Position == before, "Permite caminar estando aturdido.");
                Call(world, "TickStatus", target, CombatAffixes.LightningStunSeconds(rank));
                Call(world, "Move", target, .1f);
                Near(V2.Distance(before, target.Position), Wizard.BaseMovementSpeed * .1f, "No recupera el movimiento");
                SnapshotValidation.Check(world.Snapshot());
            }
        });
        test("Aturdimiento eléctrico: las pausas reinician y varios impactos en un tick no adelantan el segundo", () =>
        {
            var (world, source, target) = Duel(new() { LightningStun = 2 });
            source.Channel = new([Element.Lightning]); Steps(world, 59);
            source.Channel = null; world.Step(); source.Channel = new([Element.Lightning]);
            for (int i = 0; i < 59; i++)
            {
                world.Step(); Hit(world, source, target, Element.Lightning); Hit(world, source, target, Element.Lightning);
                Check(Status<float>(target, "StunTime") == 0, "La pausa no reinicia o el impacto doble cuenta dos ticks.");
            }
            world.Step(); Check(Status<float>(target, "StunTime") > 0, "No aturde al completar otro segundo.");
        });
        test("Aturdimiento eléctrico: excluye aliados, invulnerables, inmunes y magos sin el afijo", () =>
        {
            foreach (string scenario in new[] { "ally", "immune", "invulnerable", "no-affix" })
            {
                var (world, source, target) = Duel(new() { LightningStun = scenario == "no-affix" ? 0 : 2 }, allies: scenario == "ally");
                if (scenario == "immune") Status(target, "VulnerableElement", Element.Fire);
                if (scenario == "invulnerable") target.Invulnerable = 10;
                source.Channel = scenario == "ally" ? new([Element.Life, Element.Lightning]) : new([Element.Lightning]);
                Steps(world, 120);
                Check(Status<float>(target, "StunTime") == 0, "Aturde en un caso excluido: " + scenario);
            }
        });
        test("Agua ralentizante: reduce movimiento un 10 o 20 %, sin acumular frío y se elimina al secarse", () =>
        {
            foreach (int rank in new[] { 1, 2 })
            {
                var (world, source, target) = Duel(new() { WaterSlow = rank });
                Hit(world, source, target, Element.Water); Hit(world, source, target, Element.Water);
                target.Input.MoveX = 1; var before = target.Position;
                Call(world, "Move", target, .1f);
                Near(V2.Distance(before, target.Position), Wizard.BaseMovementSpeed * .1f * (rank == 1 ? .9f : .8f), "Velocidad ralentizada");
                Check(target.Chill == 0 && target.Frozen == 0 && world.Snapshot().Players.Single(p => p.Id == target.Id).WaterSlowed,
                    "El agua se convierte en frío o no comunica la ralentización.");
                target.Wet = 0; Call(world, "TickStatus", target, GameWorld.StepSeconds);
                Check(Status<int>(target, "WaterSlowRank") == 0 && Status<float>(target, "WaterSlowTime") == 0,
                    "La ralentización sigue activa al secarse.");
                SnapshotValidation.Check(world.Snapshot());
            }
        });
        test("Agua ralentizante: no prolonga una copia fuerte con otra débil, ni afecta al propio mago o aliados", () =>
        {
            var (world, source, target) = Duel(new() { WaterSlow = 2 });
            Hit(world, source, target, Element.Water); Call(world, "TickStatus", target, .5f);
            world.EquipPlayer(source.Id, new EquipmentModifiers { WaterSlow = 1 }); Hit(world, source, target, Element.Water);
            Near(Status<float>(target, "WaterSlowTime"), 1.5f, "El rango débil prolonga el fuerte");
            Check(Status<int>(target, "WaterSlowRank") == 2, "El rango débil sustituye al fuerte.");
            Call(world, "TickStatus", target, 1.5f);
            Check(Status<int>(target, "WaterSlowRank") == 0 && target.Wet > 0, "No caduca a los dos segundos.");
            Hit(world, source, source, Element.Water); Check(Status<int>(source, "WaterSlowRank") == 0, "Ralentiza al propio mago.");
            var (friendly, ally, recipient) = Duel(new() { WaterSlow = 2 }, allies: true);
            Hit(friendly, ally, recipient, Element.Water); Check(Status<int>(recipient, "WaterSlowRank") == 0, "Ralentiza aliados.");
        });
        test("Agua ralentizante: el charco moja a todos y ralentiza solo a los enemigos", () =>
        {
            var (world, source, target) = Duel(new() { WaterSlow = 2, WaterShieldPool = true });
            var ally = world.AddPlayer("Aliado", 0)!; ally.Position = new(-3, 1); ally.Invulnerable = 0;
            Call(world, "CreateEquipmentWaterPool", source); Call(world, "TickWallsAndFields", GameWorld.StepSeconds);
            Check(source.Wet > 0 && ally.Wet > 0 && target.Wet > 0 && Status<int>(target, "WaterSlowRank") == 2 &&
                Status<int>(source, "WaterSlowRank") == 0 && Status<int>(ally, "WaterSlowRank") == 0, "El charco no distingue aliados y enemigos.");
        });
        test("Estados elementales: guardados antiguos, validación y reconexión conservan los tiempos activos", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4, Seed = 47 });
            var player = world.AddPlayer("Defensor", 0)!; player.Wet = 6;
            Status(player, "StunTime", .1f); Status(player, "WaterSlowTime", 1.5f); Status(player, "WaterSlowRank", 2);
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save).Players[player.Id];
            Near(Status<float>(restored, "StunTime"), .1f, "Pierde el aturdimiento al guardar");
            Near(Status<float>(restored, "WaterSlowTime"), 1.5f, "Pierde la ralentización al guardar");
            var old = JsonNode.Parse(save.State.GetRawText())!; var body = old["Defenders"]![0]!["Body"]!.AsObject();
            foreach (string name in new[] { "StunTime", "WaterSlowTime", "WaterSlowRank" }) body.Remove(name);
            var legacy = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(old) }).Players[player.Id];
            Check(Status<float>(legacy, "StunTime") == 0 && Status<int>(legacy, "WaterSlowRank") == 0, "Un formato anterior inventa estados.");
            var invalid = JsonNode.Parse(save.State.GetRawText())!; invalid["Defenders"]![0]!["Body"]!["StunTime"] = 1;
            bool rejected = false;
            try { GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(invalid) }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Admite un aturdimiento superior al máximo.");
            world.AttachDefenseAccount(player.Id, "element-user"); world.RemovePlayer(player.Id);
            var joined = world.AddPlayer("Defensor", 0)!;
            Check(world.AttachDefenseAccount(joined.Id, "element-user"), "No recupera la cuenta.");
            Near(Status<float>(joined, "StunTime"), .1f, "La reconexión elimina el aturdimiento");
            Near(Status<float>(joined, "WaterSlowTime"), 1.5f, "La reconexión elimina la ralentización");
        });
        test("Estados elementales: el aura de Agua y el canal de Rayo actúan sobre la horda real", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4, Seed = 47 });
            var source = world.AddPlayer("Mago", 0)!; source.Position = new(-25, 0); source.Invulnerable = 0; source.LastInput = 1000;
            var pieces = new[]
            {
                EquipmentCatalogue.Generate(new(471), tier: ItemTier.Mythic, slot: EquipmentSlot.Boots, family: EquipmentFamily.Lightning)
                    with { Affix = new(ItemAffixKind.LightningStun, 2) },
                EquipmentCatalogue.Generate(new(472), tier: ItemTier.Mythic, slot: EquipmentSlot.Weapon, family: EquipmentFamily.Water)
                    with { Affix = new(ItemAffixKind.WaterSlow, 2) }
            };
            foreach (var item in pieces)
            {
                Call(world, "GrantDefenseItem", source, item);
                Check(world.ApplyMessage(source.Id, new() { Type = "defense_equip", Sequence = source.LastSequence + 1, ItemId = item.Id }),
                    "No equipa el afijo en Supervivencia.");
            }
            Check((bool)Call(world, "TrySpawnMinion", MinionKind.Melee)!, "No se crea el enemigo.");
            var minions = (IDictionary)typeof(GameWorld).GetField("_minions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            var minion = minions.Values.Cast<object>().Last();
            var target = (Wizard)minion.GetType().GetField("Body")!.GetValue(minion)!;
            target.Position = new(-23, 0); target.Health = target.MaxHealth = 1000; target.Frozen = 2; target.Invulnerable = 0;
            var defenders = (IDictionary)typeof(GameWorld).GetField("_defenders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            var progress = defenders[source.Id]!;
            var auras = (int[])progress.GetType().GetField("Auras")!.GetValue(progress)!;
            auras[(int)DefenseAura.Water] = 1;
            Call(world, "TickDefenseAuras", GameWorld.StepSeconds);
            Check(target.Wet > 0 && Status<int>(target, "WaterSlowRank") == 2, "El aura no aplica el afijo de Agua.");
            source.Input.AimX = target.Position.X; source.Input.AimY = target.Position.Y; source.Channel = new([Element.Lightning]);
            Steps(world, 60);
            var state = world.Snapshot().Defense!.Minions.Single(m => m.Id == target.Id);
            Check(state.Stunned && state.WaterSlowed && !state.Casting, "La horda no recibe o no comunica los estados.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Estados elementales: deltas de enemigos transmiten activación y fin del aturdimiento y ralentización", () =>
        {
            var original = new MinionSnapshot(1000, MinionKind.Melee, new(-1, 0), new(1, 0), 1000, 1000, 6, 0, 0, 0, false, [], false);
            var active = original with { Stunned = true, WaterSlowed = true };
            var codec = new MinionDeltaCodec([original]);
            var result = codec.Decode(codec.Encode([active])).Single();
            Check(result.Stunned && result.WaterSlowed, "La delta pierde los estados nuevos.");
            var end = new MinionDeltaCodec([active]); result = end.Decode(end.Encode([original])).Single();
            Check(!result.Stunned && !result.WaterSlowed, "La delta mantiene estados caducados.");
        });
    }
}

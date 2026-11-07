using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class AutomaticRockTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float a, float b, string message) => Check(Math.Abs(a - b) < .005f, message);
    private static object? Call(GameWorld world, string name, params object[] args) =>
        typeof(GameWorld).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static int Progress(Wizard player, string name) =>
        (int)typeof(Wizard).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
    private static void Progress(Wizard player, string name, int value) =>
        typeof(Wizard).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);
    private static IList Projectiles(GameWorld world) =>
        (IList)typeof(GameWorld).GetField("_projectiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name)!.GetValue(value)!;
    private static (GameWorld World, Wizard Caster, Wizard Victim) Arena(int rank = 2)
    {
        var world = new GameWorld(new() { Mode = GameMode.Warfare, MaxPlayers = 8, FriendlyFire = true });
        var caster = world.AddPlayer("Lanzador", 0)!; var victim = world.AddPlayer("Rival", 1)!;
        caster.Position = new(-3, 0); victim.Position = new(-1, 0);
        caster.Invulnerable = victim.Invulnerable = 0;
        world.EquipPlayer(caster.Id, EquipmentModifiers.None with { AutomaticRock = rank });
        return (world, caster, victim);
    }
    private static void Kill(GameWorld world, Wizard owner, Wizard victim, Element element = Element.Fire)
    {
        victim.Health = 1; victim.Shield = victim.ShieldTime = victim.Invulnerable = 0;
        Call(world, "Damage", victim, 10000f, owner.Id, new[] { element });
        Check(!victim.Alive, "La baja de prueba no se produjo.");
    }
    private static void Fire(GameWorld world) => Call(world, "TickAutomaticRocks");

    internal static void Run(Action<string, Action> test)
    {
        test("Roca automática: Diamante requiere cuatro bajas y no hereda fragmentación", () =>
        {
            var (world, caster, victim) = Arena(1);
            for (int i = 0; i < 3; i++) { Kill(world, caster, victim); victim.Health = 1000; Fire(world); }
            Check(Projectiles(world).Count == 0 && Progress(caster, "AutomaticRockKillCredits") == 3, "Diamante se dispara antes de cuatro bajas.");
            Kill(world, caster, victim); victim.Health = 1000; Fire(world);
            Check(Projectiles(world).Count == 1 && Field<int>(Projectiles(world)[0]!, "RockFragmentation") == 0 &&
                Progress(caster, "AutomaticRockKillCredits") == 0, "Diamante no consume cuatro bajas o recibe fragmentación gratis.");
        });
        test("Roca automática: fragmentación y disparo son independientes y pueden combinarse", () =>
        {
            var (world, caster, victim) = Arena(0);
            world.EquipPlayer(caster.Id, EquipmentModifiers.None with { RockFragmentation = 2 });
            for (int i = 0; i < 4; i++) Kill(world, caster, victim);
            victim.Health = 1000; Fire(world);
            Check(Projectiles(world).Count == 0 && Progress(caster, "AutomaticRockKillCredits") == 0,
                "Fragmentación sigue concediendo rocas automáticas.");
            world.EquipPlayer(caster.Id, EquipmentModifiers.None with { AutomaticRock = 2, RockFragmentation = 1 });
            Kill(world, caster, victim); Kill(world, caster, victim); victim.Health = 1000; Fire(world);
            Check(Projectiles(world).Count == 1 && Field<int>(Projectiles(world)[0]!, "RockFragmentation") == 1,
                "La roca automática no usa la fragmentación equipada por separado.");
        });
        test("Roca automática: dos bajas de cualquier elemento, carga máxima y mezcla compatible", () =>
        {
            var (world, caster, victim) = Arena();
            Kill(world, caster, victim, Element.Fire); victim.Health = 1000;
            Fire(world); Check(Projectiles(world).Count == 0, "Se dispara antes de la segunda baja.");
            Kill(world, caster, victim, Element.Lightning); victim.Health = 1000;
            Fire(world); Check(Projectiles(world).Count == 1, "No se dispara por bajas sin Tierra.");
            var shot = Projectiles(world)[0]!;
            Check(Field<SpellRecipe>(shot, "Recipe").Elements.SequenceEqual(new[] { Element.Earth, Element.Earth, Element.Fire }), "Mezcla incorrecta.");
            Near(Field<float>(shot, "Power"), SpellBalance.RockMinimum + SpellBalance.ChargeSeconds, "No sale a máxima carga.");
            Near(Field<V2>(shot, "Velocity").Length, 13 + SpellBalance.ChargeSeconds * 9, "Velocidad distinta a la roca cargada.");
            Near(Field<float>(shot, "Radius"), .5f, "Radio distinto al de dos cargas de Tierra.");
            Check(Field<int>(shot, "RockFragmentation") == 0 && Field<int>(shot, "Owner") == caster.Id, "Añade fragmentación sin equiparla o pierde al propietario.");
            Check(Progress(caster, "AutomaticRockKillCredits") == 0, "No consume exactamente dos bajas.");
            Check(ItemAffixes.Describe(new(ItemAffixKind.AutomaticRock, 2)).Contains("cada 2") &&
                ItemAffixes.Describe(new(ItemAffixKind.AutomaticRock, 1)).Contains("cada 4") &&
                !ItemAffixes.Describe(new(ItemAffixKind.RockFragmentation, 2)).Contains("roca automática"), "La descripción mezcla los afijos o sus rangos.");
        });
        test("Roca automática: sin afijo, bajas ajenas, aliados y suicidio no activan el disparo", () =>
        {
            var (world, caster, victim) = Arena(0);
            Kill(world, caster, victim); Kill(world, caster, victim); victim.Health = 1000; Fire(world);
            Check(Projectiles(world).Count == 0 && Progress(caster, "AutomaticRockKillCredits") == 0, "Se activa sin el afijo de roca automática.");
            var (w, p, enemy) = Arena();
            var ally = w.AddPlayer("Aliado", 0)!; ally.Position = new(-5, 3);
            Kill(w, ally, enemy); Kill(w, p, ally); Kill(w, p, p);
            Check(Progress(p, "AutomaticRockKillCredits") == 0, "Cuenta bajas ajenas, aliadas o propias.");
        });
        test("Roca automática: reserva sin objetivo, alcance y congelación sin perder el disparo", () =>
        {
            var (world, caster, victim) = Arena();
            Kill(world, caster, victim); Kill(world, caster, victim); Fire(world);
            Check(Projectiles(world).Count == 0 && Progress(caster, "AutomaticRockKillCredits") == 2, "Pierde el disparo sin objetivo.");
            victim.Health = 1000; victim.Position = caster.Position + new V2(30, 0); Fire(world);
            Check(Projectiles(world).Count == 0, "Ataca fuera del alcance cercano.");
            victim.Position = new(-1, 0); caster.Frozen = 1; Fire(world);
            Check(Projectiles(world).Count == 0 && Progress(caster, "AutomaticRockKillCredits") == 2, "Pierde la reserva al estar congelado.");
            caster.Frozen = 0; Fire(world); Check(Projectiles(world).Count == 1, "No usa la reserva al encontrar objetivo.");
        });
        test("Roca automática: evita enemigos tras muros y apunta a un rival visible", () =>
        {
            var (world, caster, victim) = Arena();
            Kill(world, caster, victim); Kill(world, caster, victim); victim.Health = 1000;
            var visible = world.AddPlayer("Visible", 1)!; visible.Position = new(-3, -4); visible.Invulnerable = 0;
            Call(world, "AddWall", victim, new SpellRecipe([Element.Shield, Element.Fire]), new V2(-2, -1), new V2(-2, 1));
            Fire(world); Check(Projectiles(world).Count == 1, "No encuentra al enemigo visible.");
            Check(Field<V2>(Projectiles(world)[0]!, "Velocity").Y < -20, "Apunta al enemigo tras el muro.");
        });
        test("Roca automática: reserva acotada y límite de proyectiles sin perder disparos preparados", () =>
        {
            var (world, caster, victim) = Arena();
            for (int i = 0; i < CombatAffixes.AutomaticRockMaximumCredits + 8; i++) Kill(world, caster, victim);
            victim.Health = 1000;
            Check(Progress(caster, "AutomaticRockKillCredits") == CombatAffixes.AutomaticRockMaximumCredits, "La reserva no tiene límite.");
            for (int i = 0; i < 128; i++) Call(world, "SpawnProjectile", caster, new SpellRecipe([Element.Fire]), new V2(1, 0), 1f, 10f, .1f);
            Fire(world);
            Check(Projectiles(world).Count == 128 && Progress(caster, "AutomaticRockKillCredits") == CombatAffixes.AutomaticRockMaximumCredits,
                "El límite de proyectiles pierde la reserva o admite más disparos.");
            Projectiles(world).RemoveAt(0); Fire(world);
            Check(Projectiles(world).Count == 128 && Progress(caster, "AutomaticRockKillCredits") == CombatAffixes.AutomaticRockMaximumCredits - 2,
                "No reanuda el disparo al liberar espacio.");
        });
        test("Roca automática: bajas múltiples, rotación elemental y controles manuales intactos", () =>
        {
            var (world, caster, victim) = Arena();
            for (int i = 0; i < 8; i++) Kill(world, caster, victim, Element.Arcane);
            victim.Health = 1000;
            var channel = new SpellRecipe([Element.Arcane]); caster.Channel = channel;
            caster.Facing = new(-1, 0); caster.Energy = 37; caster.ActionCooldown = .4f; caster.Selection.Add(Element.Water);
            foreach (var extra in new[] { Element.Fire, Element.Cold, Element.Water, Element.Arcane })
            {
                int before = Projectiles(world).Count; Fire(world);
                Check(Projectiles(world).Count == before + 1, "Se pierden bajas múltiples o se dispara más de una roca por tick.");
                Check(Field<SpellRecipe>(Projectiles(world)[before]!, "Recipe").Elements.SequenceEqual(new[] { Element.Earth, Element.Earth, extra }), "Rotación incompatible.");
            }
            Check(ReferenceEquals(caster.Channel, channel) && caster.Facing == new V2(-1, 0) && caster.Energy == 37 &&
                caster.ActionCooldown == .4f && caster.Selection.Elements.SequenceEqual(new[] { Element.Water }), "El disparo modifica controles o recursos manuales.");
            Kill(world, caster, victim); Kill(world, caster, victim); victim.Health = 1000;
            caster.Channel = null; var charging = new SpellRecipe([Element.Earth]); caster.Charging = charging; caster.ChargeTime = .6f;
            Fire(world); Check(ReferenceEquals(caster.Charging, charging) && caster.ChargeTime == .6f, "Interrumpe la roca que estabas cargando.");
        });
        test("Roca automática: perder el afijo y morir reinicia el progreso sin sumar copias", () =>
        {
            var (world, caster, victim) = Arena();
            var items = new[] { EquipmentSlot.Boots, EquipmentSlot.Hat }.Select(slot =>
                EquipmentCatalogue.Generate(new((int)slot + 7), tier: ItemTier.Mythic, slot: slot) with { Affix = new(ItemAffixKind.AutomaticRock, 2) }).ToArray();
            var gear = EquipmentModifiers.Calculate(new("rock", "Roca", 0, 0, 0, new(items[0].Id, items[1].Id), items));
            world.EquipPlayer(caster.Id, gear);
            Kill(world, caster, victim); Kill(world, caster, victim); victim.Health = 1000; Fire(world);
            Check(Projectiles(world).Count == 1, "Las copias del afijo duplican el disparo.");
            Kill(world, caster, victim); world.EquipPlayer(caster.Id, EquipmentModifiers.None);
            Check(Progress(caster, "AutomaticRockKillCredits") == 0, "Se conserva el progreso al quitar el afijo.");
            world.EquipPlayer(caster.Id, gear); Kill(world, caster, victim); victim.Health = 1000;
            Kill(world, victim, caster); Check(Progress(caster, "AutomaticRockKillCredits") == 0, "Conserva progreso tras morir.");
            Kill(world, caster, victim); Check(Progress(caster, "AutomaticRockKillCredits") == 0, "Las bajas póstumas recargan el afijo.");
        });
        test("Roca automática: las muertes por proyectil no modifican la lista durante los impactos", () =>
        {
            var (world, caster, first) = Arena();
            var second = world.AddPlayer("Segundo", 1)!; second.Position = new(1, 0); second.Invulnerable = 0;
            var next = world.AddPlayer("Siguiente", 1)!; next.Position = new(4, 0); next.Invulnerable = 0;
            first.Health = second.Health = 1;
            for (int i = 0; i < 2; i++)
                Call(world, "SpawnProjectile", caster, new SpellRecipe([Element.Fire]), new V2(1, 0), 1000f, 600f, .1f);
            world.Step();
            Check(!first.Alive && !second.Alive && Projectiles(world).Count == 1 &&
                Field<SpellRecipe>(Projectiles(world)[0]!, "Recipe").Count(Element.Earth) == 2,
                "El tick de combate no resuelve las bajas antes de crear el disparo automático.");
            world.MatchStatistics().Validate();
        });
        test("Roca automática: guardado, formato anterior y reconexión conservan el contador", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4, Seed = 47 });
            var player = world.AddPlayer("Defensor", 0)!;
            var item = EquipmentCatalogue.Generate(new(47), tier: ItemTier.Mythic, slot: EquipmentSlot.Weapon, family: EquipmentFamily.Earth)
                with { Affix = new(ItemAffixKind.AutomaticRock, 2) };
            Call(world, "GrantDefenseItem", player, item);
            Check(world.ApplyMessage(player.Id, new() { Type = "defense_equip", Sequence = player.LastSequence + 1, ItemId = item.Id }), "No equipa el afijo de prueba.");
            Progress(player, "AutomaticRockKillCredits", 1); Progress(player, "AutomaticRockElementIndex", 2);
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save).Players[player.Id];
            Check(Progress(restored, "AutomaticRockKillCredits") == 1 && Progress(restored, "AutomaticRockElementIndex") == 2, "El guardado reinicia el contador.");
            var legacy = JsonNode.Parse(save.State.GetRawText())!;
            var body = legacy["Defenders"]![0]!["Body"]!.AsObject();
            body.Remove("AutomaticRockKillCredits"); body.Remove("AutomaticRockElementIndex");
            var old = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(legacy) }).Players[player.Id];
            Check(Progress(old, "AutomaticRockKillCredits") == 0, "No admite partidas guardadas antes del afijo automático.");
            foreach (var (field, invalid) in new[] { ("AutomaticRockKillCredits", -1),
                ("AutomaticRockKillCredits", CombatAffixes.AutomaticRockMaximumCredits + 1), ("AutomaticRockElementIndex", 4) })
            {
                var damaged = JsonNode.Parse(save.State.GetRawText())!;
                damaged["Defenders"]![0]!["Body"]![field] = invalid;
                bool rejected = false;
                try { GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(damaged) }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Se admite un contador o índice elemental inválido.");
            }
            world.AttachDefenseAccount(player.Id, "rock-user"); world.RemovePlayer(player.Id);
            var rejoined = world.AddPlayer("Defensor", 0)!;
            Check(world.AttachDefenseAccount(rejoined.Id, "rock-user") && Progress(rejoined, "AutomaticRockKillCredits") == 1 &&
                Progress(rejoined, "AutomaticRockElementIndex") == 2, "La reconexión reinicia la recarga.");
        });
    }
}

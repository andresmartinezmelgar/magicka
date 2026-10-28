using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class ArcaneAffixTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < .015, $"{message}: {actual} != {expected}");
    private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? Call(GameWorld world, string name, params object?[] args) => typeof(GameWorld).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static void Tick(GameWorld world, long tick) => typeof(GameWorld).GetProperty(nameof(GameWorld.Tick))!.SetValue(world, tick);
    private static void Gear(Wizard player, EquipmentModifiers gear) => typeof(Wizard).GetProperty(nameof(Wizard.Equipment))!.SetValue(player, gear);
    private static void Damage(GameWorld world, Wizard target, Wizard owner, float amount, Element element = Element.Arcane) => Call(world, "Damage", target, amount, owner.Id, new[] { element });
    private static Wizard Player(GameWorld world, string name, int team, V2 at)
    { var player = world.AddPlayer(name, team)!; player.Position = at; player.Invulnerable = 0; return player; }
    private static GameWorld World() => new(new() { Mode = GameMode.Warfare, MaxPlayers = 8, FriendlyFire = true });
    private static Wizard Enemy(GameWorld world, MinionKind kind, V2 at, float hp = 1000)
    {
        Check((bool)Call(world, "TrySpawnMinion", kind)!, "No se creó el enemigo.");
        var state = ((IDictionary)Get(world, "_minions")).Values.Cast<object>().Last();
        var enemy = (Wizard)Get(state, "Body"); enemy.Position = at; enemy.Health = enemy.MaxHealth = hp; enemy.Invulnerable = 0; return enemy;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Marca arcana: ventana exacta, daño de todos los autores, escudos y golpe mortal sin exceso ni doble potencia", () =>
        {
            foreach (int rank in new[] { 1, 2 })
            {
                var world = World(); var caster = Player(world, "Marca", 0, new(-6, 0)); var other = Player(world, "Otro autor", 0, new(-6, -4));
                var victim = Player(world, "Marcado", 1, new(-1, 0)); var neighbor = Player(world, "Cercano", 1, new(-1, 2));
                Gear(caster, new() { ArcaneMark = rank, Power = 2, Arcane = 4 }); victim.Health = 100; victim.Shield = victim.ShieldMax = 50; victim.ShieldTime = 6;
                Damage(world, victim, other, 100, Element.Fire); // 50 escudo y 50 vida, antes de recibir la marca.
                Tick(world, 100); Damage(world, victim, caster, 10); // 20 reales; quedan 30 de vida.
                Check(world.Snapshot().Players.Single(p => p.Id == victim.Id).ArcaneMarked, "El golpe no dejó marca.");
                Tick(world, 181); Damage(world, victim, other, 10000, Element.Fire);
                Call(world, "ResolveArcaneBursts");
                double expected = rank == 1 ? 50 : 150;
                Near(1000 - neighbor.Health, expected, "La explosión no repite la ventana de su calidad");
                Call(world, "ResolveArcaneBursts"); Near(1000 - neighbor.Health, expected, "Una marca explotó dos veces");
                Check(!world.Snapshot().Players.Single(p => p.Id == neighbor.Id).ArcaneMarked, "La explosión crea marcas nuevas.");
                Near(world.MatchStatistics().Sum(StatisticGroup.HumanElementSource, StatisticMeasure.Damage, 9 + (int)Element.Arcane), expected, "La explosión no se registra como área arcana");
                Near(world.Snapshot().Players.Single(p => p.Id == caster.Id).Damage!.Total, expected + 20, "El daño personal pierde la marca");
            }
        });
        test("Marca arcana: respeta aliados, invulnerabilidad, escudos, muros y prismas", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4 });
            var caster = Player(world, "Marca", 0, new(-25, 0)); var ally = Player(world, "Aliado", 0, new(-20, -2));
            Gear(caster, new() { ArcaneMark = 2 });
            var victim = Enemy(world, MinionKind.Melee, new(-20, 0), 200);
            var protectedEnemy = Enemy(world, MinionKind.Melee, new(-21, 2));
            protectedEnemy.Shield = protectedEnemy.ShieldMax = 100; protectedEnemy.ShieldTime = 6;
            var prism = Enemy(world, MinionKind.Prism, new(-20, 2));
            typeof(Wizard).GetProperty("VulnerableElement", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(prism, Element.Fire);
            var invulnerable = Enemy(world, MinionKind.Melee, new(-19, -2)); invulnerable.Invulnerable = 1;
            var blocked = Enemy(world, MinionKind.Melee, new(-17, 0));
            Call(world, "AddWall", caster, new SpellRecipe([Element.Shield, Element.Earth]), new V2(-18.5f, -1), new V2(-18.5f, 1));
            Damage(world, victim, caster, 1); Damage(world, victim, caster, 1000, Element.Fire); Call(world, "ResolveArcaneBursts");
            Near(protectedEnemy.Shield, 0, "La protección arcana no redujo la explosión"); Near(protectedEnemy.Health, 1000, "El escudo no absorbió el daño");
            Near(prism.Health, 1000, "La explosión ignoró el elemento del prisma"); Near(invulnerable.Health, 1000, "La explosión ignoró invulnerabilidad");
            Near(blocked.Health, 1000, "La explosión cruzó una pared"); Near(ally.Health, 1000, "La explosión dañó a un aliado");
        });
        test("Marca arcana: las muertes marcadas encadenan una explosión por víctima, sin marcar ni multiplicar al resto", () =>
        {
            var world = World(); var caster = Player(world, "Marca", 0, new(-6, 0)); Gear(caster, new() { ArcaneMark = 2 });
            var a = Player(world, "Primero", 1, new(-1, 0)); var b = Player(world, "Segundo", 1, new(-1, 1));
            var c = Player(world, "Sin marca", 1, new(-1, 2)); a.Health = b.Health = 100;
            Damage(world, a, caster, 1); Damage(world, b, caster, 1); Damage(world, a, caster, 1000);
            Call(world, "ResolveArcaneBursts");
            Check(!a.Alive && !b.Alive && c.Alive, "No resolvió las muertes marcadas."); Near(c.Health, 800, "La cadena perdió o multiplicó daño");
            Near(world.MatchStatistics().Sum(StatisticGroup.Attack, StatisticMeasure.Casts, (int)StatisticAttack.ArcaneMark), 2, "Una muerte generó varias explosiones");
            Check(!world.Snapshot().Players.Single(p => p.Id == c.Id).ArcaneMarked, "La cadena se extiende creando marcas.");
        });
        test("Marca arcana: conserva la marca más fuerte, el último autor de igual rango y limpia la siguiente vida", () =>
        {
            var world = World(); var a = Player(world, "Diamante", 0, new(-6, 0)); var b = Player(world, "Mítico", 0, new(-6, 2));
            var c = Player(world, "Último", 0, new(-6, -2)); var victim = Player(world, "Marcado", 1, new(-1, 0));
            Gear(a, new() { ArcaneMark = 1 }); Gear(b, new() { ArcaneMark = 2 }); Gear(c, new() { ArcaneMark = 2 });
            Damage(world, victim, a, 1); Damage(world, victim, b, 1); Damage(world, victim, a, 1);
            int Owner() => (int)typeof(Wizard).GetProperty("ArcaneMarkOwner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(victim)!;
            Check(Owner() == b.Id, "Una marca inferior sustituyó a la mayor."); Damage(world, victim, c, 1);
            Check(Owner() == c.Id, "No prevalece el último autor de igual rango.");
            world.RemovePlayer(c.Id); Check(!world.Snapshot().Players.Single(p => p.Id == victim.Id).ArcaneMarked, "Quedó una marca sin autor.");
            Damage(world, victim, a, 1); Call(world, "Respawn", victim);
            Check(!world.Snapshot().Players.Single(p => p.Id == victim.Id).ArcaneMarked, "Reaparecer conserva una marca anterior.");
        });
        test("Eco arcano: otro objetivo cercano, mezcla y carga proporcionales, sin penetrar ni lanzar desde el enemigo", () =>
        {
            foreach (int rank in new[] { 1, 2 })
            {
                var world = World(); var caster = Player(world, "Eco", 0, new(-6, 0));
                var aimed = Player(world, "Apuntado", 1, new(-1, 0)); var secondary = Player(world, "Secundario", 1, new(-3, 4));
                var behind = Player(world, "Tras el secundario", 1, new(-.6f, 7.2f)); Player(world, "Aliado cercano", 0, new(-4, -2));
                Gear(caster, new() { ArcaneEcho = rank, BeamPierce = 3 }); caster.Facing = new(1, 0); caster.Channel = new([Element.Arcane, Element.Fire]);
                Call(world, "TickChannels", .1f);
                Near(1000 - secondary.Health, (1000 - aimed.Health) * CombatAffixes.ArcaneEchoPercent(rank) / 100, "El eco no conserva la proporción de daño");
                Near(behind.Health, 1000, "El eco heredó la penetración del haz principal");
                Check(world.Snapshot().Beams.Any(b => b.Owner == caster.Id && b.Elements.SequenceEqual(new[] { Element.Arcane, Element.Fire }) && b.From.Y > .1f && b.From.Y < 1),
                    "El segundo haz no sale del mago con su mezcla.");
                Check(world.Snapshot().Beams.All(b => V2.Distance(b.From, aimed.Position) > .2f), "Arcano saltó desde el objetivo principal.");
                Check(world.MatchStatistics().Sum(StatisticGroup.Attack, StatisticMeasure.Damage, (int)StatisticAttack.ArcaneEcho) > 0, "El eco no tiene atribución propia.");
                Call(world, "TickChannels", .1f);
                Near(world.MatchStatistics().Sum(StatisticGroup.Attack, StatisticMeasure.Casts, (int)StatisticAttack.ArcaneEcho), 1,
                    "El eco cuenta un lanzamiento nuevo en cada paso de simulación");
            }
        });
        test("Eco arcano: no apunta a aliados, no atraviesa muros y se detiene con Vida, congelación o al soltar", () =>
        {
            var world = World(); var caster = Player(world, "Eco", 0, new(-6, 0));
            var aimed = Player(world, "Apuntado", 1, new(-1, 0)); var secondary = Player(world, "Secundario", 1, new(-3, 4));
            var ally = Player(world, "Aliado", 0, new(-4, -2)); Gear(caster, new() { ArcaneEcho = 2 }); caster.Facing = new(1, 0);
            caster.Channel = new([Element.Arcane]);
            Call(world, "AddWall", caster, new SpellRecipe([Element.Shield, Element.Earth]), new V2(-6, 2), new V2(-3, 2));
            Call(world, "TickChannels", .1f); Near(secondary.Health, 1000, "El eco atraviesa una pared"); Near(ally.Health, 1000, "El eco eligió a un aliado");
            ((IList)Get(world, "_walls")).Clear(); caster.Channel = new([Element.Life]); Call(world, "TickChannels", .1f);
            Near(secondary.Health, 1000, "Un haz de Vida activa el eco");
            caster.Channel = new([Element.Arcane]); caster.Frozen = 1; Call(world, "TickChannels", .1f); Near(secondary.Health, 1000, "La congelación no interrumpe el eco");
            caster.Frozen = 0; caster.Channel = null; Call(world, "TickChannels", .1f); Near(secondary.Health, 1000, "Soltar deja un eco autónomo");
            caster.Channel = new([Element.Arcane]); secondary.Shield = secondary.ShieldMax = 1000; secondary.ShieldTime = 6;
            Call(world, "TickChannels", .1f);
            Check(secondary.Shield < 1000 && world.Snapshot().Beams.Any(b => b.Owner == secondary.Id && b.Interaction == "reflected"), "Un escudo dejó de reflejar el eco.");
        });
        test("Afijos arcanos: guardado, explosiones pendientes, marcas en delta y compatibilidad anterior", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4 });
            var caster = Player(world, "Marca", 0, new(-25, 0)); Gear(caster, new() { ArcaneMark = 1 });
            var victim = Enemy(world, MinionKind.Melee, new(-20, 0), 200); var neighbor = Enemy(world, MinionKind.Melee, new(-20, 2));
            var baseline = world.Snapshot().Defense!.Minions;
            Damage(world, victim, caster, 5); Tick(world, 120);
            var marked = world.Snapshot().Defense!.Minions; var codec = new MinionDeltaCodec(baseline);
            Check(codec.Decode(codec.Encode(marked)).Single(m => m.Id == victim.Id).ArcaneMarked, "La delta pierde la marca.");
            var save = world.SaveSurvival(caster.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(JsonSerializer.Deserialize<SurvivalSave>(JsonSerializer.Serialize(save))!);
            var restoredVictim = ((Dictionary<int, Wizard>)Get(restored, "_combatants"))[victim.Id];
            Damage(restored, restoredVictim, restored.Players[caster.Id], 1000, Element.Fire); Call(restored, "ResolveArcaneBursts");
            Near(restored.Snapshot().Defense!.Minions.Single(m => m.Id == neighbor.Id).Health, 800, "El guardado perdió historia o rango de la marca");
            Damage(world, victim, caster, 1000, Element.Fire);
            var pending = GameWorld.RestoreSurvival(world.SaveSurvival(caster.Id, Guid.NewGuid().ToString("N"))); Call(pending, "ResolveArcaneBursts");
            Near(pending.Snapshot().Defense!.Minions.Single(m => m.Id == neighbor.Id).Health, 800, "El guardado perdió la explosión pendiente");
            var legacy = JsonNode.Parse(save.State.GetRawText())!; legacy.AsObject().Remove("ArcaneBursts");
            foreach (var body in legacy["Defenders"]!.AsArray().Select(d => d!["Body"]!).Concat(legacy["Minions"]!.AsArray().Select(m => m!["Body"]!)))
            { body.AsObject().Remove("ArcaneMarkOwner"); body.AsObject().Remove("ArcaneMarkRank"); body.AsObject().Remove("RecentDamage"); }
            var old = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(legacy) }).Snapshot();
            Check(old.Defense!.Minions.All(m => !m.ArcaneMarked), "Una partida anterior inventa marcas.");
            SnapshotValidation.Check(old);
            foreach (string invalid in new[] { "future", "rank", "owner" })
            {
                var corrupted = JsonNode.Parse(save.State.GetRawText())!;
                var body = corrupted["Minions"]!.AsArray().Single(m => (int)m!["Body"]!["Id"]! == victim.Id)!["Body"]!;
                if (invalid == "future") body["RecentDamage"]![0]!["Tick"] = 121;
                else if (invalid == "rank") body["ArcaneMarkRank"] = 3;
                else body["ArcaneMarkOwner"] = 999999;
                bool rejected = false;
                try { GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(corrupted) }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "El guardado acepta una marca inválida: " + invalid);
            }
        });
    }
}

using System.Reflection;
using Arcana.Core;

internal static class SurvivalBeamTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Call(GameWorld world, string name, params object?[] args) =>
        typeof(GameWorld).GetMethod(name, Private)!.Invoke(world, args);
    private static void Cast(GameWorld world) => Call(world, "TickChannels", .1f);
    private static GameWorld World(GameMode mode = GameMode.Defense) => new(new()
    {
        Mode = mode, Map = mode == GameMode.Defense ? ArenaMapId.Bastion : ArenaMapId.Classic,
        SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4, FriendlyFire = true, Seed = 37
    });
    private static Wizard Player(GameWorld world, V2 at, bool bot = false, int team = 0)
    {
        var player = world.AddPlayer(bot ? "Bot aliado" : "Mago", team, bot)!;
        player.Position = at; player.Invulnerable = 0;
        return player;
    }
    private static Wizard Enemy(GameWorld world, V2 at)
    {
        Check((bool)Call(world, "TrySpawnMinion", MinionKind.Melee)!, "No se creó el enemigo de prueba.");
        int id = world.Snapshot().Defense!.Minions.Last().Id;
        var enemy = ((Dictionary<int, Wizard>)Field(world, "_combatants"))[id];
        enemy.Position = at; enemy.Health = enemy.MaxHealth = 1000; enemy.Invulnerable = 0;
        return enemy;
    }
    private static void Shield(Wizard player)
    { player.Shield = player.ShieldMax = 650; player.ShieldTime = 10; player.Ward = [Element.Fire]; }
    private static void Beam(Wizard player, V2 direction, params Element[] elements)
    { player.Facing = direction; player.Channel = new(elements); }

    public static void Run(Action<string, Action> test)
    {
        test("Supervivencia: todos los haces arcanos se cruzan sin fusionarse ni perder sus objetivos", CrossedVariants);
        test("Supervivencia: las mezclas arcanas atraviesan humanos y bots, con y sin escudo", AlliedBodies);
        test("Supervivencia: un cruce arcano no desvía el haz de curación", CrossedHealing);
        test("Supervivencia: Vida conserva la curación del primer aliado y su escudo", Healing);
        test("Supervivencia: el Eco arcano atraviesa aliados y conserva su daño y objetivo secundario", Echo);
        test("Supervivencia: los escudos enemigos siguen refractando los haces arcanos", EnemyShield);
        test("Supervivencia: las barreras y los límites del mapa siguen bloqueando Arcano", Walls);
        test("Otros modos: los haces arcanos siguen fusionándose", OtherModes);
        test("Guerra: los aliados siguen interceptando los haces arcanos", WarfareAlly);
    }

    private static void CrossedVariants()
    {
        foreach (var combination in SpellCatalogue.All.Where(c => c.Recipe.Shape == SpellShape.Beam && c.Recipe.Has(Element.Arcane)))
        {
            var world = World(); var a = Player(world, new(-8, 0)); var b = Player(world, new(0, -8), bot: true);
            var east = Enemy(world, new(6, 0)); var north = Enemy(world, new(0, 6));
            Beam(a, new(1, 0), Element.Arcane); Beam(b, new(0, 1), combination.Elements);
            Cast(world);
            var snapshot = world.Snapshot();
            Check(east.Health < 1000 && north.Health < 1000, "Un cruce perdió su objetivo: " + combination.Keys);
            Check(snapshot.Beams.Length == 2 && snapshot.Beams.All(ray => ray.Interaction == "direct") &&
                !snapshot.Events.Any(e => e.Kind == "beam_merge"), "Arcano se fusiona en Supervivencia: " + combination.Keys);
            Check(snapshot.Beams.Single(ray => ray.Owner == a.Id).To.X > 5 &&
                snapshot.Beams.Single(ray => ray.Owner == b.Id).To.Y > 5, "Un haz se desvió al cruzarse: " + combination.Keys);
        }
    }

    private static void AlliedBodies()
    {
        foreach (var combination in SpellCatalogue.All.Where(c => c.Recipe.Shape == SpellShape.Beam && c.Recipe.Has(Element.Arcane)))
        foreach (bool bot in new[] { false, true })
        foreach (bool shielded in new[] { false, true })
        {
            var world = World(); var caster = Player(world, new(-8, 0)); var ally = Player(world, new(-4, 0), bot);
            var enemy = Enemy(world, new(4, 0)); var behind = Enemy(world, new(8, 0));
            if (shielded) Shield(ally);
            float health = ally.Health, shield = ally.Shield;
            Beam(caster, new(1, 0), combination.Elements); Cast(world);
            Check(enemy.Health < 1000 && behind.Health == 1000,
                "El aliado bloquea el haz o el haz atraviesa enemigos sin penetración: " + combination.Keys);
            Check(ally.Health == health && ally.Shield == shield && ally.Wet == 0 && ally.Burning == 0 && ally.Frozen == 0,
                "El haz afecta al aliado: " + combination.Keys);
            Check(world.Snapshot().Beams is [{ Interaction: "direct" }], "El escudo aliado desvía el haz: " + combination.Keys);
        }
    }

    private static void CrossedHealing()
    {
        var world = World(); var caster = Player(world, new(-8, 0)); var healer = Player(world, new(0, -8));
        var ally = Player(world, new(0, 6)); ally.Health = 500;
        var enemy = Enemy(world, new(6, 0));
        Beam(caster, new(1, 0), Element.Arcane, Element.Fire); Beam(healer, new(0, 1), Element.Life);
        Cast(world);
        Check(enemy.Health < 1000 && ally.Health > 500 && world.Snapshot().Beams.All(b => b.Interaction == "direct"),
            "El cruce mezcla Arcano con Vida o impide alcanzar los objetivos.");
    }

    private static void Healing()
    {
        var world = World(); var healer = Player(world, new(-8, 0)); var ally = Player(world, new(-4, 0));
        var behind = Player(world, new(4, 0)); ally.Health = behind.Health = 500; Shield(ally);
        Beam(healer, new(1, 0), Element.Life, Element.Life, Element.Life); Cast(world);
        Check(ally.Health > 500 && ally.Shield == 650 && behind.Health == 500,
            "Vida atraviesa al aliado herido o modifica su escudo.");
    }

    private static void Echo()
    {
        var world = World(); var caster = Player(world, new(-8, 0));
        var directAlly = Player(world, new(-5, 0)); var echoAlly = Player(world, new(-6.5f, 2), bot: true);
        Shield(directAlly); Shield(echoAlly);
        var primary = Enemy(world, new(-1, 0)); var secondary = Enemy(world, new(-5, 4));
        var behind = Enemy(world, new(-2.6f, 7.2f));
        typeof(Wizard).GetProperty(nameof(Wizard.Equipment))!.SetValue(caster, new EquipmentModifiers { ArcaneEcho = 2, BeamPierce = 3 });
        Beam(caster, new(1, 0), Element.Arcane, Element.Fire); Cast(world);
        float expected = (1000 - primary.Health) * CombatAffixes.ArcaneEchoPercent(2) / 100f;
        Check(expected > 0 && Math.Abs(1000 - secondary.Health - expected) < .015f && behind.Health == 1000,
            "El eco no atraviesa al aliado, pierde su proporción de daño o penetra al objetivo secundario.");
        Check(directAlly.Shield == 650 && echoAlly.Shield == 650 &&
            world.MatchStatistics().Sum(StatisticGroup.Attack, StatisticMeasure.Damage, (int)StatisticAttack.ArcaneEcho) > 0,
            "El eco desgasta escudos aliados o pierde su atribución.");
    }

    private static void EnemyShield()
    {
        foreach (bool elemental in new[] { false, true })
        {
            var world = World(); var caster = Player(world, new(-8, 0)); var ally = Player(world, new(-5.8f, -1.05f));
            var enemy = Enemy(world, new(-2, 0)); Shield(enemy); if (!elemental) enemy.Ward = [];
            Beam(caster, new(1, 0), Element.Arcane, Element.Fire); Cast(world);
            string interaction = elemental ? "refracted" : "reflected";
            Check(enemy.Shield < 650 && world.Snapshot().Beams.Any(b => b.Owner == enemy.Id && b.Interaction == interaction),
                "Un escudo enemigo dejó de devolver el haz: " + interaction);
            var hit = elemental ? ally : caster;
            Check(hit.Health < hit.MaxHealth, "El haz devuelto por un enemigo atraviesa a los jugadores: " + interaction);
        }
    }

    private static void Walls()
    {
        var world = World(); var caster = Player(world, new(-8, 0)); var ally = Player(world, new(-8, 4));
        var enemy = Enemy(world, new(4, 0));
        Call(world, "AddWall", ally, new SpellRecipe([Element.Shield, Element.Earth]), new V2(0, -2), new V2(0, 2));
        Beam(caster, new(1, 0), Element.Arcane); Cast(world);
        Check(enemy.Health == 1000 && world.Snapshot().Beams.Any(b => b.Interaction != "direct"), "Arcano atraviesa la barrera aliada.");

        world = World(); caster = Player(world, new(10, 6)); enemy = Enemy(world, new(10, 18));
        Beam(caster, new(0, 1), Element.Arcane); Cast(world);
        Check(enemy.Health == 1000 && world.Snapshot().Beams.Single().To.Y <= 14.001f,
            "Arcano atraviesa la pared del mapa.");
    }

    private static void OtherModes()
    {
        foreach (var mode in new[] { GameMode.Duel, GameMode.Arena, GameMode.Warfare })
        {
            var world = World(mode); var a = Player(world, new(-6, 0)); var b = Player(world, new(0, -6), team: 1);
            Beam(a, new(1, 0), Element.Arcane); Beam(b, new(0, 1), Element.Arcane, Element.Fire);
            Cast(world);
            Check(world.Snapshot().Beams.Any(ray => ray.Interaction == "merged") && world.Snapshot().Events.Any(e => e.Kind == "beam_merge"),
                "Se perdió la fusión fuera de Supervivencia: " + mode);
        }
    }

    private static void WarfareAlly()
    {
        foreach (bool shielded in new[] { false, true })
        {
            var world = World(GameMode.Warfare); var caster = Player(world, new(-8, 0)); var ally = Player(world, new(-4, 0));
            var enemy = Player(world, new(4, 0), team: 1); if (shielded) Shield(ally);
            Beam(caster, new(1, 0), Element.Arcane, Element.Fire); Cast(world);
            Check(enemy.Health == 1000 && (shielded ? ally.Shield < 650 : ally.Health < ally.MaxHealth),
                "El cambio altera las colisiones con aliados fuera de Supervivencia.");
        }
    }
}

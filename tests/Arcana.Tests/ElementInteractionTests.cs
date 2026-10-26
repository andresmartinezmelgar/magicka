using Arcana.Core;
using System.Reflection;

internal static class ElementInteractionTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Near(float actual, float expected, string message)
    { Check(Math.Abs(actual - expected) < 0.015f, $"{message}: {actual} frente a {expected}"); }
    private static object? Invoke(GameWorld world, string name, params object?[] arguments) =>
        typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, arguments);
    private static void Send(GameWorld world, Wizard player, string type, Element element = Element.Water, V2? aim = null)
    {
        var to = aim ?? new V2(8, 0);
        Check(world.ApplyMessage(player.Id, new() { Type = type, Element = element, Sequence = player.LastSequence + 1,
            AimX = to.X, AimY = to.Y }), "No se aplicó la intención " + type);
    }
    private static void Select(GameWorld world, Wizard player, params Element[] elements)
    { foreach (var element in elements) Send(world, player, "element", element); }
    private static (GameWorld World, Wizard Caster, Wizard Enemy) Arena()
    {
        var world = new GameWorld(new() { Mode = GameMode.Arena });
        var caster = world.AddPlayer("Propietario", 0)!; var enemy = world.AddPlayer("Enemigo", 1)!;
        caster.Position = new(-3, 0); enemy.Position = new(3, 0); caster.Facing = new(1, 0);
        caster.Invulnerable = enemy.Invulnerable = 0;
        return (world, caster, enemy);
    }
    private static void Wall(GameWorld world, Wizard owner, params Element[] elements) =>
        Invoke(world, "AddWall", owner, new SpellRecipe(elements), new V2(0, -2.2f), new V2(0, 2.2f));
    private static void Advance(GameWorld world, Wizard player, int ticks)
    { for (int i = 0; i < ticks; i++) { Send(world, player, "input"); world.Step(); } }
    private static float WallLoss(Element defense, params Element[] attack)
    {
        var (world, caster, enemy) = Arena(); enemy.Position = new(10, 5);
        Wall(world, enemy, Element.Shield, defense);
        Check(world.Snapshot().Walls.Length == 1, "No se creó la barrera de la prueba.");
        var wall = ((System.Collections.IList)typeof(GameWorld).GetField("_walls", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!)[0]!;
        return (float)Invoke(world, "WallDamage", wall, new SpellRecipe(attack), caster.Id, 100f)!;
    }

    public static void Run(Action<string, Action> test)
    {
        test("Rayo: los aliados no ocupan objetivos ni reciben descargas ofensivas, incluso con fuego amigo", () =>
        {
            foreach (bool friendlyFire in new[] { false, true })
            foreach (int chains in new[] { 0, 2 })
            foreach (var elements in new[] { new[] { Element.Lightning }, new[] { Element.Lightning, Element.Fire }, new[] { Element.Lightning, Element.Cold } })
            {
                var world = new GameWorld(new() { Mode = GameMode.Warfare, FriendlyFire = friendlyFire, MaxPlayers = 8 });
                var caster = world.AddPlayer("Lanzador", 0)!; caster.Position = new(-4, 0); caster.Invulnerable = 0;
                world.EquipPlayer(caster.Id, new EquipmentModifiers { LightningChains = chains });
                var allies = Enumerable.Range(0, 3).Select(i =>
                {
                    var ally = world.AddPlayer("Aliado " + i, 0)!; ally.Position = new(-1 + i * .5f, (i - 1) * .3f);
                    ally.Health = 500; ally.Shield = ally.ShieldMax = 100; ally.ShieldTime = 12; ally.Invulnerable = 0;
                    return ally;
                }).ToArray();
                var enemy = world.AddPlayer("Enemigo", 1)!; enemy.Position = new(3, 0); enemy.Invulnerable = 0;
                caster.Channel = new(elements); world.Step();
                Check(enemy.Health < 1000, "Los aliados cercanos consumen los tres objetivos y evitan alcanzar al enemigo.");
                Check(allies.All(a => a.Health == 500 && a.Shield == 100 && a.Burning == 0 && a.Chill == 0),
                    "Una descarga ofensiva daña, consume escudo o aplica estados a un aliado.");
                Check(world.Snapshot().Beams.All(b => allies.All(a => V2.Distance(a.Position, b.To) > .1f)),
                    "El rayo sigue dibujando un aliado como objetivo aunque no le cause daño.");
            }
        });
        test("Rayo con Vida: conserva el objetivo aliado y puede curarlo", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Warfare, FriendlyFire = false });
            var caster = world.AddPlayer("Sanador", 0)!; var ally = world.AddPlayer("Aliado", 0)!;
            caster.Position = new(-4, 0); ally.Position = new(0, 0); caster.Invulnerable = ally.Invulnerable = 0;
            ally.Health = 500; ally.Shield = ally.ShieldMax = 100; ally.ShieldTime = 12;
            caster.Channel = new([Element.Lightning, Element.Life]); world.Step();
            Check(ally.Health > 500 && ally.Shield == 100 && world.Snapshot().Beams.Any(b => V2.Distance(b.To, ally.Position) < 1),
                "La excepción de Vida bloquea al aliado o impide curarlo.");
        });
        test("Elementos: ocho ventajas y debilidades completas, distintas y recíprocas", () =>
        {
            var elements = Enum.GetValues<Element>();
            Check(elements.Select(ElementInteractions.StrongAgainst).Distinct().Count() == 8 &&
                elements.Select(ElementInteractions.Weakness).Distinct().Count() == 8, "Un elemento quedó sin rival o tiene varios contrarios.");
            foreach (var element in elements)
            {
                Check(ElementInteractions.StrongAgainst(element) != element && ElementInteractions.Weakness(element) != element,
                    "Un elemento se supera a sí mismo.");
                Check(ElementInteractions.StrongAgainst(ElementInteractions.Weakness(element)) == element,
                    "La debilidad no corresponde a la ventaja del atacante.");
                Near(ElementInteractions.Multiplier(element, ElementInteractions.StrongAgainst(element)), 1.5f, "Falta la ventaja");
                Near(ElementInteractions.Multiplier(element, ElementInteractions.Weakness(element)), 0.5f, "Falta la resistencia rival");
            }
        });
        test("Combinaciones: el constructor y el teclado resuelven las mismas cancelaciones y límite", () =>
        {
            foreach (var a in Enum.GetValues<Element>()) foreach (var b in Enum.GetValues<Element>())
            foreach (var c in Enum.GetValues<Element>())
            {
                var buffer = new ElementBuffer(); buffer.Add(a); buffer.Add(b); buffer.Add(c);
                Check(new SpellRecipe([a, b, c]).Elements.SequenceEqual(buffer.Elements), "Una receta directa difiere del teclado.");
            }
            Check(new SpellRecipe([Element.Fire, Element.Fire, Element.Fire, Element.Cold]).Count(Element.Fire) == 2,
                "Una cancelación tras alcanzar tres elementos no liberó su plaza.");
        });
        test("Fusiones: reparto real de agua y fuego/frío y aporte del tercer elemento", () =>
        {
            var steam = new SpellRecipe([Element.Water, Element.Fire]);
            var ice = new SpellRecipe([Element.Water, Element.Cold]);
            Near(SpellBalance.Component(steam, Element.Water), 80, "Agua no aporta la mitad del vapor");
            Near(SpellBalance.Component(steam, Element.Fire), 80, "Fuego no aporta la mitad del vapor");
            Near(SpellBalance.Damage(new([Element.Water, Element.Fire, Element.Fire])), 240, "El fuego adicional se pierde");
            Near(SpellBalance.Damage(new([Element.Water, Element.Water, Element.Fire])), 240, "El agua adicional del vapor se pierde");
            Near(SpellBalance.Component(ice, Element.Water), 34, "Agua no aporta la mitad del hielo");
            Near(SpellBalance.Component(ice, Element.Cold), 34, "Frío no aporta la mitad del hielo");
            Near(SpellBalance.Damage(new([Element.Water, Element.Cold, Element.Cold])), 102, "El frío adicional se pierde");
            Near(SpellBalance.Damage(new([Element.Water, Element.Water, Element.Cold])), 102, "El agua adicional del hielo se pierde");
        });
        test("Guardas: resistencia propia, ventaja, resistencia rival y mezcla por componente", () =>
        {
            foreach (var attack in new[] { Element.Fire, Element.Cold, Element.Lightning, Element.Earth, Element.Arcane })
            foreach (var defense in Enum.GetValues<Element>().Where(e => e != Element.Shield))
            {
                var (world, caster, target) = Arena();
                target.Ward = [defense]; target.WardTime = 12; target.Shield = target.ShieldMax = 1000; target.ShieldTime = 12;
                var recipe = new SpellRecipe([attack]);
                Invoke(world, "Hit", target, recipe, 1f, caster.Id, V2.Zero, 1f);
                Near(1000 - target.Shield, SpellBalance.Damage(recipe) * ElementInteractions.Multiplier(attack, defense),
                    "La guarda no aplica su relación elemental");
            }
            var (mixed, source, shield) = Arena();
            shield.Ward = [Element.Fire, Element.Water]; shield.WardTime = 12;
            shield.Shield = shield.ShieldMax = 1000; shield.ShieldTime = 12;
            Invoke(mixed, "Hit", shield, new SpellRecipe([Element.Fire, Element.Arcane]), 1f, source.Id, V2.Zero, 1f);
            Near(1000 - shield.Shield, 58 * 0.34f + 115, "La mezcla multiplicó todo el hechizo por una única ventaja");
        });
        test("Escudo básico: rayo desgasta más, arcano menos y el exceso llega a salud", () =>
        {
            foreach (var (element, expected) in new[] { (Element.Lightning, 150f), (Element.Arcane, 57.5f), (Element.Fire, 58f) })
            {
                var (world, caster, target) = Arena(); target.Shield = target.ShieldMax = 1000; target.ShieldTime = 12;
                Invoke(world, "Hit", target, new SpellRecipe([element]), 1f, caster.Id, V2.Zero, 1f);
                Near(1000 - target.Shield, expected, "Relación del escudo básico incorrecta");
            }
            var (broken, source, enemy) = Arena(); enemy.Shield = enemy.ShieldMax = 10; enemy.ShieldTime = 12;
            Invoke(broken, "Hit", enemy, new SpellRecipe([Element.Lightning]), 1f, source.Id, V2.Zero, 1f);
            Near(enemy.Shield, 0, "El escudo no se rompe"); Near(enemy.Health, 860, "Se perdió el daño sobrante");
        });
        test("Vapor y hielo: las guardas afectan por separado a ambos componentes de la fusión", () =>
        {
            var (world, caster, target) = Arena(); target.Ward = [Element.Fire]; target.WardTime = 12;
            Invoke(world, "Hit", target, new SpellRecipe([Element.Water, Element.Fire, Element.Fire]), 1f, caster.Id, V2.Zero, 1f);
            Near(target.Health, 1000 - 80 * 1.5f - 160 * 0.18f, "La guarda de fuego redujo también el agua del vapor");
        });
        test("Agua y Vida desgastan escudos enemigos sin dañar salud ni romper la protección aliada", () =>
        {
            foreach (var (attack, defense) in new[] { (Element.Water, Element.Fire), (Element.Life, Element.Earth) })
            {
                var (world, caster, target) = Arena(); target.Ward = [defense]; target.WardTime = 12;
                target.Shield = target.ShieldMax = 100; target.ShieldTime = 12; target.Health = 500;
                Invoke(world, "Hit", target, new SpellRecipe([attack]), 1f, caster.Id, V2.Zero, 1f);
                Near(target.Shield, 47.5f, "No se aplicó la presión con ventaja sobre el escudo");
                Near(target.Health, attack == Element.Life ? 640 : 500, "La presión de utilidad dañó la salud");
                target.Shield = 1;
                Invoke(world, "Hit", target, new SpellRecipe([attack]), 1f, caster.Id, V2.Zero, 1f);
                Near(target.Shield, 0, "La presión no rompió el escudo");
                Near(target.Health, attack == Element.Life ? 780 : 500, "El exceso de presión pasó a la salud");
            }
            var allied = new GameWorld(new() { Mode = GameMode.Warfare, FriendlyFire = true });
            var healer = allied.AddPlayer("Sanador", 0)!; var friend = allied.AddPlayer("Aliado", 0)!;
            friend.Invulnerable = 0; friend.Health = 500; friend.Shield = friend.ShieldMax = 100; friend.ShieldTime = 12;
            Invoke(allied, "Hit", friend, new SpellRecipe([Element.Life]), 1f, healer.Id, V2.Zero, 1f);
            Near(friend.Health, 640, "No se curó al aliado"); Near(friend.Shield, 100, "La curación consumió el escudo aliado");
        });
        test("Barreras: agua supera fuego, vida supera tierra y las mezclas conservan sus pesos", () =>
        {
            Near(WallLoss(Element.Fire, Element.Water), 150, "Agua no derriba fuego con ventaja");
            Near(WallLoss(Element.Water, Element.Fire), 50, "Fuego no respeta la resistencia de agua");
            Near(WallLoss(Element.Earth, Element.Life), 150, "Vida no desgasta tierra con ventaja");
            Near(WallLoss(Element.Fire, Element.Water, Element.Fire), 100 * (1.5f + 0.18f) / 2,
                "Vapor no compara por separado agua y fuego");
            var (world, caster, enemy) = Arena(); enemy.Health = 500;
            Invoke(world, "Hit", enemy, new SpellRecipe([Element.Life]), 1f, caster.Id, V2.Zero, 1f);
            Near(enemy.Health, 640, "Una ventaja convirtió la curación en daño de salud");
        });
        test("Escudo del propietario: rayos, conos, cadenas, rocas e hielo pasan sin dañarlo", () =>
        {
            foreach (var elements in new[] { new[] { Element.Arcane }, new[] { Element.Fire }, new[] { Element.Lightning },
                new[] { Element.Earth }, new[] { Element.Water, Element.Cold } })
            {
                var (world, caster, enemy) = Arena(); Wall(world, caster, Element.Shield);
                Select(world, caster, elements); Send(world, caster, "cast");
                if (elements.Contains(Element.Earth)) Send(world, caster, "release");
                Advance(world, caster, 25);
                Check(enemy.Health < 1000, "El escudo propio bloqueó " + SpellCatalogue.Name(elements));
                Near(world.Snapshot().Walls.Single().Health, 650, "El hechizo propio desgastó su escudo");
                Check(world.Snapshot().Beams.All(b => b.Interaction != "reflected"), "El escudo propio reflejó su rayo");
                Check(!world.Walkable(V2.Zero), "El cambio dejó pasar el movimiento");
            }
        });
        test("Escudos ajenos y barreras imbuidas: bloquean también a aliados y al propietario", () =>
        {
            foreach (bool imbued in new[] { true, false })
            {
                var (world, caster, enemy) = Arena();
                Wall(world, imbued ? caster : enemy, imbued ? [Element.Shield, Element.Earth] : [Element.Shield]);
                Select(world, caster, Element.Earth); Send(world, caster, "cast"); Send(world, caster, "release");
                Advance(world, caster, 25);
                Near(enemy.Health, 1000, "Una pared bloqueante dejó pasar la roca");
                Check(world.Snapshot().Walls.Single().Health < (imbued ? 1300 : 650), "La pared no recibió el impacto");
            }
            var allied = new GameWorld(new() { Mode = GameMode.Warfare, FriendlyFire = false });
            var owner = allied.AddPlayer("Aliado", 0)!; var caster2 = allied.AddPlayer("Lanzador", 0)!;
            owner.Position = new(12, 0); caster2.Position = new(-3, 0); caster2.Invulnerable = 0;
            Wall(allied, owner, Element.Shield); Select(allied, caster2, Element.Arcane); Send(allied, caster2, "cast"); Advance(allied, caster2, 1);
            Check(allied.Snapshot().Beams.Any(b => b.Interaction == "reflected"), "Ser aliado permitió atravesar el escudo de otro propietario.");
        });
        test("Un rayo combinado no usa el permiso del propietario para atravesar hechizos ajenos", () =>
        {
            var (world, caster, second) = Arena(); var target = world.AddPlayer("Tras el escudo")!;
            caster.Position = new(-5, -2); second.Position = new(-5, 2); target.Position = new(5, 0);
            target.Invulnerable = 0;
            Wall(world, caster, Element.Shield);
            Send(world, caster, "input", aim: new(-2, 0)); Send(world, second, "input", aim: new(-2, 0)); world.Step();
            Select(world, caster, Element.Arcane); Select(world, second, Element.Arcane);
            Send(world, caster, "cast"); Send(world, second, "cast"); world.Step();
            Check(world.Snapshot().Beams.Any(b => b.Interaction == "merged"), "La prueba no produjo un cruce real.");
            Check(world.Snapshot().Beams.Where(b => b.Interaction == "merged").All(b => b.To.X <= 0.001f),
                "Una mezcla de propietarios atravesó el escudo de uno de ellos.");
            Near(target.Health, 1000, "El rayo combinado dañó a un objetivo detrás del escudo");
        });
        test("El origen y la explosión no saltan paredes ajenas pero atraviesan el escudo propio", () =>
        {
            var (world, caster, enemy) = Arena();
            Wall(world, caster, Element.Shield);
            Check(world.Snapshot().Walls.Length == 1, "La prueba debe contener un escudo real.");
            caster.Position = new(-0.55f, 0); enemy.Position = new(3, 0);
            Select(world, caster, Element.Earth, Element.Fire); Send(world, caster, "cast"); Send(world, caster, "release");
            Advance(world, caster, 20);
            Check(enemy.Health < 1000, "El origen de la roca quedó detenido por el escudo propio.");
            Near(world.Snapshot().Walls.Single().Health, 650, "El origen o el splash dañaron el escudo propio");
        });
    }
}

using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class ShieldAbsorptionAffixTests
{
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static void Near(float actual, float expected, string reason) => Check(Math.Abs(actual - expected) < .006f, $"{reason}: {actual} != {expected}");
    private static object? Call(GameWorld world, string method, params object?[] args) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static (GameWorld World, Wizard Defender, Wizard Enemy) Arena(EquipmentModifiers gear, bool friendly = false)
    {
        var world = new GameWorld(new() { Mode = GameMode.Warfare, FriendlyFire = friendly });
        var defender = world.AddPlayer("Mago", 0)!; var enemy = world.AddPlayer("Rival", friendly ? 0 : 1)!;
        defender.Position = new(-3, 0); enemy.Position = new(-1, 0); defender.Invulnerable = enemy.Invulnerable = 0;
        world.EquipPlayer(defender.Id, gear); return (world, defender, enemy);
    }
    private static void Shield(Wizard player, float value = 200)
    { player.Shield = player.ShieldMax = value; player.ShieldTime = player.WardTime = 10; player.Ward = []; }
    private static void Damage(GameWorld world, Wizard defender, int owner, float amount, Element[]? elements = null) =>
        Call(world, "Damage", defender, amount, owner, elements);

    internal static void Run(Action<string, Action> test)
    {
        test("Escudo vital: absorción real, vida máxima, rotura, atribución y exclusión del remiendo", () =>
        {
            foreach (int rank in new[] { 1, 2 })
            {
                var (world, defender, enemy) = Arena(new() { ShieldLeech = rank, MendingWard = 2, Healing = 1.6f });
                defender.Health = 500; Shield(defender, 100); Damage(world, defender, enemy.Id, 40);
                float healed = rank == 1 ? 4 : 6;
                Near(defender.Health, 500 + healed, "Conversión de daño absorbido en vida");
                Near(defender.Shield, 60, "La curación del afijo repara el escudo o usa curación de equipo");
                Near((float)world.MatchStatistics().Sum(StatisticGroup.Attack, StatisticMeasure.Healing, (int)StatisticAttack.ShieldLeech), healed, "Origen de la curación");
                defender.Health = 998; Shield(defender, 100); Damage(world, defender, enemy.Id, 40);
                Near(defender.Health, 1000, "Sobrecuración del afijo");
                defender.Health = 500; Shield(defender, 20); Damage(world, defender, enemy.Id, 60);
                Near(defender.Health, 460 + (rank == 1 ? 2 : 3), "Se cura por el daño a vida o se pierde la absorción del último golpe");
                Near(defender.Shield, 0, "Recrea un escudo roto");
                defender.Health = 10; Shield(defender, 20); Damage(world, defender, enemy.Id, 200);
                Near(defender.Health, 0, "El afijo reanima al portador");
                world.MatchStatistics().Validate();
            }
        });
        test("Afijos de absorción: excluyen daño propio, aliados, invulnerabilidad y barreras colocadas", () =>
        {
            var gear = EquipmentModifiers.None with { ShieldLeech = 2, ShieldReflection = 2 };
            var (world, defender, enemy) = Arena(gear, friendly: true);
            defender.Health = 500; Shield(defender); Damage(world, defender, enemy.Id, 40);
            Near(defender.Health, 500, "El fuego amigo cura"); Near(enemy.Health, 1000, "El fuego amigo se refleja");
            Shield(defender); Damage(world, defender, defender.Id, 40); Near(defender.Health, 500, "El daño propio cura o se refleja");
            var (hostile, player, rival) = Arena(gear);
            player.Health = 500; Shield(player); player.Invulnerable = 1; Damage(hostile, player, rival.Id, 40);
            Near(player.Health, 500, "La invulnerabilidad genera curación"); Near(rival.Health, 1000, "La invulnerabilidad genera reflejo");
            player.Invulnerable = 0; player.Shield = player.ShieldMax = 0;
            Call(hostile, "AddWall", player, new SpellRecipe([Element.Shield]), new V2(-5, -4), new V2(-5, -2));
            var walls = (IList)typeof(GameWorld).GetField("_walls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hostile)!;
            Check(walls.Count == 1, "No se creó la barrera de referencia.");
            Call(hostile, "DamageWall", walls[0], new SpellRecipe([Element.Arcane]), rival.Id, 100f);
            Near(player.Health, 500, "Una barrera colocada genera curación"); Near(rival.Health, 1000, "Una barrera colocada refleja daño");
        });
        test("Espejo protector: porcentajes por calidad, defensas del atacante y ausencia de multiplicadores ofensivos", () =>
        {
            foreach (int rank in new[] { 1, 2 })
            {
                var (world, defender, enemy) = Arena(new() { ShieldReflection = rank, Power = 3, Arcane = 2 });
                world.EquipPlayer(enemy.Id, new EquipmentModifiers { Power = 2, Incoming = .5f });
                Shield(defender); Damage(world, defender, enemy.Id, 50);
                float reflected = rank == 1 ? 7.5f : 12.5f;
                Near(defender.Shield, 100, "Absorción original"); Near(enemy.Health, 1000 - reflected, "Daño reflejado con armadura sin poder del portador");
                Near((float)world.MatchStatistics().Sum(StatisticGroup.Attack, StatisticMeasure.Damage, (int)StatisticAttack.ShieldReflection), reflected, "Origen del reflejo");
                world.MatchStatistics().Validate();
            }
            var (immune, caster, target) = Arena(new() { ShieldReflection = 2 });
            Shield(caster); target.Ward = [Element.Fire, Element.Fire]; target.WardTime = 10;
            Damage(immune, caster, target.Id, 100, [Element.Fire]);
            float resistance = ElementInteractions.ProtectionMultiplier(Element.Fire, target.Ward);
            Near(target.Health, 1000 - 25 * resistance, "El reflejo omite la protección elemental");
            target.Invulnerable = 1; float before = target.Health; Damage(immune, caster, target.Id, 50, [Element.Fire]);
            Near(target.Health, before, "El reflejo atraviesa invulnerabilidad");
        });
        test("Espejo protector: no produce cadenas, curación ni energía en el receptor del reflejo", () =>
        {
            var gear = EquipmentModifiers.None with { ShieldReflection = 2, ShieldLeech = 2, ShieldBattery = 2, ShieldReprisal = 2 };
            var (world, defender, enemy) = Arena(gear); world.EquipPlayer(enemy.Id, gear);
            defender.Health = 500; Shield(defender); Shield(enemy, 10); enemy.Energy = 20;
            Damage(world, defender, enemy.Id, 100);
            Near(defender.Shield, 100, "El daño regresa de nuevo al portador"); Near(defender.Health, 515, "Curación de la absorción inicial");
            Near(enemy.Health, 985, "El daño reflejado activa curación de absorción"); Near(enemy.Energy, 20, "El reflejo activa batería");
            Check(defender.Knockback == V2.Zero, "El reflejo activa repulsión.");
            world.MatchStatistics().Validate();
        });
        test("Espejo protector: el agua sigue afectando solo a escudos y las bajas se atribuyen al portador", () =>
        {
            var (world, defender, enemy) = Arena(new() { ShieldReflection = 2 });
            Shield(defender); Call(world, "DamageProtection", defender, 100f, enemy.Id, new[] { Element.Water });
            Near(enemy.Health, 1000, "La presión de agua reflejada causa daño a vida");
            Shield(enemy); float before = enemy.Shield;
            Call(world, "DamageProtection", defender, 100f, enemy.Id, new[] { Element.Water });
            Check(enemy.Shield < before && enemy.Health == 1000, "El reflejo del agua no conserva el daño exclusivo a escudos.");
            Shield(defender); enemy.Shield = 0; enemy.Health = 10; Damage(world, defender, enemy.Id, 100);
            Check(!enemy.Alive && defender.Kills == 1 && enemy.Deaths == 1, "La baja reflejada no pertenece al portador.");
            world.MatchStatistics().Validate();
        });
    }
}

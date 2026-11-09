using Arcana.Core;

internal static class MeteorTargetingTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Meteoro automático: prioriza el grupo mayor frente al enemigo más cercano", () =>
        {
            V2[] points = [new(1, 0), new(10, 0), new(10, 1), new(11, 0), new(11, 1)];
            var target = MeteorTargeting.Find(V2.Zero, points, _ => true) ?? throw new Exception("Falta objetivo.");
            if (points.Count(p => V2.Distance(p, target) <= SpellBalance.MeteorRadius + .001f) != 4)
                throw new Exception("Se eligió cercanía en vez de concentración.");
        });
        test("Meteoro automático: apunta entre enemigos separados", () =>
        {
            V2[] points = [new(7, -3.9f), new(7, 3.9f)];
            var target = MeteorTargeting.Find(V2.Zero, points, _ => true) ?? throw new Exception("Falta objetivo.");
            if (points.Any(p => V2.Distance(p, target) > SpellBalance.MeteorRadius + .001f))
                throw new Exception("Se apunta a un enemigo y se pierde el otro extremo del grupo.");
        });
        test("Meteoro automático: respeta el alcance y aprovecha el radio de impacto", () =>
        {
            var target = MeteorTargeting.Find(V2.Zero, [new V2(17.5f, 0)], _ => true) ?? throw new Exception("Se perdió el borde de la explosión.");
            if (target.Length > MagickRules.MeteorRange + .001f || V2.Distance(target, new(17.5f, 0)) > SpellBalance.MeteorRadius + .001f ||
                MeteorTargeting.Find(V2.Zero, [new V2(18.1f, 0)], _ => true) != null)
                throw new Exception("Se ignora el alcance real del hechizo.");
        });
        test("Meteoro automático: no lanza sobre suelo inválido ni sin enemigos", () =>
        {
            if (MeteorTargeting.Find(V2.Zero, [new V2(5, 0)], _ => false) != null ||
                MeteorTargeting.Find(V2.Zero, [], _ => true) != null)
                throw new Exception("Se inventó un objetivo inválido.");
        });
        test("Meteoro automático: excluye aliados y enemigos muertos", () =>
        {
            var world = new GameWorld(new()
            {
                Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4,
                Bots = 0, SurvivalMap = new(271)
            });
            world.AddPlayer("Mago", 0); var snapshot = world.Snapshot(); var source = snapshot.Players.Single();
            var ally = source with { Id = source.Id + 1, Position = source.Position + new V2(10, 0) };
            var dead = new MinionSnapshot(5000, MinionKind.Runner, ally.Position, new(1, 0), 0, 100, 0, 0, 0, 0, false, [], false);
            snapshot = snapshot with { Players = [source, ally], Defense = snapshot.Defense! with { Minions = [dead] } };
            if (MeteorTargeting.Find(snapshot, source) != null) throw new Exception("Se cuentan un aliado o un cadáver.");
            var enemy = dead with { Id = 5001, Position = source.Position, Health = 100 };
            snapshot = snapshot with { Defense = snapshot.Defense with { Minions = [dead, enemy] } };
            if (MeteorTargeting.Find(snapshot, source) is not { } target || V2.Distance(target, source.Position) > SpellBalance.MeteorRadius)
                throw new Exception("Se descarta un enemigo vivo dentro del alcance.");
        });
    }
}

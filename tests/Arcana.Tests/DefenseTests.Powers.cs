using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    private static void RunPowers(Action<string, Action> test)
    {
        test("Defensa: poderes automáticos respetan recarga, objetivos, coste y reinicio", () =>
        {
            var w = World(); var p = w.AddPlayer("Humano")!; p.Position = new(-22, 0);
            var ally = w.AddPlayer("Aliado")!; ally.Position = p.Position;
            var enemy = Enemy(w); enemy.Position = p.Position + new V2(2, 0); enemy.Health = enemy.MaxHealth = 10000;
            Gold(w, p, 10000);
            for (int i = 0; i < 4; i++) Check(Command(w, p, "defense_passive", i), "No compró habilidad.");
            Check(Player(w, p).Gold == 10000 - 4 * DefenseRules.PassiveCost(0), "Coste de habilidades incorrecto.");
            Invoke(w, "TickDefensePassives", 2f);
            Check(w.Snapshot().Fields.Length == 0 && enemy.Health == 10000, "Disparó antes de tiempo.");
            Invoke(w, "TickDefensePassives", 1f);
            Check(w.Snapshot().Fields.Count(f => f.Kind == "defense_meteor") == 1, "No apareció meteorito a los 3 s.");
            float allyHealth = ally.Health, ownHealth = p.Health;
            Invoke(w, "TickWallsAndFields", 1.45f);
            Check(enemy.Health < 10000 && p.Health == ownHealth && ally.Health == allyHealth, "El meteorito no dañó al enemigo o dañó aliados.");
            Invoke(w, "TickDefensePassives", 13f);
            var stats = Player(w, p);
            Check(stats.BerserkerRemaining > 0 && stats.DamageMultiplier > 1.49f && stats.SpeedMultiplier > 1.14f && enemy.Chill > 0, "No aplicó berserker o pulso glacial.");
            float health = enemy.Health; Invoke(w, "TickDefensePassives", 0.01f);
            Check(enemy.Health == health, "Ignoró las recargas.");
            Invoke(w, "TickDefensePassives", 5f); Check(Player(w, p).BerserkerRemaining == 0, "Berserker se hizo permanente.");
            Command(w, p, "defense_shop", 1); w.RemovePlayer(ally.Id); var paused = Protocol.Serialize(w.Snapshot());
            Advance(w, 360); Check(Protocol.Serialize(w.Snapshot()) == paused, "Las habilidades no respetaron la pausa.");
            Command(w, p, "defense_shop", 0); End(w, enemy.Id); w.RestartDefense();
            Check(Player(w, p).Passives.All(n => n == 0), "Las habilidades pasaron a otra partida.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Defensa: la cadena visual comunica origen y objetivos sin repetir daño", () =>
        {
            var w = World(); var p = w.AddPlayer("Relámpagos")!; p.Position = new(-22,0);
            var enemies = Enumerable.Range(0,3).Select(i =>
            {
                var enemy = Enemy(w); enemy.Position = p.Position + new V2(2+i,0);
                enemy.Invulnerable = 0; enemy.Health = enemy.MaxHealth = 10000; return enemy;
            }).ToArray();
            Gold(w,p,10000); Check(Command(w,p,"defense_passive",3), "No compró la pasiva eléctrica.");
            Invoke(w,"TickDefensePassives",4f);
            var snapshot = w.Snapshot();
            var source = snapshot.Events.Single(e => e.Kind == "passive_chain");
            var hits = snapshot.Events.Where(e => e.Text == "Relámpago").ToArray();
            Check(source.Position == p.Position && source.Elements.SequenceEqual([Element.Lightning]) &&
                hits.Length == 3 && hits.All(e => e.Id > source.Id) &&
                hits.Select(e => e.Position).SequenceEqual(enemies.Select(e => e.Position)), "Origen u orden de objetivos incorrecto.");
            Check(enemies.All(e => Math.Abs(e.Health-9860)<0.01f) && Player(w,p).PassiveCooldowns[3] == 4,
                "La información visual alteró daño o recarga.");
            SnapshotValidation.Check(snapshot);
        });
        test("Defensa: premios propios por hitos sin relajar reglas PvP", () =>
        {
            string run = Guid.NewGuid().ToString("N"), uid = "humano";
            for (int n = 1; n <= 4; n++)
            {
                string id = RewardRules.DefenseReceiptId(run, uid, n);
                var reward = new RewardMatch(id, "server", GameMode.Defense, ArenaMapId.Bastion, DateTimeOffset.UtcNow,
                    [new(uid, 1, 0, n * 300, true)], 2, run, n);
                Check(RewardRules.Eligible(reward).Length == 1, "Hito válido rechazado.");
                Check(RewardRules.Eligible(reward with { DefenseCheckpoint = 5 }).Length == 0, "Más de cuatro premios.");
                Check(RewardRules.Eligible(reward with { Id = Guid.NewGuid().ToString("N") }).Length == 0, "Hito duplicable con ID arbitrario.");
                Check(RewardRules.Eligible(reward with { Participants = [new(uid, 1, 0, n * 300 - 1, true)] }).Length == 0, "Premio anticipado.");
                Check(RewardRules.Eligible(reward with { Mode = GameMode.Duel }).Length == 0, "Hito reutilizable en PvP.");
            }
        });
    }
}

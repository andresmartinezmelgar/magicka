using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    private static void RunContinuity(Action<string, Action> test)
    {
        test("Supervivencia: los minions encuentran al humano y agotan sus vidas", () =>
        {
            var w = World(); var p = w.AddPlayer("Sin combatir")!; p.Position = new(76, 52);
            for (int tick = 0; tick < 18000 && !w.MatchEnded; tick++) w.Step();
            Check(w.MatchEnded && p.Deaths == 2 && !p.Alive, "Los minions no completaron su objetivo real.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: segunda muerte, resumen estable y reinicio desde cero", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; Gold(w, p, 1000); Command(w, p, "defense_aura", 1);
            var enemy = Enemy(w);
            Damage(w, p, 100000, enemy.Id); Check(!w.MatchEnded && p.Deaths == 1, "La primera muerte terminó la partida.");
            Advance(w, 271); Damage(w, p, 100000, enemy.Id);
            Check(w.MatchEnded && !p.Alive && RoundPresentation.Message(w.Snapshot(), p.Id).Contains("SUPERVIVENCIA"), "No terminó tras dos muertes.");
            Advance(w, 700); Check(w.MatchEnded, "Reinició automáticamente y ocultó los resultados.");
            Check(w.RestartDefense(), "No permitió otra partida.");
            var after = Player(w, p);
            Check(p.Alive && p.Deaths == 0 && p.Kills == 0 && after.Level == 1 && after.Gold == 0 && after.Items.Length == 0 && after.Auras.All(v => v == 0) &&
                after.LivesRemaining == 2 && State(w).BossesKilled == 0, "No se reinició toda la partida.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Defensa: reconexión conserva progreso y tiempo; paquete independiente y acotado", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; w.AttachDefenseAccount(p.Id, "uid"); Gold(w, p, 190);
            Command(w, p, "defense_aura", 2);
            int remainingGold = Player(w, p).Gold;
            var damagedEnemy = Enemy(w); Invoke(w, "Damage", damagedEnemy, 17f, p.Id, new[] { Element.Arcane });
            Advance(w, 10); w.RemovePlayer(p.Id);
            var reconnect = w.AddPlayer("A")!; w.AttachDefenseAccount(reconnect.Id, "uid");
            Check(Player(w, reconnect).Gold == remainingGold && Player(w, reconnect).Auras[2] == 1, "Reconexión perdió progreso o reinició compras.");
            Check(w.Snapshot().Players.Single(q => q.Id == reconnect.Id).Damage?.Health[(int)Element.Arcane] == 17,
                "Reconectar perdió el daño personal acumulado en la misma partida.");
            Invoke(w, "Damage", damagedEnemy, 5f, p.Id, new[] { Element.Fire });
            Check(w.Snapshot().Players.Single(q => q.Id == reconnect.Id).Damage?.Health[(int)Element.Fire] == 5,
                "Un efecto del identificador anterior dejó de sumar al jugador reconectado.");
            for (int i = 0; i < DefenseRules.MaxMinions; i++) Enemy(w);
            var snapshot = w.Snapshot(); SnapshotValidation.Check(snapshot);
            byte[] encoded = Protocol.SerializeUtf8(new ServerMessage { Type = "snapshot", Snapshot = snapshot });
            Check(encoded.Length < Protocol.MaxSnapshotBytes, "El estado superó el presupuesto de red.");
            bool rejected = false;
            try { SnapshotValidation.Check(snapshot with { Defense = snapshot.Defense! with { Minions = [.. snapshot.Defense.Minions, snapshot.Defense.Minions[0]] } }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Aceptó una horda mayor que el máximo.");
            Check(reconnect.Team == 0 && w.Players.Count == 1, "Minions alteraron plazas o equipo.");
        });
        test("Supervivencia: salir y volver no cura, rellena energía ni borra estados o recargas", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; w.AddPlayer("B"); w.AttachDefenseAccount(p.Id, "uid-a");
            p.Health = 100; p.Energy = 5; p.Burning = 5; p.Wet = 3; p.Chill = 40; p.Frozen = 1;
            p.Invulnerable = 0; p.ActionCooldown = 0.4f; p.MagickCooldowns[1] = 10;
            p.Shield = 30; p.ShieldMax = 100; p.ShieldTime = 2;
            p.Ward = [Element.Fire]; p.WardTime = 3; p.TeleportCooldown = 2; p.TeleportNeedsExit = true;
            var position = p.Position; w.RemovePlayer(p.Id);
            var rejoin = w.AddPlayer("A")!;
            Check(w.AttachDefenseAccount(rejoin.Id, "uid-a"), "No se detectó el estado anterior.");
            Check(rejoin.Health == 100 && rejoin.Energy == 5 && rejoin.Deaths == 0 && rejoin.Burning == 5 && rejoin.Wet == 3 &&
                rejoin.Chill == 40 && rejoin.Frozen == 1 && rejoin.Invulnerable == 0 && rejoin.ActionCooldown == 0.4f && rejoin.MagickCooldowns[1] == 10 &&
                rejoin.Shield == 30 && rejoin.ShieldMax == 100 && rejoin.ShieldTime == 2 && rejoin.Ward.SequenceEqual(new[] { Element.Fire }) &&
                rejoin.WardTime == 3 && rejoin.TeleportCooldown == 2 && rejoin.TeleportNeedsExit && rejoin.Position == position,
                "La nueva entrada restableció recursos o borró estados.");
            rejoin.Health = 0; rejoin.Deaths = 1; rejoin.Respawn = 3.5f;
            ((IDictionary)Field(w, "_defenders"))[rejoin.Id]!.GetType().GetField("Deaths")!.SetValue(((IDictionary)Field(w, "_defenders"))[rejoin.Id], 1);
            w.RemovePlayer(rejoin.Id); var dead = w.AddPlayer("A")!; w.AttachDefenseAccount(dead.Id, "uid-a");
            Check(!dead.Alive && dead.Respawn == 3.5f && dead.Deaths == 1, "Volver omitió el tiempo de reaparición.");
            w.RemovePlayer(dead.Id); var another = w.AddPlayer("C")!; w.AttachDefenseAccount(another.Id, "uid-c");
            Check(another.Health == another.MaxHealth && another.Energy == 100 && another.Deaths == 0, "Se mezclaron estados entre cuentas.");
        });
    }
}

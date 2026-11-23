using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineMatchTests
{
    public static void RunModes(Action<string, Action> Test)
    {
        Test("Duelo y arena respetan su límite, ganador y metadatos de victoria", () =>
        {
            foreach (var mode in new[] { GameMode.Duel, GameMode.Arena })
            {
                var w = new GameWorld(new() { Mode = mode, ScoreLimit = 2, Tickets = 33 });
                var a = w.AddPlayer("Ganador", 0, wizardType: WizardType.Pyromancer)!; var b = w.AddPlayer("Rival", 1)!;
                for (int kill = 0; kill < 2; kill++)
                {
                    a.Position = new(-2, 0); b.Position = new(1, 0); b.Health = 1; a.Invulnerable = b.Invulnerable = a.ActionCooldown = 0;
                    Select(w, a, Element.Arcane); Send(w, a, "nova"); w.Step();
                    if (kill == 0) Check(!w.MatchEnded, "La partida terminó antes del límite.");
                }
                var state = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!;
                Check(state.MatchEnded && state.Winner == (mode == GameMode.Duel ? a.Team : a.Id), "Ganador incorrecto.");
                Check(state.ScoreLimit == 2 && state.StartingTickets == 33 && MatchRules.Objective(mode, 2, 33).Contains("2"), "La ayuda no recibe el límite real.");
                for (int i = 0; i < 550; i++) w.Step();
                Check(!w.MatchEnded && a.Kills == 0 && a.WizardType == WizardType.Pyromancer, "La nueva partida perdió el tipo o mantuvo bajas.");
            }
        });
        Test("Obstáculos bloquean movimiento y rayos", () =>
        {
            var (w, a, b) = Setup(); a.Position = new(-16, 0); b.Position = new(-10, 0);
            Send(w, a, "input", aim: b.Position); w.Step();
            Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane); Send(w, a, "cast");
            for (int i = 0; i < 60; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
            Check(b.Health == 1000, "El rayo atravesó una columna.");
            Check(!w.Walkable(new(-13, 0)), "Se puede caminar dentro de la columna.");
        });
        Test("Joystick: intensidad proporcional, diagonal limitada y ruta a velocidad normal", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Arena });
            var player = world.AddPlayer("Joystick")!;
            float Distance(V2 movement, bool moveTo = false)
            {
                player.Position = V2.Zero;
                world.ApplyMessage(player.Id, new() { Type = "input", Sequence = player.LastSequence + 1,
                    MoveX = movement.X, MoveY = movement.Y, MoveTo = moveTo, TargetX = 10, AimX = 10 });
                world.Step();
                return player.Position.Length;
            }
            float maximum = Distance(new(1, 0));
            Check(Math.Abs(maximum - 6.2f / 60) < 0.0001f, "Cambió la velocidad máxima.");
            Check(Math.Abs(Distance(new(0.25f, 0)) - maximum * 0.25f) < 0.0001f, "Una inclinación pequeña se normaliza a velocidad completa.");
            Check(Math.Abs(Distance(new(0.3f, 0.4f)) - maximum * 0.5f) < 0.0001f, "El joystick diagonal pierde su intensidad.");
            Check(Math.Abs(Distance(new(1, 1)) - maximum) < 0.0001f, "Las flechas diagonales exceden la velocidad máxima.");
            Check(Distance(V2.Zero) == 0, "El mago se mueve con el joystick suelto.");
            Check(Math.Abs(Distance(V2.Zero, true) - maximum) < 0.0001f, "La ruta del ratón perdió su velocidad.");
        });
        Test("Se aplica la configuración de fuego amigo", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Warfare, FriendlyFire = false });
            var a = world.AddPlayer("A", 0)!; world.AddPlayer("Enemigo", 1); var ally = world.AddPlayer("Aliado", 0)!;
            a.Position = new(-2, 0); ally.Position = new(2, 0); a.Invulnerable = ally.Invulnerable = 0;
            Send(world, a, "input"); world.Step(); Select(world, a, Element.Arcane, Element.Arcane, Element.Arcane);
            Send(world, a, "cast"); BaselineTestSupport.Run(world, a, 30);
            Check(ally.Health == 1000, "Se dañó al aliado con fuego amigo desactivado.");
        });
        Test("El bastón respeta el fuego amigo en vida y escudo sin proteger rivales", () =>
        {
            foreach (var mode in new[] { GameMode.Warfare, GameMode.Arena })
            foreach (bool friendly in new[] { false, true })
            {
                var world = new GameWorld(new() { Mode = mode, FriendlyFire = friendly });
                var attacker = world.AddPlayer("Atacante", 0)!;
                var enemy = world.AddPlayer("Rival", 1)!;
                var ally = world.AddPlayer("Mismo color", 0)!;
                attacker.Position = new(-8, 0); attacker.Facing = new(1, 0);
                enemy.Position = new(-7, 0); ally.Position = new(-6.7f, 0);
                foreach (var player in world.Players.Values) player.Invulnerable = 0;
                ally.Shield = ally.ShieldMax = 50; ally.ShieldTime = 12;
                Send(world, attacker, "melee");
                bool protectedAlly = mode == GameMode.Warfare && !friendly;
                Check(enemy.Health == 910, "El bastón dejó de dañar al rival.");
                Check(ally.Health == (protectedAlly ? 1000 : 960) && ally.Shield == (protectedAlly ? 50 : 0),
                    $"Daño o absorción incorrectos: {mode}, fuego amigo {friendly}.");
            }
        });
        Test("Captura, desgaste de tickets, victoria y nueva partida", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Warfare, Tickets = 5 }); var a = w.AddPlayer("A", 0)!;
            a.Position = V2.Zero;
            for (int i = 0; i < 1000 && !w.MatchEnded; i++) { Send(w, a, "input"); w.Step(); }
            Check(w.MatchEnded && w.Winner == 0 && w.Scores[1] == 0, "La captura no terminó la partida.");
            for (int i = 0; i < 550; i++) w.Step();
            Check(!w.MatchEnded && w.Scores[0] == 5 && w.Scores[1] == 5, "No se reinició la partida.");
        });
    }

    public static void RunCaptureReplication(Action<string, Action> Test)
    {
        Test("Captura: progreso firmado, disputa, neutralización y replicación", () =>
        {
            var (world, a, b) = Setup(GameMode.Warfare); a.Position = V2.Zero; b.Position = new(18, 8);
            for (int i = 0; i < 60; i++) world.Step();
            var point = world.Snapshot().Points[1]; Check(Math.Abs(point.Progress + 0.24f) < 0.001f && point.Owner == -1, "No hay captura azul gradual.");
            b.Position = new(0, 1); for (int i = 0; i < 60; i++) world.Step();
            point = world.Snapshot().Points[1]; Check(point.Contested && Math.Abs(point.Progress + 0.24f) < 0.001f, "La disputa no bloqueó el progreso.");
            Check(Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!.Points[1].Contested, "No se replicó la disputa.");
            b.Position = new(18, 8); for (int i = 0; i < 200; i++) world.Step();
            Check(world.Snapshot().Points[1].Owner == 0, "No terminó la captura azul.");
            a.Position = new(-5, 0); b.Position = V2.Zero; for (int i = 0; i < 360; i++) world.Step();
            point = world.Snapshot().Points[1]; Check(point.Owner == -1 && point.Progress > 0 && point.Progress < 1 && !point.Contested, "El rojo no neutralizó primero el progreso azul.");
        });
    }

    public static void RunSpawnRegressions(Action<string, Action> Test)
    {
        Test("La reaparición encuentra suelo libre aunque los cuatro puestos estén cubiertos por muros", () =>
        {
            var (w, a, b) = Setup(GameMode.Warfare);
            a.Health = 0; a.Respawn = 10;
            foreach (float y in new[] { -2.2f, 2.2f })
            {
                b.Position = new(-21.2f, y); b.Facing = new(1, 0); b.ActionCooldown = 0;
                Select(w, b, Element.Shield, Element.Earth); Send(w, b, "cast");
            }
            Check(w.Snapshot().Walls.Length == 2 && Enumerable.Range(0, 4).All(i => !w.Walkable(w.Map.Spawn(a.Team, i))), "La prueba requiere cuatro puestos bloqueados.");
            a.Respawn = GameWorld.StepSeconds / 2; w.Step();
            Check(a.Alive && w.Walkable(a.Position), "El mago reapareció dentro de un muro y no puede moverse.");
        });
        Test("Todos contra todos reparte entradas y reapariciones por los cuatro mapas", () =>
        {
            foreach (var map in Enum.GetValues<ArenaMapId>().Where(m => !ArenaMap.Get(m).IsPractice && ArenaMap.Get(m).SupportsMode(GameMode.Arena)))
            {
                var world = new GameWorld(new() { Mode = GameMode.Arena, Map = map, MaxPlayers = 16, SpawnSeed = 902, PickupSeed = 1 });
                var players = Enumerable.Range(0, 16).Select(i => world.AddPlayer("Mago " + i, i % 2)!).ToArray();
                Check(players.All(p => world.Walkable(p.Position) && p.Invulnerable == 1), "Entrada bloqueada o sin protección en " + map);
                foreach (var a in players) foreach (var b in players)
                    if (a.Id != b.Id) Check(V2.Distance(a.Position, b.Position) >= 3, "Dos jugadores entraron juntos en " + map);
                Check(players.Any(p => Math.Abs(p.Position.X) < world.Map.HalfWidth * 0.5f), "Las entradas siguen confinadas a las bases.");
                var respawns = new List<V2>(); var player = players[0];
                for (int i = 0; i < 48; i++)
                {
                    var before = player.Position; player.Health = 0; player.Deaths++; player.Respawn = GameWorld.StepSeconds / 2;
                    world.Step(); respawns.Add(player.Position);
                    Check(player.Alive && world.Walkable(player.Position) && player.Invulnerable == 1 && V2.Distance(before, player.Position) >= 4,
                        "Reaparición inválida en " + map);
                    Check(players.Where(p => p.Id != player.Id).All(p => V2.Distance(p.Position, player.Position) >= 3), "Reaparición sobre un rival.");
                }
                Check(respawns.Any(p => p.X < 0) && respawns.Any(p => p.X > 0) && respawns.Any(p => p.Y < 0) && respawns.Any(p => p.Y > 0),
                    "El equipo limita la posición aleatoria en " + map);
            }
        });
        Test("La semilla reproduce apariciones sin cambiar las bases de los modos por equipos", () =>
        {
            var first = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 91 });
            var second = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 91 });
            var different = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 92 });
            for (int i = 0; i < 8; i++)
                Check(first.AddPlayer("Mago")!.Position == second.AddPlayer("Mago")!.Position, "La semilla no reproduce las entradas.");
            Check(first.Players[1].Position != different.AddPlayer("Mago")!.Position, "Cambiar de semilla no cambia la aparición.");
            var guarded = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 72 });
            var caster = guarded.AddPlayer("Mago")!; var returning = guarded.AddPlayer("Rival")!;
            caster.Position = new(-2, 0); returning.Position = new(20, 10); caster.Invulnerable = 0;
            Select(guarded, caster, Element.Shield, Element.Earth); Send(guarded, caster, "cast");
            caster.ActionCooldown = 0; Send(guarded, caster, "input", aim: new(8, 4)); guarded.Step(); Send(guarded, caster, "magick", slot: 2);
            var danger = guarded.Snapshot().Fields.Single();
            Check(guarded.Snapshot().Walls.Length == 1 && danger.Kind == "meteor", "Faltan los peligros de la prueba.");
            for (int i = 0; i < 30; i++)
            {
                returning.Health = 0; returning.Deaths++; returning.Respawn = GameWorld.StepSeconds / 2; guarded.Step();
                Check(guarded.Walkable(returning.Position) && V2.Distance(returning.Position, danger.Position) >= danger.Radius + ArenaMap.WizardRadius,
                    "La reaparición ignora un muro o el área de un meteorito.");
            }
            foreach (var mode in new[] { GameMode.Duel, GameMode.Warfare })
            {
                var world = new GameWorld(new() { Mode = mode });
                var blue = world.AddPlayer("Azul", 0)!; var red = world.AddPlayer("Rojo", 1)!;
                Check(blue.Position == world.Map.Spawn(0, 0) && red.Position == world.Map.Spawn(1, 0), "Cambió una base de " + mode);
                blue.Health = 0; blue.Respawn = GameWorld.StepSeconds / 2; world.Step();
                Check(blue.Position == world.Map.Spawn(0, blue.Id % 4) && blue.Invulnerable == 1.2f, "Cambió la reaparición de " + mode);
            }
        });
        Test("La protección de Arena bloquea daño y estados y caduca exactamente al segundo", () =>
        {
            var (w, a, b) = Setup(); b.Invulnerable = 1; b.Position = new(1, 0);
            Select(w, a, Element.Fire, Element.Fire, Element.Fire); Send(w, a, "cast");
            BaselineTestSupport.Run(w, a, 59);
            Check(b.Health == b.MaxHealth && b.Burning == 0 && b.Invulnerable > 0, "La protección terminó antes de un segundo.");
            BaselineTestSupport.Run(w, a, 1);
            Check(b.Invulnerable == 0 && b.Health < b.MaxHealth && b.Burning > 0, "La protección dura más de un segundo.");
            Send(w, a, "release"); b.Health = 0; b.Respawn = GameWorld.StepSeconds / 2; b.Deaths++;
            w.Step();
            Check(b.Alive && b.Invulnerable == 1 && w.Snapshot().Players.Single(p => p.Id == b.Id).InvulnerableSeconds == 1,
                "La reaparición no replica un segundo de protección.");
            a.Position = b.Position + new V2(-1, 0); a.ActionCooldown = 0;
            Select(w, a, Element.Water, Element.Cold, Element.Cold); Send(w, a, "nova");
            Check(b.Health == b.MaxHealth && b.Wet == 0 && b.Chill == 0 && b.Frozen == 0 && b.Knockback == V2.Zero,
                "El escudo inmortal permite estados o empujes.");
            BaselineTestSupport.Run(w, a, 60);
            Check(b.Invulnerable == 0 && w.Snapshot().Players.Single(p => p.Id == b.Id).InvulnerableSeconds == 0, "El escudo no desaparece tras 60 pasos.");
        });
        Test("La intensidad del escudo conserva elementos repetidos y la red valida la protección", () =>
        {
            var (w, a, _) = Setup();
            Select(w, a, Element.Shield, Element.Earth, Element.Earth); Send(w, a, "self");
            var snapshot = w.Snapshot(); var player = snapshot.Players.Single(p => p.Id == a.Id);
            Check(player.ShieldCharges == 3 && player.Ward.SequenceEqual(new[] { Element.Earth }) && player.ShieldMax == 650,
                "El escudo perdió las cargas repetidas o cambió su capacidad.");
            var copy = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(snapshot))!; SnapshotValidation.Check(copy);
            Check(copy.Players.Single(p => p.Id == a.Id).ShieldCharges == 3, "La red perdió la intensidad del escudo.");
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(Protocol.Serialize(snapshot))!;
            foreach (var oldPlayer in legacy["players"]!.AsArray())
            {
                oldPlayer!.AsObject().Remove("shieldCharges"); oldPlayer.AsObject().Remove("invulnerableSeconds");
            }
            var previousVersion = Protocol.Deserialize<WorldSnapshot>(legacy.ToJsonString())!; SnapshotValidation.Check(previousVersion);
            Check(previousVersion.Players.All(p => p.ShieldCharges == 1 && p.InvulnerableSeconds == 0), "Los campos opcionales rompen el protocolo anterior.");
            foreach (var invalid in new[] { player with { ShieldCharges = 0 }, player with { ShieldCharges = 4 },
                player with { InvulnerableSeconds = float.NaN }, player with { InvulnerableSeconds = 1.21f } })
            {
                bool rejected = false;
                try { SnapshotValidation.Check(snapshot with { Players = snapshot.Players.Select(p => p.Id == a.Id ? invalid : p).ToArray() }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Se aceptó protección o intensidad inválida.");
            }
        });
        Test("Reanimación evita devolver al aliado al interior de un muro creado tras su muerte", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Warfare });
            var a = w.AddPlayer("Reanimador", 0)!; w.AddPlayer("Rojo", 1); var ally = w.AddPlayer("Aliado", 0)!;
            a.Position = new(-2, 0); ally.Position = new(1.2f, 0); ally.Health = 0; ally.Respawn = 10;
            Select(w, a, Element.Shield, Element.Earth); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 43);
            Check(!w.Walkable(ally.Position), "La prueba requiere un muro encima del aliado caído.");
            Send(w, a, "magick", slot: 3);
            Check(ally.Alive && ally.Health == 600 && w.Walkable(ally.Position), "El aliado reanimado quedó atrapado dentro del muro.");
        });
    }
}

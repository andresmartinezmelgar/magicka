using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineProtocolTests
{
    public static void RunAddresses(Action<string, Action> Test)
    {
        Test("Direcciones IP, dominio, IPv6 y WSS", () =>
        {
            Check(ArenaConnection.Address("192.168.1.10").Port == 27015, "Puerto por defecto incorrecto.");
            Check(ArenaConnection.Address("127.0.0.1:29015").AbsolutePath == "/play", "Ruta por defecto incorrecta.");
            Check(ArenaConnection.Address("::1").Port == 27015, "IPv6 inválida.");
            Check(ArenaConnection.Address("wss://juego.example/play").Scheme == "wss", "No se acepta WSS.");
            Check(RoomBrowser.Endpoint("wss://juego.example/arcana/play", "rooms").ToString() == "https://juego.example/arcana/rooms", "No se conservó el prefijo del proxy para salas.");
        });
    }

    public static void RunValidation(Action<string, Action> Test)
    {
        Test("Una intención inválida no envenena la secuencia y no se conserva su referencia", () =>
        {
            var (w, a, _) = Setup();
            Check(!w.ApplyMessage(a.Id, new() { Type = "input", Sequence = long.MaxValue, MoveX = float.NaN }), "NaN aceptado.");
            Check(!w.ApplyMessage(a.Id, new() { Type = "element", Sequence = long.MaxValue, Element = (Element)999 }), "Elemento desconocido aceptado.");
            var message = new ClientMessage { Type = "input", Sequence = 1, MoveX = 1, AimX = 4 };
            Check(w.ApplyMessage(a.Id, message), "La secuencia quedó bloqueada por un mensaje inválido.");
            message.MoveX = -1; message.AimX = float.NaN;
            var before = a.Position; w.Step();
            Check(a.Position.X > before.X && a.Facing.IsFinite, "Modificar el mensaje alteró una entrada ya aceptada.");
        });
        Test("Las recetas de proyectiles, muros, campos y eventos del snapshot son copias", () =>
        {
            var (w, a, _) = Setup();
            Select(w, a, Element.Earth); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 10); Send(w, a, "release");
            var snapshot = w.Snapshot(); Check(snapshot.Projectiles.Length == 1, "Falta la roca de prueba.");
            snapshot.Projectiles[0].Elements[0] = Element.Life;
            foreach (var e in snapshot.Events) if (e.Elements.Length > 0) e.Elements[0] = Element.Life;
            Check(w.Snapshot().Projectiles[0].Elements[0] == Element.Earth && !w.Snapshot().Events.Any(e => e.Elements.Contains(Element.Life)), "Una copia alteró el mundo.");
            var (walls, b, _) = Setup(); Select(walls, b, Element.Shield, Element.Earth); Send(walls, b, "cast");
            var wallSnapshot = walls.Snapshot(); Check(wallSnapshot.Walls.Length == 1, "Falta el muro de prueba.");
            wallSnapshot.Walls[0].Elements[0] = Element.Life;
            Check(!walls.Snapshot().Walls[0].Elements.Contains(Element.Life), "El snapshot modificó un muro.");
            var (fields, c, _) = Setup(); Send(fields, c, "magick", slot: 2);
            var fieldSnapshot = fields.Snapshot(); Check(fieldSnapshot.Fields.Length == 1, "Falta el meteoro de prueba.");
            fieldSnapshot.Fields[0].Elements[0] = Element.Life;
            Check(!fields.Snapshot().Fields[0].Elements.Contains(Element.Life), "El snapshot modificó un campo.");
        });
        Test("Validación de snapshots acepta combates y rechaza estado parcial o no finito", () =>
        {
            var world = new GameWorld(new() { Bots = 8 });
            for (int i = 0; i < 1800; i++) { world.Step(); if (i % 30 == 0) SnapshotValidation.Check(world.Snapshot()); }
            var snapshot = world.Snapshot();
            foreach (var invalid in new[] { snapshot with { Scores = [1] }, snapshot with { Players = null! },
                snapshot with { Points = [] }, snapshot with { Players = [snapshot.Players[0] with { Position = new(float.NaN, 0) }] },
                snapshot with { Players = [snapshot.Players[0] with { Cooldowns = [0] }] } })
            {
                bool rejected = false; try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Se aceptó un estado que rompe el renderer.");
            }
        });
        Test("Snapshots: límites espaciales antes del renderer y combate válido en los cinco mapas", () =>
        {
            foreach (var id in Enum.GetValues<ArenaMapId>().Where(m => !ArenaMap.Get(m).IsPractice && m != ArenaMapId.Bastion))
            {
                var world = new GameWorld(new() { Map = id, Mode = ArenaMap.Get(id).SequentialCapture ? GameMode.Warfare : GameMode.Arena,
                    MaxPlayers = 16, Bots = 16, Seed = 41, SpawnSeed = 27, PickupSeed = 31, EnvironmentSeed = 21 });
                for (int tick = 0; tick < 900; tick++)
                {
                    world.Step();
                    if (tick % 30 == 0)
                    {
                        try { SnapshotValidation.Check(world.Snapshot()); }
                        catch (InvalidDataException e) { throw new Exception($"{id}, tick {tick}: {e.Message}"); }
                    }
                }
                var state = world.Snapshot(); var map = world.Map; var player = state.Players[0];
                var huge = new V2(float.MaxValue, float.MaxValue);
                foreach (var invalid in new[]
                {
                    state with { Players = [player with { Position = huge }] },
                    state with { Players = [player with { Facing = new(2, 0) }] },
                    state with { Projectiles = [new(10001, player.Id, huge, new(1, 0), [Element.Earth], 0.4f)] },
                    state with { Projectiles = [new(10001, player.Id, V2.Zero, huge, [Element.Earth], 0.4f)] },
                    state with { Projectiles = [new(10001, player.Id, V2.Zero, new(1, 0), [Element.Earth], 100)] },
                    state with { Walls = [new(10001, player.Id, V2.Zero, new(0, 6), 650, 7, [Element.Shield])] },
                    state with { Walls = [new(10001, player.Id, V2.Zero, huge, 650, 7, [Element.Shield])] },
                    state with { Fields = [new(10001, player.Id, V2.Zero, 100, 1, [Element.Fire], "meteor")] },
                    state with { Fields = [new(10001, player.Id, huge, 4, 1, [Element.Fire], "meteor")] },
                    state with { Fields = [new(10001, player.Id, V2.Zero, 4, 1, [Element.Fire], "meteor", 100)] },
                    state with { Beams = [new(player.Id, V2.Zero, huge, [Element.Arcane], SpellShape.Beam)] },
                    state with { Beams = [new(player.Id, new(-9, 0), new(9, 0), [Element.Arcane], SpellShape.Beam)] },
                    state with { Points = state.Points.Select(p => p.Id == 0 ? p with { Position = huge } : p).ToArray() },
                    state with { Events = [new(1, "hurt", huge, [], "")] }
                })
                {
                    bool rejected = false;
                    try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "Se aceptó una dimensión o posición no representable en " + id);
                }
                // El extremo de un rayo y el destello del bastón pueden tocar o sobrepasar la frontera.
                var edge = map.Boundary[0]; var midpoint = (edge.A + edge.B) / 2;
                SnapshotValidation.Check(state with { Projectiles = [], Walls = [], Fields = [],
                    Beams = [new(player.Id, midpoint, midpoint, [Element.Arcane], SpellShape.Beam)],
                    Events = [new(1, "melee", midpoint + (midpoint - V2.Zero).Normalized * 0.5f, [], "")] });
                var lightningOrigin = id == ArenaMapId.Frontline ? new V2(-150, -12) : new V2(4, 0);
                SnapshotValidation.Check(state with { Beams = [new(player.Id, lightningOrigin + new V2(0, -5), lightningOrigin + new V2(0, 5), [Element.Lightning], SpellShape.Lightning)] });
            }
        });
        Test("Snapshots rechazan entidades y eventos repetidos, eventos desordenados y carga o vida contradictorias", () =>
        {
            var (w, a, _) = Setup();
            Select(w, a, Element.Earth); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 10); Send(w, a, "release");
            var state = w.Snapshot(); var p = state.Players[0]; var shot = state.Projectiles.Single();
            foreach (var invalid in new[]
            {
                state with { Players = [p with { Alive = false }] },
                state with { Players = [p with { Charge = 1.1f, Casting = true }] },
                state with { Players = [p with { Health = 0, Alive = false, Casting = true }] },
                state with { Projectiles = [shot, shot] },
                state with { Projectiles = [], Walls = [new(1, a.Id, V2.Zero, new(0, 2), 10, 1, [Element.Shield]), new(1, a.Id, V2.Zero, new(0, 2), 10, 1, [Element.Shield])] },
                state with { Projectiles = [], Fields = [new(1, a.Id, V2.Zero, 2, 1, [Element.Fire], "burning_ground"), new(1, a.Id, V2.Zero, 2, 1, [Element.Fire], "burning_ground")] },
                state with { Events = [state.Events[0], state.Events[0]] },
                state with { Events = state.Events.Reverse().ToArray() }
            })
            {
                bool rejected = false; try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Se aceptó estado contradictorio o identificadores que el renderer confunde.");
            }
            // Protocolos 8 anteriores podían conservar un valor de carga después de interrumpirla.
            SnapshotValidation.Check(state with { Players = [p with { Charge = 0.5f, Casting = false }] });
        });
        Test("Direcciones con ruta e IPv6 entre corchetes; se rechazan credenciales en URL", () =>
        {
            Check(ArenaConnection.Address("[::1]").Port == Protocol.GamePort, "Se duplicaron corchetes IPv6.");
            Check(ArenaConnection.Address("[::1]:29015").Port == 29015, "Puerto IPv6 incorrecto.");
            Check(ArenaConnection.Address("juego.example/prefijo/play").Port == Protocol.GamePort, "Puerto incorrecto con ruta.");
            foreach (string invalid in new[] { "ws://usuario:clave@juego.example/play", "ws://juego.example/play?password=clave", "http://juego.example" })
            {
                bool rejected = false; try { ArenaConnection.Address(invalid); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Se aceptó una dirección inválida.");
            }
        });
    }
}

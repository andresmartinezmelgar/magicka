using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class UpgradeTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static object? Call(GameWorld w, string method, params object[] args) => typeof(GameWorld)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, args);
    private static object Field(object o, string name) => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(o)!;
    public static void Run(Action<string, Action> test)
    {
        test("Red: bases confirmadas sobreviven a keyframes descartados, ACK perdido y pausa", () =>
        {
            var w = new GameWorld(); var p = w.AddPlayer("Mago")!;
            var hello = new ServerMessage { Type = "welcome", Snapshot = w.Snapshot(), ReadyPlayerIds = [p.Id], ResumeToken = "privado" };
            var sender = new SnapshotSender(hello); var receiver = new SnapshotReceiver(hello);
            long keyframe = 0; int decoded = 0;
            for (int i = 0; i < 180; i++)
            {
                if (i < 100) w.Step(); // La pausa conserva tick, pero sigue permitiendo cambiar de base.
                var current = new ServerMessage { Type = "snapshot", Snapshot = w.Snapshot(), ReadyPlayerIds = [p.Id] };
                var data = sender.Encode(current, out var control);
                if (control != null && i != 39)
                {
                    var controlRaw = control[0] == 0xA1 ? Protocol.DecompressSnapshotPayload(control) : control;
                    var message = Protocol.Deserialize<ServerMessage>(controlRaw)!;
                    receiver.Accept(message); keyframe = message.BaselineId;
                    if (i > 75) sender.Acknowledge(keyframe);
                }
                var raw = data[0] == 0xA1 ? Protocol.DecompressSnapshotPayload(data) : data;
                var rebuilt = receiver.Decode(raw)!;
                Check(Protocol.Serialize(rebuilt.Snapshot) == Protocol.Serialize(current.Snapshot), "La base descartada corrompió un estado.");
                Check(!System.Text.Encoding.UTF8.GetString(raw).Contains("privado"), "El delta repite credenciales del saludo.");
                decoded++;
            }
            Check(keyframe >= 3 && decoded > 150, "No renovó bases después de perder una confirmación.");
        });
        test("Red: altas, bajas y reordenación por ID mantienen las entidades y los atributos", () =>
        {
            var w = new GameWorld(); w.AddPlayer("Uno"); w.AddPlayer("Dos");
            var before = new ServerMessage { Type = "snapshot", Snapshot = w.Snapshot() };
            w.RemovePlayer(w.Players.Keys.First()); w.AddPlayer("Tres");
            var state = w.Snapshot(); state = state with { Players = state.Players.Reverse().ToArray() };
            var after = new ServerMessage { Type = "snapshot", Snapshot = state };
            var restored = SnapshotDelta.Decode(SnapshotDelta.Encode(after, before), before);
            Check(Protocol.Serialize(restored.Snapshot) == Protocol.Serialize(state), "Perdió, duplicó o reordenó entidades.");
        });
        test("Red: estado binario de diez tipos conserva todos los campos y rechaza truncados", () =>
        {
            var old = Enum.GetValues<MinionKind>().Select((k, i) => new MinionSnapshot(1000 + i, k,
                new(i, 0), new(1, 0), 100, 100, 0, 0, 0, 0, false, [], false)).ToArray();
            var now = old.Reverse().Select(m => m with { Position = m.Position + new V2(0.2f, 0.3f), Facing = new(0, 1),
                Health = 85, MaxHealth = 110, Wet = 1, Burning = 2, Chill = 3, Frozen = 4, Casting = true,
                Elements = [Element.Arcane, Element.Fire], Empowered = true, SpeedBoostSeconds = 2, AttackWarningSeconds = 0.7f }).ToArray();
            var codec = new MinionDeltaCodec(old); var bytes = codec.Encode(now);
            Check(Protocol.Serialize(codec.Decode(bytes)) == Protocol.Serialize(now), "Cambió algún campo dinámico del enemigo.");
            bool rejected = false; try { codec.Decode(bytes[..^1]); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Aceptó un enemigo incompleto.");
            var born = now[0] with { Id = 1999 };
            Check(codec.Decode(codec.Encode([born])).Single() == born ||
                Protocol.Serialize(codec.Decode(codec.Encode([born]))) == Protocol.Serialize(new[] { born }), "Perdió una aparición nueva.");
        });
        test("Red: la horda y el inventario se reconstruyen exactamente entre bases renovadas", () =>
        {
            var session = new SurvivalPerformanceSession();
            var initial = new ServerMessage { Type = "welcome", Snapshot = session.Frame(0) };
            var sender = new SnapshotSender(initial); var receiver = new SnapshotReceiver(initial);
            for (int i = 0; i < 100; i++)
            {
                var current = new ServerMessage { Type = "snapshot", Snapshot = session.Frame(0.05) };
                var bytes = sender.Encode(current, out var control);
                if (control != null)
                {
                    var key = Protocol.DeserializeFrame(control, control[0] == 0xA1)!;
                    receiver.Accept(key); sender.Acknowledge(key.BaselineId);
                }
                var decoded = receiver.Decode(bytes[0] == 0xA1 ? Protocol.DecompressSnapshotPayload(bytes) : bytes)!;
                Check(Protocol.Serialize(decoded.Snapshot) == Protocol.Serialize(current.Snapshot), "Cambió una entidad o el equipo temporal.");
                SnapshotValidation.Check(decoded.Snapshot!);
            }
        });
        test("IA: la consulta espacial conserva exactamente la separación anterior", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 }); w.AddPlayer("Mago");
            var random = new Random(27);
            for (int i = 0; i < 90; i++) Call(w, "TrySpawnMinion", MinionKind.Melee);
            var minions = ((IDictionary)Field(w, "_minions")).Values.Cast<object>().ToArray();
            foreach (var m in minions) ((Wizard)Field(m, "Body")).Position = new((float)random.NextDouble() * 8 - 4, (float)random.NextDouble() * 8 - 4);
            Call(w, "BuildDefenseCells");
            foreach (var m in minions)
            {
                var body = (Wizard)Field(m, "Body"); V2 expected = V2.Zero;
                foreach (var other in minions)
                {
                    var b = (Wizard)Field(other, "Body"); if (body.Id == b.Id || !b.Alive) continue;
                    var away = body.Position - b.Position;
                    if (away.LengthSquared is > 0.0001f and < 1.2f) expected += away.Normalized * (1.1f - away.Length);
                }
                Check((V2)Call(w, "DefenseSeparation", m)! == expected, "La vecindad altera la separación.");
            }
        });
        test("Rendimiento: picos sostenidos y sobrecarga del servidor reducen el presupuesto", () =>
        {
            var budget = new DefensePerformanceBudget(192); budget.Observe(60, 55);
            Check(budget.Limit == 48, "Ignoró el percentil 95.");
            var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 }); w.AddPlayer("Mago");
            for (int i = 0; i < 300; i++) w.ReportSimulationStep(40);
            Check(w.Snapshot().Defense!.PerformanceLimit == 32, "Ignoró la carga del servidor.");
        });
        test("Reconexión: suspender cancela órdenes sin curar ni restituir vidas", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 }); var p = w.AddPlayer("Mago")!;
            p.Health = 120; p.Deaths = 1; p.Invulnerable = 0;
            w.ApplyMessage(p.Id, new() { Type = "input", Sequence = 45, MoveX = 1 });
            w.SuspendPlayer(p.Id);
            Check(p.Health == 120 && p.Deaths == 1 && p.Invulnerable == 0 && p.LastSequence == 0 && p.Input.MoveX == 0,
                "La suspensión regala vida, protección o mantiene órdenes.");
        });
        test("Inventario: filtros combinados y diferencias incluyen atributos que se pierden", () =>
        {
            var item = EquipmentCatalogue.Generate(new Random(4), tier: ItemTier.Gold, slot: EquipmentSlot.Boots);
            Check(new EquipmentFilter(item.Family, item.Tier, item.Bonuses[0].Stat).Matches(item), "El filtro excluye una coincidencia.");
            Check(!new EquipmentFilter(Tier: ItemTier.Mythic).Matches(item), "No filtra por calidad.");
            var previous = item with { Bonuses = [new(EquipmentStat.Power, 0, 1, 0.2f)] };
            var candidate = item with { Bonuses = [new(EquipmentStat.Defense, 0, 1, 0.1f)] };
            var changes = EquipmentQuery.Compare(candidate, previous);
            Check(changes.Any(c => c.Stat == EquipmentStat.Power && c.Difference == -0.2f) && changes.Any(c => c.Difference == 0.1f), "No muestra la ganancia y la pérdida.");
        });
        test("Medición integral: combate real, inventario y portal sin cuentas ni premios", () =>
        {
            var session = new SurvivalPerformanceSession();
            var state = session.Frame(1 / 30.0); SnapshotValidation.Check(state);
            Check(state.Defense!.Defenders.Sum(p => p.Items.Length) == 96 && state.Defense.Minions.Length > 150 &&
                session.SimulatedTicks >= 1 && session.PortalCrossings == 1, "La medición no incorpora la carga completa.");
        });
    }

    public static async Task RunNetwork(string address, string password, Func<string, Func<Task>, Task> test)
    {
        await test("Reconexión real: conserva ID, posición y tiempo; rechaza tokens ajenos", async () =>
        {
            var room = await RoomBrowser.CreateAsync(address, new() { Name = "Recuperación", Mode = GameMode.Defense,
                Map = ArenaMapId.Bastion, MaxPlayers = 1, ServerPassword = password });
            await using var first = new ArenaConnection(); await first.ConnectAsync(address, "Mago", 0, password, roomId: room.Id);
            await Task.Delay(250); int id = first.PlayerId; string token = first.ResumeToken;
            Check(token.Length == 64, "No emitió un identificador de recuperación.");
            first.ReconnectAfterStall();
            await Task.Delay(300);
            await using var wrong = new ArenaConnection(); bool rejected = false;
            try { await wrong.ConnectAsync(address, "Intruso", 0, password, roomId: room.Id, resumeToken: new string('a', 64)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Permitió recuperar un personaje con un token incorrecto.");
            await using var resumed = new ArenaConnection();
            await resumed.ConnectAsync(address, "Mago", 0, password, roomId: room.Id, resumeToken: token);
            Check(resumed.PlayerId == id && resumed.ResumeToken == token && resumed.Latest!.Players.Length == 1,
                "La reconexión creó otro personaje o perdió su reserva.");
            await Task.Delay(2300); Check(resumed.Connected && resumed.Latest!.Tick > first.Latest!.Tick, "La nueva base impidió continuar la conexión.");
        });
    }
}

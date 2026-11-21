extern alias baseline;
using Old = baseline::Arcana.Core;
using Arcana.Core;
using System.Text.Json;
using System.Diagnostics;

static byte[] Raw(byte[] packet) => packet[0] == 0xA1 ? Old.Protocol.DecompressSnapshotPayload(packet) : packet;
static ServerMessage Candidate(Old.ServerMessage message) => Protocol.Deserialize<ServerMessage>(Old.Protocol.SerializeUtf8(message))!;
static void Equal(bool condition, string context)
{
    if (!condition) throw new InvalidDataException(context);
}

foreach (long sequence in new long[] { 1, 127, 128, 16383, 16384, int.MaxValue, long.MaxValue })
foreach (string type in new[] { "input", "element", "magick", "inspect", "cast", "release", "clear", "melee", "self", "nova", "ready" })
{
    var action = new ClientMessage { Type = type, Sequence = sequence, MoveX = .347f, MoveY = -.329f,
        MoveTo = true, TargetX = -23.25f, TargetY = 18.75f, AimX = 1.5f, AimY = -4.5f, Element = Element.Fire,
        Slot = 2, InspectionRequestId = long.MaxValue, InspectPlayerId = 3 };
    byte[] legacy = Protocol.SerializeBinaryAction(action);
    byte[] compact = Protocol.SerializeBinaryAction(action, true);
    Equal(Old.Protocol.Serialize(Old.Protocol.DeserializeBinaryAction(legacy)) == Protocol.Serialize(Protocol.DeserializeBinaryAction(compact)), type);
    try { Protocol.DeserializeBinaryAction(compact.AsSpan(0, compact.Length - 1)); throw new Exception("Trama truncada aceptada"); }
    catch (InvalidDataException) { }
}
foreach (int players in new[] { 2, 4, 8 })
{
    var world = new Old.GameWorld(new Old.WorldOptions { Mode = players == 2 ? Old.GameMode.Duel : Old.GameMode.Arena,
        MaxPlayers = players, Bots = 0, Seed = 73, PickupSeed = 101, SpawnSeed = 103, EnvironmentSeed = 107 });
    for (int i = 0; i < players; i++) world.AddPlayer("P" + i, i % 2);
    Old.ServerMessage Frame() => new() { Type = "snapshot", Snapshot = world.Snapshot() with { StatisticsRunId = new string('a', 32) } };
    var initial = Frame();
    var oldSender = new Old.SnapshotSender(initial);
    var newSender = new SnapshotSender(Candidate(initial));
    var oldClient = new Old.SnapshotReceiver(initial);
    var newClient = new SnapshotReceiver(Candidate(initial));
    var sequences = new long[players];
    long oldUp = 0, newUp = 0, oldDown = 0, newDown = 0, inputCount = 0, bases = 0;
    var oldEncode = new List<double>(); var newEncode = new List<double>();
    void Send(int index, Old.ClientMessage message)
    {
        message.Sequence = ++sequences[index];
        byte[] oldPacket = Old.Protocol.SerializeBinaryAction(message);
        var candidate = Protocol.Deserialize<ClientMessage>(Old.Protocol.SerializeUtf8(message))!;
        byte[] newPacket = Protocol.SerializeBinaryAction(candidate, true);
        if (index == 0)
        {
            oldUp += oldPacket.Length; newUp += newPacket.Length;
            if (message.Type == "input") inputCount++;
        }
        if (oldPacket[0] == 0xB1)
        {
            var decoded = Old.Protocol.DeserializeBinaryAction(oldPacket);
            Equal(Old.Protocol.Serialize(decoded) == Protocol.Serialize(Protocol.DeserializeBinaryAction(newPacket)), message.Type);
            world.ApplyMessage(index + 1, decoded);
        }
    }
    for (int tick = 1; tick <= 3600; tick++)
    {
        for (int i = 0; i < players; i++)
        {
            float angle = (tick / 60f) + i;
            if (tick % 2 == 0) Send(i, new() { Type = "input", MoveX = tick < 600 ? 0 : MathF.Cos(angle),
                MoveY = tick < 600 ? 0 : MathF.Sin(angle), AimX = MathF.Sin(angle) * 6, AimY = MathF.Cos(angle) * 6 });
            if (tick >= 1200 && tick % 90 == 1) Send(i, new() { Type = "element", Element = Old.Element.Fire });
            if (tick >= 1200 && tick % 90 == 2) Send(i, new() { Type = "cast" });
            if (tick >= 1200 && tick % 90 == 27) Send(i, new() { Type = "release" });
        }
        if (tick % 60 == 0)
        {
            Send(0, new() { Type = "ping", PingId = tick });
            oldDown += Old.Protocol.SerializePong(tick).Length; newDown += Protocol.SerializePong(tick).Length;
        }
        if (tick % 120 == 0) Send(0, new() { Type = "match_performance", FramesPerSecond = 60, FrameTime95 = 16.67f });
        world.Step();
        if (tick % 3 != 0) continue;
        var frame = Frame();
        var candidateFrame = Candidate(frame);
        long started = Stopwatch.GetTimestamp();
        byte[] before = oldSender.Encode(frame, out var oldKey);
        oldEncode.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        started = Stopwatch.GetTimestamp();
        byte[] after = newSender.Encode(candidateFrame, out var newKey);
        newEncode.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        oldDown += before.Length; newDown += after.Length;
        var expected = Old.Protocol.Serialize(frame.Snapshot);
        Equal(Old.Protocol.Serialize(oldClient.Decode(Raw(after))!.Snapshot) == expected, "Receptor publicado, tick " + tick);
        Equal(Protocol.Serialize(newClient.Decode(Raw(after))!.Snapshot) == expected, "Receptor actual, tick " + tick);
        Equal((oldKey == null) == (newKey == null), "Calendario de bases");
        if (newKey == null) continue;
        oldDown += oldKey!.Length; newDown += newKey.Length; bases++;
        var key = Old.Protocol.Deserialize<Old.ServerMessage>(Raw(newKey))!;
        oldClient.Accept(key); newClient.Accept(Candidate(key));
        // Lose one ACK. The next delta must still decode against the confirmed base.
        if (bases == 3) continue;
        Send(0, new() { Type = "baseline_ack", BaselineId = key.BaselineId });
        oldSender.Acknowledge(key.BaselineId); newSender.Acknowledge(key.BaselineId);
    }
    oldEncode.Sort(); newEncode.Sort();
    Console.WriteLine(JsonSerializer.Serialize(new { oldP95Ms = oldEncode[1139], newP95Ms = newEncode[1139], players, seconds = 60, inputs = inputCount, frames = 1200, bases, oldUp, newUp, oldDown, newDown }));
}

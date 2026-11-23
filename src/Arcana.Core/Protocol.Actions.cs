using System.Text.Json;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Buffers;

namespace Arcana.Core;

public static partial class Protocol
{
    private static short QuantizeAxis(float value)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException("Eje no finito.");
        return (short)MathF.Round(Math.Clamp(value, -1f, 1f) * 32767f);
    }

    public static byte[] SerializeBinaryAction(ClientMessage message, bool compact = false)
    {
        int code = Array.IndexOf(BinaryActionNames, message.Type);
        if (code < 0) return SerializeAction(message);
        using var stream = new MemoryStream(48);
        using var writer = new BinaryWriter(stream);
        writer.Write(compact ? (byte)0xB2 : (byte)0xB1);
        writer.Write((byte)code);
        if (compact) writer.Write7BitEncodedInt64(message.Sequence);
        else writer.Write(message.Sequence);
        switch (message.Type)
        {
            case "input":
                writer.Write(QuantizeAxis(message.MoveX)); writer.Write(QuantizeAxis(message.MoveY)); writer.Write(message.MoveTo);
                if (message.MoveTo) { writer.Write(message.TargetX); writer.Write(message.TargetY); }
                writer.Write(message.AimX); writer.Write(message.AimY); break;
            case "element": writer.Write((byte)message.Element); break;
            case "magick": writer.Write((byte)message.Slot); break;
            case "inspect": writer.Write(message.InspectionRequestId); writer.Write(message.InspectPlayerId); break;
        }
        return stream.ToArray();
    }

    public static ClientMessage DeserializeBinaryAction(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 3 || bytes[0] is not (0xB1 or 0xB2) || bytes[1] >= BinaryActionNames.Length)
            throw new InvalidDataException("Acción binaria inválida.");
        using var stream = new MemoryStream(bytes.ToArray(), false);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        var message = new ClientMessage { Type = BinaryActionNames[reader.ReadByte()] };
        try
        {
            message.Sequence = bytes[0] == 0xB2 ? reader.Read7BitEncodedInt64() : reader.ReadInt64();
            switch (message.Type)
            {
                case "input":
                    message.MoveX = reader.ReadInt16() / 32767f; message.MoveY = reader.ReadInt16() / 32767f;
                    message.MoveTo = reader.ReadBoolean();
                    if (message.MoveTo) { message.TargetX = reader.ReadSingle(); message.TargetY = reader.ReadSingle(); }
                    message.AimX = reader.ReadSingle();
                    message.AimY = reader.ReadSingle(); break;
                case "element": message.Element = (Element)reader.ReadByte(); break;
                case "magick": message.Slot = reader.ReadByte(); break;
                case "inspect": message.InspectionRequestId = reader.ReadInt64(); message.InspectPlayerId = reader.ReadInt32(); break;
            }
        }
        catch (Exception error) when (error is EndOfStreamException or FormatException)
        { throw new InvalidDataException("Acción binaria truncada o inválida.", error); }
        if (stream.Position != stream.Length) throw new InvalidDataException("Acción binaria con datos adicionales.");
        return message;
    }

    public static byte[] SerializeAction(ClientMessage message) => message.Type switch
    {
        "ping" => JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence, pingId = message.PingId }, Json),
        "baseline_ack" => JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence, baselineId = message.BaselineId }, Json),
        "defense_performance" or "match_performance" => JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence,
            framesPerSecond = message.FramesPerSecond, frameTime95 = message.FrameTime95 }, Json),
        "lobby-team" => JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence, team = message.Team }, Json),
        "input" => SerializeInput(message),
        "element" => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = message.Type, sequence = message.Sequence, element = message.Element
        }, Json),
        "magick" or "spell_selection" => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = message.Type, sequence = message.Sequence, slot = message.Slot
        }, Json),
        "inspect" => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = message.Type, sequence = message.Sequence,
            inspectionRequestId = message.InspectionRequestId, inspectPlayerId = message.InspectPlayerId
        }, Json),
        "cast" or "release" or "clear" or "melee" or "self" or "nova" or "ready" or
        "statistics" or "lobby-ready" or "lobby-start" or "defense_restart" =>
            JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence }, Json),
        _ => SerializeUtf8(message) // Saludo y extensiones futuras conservan el formato completo.
    };

    public static byte[] SerializePong(long pingId) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type = "pong", pingId }, Json);

    private static byte[] SerializeInput(ClientMessage message)
    {
        // Los ejes del joystick ya están normalizados; los objetivos se transmiten solo si están activos.
        // El apuntado sigue siendo preciso y no se redondea en esta fase.
        if (message.MoveTo)
            return JsonSerializer.SerializeToUtf8Bytes(new { type = "input", sequence = message.Sequence,
                moveX = message.MoveX, moveY = message.MoveY, moveTo = true,
                targetX = message.TargetX, targetY = message.TargetY,
                aimX = message.AimX, aimY = message.AimY }, Json);
        return JsonSerializer.SerializeToUtf8Bytes(new { type = "input", sequence = message.Sequence,
            moveX = message.MoveX, moveY = message.MoveY,
            aimX = message.AimX, aimY = message.AimY }, Json);
    }
}

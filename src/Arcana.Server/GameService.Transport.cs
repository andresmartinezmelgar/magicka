using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Arcana.Core;

namespace Arcana.Server;

public sealed partial class GameService
{
    private static bool PasswordMatches(string? supplied, string expected)
    {
        var a = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? ""));
        var b = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static async Task SendLoop(PlayerSession session, CancellationToken token)
    {
        await foreach (var data in session.Outbox.Reader.ReadAllAsync(token))
            await SendSession(session, data, token);
    }

    private static async Task SendSession(PlayerSession session, byte[] data, CancellationToken token)
    {
        await session.SendGate.WaitAsync(token);
        try { await session.Socket.SendAsync(data.AsMemory(), data.Length > 0 && data[0] == 0xA1 ? WebSocketMessageType.Binary : WebSocketMessageType.Text, true, token); }
        finally { session.SendGate.Release(); }
    }

    private static Task Send(WebSocket socket, ServerMessage message, CancellationToken token) =>
        socket.SendAsync(Protocol.SerializeUtf8(message), WebSocketMessageType.Text, true, token);

    private static async Task Reject(WebSocket socket, string reason, CancellationToken token)
    {
        await Send(socket, new ServerMessage { Type = "error", Error = reason }, token);
        await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Solicitud rechazada", token);
    }

    private static async Task<ClientMessage?> Receive(WebSocket socket, byte[] buffer, CancellationToken token)
    {
        int size = 0;
        bool binary = false;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(size), token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType is not (WebSocketMessageType.Text or WebSocketMessageType.Binary)) throw new InvalidDataException("Tipo de mensaje no admitido.");
            if (size > 0 && binary != (result.MessageType == WebSocketMessageType.Binary)) throw new InvalidDataException("Fragmentos incompatibles.");
            binary = result.MessageType == WebSocketMessageType.Binary;
            size += result.Count;
            if (size > Protocol.MaxMessageBytes) throw new InvalidDataException("Mensaje demasiado grande.");
            if (result.EndOfMessage)
            {
                var message = binary ? Protocol.DeserializeBinaryAction(buffer.AsSpan(0, size)) : Protocol.Deserialize<ClientMessage>(buffer.AsSpan(0, size));
                if (message != null && ((message.Name?.Length ?? 0) > 64 || (message.Password?.Length ?? 0) > 128 ||
                    (message.RoomPassword?.Length ?? 0) > 128 || (message.RoomId?.Length ?? 0) > 64 ||
                    message.AccountToken == null || message.AccountToken.Length > 8192))
                    throw new InvalidDataException("Datos demasiado largos.");
                return message;
            }
        }
    }
}

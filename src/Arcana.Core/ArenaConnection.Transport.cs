using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Arcana.Core;

public sealed partial class ArenaConnection
{
    public bool Send(ClientMessage message)
    {
        lock (_sendGate)
        {
            if (!Connected) return false;
            var queued = new ClientMessage { Type = message.Type, Sequence = ++_sequence,
                PingId = message.PingId,
                InspectionRequestId = message.InspectionRequestId, InspectPlayerId = message.InspectPlayerId,
                Element = message.Element, Slot = message.Slot, Team = message.Team, ItemId = message.ItemId,
                FramesPerSecond = message.FramesPerSecond, FrameTime95 = message.FrameTime95, BaselineId = message.BaselineId, Loadout = message.Loadout,
                AutoSellStyle = message.AutoSellStyle, DefenseBotId = message.DefenseBotId,
                MoveX = message.MoveX, MoveY = message.MoveY,
                MoveTo = message.MoveTo, TargetX = message.TargetX, TargetY = message.TargetY,
                AimX = message.AimX, AimY = message.AimY };
            if (_outgoing.Writer.TryWrite(queued)) { message.Sequence = queued.Sequence; return true; }
            // Perder release/input puede dejar un canal activo: no continúa una conexión saturada.
            Fail("Cola de conexión saturada. Vuelve a conectar.", null, "network.queue_full"); return false;
        }
    }

    private async Task SendFrame(ClientMessage message, CancellationToken token)
    {
        await _socketSend.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposing) != 0) return;
            var data = message.Type == "hello" ? Protocol.SerializeAction(message) : Protocol.SerializeBinaryAction(message, _compactActions);
            if (message.Type == "ping") MarkPingSent(message.PingId);
            await _socket.SendAsync(data, data.Length > 0 && data[0] is (0xB1 or 0xB2) ? WebSocketMessageType.Binary : WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            Interlocked.Add(ref _bytesSent, data.Length);
            RecordSentTraffic(message.Type, data.Length);
        }
        finally { _socketSend.Release(); }
    }

    private async Task WriteLoop()
    {
        try { await foreach (var message in _outgoing.Reader.ReadAllAsync(_lifetime.Token)) await SendFrame(message, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        { if (Volatile.Read(ref _disposing) == 0 && !_lifetime.IsCancellationRequested) Fail("Conexión interrumpida.", e, "network.send_failed"); }
    }

    private async Task ReadLoop()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await Read(_lifetime.Token).ConfigureAwait(false);
                if (message == null)
                {
                    if (Volatile.Read(ref _disposing) == 0)
                        Fail("El servidor ha cerrado la conexión.", null, "network.server_closed");
                    break;
                }
                if (message.Type == "pong") AcceptPong(message.PingId);
                else if (message.Type == "keyframe")
                { _snapshotReceiver!.Accept(message); Send(new() { Type = "baseline_ack", BaselineId = message.BaselineId }); }
                else if (message.Type == "snapshot" && message.Snapshot != null)
                { ApplyReadiness(message); Volatile.Write(ref _latest, message.Snapshot); }
                else if (message.Type == "statistics" && message.Statistics != null)
                { message.Statistics.Validate(); Volatile.Write(ref _latestStatistics, message.Statistics); }
                else if (message.Type == "account" && message.AccountNotice != null) AccountChanged?.Invoke(message.AccountNotice);
                else if (message.Type == "equipment")
                {
                    var pending = Volatile.Read(ref _inspection);
                    if (pending == null || pending.Id != message.InspectionRequestId) continue;
                    if (message.Error.Length > 0) pending.Completion.TrySetException(new InvalidOperationException(message.Error));
                    else
                    {
                        if (message.Inspection == null || message.Inspection.PlayerId != pending.PlayerId)
                            throw new InvalidDataException("La ficha no corresponde al mago solicitado.");
                        PublicEquipment.Check(message.Inspection);
                        pending.Completion.TrySetResult(message.Inspection);
                    }
                }
                else if (message.Type == "error") throw new IOException(message.Error);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or System.Text.Json.JsonException or ObjectDisposedException)
        { if (Volatile.Read(ref _disposing) == 0 && !_lifetime.IsCancellationRequested) Fail(e is IOException ? e.Message : "Se ha perdido la conexión con el servidor.", e, "network.receive_failed"); }
        finally { Volatile.Write(ref _connected, 0); _lifetime.Cancel(); _outgoing.Writer.TryComplete(); }
    }

    private async Task<ServerMessage?> Read(CancellationToken token)
    {
        var stream = _receiveStream; var buffer = _receiveBuffer;
        stream.SetLength(0);
        WebSocketMessageType? frameType = null;
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType is not (WebSocketMessageType.Text or WebSocketMessageType.Binary) ||
                frameType != null && frameType != result.MessageType) throw new IOException("Respuesta de red inválida.");
            frameType = result.MessageType;
            if (stream.Length + result.Count > Protocol.MaxSnapshotBytes) throw new IOException("Respuesta de red demasiado grande.");
            stream.Write(buffer, 0, result.Count);
            Interlocked.Add(ref _bytesReceived, result.Count);
            if (!result.EndOfMessage) continue;
            // El buffer de MemoryStream ya contiene la trama completa: evitar ToArray()
            // por snapshot. El objeto deserializado no retiene referencias al buffer.
            ReadOnlyMemory<byte> payload = stream.GetBuffer().AsMemory(0, (int)stream.Length);
            ReadOnlyMemory<byte> raw = frameType == WebSocketMessageType.Binary
                ? Protocol.DecompressSnapshotPayload(payload.Span) : payload;
            string? type = SnapshotReceiver.ReadMessageType(raw.Span);
            RecordReceivedTraffic(type, (int)stream.Length);
            ServerMessage? message;
            if (type == "delta")
            {
                if (_snapshotReceiver == null) { stream.SetLength(0); frameType = null; continue; }
                // El delta se descomprime una sola vez y comparte el árbol con su reconstructor.
                try { message = _snapshotReceiver.Decode(raw.Span); if (message == null) { stream.SetLength(0); frameType = null; continue; } }
                catch (InvalidDataException) { stream.SetLength(0); frameType = null; continue; }
            }
            else message = Protocol.Deserialize<ServerMessage>(raw.Span);
            // Las bases se renuevan mediante keyframes confirmados y conservan la referencia anterior.
            if (message?.Snapshot != null) SnapshotValidation.Check(message.Snapshot);
            if (message?.Snapshot != null) CheckReadiness(message);
            if (message?.Type == "snapshot" && message.Snapshot == null) throw new InvalidDataException("Estado de partida ausente.");
            return message;
        }
    }
}

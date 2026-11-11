using System.Diagnostics;

namespace Arcana.Core;

/// <summary>Contenido WebSocket de mensajes completos; no incluye cabeceras, TLS ni HTTP/Firebase.</summary>
public readonly record struct NetworkMessageTraffic(long Messages, long Bytes)
{
    public static NetworkMessageTraffic operator -(NetworkMessageTraffic current, NetworkMessageTraffic previous) =>
        new(current.Messages - previous.Messages, current.Bytes - previous.Bytes);
}

public readonly record struct NetworkTrafficSnapshot(NetworkMessageTraffic InputSent, NetworkMessageTraffic ActionsSent,
    NetworkMessageTraffic ControlSent, NetworkMessageTraffic DeltasReceived, NetworkMessageTraffic KeyframesReceived,
    NetworkMessageTraffic FullStatesReceived, NetworkMessageTraffic StatisticsReceived, NetworkMessageTraffic ControlReceived)
{
    public long SentBytes => InputSent.Bytes + ActionsSent.Bytes + ControlSent.Bytes;
    public long ReceivedBytes => DeltasReceived.Bytes + KeyframesReceived.Bytes + FullStatesReceived.Bytes +
        StatisticsReceived.Bytes + ControlReceived.Bytes;
    public static NetworkTrafficSnapshot operator -(NetworkTrafficSnapshot current, NetworkTrafficSnapshot previous) =>
        new(current.InputSent - previous.InputSent, current.ActionsSent - previous.ActionsSent,
            current.ControlSent - previous.ControlSent, current.DeltasReceived - previous.DeltasReceived,
            current.KeyframesReceived - previous.KeyframesReceived, current.FullStatesReceived - previous.FullStatesReceived,
            current.StatisticsReceived - previous.StatisticsReceived, current.ControlReceived - previous.ControlReceived);
}

/// <summary>Contadores sin asignaciones por mensaje. Siempre reciben la longitud enviada, antes de descomprimir.</summary>
public sealed class NetworkTrafficCounters
{
    private const int Input = 0, Action = 1, SentControl = 2, Delta = 3, Keyframe = 4, FullState = 5,
        Statistics = 6, ReceivedControl = 7;
    private readonly long[] _messages = new long[8], _bytes = new long[8];

    public void RecordSent(string? type, int bytes) => Add(type switch
    {
        "input" => Input,
        "element" or "magick" or "cast" or "release" or "clear" or "melee" or "self" or "nova" => Action,
        _ => SentControl
    }, bytes);

    public void RecordReceived(string? type, int bytes) => Add(type switch
    {
        "delta" => Delta, "keyframe" => Keyframe, "welcome" or "snapshot" => FullState,
        "statistics" => Statistics, _ => ReceivedControl
    }, bytes);

    private void Add(int category, int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        Interlocked.Add(ref _bytes[category], bytes);
        Interlocked.Increment(ref _messages[category]);
    }

    // Lectura concurrente aproximada: una trama en curso puede acabar en la muestra siguiente.
    public NetworkTrafficSnapshot Capture() => new(Read(Input), Read(Action), Read(SentControl), Read(Delta),
        Read(Keyframe), Read(FullState), Read(Statistics), Read(ReceivedControl));
    private NetworkMessageTraffic Read(int category) => new(Interlocked.Read(ref _messages[category]), Interlocked.Read(ref _bytes[category]));
}

public sealed partial class ArenaConnection
{
    private readonly NetworkTrafficCounters _traffic = new();
    private readonly object _trafficLogGate = new();
    private long _trafficLoggedAt;
    private NetworkTrafficSnapshot _trafficLogged;
    public NetworkTrafficSnapshot Traffic => _traffic.Capture();

    private void RecordSentTraffic(string? type, int bytes)
    { _traffic.RecordSent(type, bytes); LogTrafficIfDue(); }

    private void RecordReceivedTraffic(string? type, int bytes)
    { _traffic.RecordReceived(type, bytes); LogTrafficIfDue(); }

    private void LogTrafficIfDue()
    {
        long now = Stopwatch.GetTimestamp();
        long previousAt = Interlocked.CompareExchange(ref _trafficLoggedAt, now, 0);
        if (previousAt == 0 || Stopwatch.GetElapsedTime(previousAt, now).TotalSeconds < 30) return;
        lock (_trafficLogGate)
        {
            double seconds = Stopwatch.GetElapsedTime(Interlocked.Read(ref _trafficLoggedAt), now).TotalSeconds;
            if (seconds < 30) return;
            var current = Traffic;
            var interval = current - _trafficLogged;
            _trafficLogged = current;
            Interlocked.Exchange(ref _trafficLoggedAt, now);
            var context = Context();
            context["seconds"] = Math.Round(seconds, 3);
            context["sentPayloadBytes"] = current.SentBytes;
            context["receivedPayloadBytes"] = current.ReceivedBytes;
            context["sentMBPerMinute"] = Math.Round(interval.SentBytes * 60d / seconds / 1_000_000d, 4);
            context["receivedMBPerMinute"] = Math.Round(interval.ReceivedBytes * 60d / seconds / 1_000_000d, 4);
            Add("inputSent", interval.InputSent); Add("actionsSent", interval.ActionsSent); Add("controlSent", interval.ControlSent);
            Add("deltasReceived", interval.DeltasReceived); Add("keyframesReceived", interval.KeyframesReceived);
            Add("fullStatesReceived", interval.FullStatesReceived); Add("statisticsReceived", interval.StatisticsReceived);
            Add("controlReceived", interval.ControlReceived);
            Diagnostics.Info("network.traffic", "Tráfico WebSocket por tipo; MB decimales, sin cabeceras ni HTTP.", context);
            void Add(string name, NetworkMessageTraffic traffic)
            { context[name + "Bytes"] = traffic.Bytes; context[name + "Messages"] = traffic.Messages; }
        }
    }
}

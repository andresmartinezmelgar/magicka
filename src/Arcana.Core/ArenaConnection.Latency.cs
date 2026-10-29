using System.Diagnostics;

namespace Arcana.Core;

public sealed partial class ArenaConnection
{
    private const double PingIntervalSeconds = 1, PingTimeoutSeconds = 5;
    private readonly object _pingGate = new();
    private long _pingSequence, _pendingPingId, _pingRequestedAt, _pingSentAt, _pingSampleAt;
    private double? _pingMilliseconds;

    /// <summary>Ida y vuelta por el mismo WebSocket del juego; null sin una respuesta reciente.</summary>
    public double? PingMilliseconds
    {
        get
        {
            if (!Connected) return null;
            lock (_pingGate)
                return _pingMilliseconds.HasValue && Stopwatch.GetElapsedTime(_pingSampleAt).TotalSeconds < PingTimeoutSeconds
                    ? _pingMilliseconds : null;
        }
    }

    /// <summary>Como máximo una consulta por segundo y una pendiente; no bloquea el hilo gráfico.</summary>
    public bool RequestPing()
    {
        if (!Connected) return false;
        long id;
        lock (_pingGate)
        {
            long now = Stopwatch.GetTimestamp();
            if (_pingSequence > 0 && Stopwatch.GetElapsedTime(_pingRequestedAt, now).TotalSeconds < PingIntervalSeconds ||
                _pendingPingId != 0 && Stopwatch.GetElapsedTime(_pingRequestedAt, now).TotalSeconds < PingTimeoutSeconds)
                return false;
            _pingRequestedAt = now;
            _pendingPingId = id = ++_pingSequence;
            _pingSentAt = 0;
        }
        if (Send(new() { Type = "ping", PingId = id })) return true;
        lock (_pingGate) if (_pendingPingId == id) _pendingPingId = 0;
        return false;
    }

    private void MarkPingSent(long id)
    {
        // Excluye la espera en la cola del cliente y no depende de relojes sincronizados.
        lock (_pingGate) if (_pendingPingId == id) _pingSentAt = Stopwatch.GetTimestamp();
    }

    private void AcceptPong(long id)
    {
        lock (_pingGate)
        {
            if (id <= 0 || id != _pendingPingId || _pingSentAt == 0) return;
            long now = Stopwatch.GetTimestamp();
            double milliseconds = Stopwatch.GetElapsedTime(_pingSentAt, now).TotalMilliseconds;
            _pendingPingId = 0;
            if (milliseconds >= PingTimeoutSeconds * 1000) return;
            _pingMilliseconds = milliseconds;
            _pingSampleAt = now;
        }
    }
}

using Arcana.Core;

internal static class NetworkTrafficTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Tráfico separa estados, bases, estadísticas y acciones sin contar bytes descomprimidos", () =>
        {
            var counters = new NetworkTrafficCounters();
            var input = Protocol.SerializeBinaryAction(new() { Type = "input", Sequence = 1, AimX = 10 });
            var raw = Protocol.SerializeUtf8(new { type = "delta", repeated = new string('x', 4096) });
            var compressed = Protocol.CompressSnapshotPayload(raw);
            if (compressed.Length >= raw.Length) throw new Exception("El fixture debe estar comprimido.");
            counters.RecordSent("input", input.Length);
            counters.RecordSent("cast", 10);
            counters.RecordSent("ping", 40);
            counters.RecordReceived("delta", compressed.Length);
            counters.RecordReceived("keyframe", 100);
            counters.RecordReceived("welcome", 250);
            counters.RecordReceived("snapshot", 150);
            counters.RecordReceived("statistics", 120);
            counters.RecordReceived("pong", 25);
            counters.RecordReceived("future_control", 17);
            var capture = counters.Capture();
            if (capture.InputSent != new NetworkMessageTraffic(1, input.Length) ||
                capture.ActionsSent != new NetworkMessageTraffic(1, 10) ||
                capture.ControlSent != new NetworkMessageTraffic(1, 40) ||
                capture.DeltasReceived != new NetworkMessageTraffic(1, compressed.Length) ||
                capture.KeyframesReceived != new NetworkMessageTraffic(1, 100) ||
                capture.FullStatesReceived != new NetworkMessageTraffic(2, 400) ||
                capture.StatisticsReceived != new NetworkMessageTraffic(1, 120) ||
                capture.ControlReceived != new NetworkMessageTraffic(2, 42) ||
                capture.SentBytes != input.Length + 50 || capture.ReceivedBytes != compressed.Length + 662)
                throw new Exception("El desglose no corresponde al contenido transmitido.");
            counters.RecordReceived("delta", 11);
            var interval = counters.Capture() - capture;
            if (interval.DeltasReceived != new NetworkMessageTraffic(1, 11) || interval.ReceivedBytes != 11 || interval.SentBytes != 0)
                throw new Exception("El intervalo incluye tráfico de una muestra anterior.");
        });
        test("Contadores de tráfico conservan mensajes concurrentes y rechazan longitudes negativas", () =>
        {
            var counters = new NetworkTrafficCounters();
            Parallel.For(0, 1000, _ =>
            {
                counters.RecordSent("input", 23);
                counters.RecordReceived("delta", 200);
            });
            var capture = counters.Capture();
            if (capture.InputSent != new NetworkMessageTraffic(1000, 23000) ||
                capture.DeltasReceived != new NetworkMessageTraffic(1000, 200000))
                throw new Exception("Se han perdido muestras concurrentes.");
            bool rejected = false;
            try { counters.RecordReceived("delta", -1); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            if (!rejected || counters.Capture() != capture) throw new Exception("Una longitud inválida altera los contadores.");
        });
    }
}

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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Arcana Arena {Version}: TCP {Port}, hasta {Rooms} salas", BuildInfo.Version, settings.Port, MaxRooms);
        var clock = Stopwatch.StartNew();
        double previous = clock.Elapsed.TotalSeconds, accumulator = 0;
        long cleanupAt = Environment.TickCount64, metricsAt = cleanupAt, lagAt = 0;
        var snapshots = new List<(ServerMessage Message, PlayerSession[] Recipients)>(MaxRooms);
        var dirty = new HashSet<GameRoom>();
        double maxStepMs = 0;
        long snapshotBytes = 0, snapshotFrames = 0, compressedFrames = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
        rewards.Delivered += NotifyAccounts;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                double now = clock.Elapsed.TotalSeconds;
                double elapsed = now - previous;
                if (elapsed > 0.15 && Environment.TickCount64 - lagAt > 10000)
                {
                    Diagnostics.Warn("simulation.lag", "La simulación ha sufrido un retraso.", context:
                        new Dictionary<string, object?> { ["tick"] = CurrentTick, ["elapsedMs"] = elapsed * 1000 });
                    lagAt = Environment.TickCount64;
                }
                accumulator += Math.Min(0.15, elapsed); previous = now;
                snapshots.Clear(); dirty.Clear();
                long stepStarted = Stopwatch.GetTimestamp();
                lock (_gate)
                {
                    while (accumulator >= GameWorld.StepSeconds)
                    {
                        foreach (var room in _rooms.Values)
                        {
                            ExpireReservations(room);
                            if (_draining && !room.Started)
                            {
                                foreach (var waiting in room.Sessions.Values) waiting.Socket.Abort();
                                continue;
                            }
                            if (!AcceptingNewGames && (!room.Started || Completed(room)))
                            {
                                // Conservar el resultado final sin que el reloj
                                // del mundo empiece otra partida automáticamente.
                                room.StartRequested = false;
                                room.RewardStatus = "Partida terminada. Hay una nueva versión del servidor; vuelve al menú para jugar de nuevo.";
                                if (!room.Started) room.RewardStatus = UpdatingMessage;
                                if (CurrentTick % 3 == 0 && room.Sessions.Count > 0) dirty.Add(room);
                                continue;
                            }
                            // Pausa los bots y el reloj de las salas sin jugadores humanos.
                            if (room.Sessions.Count == 0) continue;
                            int readyPlayers = ProcessRoomInput(room);
                            if (readyPlayers == 0 || !room.Started && readyPlayers != room.Sessions.Count)
                            { if (CurrentTick % 3 == 0) dirty.Add(room); continue; }
                            if (room.BestOf > 1 && !room.Started)
                            {
                                bool enoughPlayers = room.World.Players.Count >= 2;
                                bool everyoneReady = room.Sessions.Values.All(s => s.LobbyReady);
                                bool teamsReady = room.Options.Mode == GameMode.Arena ||
                                    room.World.Players.Values.Any(p => p.Team == 0) &&
                                    room.World.Players.Values.Any(p => p.Team == 1);
                                if (!room.StartRequested || !enoughPlayers || !everyoneReady || !teamsReady)
                                { if (CurrentTick % 3 == 0) dirty.Add(room); continue; }
                            }
                            if (!room.Started && room.AwaitingRematch)
                            {
                                room.AwaitingRematch = false;
                                room.RewardStatus = "";
                            }
                            room.Started = true;
                            // Entrar en una partida iniciada no la pausa: el nuevo jugador
                            // permanece protegido y sin acciones hasta terminar su carga.
                            foreach (var session in room.Sessions.Values)
                                if (!session.Ready && !session.Resumed && !session.RestoredDefense && room.World.Players.TryGetValue(session.Id, out var player)) player.Invulnerable = 1.2f;
                            bool endedBefore = room.World.MatchEnded;
                            long simulationStarted = Stopwatch.GetTimestamp();
                            room.World.Step();
                            room.World.ReportSimulationStep(Stopwatch.GetElapsedTime(simulationStarted).TotalMilliseconds);
                            TrackAccounts(room, endedBefore);
                            TrackStatistics(room);
                            room.LastOccupied = Environment.TickCount64;
                            // Comunicar el desenlace inmediatamente a TODOS los participantes,
                            // sin esperar al siguiente ciclo de envío periódico.
                            // El estado se sigue replicando durante toda la cuenta atrás.
                            if (CurrentTick % 3 == 0 || endedBefore != room.World.MatchEnded)
                                dirty.Add(room);
                        }
                        Interlocked.Increment(ref _tick);
                        accumulator -= GameWorld.StepSeconds;
                    }
                    // Si hubo varios pasos atrasados, envía únicamente el estado más reciente.
                    foreach (var room in dirty) snapshots.Add((RoomSnapshot(room), room.Sessions.Values.ToArray()));
                    if (Environment.TickCount64 - cleanupAt > 60000) { RemoveIdleRooms(); cleanupAt = Environment.TickCount64; }
                    maxStepMs = Math.Max(maxStepMs, Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds);
                    if (Environment.TickCount64 - metricsAt >= 30000)
                    {
                        Diagnostics.Info("server.metrics", "Estado periódico de la simulación.", new Dictionary<string, object?>
                        { ["tick"] = CurrentTick, ["rooms"] = _rooms.Count, ["connections"] = _sessions.Count,
                            ["activeRooms"] = _rooms.Values.Count(r => r.Sessions.Count > 0), ["maxStepMs"] = maxStepMs,
                            ["ignoredCommands"] = _ignoredCommands, ["managedBytes"] = GC.GetTotalMemory(false),
                             ["queuedSnapshotPayloadBytes"] = Interlocked.Read(ref snapshotBytes),
                             ["snapshotRecipientFrames"] = Interlocked.Read(ref snapshotFrames),
                             ["compressedSnapshotFrames"] = Interlocked.Read(ref compressedFrames) });
                        metricsAt = Environment.TickCount64; maxStepMs = 0; _ignoredCommands = 0;
                        Interlocked.Exchange(ref snapshotBytes, 0); Interlocked.Exchange(ref snapshotFrames, 0);
                        Interlocked.Exchange(ref compressedFrames, 0);
                    }
                }
                foreach (var snapshot in snapshots)
                {
                    // PvP comparte la preparación JSON. Supervivencia se filtra antes para cada jugador.
                    var shared = snapshot.Message.Snapshot?.Defense == null ? new PreparedSnapshotFrame(snapshot.Message) : null;
                    // Cada destinatario conserva su propia base confirmada y sus datos privados.
                    // Keyframes y deltas se pueden descartar sin crear dependencias entre fotogramas.
                    foreach (var session in snapshot.Recipients)
                    {
                        var frame = shared ?? new PreparedSnapshotFrame(DefenseFrame(snapshot.Message, session.Id));
                        byte[] bytes = session.Network.EncodePrepared(frame, out var keyframe);
                        if (keyframe != null && session.Outbox.Writer.TryWrite(keyframe)) Interlocked.Add(ref snapshotBytes, keyframe.Length);
                        if (session.Outbox.Writer.TryWrite(bytes))
                        {
                            Interlocked.Add(ref snapshotBytes, bytes.Length);
                            Interlocked.Increment(ref snapshotFrames);
                            if (bytes.Length > 0 && bytes[0] == 0xA1) Interlocked.Increment(ref compressedFrames);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            rewards.Delivered -= NotifyAccounts;
            foreach (var session in _sessions.Values) { session.Outbox.Writer.TryComplete(); session.Socket.Abort(); }
        }
    }
}

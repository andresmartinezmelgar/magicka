using System.Security.Cryptography;
using System.Text;
using Arcana.Core;

namespace Arcana.Server;

public sealed record DrainRequest(bool Enabled);
public sealed record DeploymentState(string Version, bool Draining, int Connections, int Reservations, bool PendingDeliveries);

public sealed partial class GameService
{
    private bool _draining;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Task> _persistenceWrites = new();
    private Task<bool> PersistOutsideSimulation(Func<Task<bool>> operation)
    {
        if (_persistenceWrites.Count >= 1024) return Task.FromResult(false);
        Guid id = Guid.NewGuid(); var task = Task.Run(operation); _persistenceWrites[id] = task;
        _ = task.ContinueWith(finished =>
        {
            _persistenceWrites.TryRemove(id, out _);
            if (finished.IsFaulted) Diagnostics.Error("journal.write_failed", "Fallo al guardar un recibo pendiente.", finished.Exception);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }
    public void SetDraining(bool enabled) { lock (_gate) _draining = enabled; }
    public DeploymentState DeploymentState()
    {
        int connections, reservations; bool draining;
        lock (_gate) { connections = _sessions.Count; reservations = _rooms.Values.Sum(r => r.Reservations.Count); draining = _draining; }
        return new(BuildInfo.Version, draining, connections, reservations,
            !_persistenceWrites.IsEmpty || rewards.HasPendingDeliveries || statistics.PendingCount > 0);
    }
}

public static class DeploymentEndpoints
{
    private static bool Authorized(HttpContext context)
    {
        string expected = Environment.GetEnvironmentVariable("ARCANA_DEPLOYMENT_KEY") ?? "";
        string supplied = context.Request.Headers["X-Arcana-Deployment-Key"].ToString();
        return expected.Length is >= 32 and <= 256 && supplied.Length is >= 32 and <= 256 &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }
    public static void MapDeployment(this WebApplication app)
    {
        app.MapGet("/internal/lifecycle", (HttpContext context, GameService game) =>
            Authorized(context) ? Results.Json(game.DeploymentState(), Protocol.Json) : Results.Unauthorized());
        app.MapPost("/internal/drain", (DrainRequest request, HttpContext context, GameService game) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            game.SetDraining(request.Enabled);
            return Results.Json(game.DeploymentState(), Protocol.Json);
        });
    }
}

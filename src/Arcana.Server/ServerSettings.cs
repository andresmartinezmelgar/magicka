using Arcana.Core;

namespace Arcana.Server;

public sealed class ServerSettings
{
    public string Name { get; init; } = "Arcana Arena";
    public string Password { get; init; } = "";
    public int Port { get; init; } = Protocol.GamePort;
    public int DiscoveryPort { get; init; } = Protocol.DiscoveryPort;
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public static string LogDirectory => Environment.GetEnvironmentVariable("ARCANA_LOG_DIRECTORY") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcanaArena", "logs");
    public WorldOptions World { get; init; } = new();
    public static ServerSettings FromEnvironment()
    {
        int Number(string key, int fallback, int min, int max) =>
            int.TryParse(Environment.GetEnvironmentVariable(key), out int n) ? Math.Clamp(n, min, max) : fallback;
        var mode = Enum.TryParse<GameMode>(Environment.GetEnvironmentVariable("GAME_MODE"), true, out var value)
            && Enum.IsDefined(value) ? value : GameMode.Warfare;
        var map = Enum.TryParse<ArenaMapId>(Environment.GetEnvironmentVariable("ARENA_MAP"), true, out var mapValue)
            && Enum.IsDefined(mapValue) ? mapValue : ArenaMapId.Classic;
        if (ArenaMap.Get(map).IsPractice) throw new ArgumentException("El laboratorio se inicia sin conexión desde Práctica, no mediante ARENA_MAP.");
        int maxPlayers = mode == GameMode.Duel ? 2 : Number("MAX_PLAYERS", 8, 2, 16);
        string name = new((Environment.GetEnvironmentVariable("SERVER_NAME") ?? "Arcana Arena")
            .Where(c => !char.IsControl(c)).Take(64).ToArray());
        string password = Environment.GetEnvironmentVariable("SERVER_PASSWORD") ?? "";
        if (password.Length > 128) throw new ArgumentException("SERVER_PASSWORD admite un máximo de 128 caracteres.");
        return new()
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Arcana Arena" : name.Trim(),
            Password = password,
            InstanceId = Guid.TryParse(Environment.GetEnvironmentVariable("ARCANA_SERVER_INSTANCE"), out var instance)
                ? instance.ToString("N") : Guid.NewGuid().ToString("N"),
            Port = Number("GAME_PORT", Protocol.GamePort, 1024, 65535),
            DiscoveryPort = Number("DISCOVERY_PORT", Protocol.DiscoveryPort, 1024, 65535),
            World = new()
            {
                Mode = mode, Map = map, MaxPlayers = maxPlayers,
                Bots = Number("BOTS", 0, 0, maxPlayers - 1),
                FriendlyFire = !bool.TryParse(Environment.GetEnvironmentVariable("FRIENDLY_FIRE"), out bool friendly) || friendly,
                ScoreLimit = Number("SCORE_LIMIT", 15, 1, 200), Tickets = Number("TICKETS", 75, 5, 1000)
            }
        };
    }
}

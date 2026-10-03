using Arcana.Core;

namespace Arcana.Server;

public sealed class ServerSettings
{
    public string Name { get; init; } = "Arcana Arena";
    public string Password { get; init; } = "";
    public int Port { get; init; } = Protocol.GamePort;
    public int DiscoveryPort { get; init; } = Protocol.DiscoveryPort;
    public WorldOptions World { get; init; } = new();
    public static ServerSettings FromEnvironment()
    {
        int Number(string key, int fallback, int min, int max) =>
            int.TryParse(Environment.GetEnvironmentVariable(key), out int n) ? Math.Clamp(n, min, max) : fallback;
        var mode = Enum.TryParse<GameMode>(Environment.GetEnvironmentVariable("GAME_MODE"), true, out var value)
            ? value : GameMode.Warfare;
        int maxPlayers = mode == GameMode.Duel ? 2 : Number("MAX_PLAYERS", 8, 2, 16);
        return new()
        {
            Name = Environment.GetEnvironmentVariable("SERVER_NAME") ?? "Arcana Arena",
            Password = Environment.GetEnvironmentVariable("SERVER_PASSWORD") ?? "",
            Port = Number("GAME_PORT", Protocol.GamePort, 1024, 65535),
            DiscoveryPort = Number("DISCOVERY_PORT", Protocol.DiscoveryPort, 1024, 65535),
            World = new()
            {
                Mode = mode, MaxPlayers = maxPlayers,
                Bots = Number("BOTS", 0, 0, maxPlayers - 1),
                FriendlyFire = Environment.GetEnvironmentVariable("FRIENDLY_FIRE") != "false",
                ScoreLimit = Number("SCORE_LIMIT", 15, 1, 200), Tickets = Number("TICKETS", 75, 5, 1000)
            }
        };
    }
}

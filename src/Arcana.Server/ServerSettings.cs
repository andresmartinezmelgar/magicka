using Arcana.Core;

namespace Arcana.Server;

public sealed class ServerSettings
{
    public string Name { get; init; } = "Arcana Arena";
    public string Password { get; init; } = "";
    public int Port { get; init; } = Protocol.GamePort;
    public int DiscoveryPort { get; init; } = Protocol.DiscoveryPort;
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public string Generation { get; init; } = "";
    public string ManagementToken { get; init; } = "";
    public string ManagementPeer { get; init; } = "";
    public string AdmissionFile { get; init; } = "";
    public int AdvertisedPort { get; init; }
    public int AdvertisedDiscoveryPort { get; init; }
    public bool DiscoveryEnabled { get; init; } = true;
    public bool LoopbackOnly { get; init; }
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
            && Enum.IsDefined(mapValue) ? mapValue : mode == GameMode.Defense ? ArenaMapId.Bastion : ArenaMapId.Classic;
        if (ArenaMap.Get(map).IsPractice) throw new ArgumentException("El laboratorio se inicia sin conexión desde Práctica, no mediante ARENA_MAP.");
        int maxPlayers = mode switch
        {
            GameMode.Duel => 2,
            GameMode.Defense => Number("MAX_PLAYERS", 4, 1, 4),
            _ => Number("MAX_PLAYERS", 8, 2, 16)
        };
        string name = new((Environment.GetEnvironmentVariable("SERVER_NAME") ?? "Arcana Arena")
            .Where(c => !char.IsControl(c)).Take(64).ToArray());
        string password = Environment.GetEnvironmentVariable("SERVER_PASSWORD") ?? "";
        if (password.Length > 128) throw new ArgumentException("SERVER_PASSWORD admite un máximo de 128 caracteres.");
        string generation = Environment.GetEnvironmentVariable("ARCANA_SERVER_GENERATION") ?? "";
        if (generation.Length > 0 && (!Guid.TryParseExact(generation, "N", out _) || generation != generation.ToLowerInvariant()))
            throw new ArgumentException("ARCANA_SERVER_GENERATION debe ser una identidad hexadecimal de 32 caracteres.");
        string management = Environment.GetEnvironmentVariable("ARCANA_MANAGEMENT_TOKEN") ?? "";
        if (management.Length > 0 && (management.Length != 64 || !management.All(Uri.IsHexDigit)))
            throw new ArgumentException("El token de administración debe tener 64 caracteres hexadecimales.");
        string managementPeer = Environment.GetEnvironmentVariable("ARCANA_MANAGEMENT_PEER") ?? "";
        if (managementPeer.Length > 0 && !System.Net.IPAddress.TryParse(managementPeer, out _)) throw new ArgumentException("Dirección de administración no válida.");
        return new()
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Arcana Arena" : name.Trim(),
            Password = password,
            InstanceId = generation.Length > 0 ? generation : Guid.TryParse(Environment.GetEnvironmentVariable("ARCANA_SERVER_INSTANCE"), out var instance)
                ? instance.ToString("N") : Guid.NewGuid().ToString("N"),
            Generation = generation, ManagementToken = management, ManagementPeer = managementPeer,
            AdmissionFile = Environment.GetEnvironmentVariable("ARCANA_ADMISSION_FILE") ?? "",
            AdvertisedPort = Number("ARCANA_ADVERTISED_PORT", 0, 1024, 65535),
            AdvertisedDiscoveryPort = Number("ARCANA_ADVERTISED_DISCOVERY_PORT", 0, 1024, 65535),
            DiscoveryEnabled = Environment.GetEnvironmentVariable("ARCANA_DISCOVERY_ENABLED") != "false",
            LoopbackOnly = Environment.GetEnvironmentVariable("ARCANA_LOOPBACK_ONLY") == "true",
            Port = Number("GAME_PORT", Protocol.GamePort, 1024, 65535),
            DiscoveryPort = Number("DISCOVERY_PORT", Protocol.DiscoveryPort, 1024, 65535),
            World = new()
            {
                Mode = mode, Map = map, MaxPlayers = maxPlayers,
                Bots = Number("BOTS", 0, 0, maxPlayers - 1),
                FriendlyFire = mode != GameMode.Defense && (!bool.TryParse(Environment.GetEnvironmentVariable("FRIENDLY_FIRE"), out bool friendly) || friendly),
                ScoreLimit = Number("SCORE_LIMIT", 15, 1, 200), Tickets = Number("TICKETS", 75, 5, 1000)
            }
        };
    }
}

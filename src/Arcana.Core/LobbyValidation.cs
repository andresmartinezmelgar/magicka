namespace Arcana.Core;

/// <summary>Las salas recibidas por HTTP y WebSocket respetan las mismas reglas.</summary>
public static class LobbyValidation
{
    public static bool IsValid(ServerInfo? server) => server != null &&
        !string.IsNullOrWhiteSpace(server.Name) && server.Name.Length <= 64 &&
        server.Port is >= 1 and <= 65535 && server.DiscoveryPort is >= 1 and <= 65535 &&
        Enum.IsDefined(server.Mode) && server.Protocol == Protocol.Version &&
        server.MaxPlayers is >= 2 and <= 256 && server.Players >= 0 && server.Players <= server.MaxPlayers &&
        server.InstanceId is not { Length: > 64 };

    public static void Check(RoomInfo? room)
    {
        if (room == null || string.IsNullOrWhiteSpace(room.Id) || room.Id.Length > 64 ||
            string.IsNullOrWhiteSpace(room.Name) || room.Name.Length > 32 || !Enum.IsDefined(room.Mode) ||
            !Enum.IsDefined(room.Map) || ArenaMap.Get(room.Map).IsPractice || !ArenaMap.Get(room.Map).SupportsMode(room.Mode) || room.MaxPlayers < (room.Mode == GameMode.Defense ? 1 : 2) || room.MaxPlayers > (room.Mode == GameMode.Defense ? DefenseRules.MaxDefenders : 16) || room.Players < 0 || room.Players > room.MaxPlayers ||
            room.Bots < 0 || room.Bots >= room.MaxPlayers || room.Bots > room.Players ||
            room.ScoreLimit is < 1 or > 200 || room.Tickets is < 5 or > 1000 ||
            room.BestOf is not (1 or 5 or 7 or 9) ||
            (room.Mode is GameMode.Arena or GameMode.Defense) && room.BestOf > 1 || !Enum.IsDefined(room.DefenseFlow) ||
            room.Mode == GameMode.Duel && room.MaxPlayers != 2)
            throw new InvalidDataException("El servidor devolvió una sala inválida.");
    }

    public static void Check(RoomDirectory? directory)
    {
        if (directory?.Server == null) throw new InvalidDataException("Directorio de salas inválido.");
        if (directory.Server.Protocol != Protocol.Version)
            throw new InvalidOperationException("Actualiza el juego: la versión del servidor es diferente.");
        if (directory.Server.GameVersion is { Length: > 0 } gameVersion &&
            !string.Equals(gameVersion, BuildInfo.Version, StringComparison.Ordinal))
            throw new InvalidOperationException($"La versión del servidor ({gameVersion}) no coincide con tu juego ({BuildInfo.Version}). Actualiza Arcana Arena.");
        if (!IsValid(directory.Server) || directory.Rooms is not { Length: <= 16 })
            throw new InvalidDataException("Directorio de salas inválido.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in directory.Rooms)
        {
            Check(room);
            if (!ids.Add(room.Id)) throw new InvalidDataException("El servidor repitió una sala en el directorio.");
        }
    }
}

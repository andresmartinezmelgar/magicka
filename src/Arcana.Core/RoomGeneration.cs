namespace Arcana.Core;

/// <summary>La identidad de una sala permite recuperar su proceso sin cambiar la URL pública.</summary>
public static class RoomGeneration
{
    public static string Scope(string generation, string room) => generation.Length == 0 ? room : generation + "_" + room;
    public static string? Of(string? room)
    {
        if (room is not { Length: > 33 and <= 64 } || room[32] != '_') return null;
        string value = room[..32];
        return value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? value : null;
    }
    public static bool Matches(string requested, RoomInfo room, ServerInfo server) => room.Id == requested ||
        requested == "default" && server.InstanceId is { Length: 32 } instance && room.Id == Scope(instance, "default");
}

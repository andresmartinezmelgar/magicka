using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Arcana.Core;

namespace Arcana.Server;

internal sealed class GameRoom(string id, string name, string password, WorldOptions options)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Password { get; } = password;
    public WorldOptions Options { get; } = options;
    public GameWorld World { get; } = NetworkWorld(options);
    private static GameWorld NetworkWorld(WorldOptions options) { var world = new GameWorld(options); world.SetStatisticsNetwork(); return world; }
    public string StatisticsRun = "";
    public long NextStatisticsTick;
    public bool StatisticsFinal;
    public Dictionary<int, PlayerSession> Sessions { get; } = [];
    public Dictionary<string, ReservedPlayer> Reservations { get; } = new(StringComparer.Ordinal);
    public long LastOccupied { get; set; } = Environment.TickCount64;
    public bool Started { get; set; }
    public string MatchId { get; set; } = Guid.NewGuid().ToString("N");
    public bool ResultQueued { get; set; }
    public bool SeriesFinished { get; set; }
    public bool AwaitingRematch { get; set; }
    public string RewardStatus { get; set; } = "";
    public int[] SeriesWins { get; } = new int[2];
    public int BestOf { get; set; } = 1;
    public int HostPlayerId { get; set; }
    public bool StartRequested { get; set; }
    public HashSet<string> DefenseRewards { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Task<bool>> PendingRewardWrites { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, MatchParticipant> Participants { get; } = new(StringComparer.Ordinal);
    public RoomInfo Info() => new(Id, Name, Options.Mode, World.Players.Count, Options.MaxPlayers,
        Options.Bots, Options.FriendlyFire, Options.ScoreLimit, Options.Tickets, Password.Length > 0, Options.Map, BestOf, Started, Options.DefenseFlow);
}

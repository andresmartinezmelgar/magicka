using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Arcana.Core;

namespace Arcana.Server;

internal sealed class PlayerSession(int id, GameRoom room, WebSocket socket)
{
    // Los IDs de magos son locales a la sala. La sesión de transporte es única en todo el servidor.
    public Guid Key { get; } = Guid.NewGuid();
    public int Id { get; } = id;
    public GameRoom Room { get; } = room;
    public WebSocket Socket { get; } = socket;
    public SnapshotSender Network { get; set; } = null!;
    public bool Resumed { get; init; }
    public bool RestoredDefense { get; init; }
    public long ReconnectUntil { get; init; }
    public string ResumeToken { get; init; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    public long LastPacketAt = Environment.TickCount64;
    public bool Ready { get; set; }
    public bool LobbyReady { get; set; }
    public AccountIdentity? Account { get; init; }
    public PreparedEquipment Equipment { get; set; } = new(EquipmentModifiers.None, PublicEquipment.Empty);
    public SemaphoreSlim SendGate { get; } = new(1, 1);
    public long LastStatisticsAt { get; set; }
    public long LastPingAt { get; set; }
    public long LastInspectionAt { get; set; }
    public int InspectionsInWindow { get; set; }
    public long LastActivityTick { get; set; }
    public long BestiaryKillsSaved { get; set; }
    public double NextBestiaryAt { get; set; }
    public Task<bool>? BestiaryWrite { get; set; }
    public long BestiaryWriteKills { get; set; }
    public string BestiaryWriteRun { get; set; } = "";
    public long LoadingStartedAt { get; } = Environment.TickCount64;
    public Channel<ClientMessage> Commands { get; } = Channel.CreateBounded<ClientMessage>(new BoundedChannelOptions(64)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    public Channel<byte[]> Outbox { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
}

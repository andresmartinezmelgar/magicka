using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private string? _lobbySmoke, _previewRooms;
    private int _lobbyPhase;
    private ArenaConnection? _lobbyPeer;
    private Task? _lobbyPeerConnecting;
    private long _lobbyWaitStarted;

    private void CheckLobbyFrame()
    {
        try
        {
            if (_lobbyPhase == 0)
            {
                var team = _runtime.Launcher.FindChild("RoomTeamChoice", true, false) as OptionButton;
                if (team == null || team.Disabled) return;
                team.Select(team.GetItemIndex(1));
                if (_previewRooms != null) return;
                var create = FindButton(_runtime.Launcher, "Crear y entrar");
                if (create == null || create.Disabled) return;
                _lobbyPhase = 1; create.EmitSignal(Button.SignalName.Pressed);
            }
            else if (_lobbyPhase == 1 && _runtime.Loading && _runtime.Connection is { } connection)
            {
                if (connection.Latest!.Players.Single(p => p.Id == connection.PlayerId).Team != 1)
                    throw new InvalidDataException("Crear y entrar no respetó Rojo en el formulario.");
                _lobbyPeer = new ArenaConnection();
                _lobbyPeerConnecting = _lobbyPeer.ConnectAsync(_lobbySmoke!, "Segundo móvil", 0, "",
                    roomId: connection.Room!.Id, deferReady: true);
                _lobbyPhase = 2;
            }
            else if (_lobbyPhase == 2 && _lobbyPeerConnecting is { IsCompleted: true })
            {
                _lobbyPeerConnecting.GetAwaiter().GetResult();
                if (_runtime.Connection!.PreparingPlayers < 1) return;
                if (_runtime.Playing || _lobbyPeer!.Ready || _lobbyPeer.Latest!.Tick != 0)
                    throw new InvalidDataException("La partida comenzó mientras el otro móvil cargaba.");
                if (_runtime.LoadingProgress < 98 || !_runtime.Connection.Ready) return;
                _lobbyWaitStarted = System.Environment.TickCount64; _lobbyPhase = 3;
            }
            else if (_lobbyPhase == 3 && System.Environment.TickCount64 - _lobbyWaitStarted >= 180)
            {
                if (!_runtime.Loading || _runtime.Playing || _runtime.Connection!.Latest!.Tick != 0)
                    throw new InvalidDataException("La sala no mantuvo la espera conjunta.");
                if (!_lobbyPeer!.CompleteLoading()) throw new IOException("El segundo móvil no pudo confirmar.");
                _lobbyPhase = 4;
            }
            else if (_lobbyPhase == 4 && _runtime.Playing)
            {
                if (_runtime.Loading || !_runtime.Connection!.Ready || _runtime.Connection.PreparingPlayers != 0 ||
                    !_lobbyPeer!.Ready || _runtime.Snapshot!.Players.Single(p => p.Id == _lobbyPeer.PlayerId).Team != 0)
                    throw new InvalidDataException("La partida empezó sin confirmar los dos clientes y sus equipos.");
                _lobbyPhase = 5; _ = _lobbyPeer.DisposeAsync();
                CompleteSmoke("LOBBY_SMOKE_OK: crear desde el formulario en Rojo, rival Azul y carga conjunta antes de jugar.");
            }
        }
        catch (Exception error)
        { if (_lobbyPeer != null) _ = _lobbyPeer.DisposeAsync(); GD.PushError("LOBBY_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); }
    }
    private static Button? FindButton(Node root, string text)
    {
        if (root is Button button && button.Text == text) return button;
        foreach (var child in root.GetChildren()) if (FindButton(child, text) is { } found) return found;
        return null;
    }
}

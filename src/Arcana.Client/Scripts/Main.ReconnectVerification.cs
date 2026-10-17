using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private async Task VerifyReconnectInterface(string[] args)
    {
        try
        {
            string server = args.First(a => a.StartsWith("--reconnect-server="))[19..];
            async Task Until(Func<bool> condition, double seconds, string failure)
            {
                ulong start = Time.GetTicksMsec();
                while (!condition())
                {
                    if (Time.GetTicksMsec() - start > seconds * 1000) throw new InvalidOperationException(failure);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Interfaz de recuperación", Mode = GameMode.Defense,
                Map = ArenaMapId.Bastion, MaxPlayers = 1 });
            string? error = null;
            async Task Enter()
            {
                error = null;
                await ConnectOnline(server, "Cliente", 0, "", room.Id, onError: message => error = message);
                await Until(() => error != null || _playing && _connection?.Ready == true && !Loading, 45, "La partida no terminó de cargar.");
                if (error != null) throw new InvalidOperationException(error);
            }
            await Enter();
            var first = _connection!; int id = first.PlayerId;
            first.ReconnectAfterStall();
            await Until(() => _playing && !Loading && _connection != first && _connection?.Ready == true, 35,
                "El cliente no recuperó la partida después del corte.");
            if (_localId != id || _reconnecting || _reconnectScreen != null) throw new InvalidOperationException("La recuperación cambió de personaje o dejó la ventana abierta.");
            GD.Print("RECONNECT_RECOVERY_OK: mismo personaje, controles y partida recuperados desde el cliente.");

            var cancel = RecoverConnection(_connection!, server, "Cliente", 0, "", room.Id, "", WizardType.Balanced, "Prueba de cancelación");
            ReturnToLauncher(); await cancel;
            if (_reconnecting || _connection != null || !_launcher.Visible) throw new InvalidOperationException("Cancelar no volvió al menú.");
            GD.Print("RECONNECT_CANCEL_OK: cancelación sin recuperar una conexión abandonada.");

            await Enter();
            // El puerto local cerrado reproduce un servidor inaccesible sin utilizar servicios externos.
            var timeout = RecoverConnection(_connection!, "127.0.0.1:1", "Cliente", 0, "", room.Id, "", WizardType.Balanced, "Prueba de plazo");
            await timeout;
            await Until(() => !_reconnecting && _launcher.Visible && _connection == null, 5, "Agotar el plazo no volvió al menú.");
            if (_engineLog.ErrorCount != 0) throw new InvalidOperationException("Godot registró errores durante la recuperación.");
            GD.Print("RECONNECT_SMOKE_OK: recuperación real, cancelación y plazo agotado sin errores.");
            GetTree().Quit();
        }
        catch (Exception exception)
        { GD.PrintErr("RECONNECT_SMOKE_FAILED: " + exception); GetTree().Quit(1); }
    }
}

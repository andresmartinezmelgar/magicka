using Arcana.Core;

namespace Arcana.Client;

public partial class Main
{
    private bool _reconnecting;
    private CancellationTokenSource? _reconnectLifetime;
    private LoadingScreen? _reconnectScreen;
    private void CancelReconnect()
    {
        _reconnectLifetime?.Cancel(); _reconnectLifetime = null;
        _reconnecting = false; _reconnectScreen?.QueueFree(); _reconnectScreen = null;
    }
    private async Task RecoverConnection(ArenaConnection lost, string address, string name, int team, string password,
        string roomId, string roomPassword, WizardType wizardType, string reason)
    {
        if (_connection != lost || _reconnecting) return;
        if (lost.ResumeToken.Length == 0 || _snapshot?.MatchEnded != false) { ReturnToLauncher(reason); return; }
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30)); _reconnectLifetime = lifetime;
        _reconnecting = true; _playing = false; _touch?.Reset();
        ClosePlayerEquipment(); _defensePanel?.CloseShop();
        _reconnectScreen = new LoadingScreen(); _ui.AddChild(_reconnectScreen);
        _reconnectScreen.Build("Restableciendo la conexión", () => ReturnToLauncher());
        _reconnectScreen.UpdateProgress(0, "Se conserva tu personaje. La partida continúa en el servidor.");
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await ConnectOnline(address, name, team, password, roomId, roomPassword, wizardType,
                    _ => completion.TrySetResult(false), lost.ResumeToken, () => completion.TrySetResult(true));
                if (await completion.Task.WaitAsync(lifetime.Token)) return;
                await Task.Delay(1000, lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_reconnectLifetime, lifetime))
            {
                if (!Post(() => { if (ReferenceEquals(_reconnectLifetime, lifetime)) ReturnToLauncher("No se pudo recuperar la partida dentro del plazo de reconexión."); lifetime.Dispose(); }))
                { _reconnectLifetime = null; lifetime.Dispose(); }
            }
            else lifetime.Dispose();
        }
    }
}

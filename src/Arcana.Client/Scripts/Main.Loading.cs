using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private LoadingScreen? _loadingScreen;
    private List<(string Name, Action Work)>? _loadingSteps;
    private int _loadingIndex;
    private long _drawnFrames, _loadingDrawMarker, _loadingStartedAt;
    private int _loadingCreatedFrame;
    private bool _loadingAwaitDraw, _loadingReadySent, _loadingFinalFrame, _loadingAudio;
    internal bool Loading => _loadingSteps != null;
    private void FrameDrawn() => Interlocked.Increment(ref _drawnFrames);

    private void StartGame(WorldSnapshot snapshot, bool resume = false)
    {
        CancelGameLoading();
        _playing = false; _paused = false; _moveTo = false; _snapshot = snapshot;
        _aimMarker.Hide();
        _launcher.Visible = false; _launcher.CloseRooms(); _help.Visible = _pause.Visible = false;
        _loadingScreen = new LoadingScreen(); _ui.AddChild(_loadingScreen);
        _loadingScreen.Build(ArenaMap.Get(snapshot.Map).Name, () => ReturnToLauncher());
        _loadingAudio = _renderer.AudioEnabled; _renderer.AudioEnabled = false;
        _loadingSteps = resume ? new() : _renderer.PrepareVisuals(snapshot, _localId).ToList();
        _loadingSteps.Add(("Preparando los controles…", () => PrepareGameControls(snapshot)));
        _loadingSteps.Add(("Preparando la vista de la partida…", () =>
            _renderer.Render(_connection?.Latest ?? _practice?.Snapshot() ?? snapshot, _localId, 1 / 60.0, true)));
        _loadingIndex = 0; _loadingReadySent = _loadingFinalFrame = false;
        _loadingStartedAt = System.Environment.TickCount64; _loadingCreatedFrame = _renderFrames;
        _loadingDrawMarker = Volatile.Read(ref _drawnFrames); _loadingAwaitDraw = true;
        Diagnostics.Info("game.loading", "Preparando la partida antes de habilitarla.", new Dictionary<string, object?>
            { ["map"] = snapshot.Map, ["steps"] = _loadingSteps.Count, ["online"] = _connection != null });
    }

    private void ProcessLoading()
    {
        try
        {
            if (_loadingSteps == null || _loadingScreen == null) return;
            if (_connection != null && !_connection.Connected)
            { ReturnToLauncher("La conexión se cerró mientras se preparaba la partida."); return; }
            if (System.Environment.TickCount64 - _loadingStartedAt > Protocol.LoadingTimeoutSeconds * 1000L)
            { ReturnToLauncher("No se pudo terminar la preparación de la partida a tiempo."); return; }
            // Primero dibuja la barra; cada trabajo posterior espera su fotograma real.
            if (_renderFrames <= _loadingCreatedFrame || _loadingAwaitDraw && DisplayServer.GetName() != "headless" &&
                Volatile.Read(ref _drawnFrames) <= _loadingDrawMarker) return;
            _loadingAwaitDraw = false;
            if (_loadingFinalFrame)
            {
                var snapshot = _connection?.Latest ?? _practice?.Snapshot() ?? _snapshot!;
                double elapsed = (System.Environment.TickCount64 - _loadingStartedAt) / 1000.0;
                CancelGameLoading(); ActivateGame(snapshot, true);
                Diagnostics.Info("game.loaded", "Partida preparada.", new Dictionary<string, object?> { ["seconds"] = elapsed });
                return;
            }
            if (_loadingIndex < _loadingSteps.Count)
            {
                var step = _loadingSteps[_loadingIndex];
                _loadingScreen.UpdateProgress(96.0 * _loadingIndex / _loadingSteps.Count, step.Name);
                step.Work(); _loadingIndex++;
                _ui.MoveChild(_loadingScreen, -1);
            }
            else
            {
                _loadingScreen.UpdateProgress(98, "Confirmando la preparación…");
                if (_connection != null)
                {
                    if (!_loadingReadySent)
                    { if (!_connection.CompleteLoading()) { ReturnToLauncher("No se pudo confirmar la carga al servidor."); return; } _loadingReadySent = true; }
                    if (!_connection.Ready || _connection.PreparingPlayers > 0)
                    { _loadingScreen.UpdateProgress(98, "Esperando a que los jugadores terminen de cargar…"); return; }
                }
                var snapshot = _connection?.Latest ?? _practice?.Snapshot() ?? _snapshot!;
                _renderer.Render(snapshot, _localId, 1 / 60.0, true); _hud?.Update(snapshot, _localId, _serverName, 0);
                _loadingScreen.UpdateProgress(100, "Partida lista"); _loadingFinalFrame = true;
            }
            _loadingDrawMarker = Volatile.Read(ref _drawnFrames); _loadingAwaitDraw = true;
        }
        catch (Exception error)
        {
            Diagnostics.Error("game.loading_failed", "Fallo al preparar los recursos de la partida.", error, Context());
            if (_verification?.FailFrame(error) == true) return;
            ReturnToLauncher("No se pudieron cargar los recursos de la partida. Consulta el diagnóstico.");
        }
    }
    private void CancelGameLoading()
    {
        if (_loadingSteps != null) _renderer.AudioEnabled = _loadingAudio;
        _loadingSteps = null; _loadingIndex = 0;
        _loadingScreen?.QueueFree(); _loadingScreen = null;
    }
    private void PrepareGameControls(WorldSnapshot snapshot)
    {
        _hud?.QueueFree(); _hud = new Hud(); _ui.AddChild(_hud); _hud.Build(); _hud.Update(snapshot, _localId, _serverName, 0);
        _hud.EquipmentRequested += OpenPlayerEquipment;
        _practicePanel?.QueueFree(); _practicePanel = null;
        if (_practiceSession != null)
        { _practicePanel = new PracticePanel(); _ui.AddChild(_practicePanel); _practicePanel.Build(_practiceSession); }
        _ui.MoveChild(_aimMarker, -1);
        BuildTouchControls(); _touch?.Update(snapshot.Players.FirstOrDefault(p => p.Id == _localId));
        BuildDefenseControls(snapshot);
        UpdateTouchLayout();
        _ui.MoveChild(_help, -1); _ui.MoveChild(_pause, -1);
    }
}

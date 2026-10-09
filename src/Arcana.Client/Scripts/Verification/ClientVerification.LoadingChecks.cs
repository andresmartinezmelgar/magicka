using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool _loadingSmoke, _previewLoading, _loadingCanceled, _loadingCapture;
    private double _loadingPrevious;
    private int _loadingFrames;
    internal void CheckLoadingFrame()
    {
        if (_lobbySmoke != null) { CheckLobbyFrame(); return; }
        if (!_loadingSmoke && !_previewLoading) return;
        try
        {
            _loadingFrames++;
            if (_previewLoading && !_loadingCapture && _runtime.LoadingProgress >= 35 && _capturePath != null)
            { _loadingCapture = true; _ = Capture(_capturePath); }
            if (!_loadingSmoke) return;
            if (_runtime.Loading)
            {
                if (_runtime.Playing || _runtime.Practice!.Tick != 0 || _runtime.LoadingProgress < _loadingPrevious)
                    throw new InvalidDataException("La partida avanza durante la carga o el progreso retrocede.");
                _loadingPrevious = _runtime.LoadingProgress;
                var player = _runtime.Practice.Players[_runtime.LocalId];
                _runtime.Submit(new() { Type = "element", Element = Element.Fire });
                if (player.Selection.Elements.Count != 0) throw new InvalidDataException("Se admitió una acción durante la carga.");
                if (!_loadingCanceled && _loadingPrevious >= 20)
                {
                    if (Ui.IsMobile) _runtime.Notify((int)NotificationWMGoBackRequest); else _runtime.ReturnToLauncher();
                    if (_runtime.Loading || _runtime.Playing || _runtime.Practice != null)
                        throw new InvalidDataException("Cancelar dejó una partida o una carga activas.");
                    _loadingCanceled = true; _loadingPrevious = 0;
                    _runtime.StartPractice("Carga completa", 1, GameMode.Warfare, 2, ArenaMapId.Frontline);
                }
            }
            else
            {
                var choice = Ui.TeamChoice();
                try
                {
                    if (Ui.SelectedTeam(choice) != -1) throw new InvalidDataException("Automático no envía -1.");
                    choice.Select(choice.GetItemIndex(0)); if (Ui.SelectedTeam(choice) != 0) throw new InvalidDataException("No se puede elegir Azul.");
                    choice.Select(choice.GetItemIndex(1)); if (Ui.SelectedTeam(choice) != 1) throw new InvalidDataException("No se puede elegir Rojo.");
                }
                finally { choice.Free(); }
                if (!_loadingCanceled || !_runtime.Playing || _runtime.Practice!.Tick != 0 ||
                    _runtime.Practice.Players[_runtime.LocalId].Team != 1 ||
                    _runtime.Renderer.CurrentMap != ArenaMapId.Frontline || _runtime.Renderer.Effects.PooledCount != CombatEffects.ParticleLimit + CombatEffects.RingLimit)
                    throw new InvalidDataException("La carga no terminó con el mapa, equipo y efectos preparados.");
                CompleteSmoke($"LOADING_SMOKE_OK: progreso, fotogramas, cancelación, equipo rojo y partida detenida hasta terminar; {_loadingFrames} fotogramas.");
            }
        }
        catch (Exception error) { GD.PushError("LOADING_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); }
    }
}

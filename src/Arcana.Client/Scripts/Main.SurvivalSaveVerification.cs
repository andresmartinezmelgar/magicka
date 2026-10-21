using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _survivalSaveSmoke;
    private int _survivalSaveSmokePhase, _survivalSaveSmokeFrames;
    private string? _survivalSaveCaptures;
    private string _survivalSaveSmokePath = "", _survivalSaveSmokeRun = "";
    private long _survivalSaveSmokeTick;
    private void StartSurvivalSaveSmoke(string[] arguments)
    {
        _survivalSaveSmoke = true;
        _survivalSaveCaptures = arguments.FirstOrDefault(a => a.StartsWith("--survival-save-captures="))?["--survival-save-captures=".Length..];
        string? requested = arguments.FirstOrDefault(a => a.StartsWith("--smoke-viewport="));
        if (requested != null)
        {
            var parts = requested["--smoke-viewport=".Length..].Split('x');
            var size = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
            GetWindow().ContentScaleSize = size; GetWindow().Size = size;
        }
        _survivalSaveSmokePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arcana-save-ui-" + Guid.NewGuid().ToString("N"));
        _survivalSaves = new(System.IO.Path.Combine(_survivalSaveSmokePath, "survival.json"));
        Accounts.RestoreOfflineProfile(new("survival-save-ui", "Defensor", 0, 0, 0, new(), []));
        _launcher.UpdateSurvivalSave();
        StartPractice("Defensor", 0, GameMode.Defense, 1, map: ArenaMapId.Bastion);
    }
    private bool CheckSurvivalSaveSmoke()
    {
        if (!_survivalSaveSmoke) return false;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        try
        {
            if (Loading || !_playing) return false;
            if (_survivalSaveSmokePhase == 0)
            {
                var player = _practice!.Players[_localId]; player.Health = 712; player.Deaths = 1; player.Invulnerable = 8;
                player.BuffStacks[(int)BuffKind.Energy] = 2;
                _snapshot = _practice.Snapshot(); TogglePause();
                _survivalSaveSmokePhase = 1; _survivalSaveSmokeFrames = 0; return false;
            }
            if (_survivalSaveSmokePhase == 1)
            {
                if (++_survivalSaveSmokeFrames < 3) return false;
                Check(_survivalSaveActions!.IsVisibleInTree(), "No aparece el guardado en supervivencia local.");
                Check(GetViewport().GetVisibleRect().Encloses(_responsivePauseModal!.GetGlobalRect()) &&
                    GetViewport().GetVisibleRect().Encloses(_survivalSaveActions.GetGlobalRect()) && _survivalSaveActions.Size.X > 230,
                    "El menú o los botones de guardado desbordan la pantalla.");
                if (_survivalSaveCaptures != null)
                {
                    System.IO.Directory.CreateDirectory(_survivalSaveCaptures);
                    GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_survivalSaveCaptures, "survival-save.png"));
                }
                _survivalSaveSmokeRun = _localBestiaryRun; _survivalSaveSmokeTick = _practice!.Tick;
                Check(SaveLocalSurvival(false) && HasSurvivalSave && _survivalSaveStatus!.Text.Contains("Partida guardada"), "No se confirmó el guardado.");
                Check(SaveLocalSurvival(true) && !_playing && _launcher.Visible, "Guardar y salir no volvió al menú.");
                _launcher.NavigateOptionA(2);
                Check(_launcher.FindChild("ContinueSurvival", true, false) is Button { Visible: true, Disabled: false },
                    "El menú no ofrece continuar la partida guardada.");
                ContinueSurvival();
                Check(Loading && HasSurvivalSave, "Preparar los recursos consume el archivo antes de activar el juego.");
                ReturnToLauncher();
                Check(HasSurvivalSave, "Cancelar la carga consumió la partida.");
                ContinueSurvival();
                Check(_practice!.Tick == _survivalSaveSmokeTick && _practice.Players[_localId].Health == 712 &&
                    _practice.Players.Count == 2 && _practice.Players[_localId].Deaths == 1, "Continuar creó una partida nueva.");
                _survivalSaveSmokePhase = 2; return false;
            }
            Check(_localBestiaryRun == _survivalSaveSmokeRun && _localBestiaryUid == "survival-save-ui", "Cambió la identidad del bestiario al continuar.");
            Check(_practice!.Players[_localId].BuffStacks[(int)BuffKind.Energy] == 2 && _practice.Tick >= _survivalSaveSmokeTick,
                "Se perdió el progreso al terminar de cargar.");
            Check(!HasSurvivalSave, "La partida ya continuada sigue disponible para rebobinar.");
            if (_survivalSaveSmokePhase == 2)
            {
                TogglePause(); _survivalSaveSmokeTick = _practice.Tick;
                Check(SaveLocalSurvival(true) && HasSurvivalSave, "Guardar otra vez no habilita el punto nuevo.");
                ContinueSurvival(); Check(Loading && _practice!.Tick == _survivalSaveSmokeTick, "No preparó el nuevo punto guardado.");
                _survivalSaveSmokePhase = 3; return false;
            }
            ReturnToLauncher();
            ContinueSurvival(); Check(!Loading && !_playing, "El mismo archivo se pudo continuar por segunda vez.");
            GD.Print("SURVIVAL_SAVE_SMOKE_OK: estado, aliados e identidad; cancelar conserva el punto, activarlo lo consume, guardar de nuevo habilita otro punto y se bloquea una segunda carga.");
            GetTree().Quit(0); _survivalSaveSmoke = false;
        }
        catch (Exception error)
        { GD.PushError("SURVIVAL_SAVE_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); _survivalSaveSmoke = false; }
        finally
        {
            if (!_survivalSaveSmoke && System.IO.Directory.Exists(_survivalSaveSmokePath))
                System.IO.Directory.Delete(_survivalSaveSmokePath, true);
        }
        return false;
    }
}

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
    private SurvivalMapSpec? _survivalSaveSmokeMap;
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
        var world = _practice; long loadingStart = _loadingStartedAt;
        HandleApplicationLifecycle(NotificationApplicationFocusOut);
        HandleApplicationLifecycle(NotificationApplicationPaused);
        _applicationSuspendedAt -= 130_000;
        HandleApplicationLifecycle(NotificationApplicationResumed);
        if (!_applicationSuspended) throw new InvalidOperationException("Reanudó antes de recuperar el foco.");
        HandleApplicationLifecycle(NotificationApplicationFocusIn);
        if (!Loading || !ReferenceEquals(world, _practice) || _applicationSuspended || _loadingStartedAt < loadingStart + 130_000)
            throw new InvalidOperationException("Android canceló la carga o contó el tiempo en segundo plano.");
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
                Check(HasSurvivalSave, "La partida nueva no crea un autoguardado.");
                _survivalSaveSmokeMap = _practice!.Map.SurvivalMap;
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
            Check(HasSurvivalSave && _practice.Map.SurvivalMap == _survivalSaveSmokeMap,
                "Continuar consumió el autoguardado o cambió el terreno.");
            if (_survivalSaveSmokePhase == 2)
            {
                var running = _practice; long tick = running.Tick;
                running.Players[_localId].Health = 611;
                HandleApplicationLifecycle(NotificationApplicationFocusOut);
                HandleApplicationLifecycle(NotificationApplicationPaused);
                _PhysicsProcess(1 / 60.0);
                Check(ReferenceEquals(running, _practice) && running.Tick == tick && HasSurvivalSave &&
                    SurvivalSaves.Load().World.Players[_localId].Health == 611,
                    "Cambiar de aplicación pierde la partida, simula el fondo o no guarda el último estado.");
                HandleApplicationLifecycle(NotificationApplicationFocusIn);
                Check(_applicationSuspended, "Recuperar el foco ignoró que Android seguía en pausa.");
                HandleApplicationLifecycle(NotificationApplicationResumed);
                Check(_playing && _paused && !_applicationSuspended && ReferenceEquals(running, _practice),
                    "Volver de otra app empezó una partida nueva o perdió la pausa.");
                _survivalSaveSmokeTick = _practice.Tick;
                Check(SaveLocalSurvival(true) && HasSurvivalSave, "Guardar otra vez no habilita el punto nuevo.");
                ContinueSurvival(); Check(Loading && _practice!.Tick == _survivalSaveSmokeTick, "No preparó el nuevo punto guardado.");
                _survivalSaveSmokePhase = 3; return false;
            }
            var human = _practice.Players[_localId];
            human.Health = 1; human.Deaths = DefenseRules.PlayerLives - 1; human.Invulnerable = 0; human.Shield = 0;
            // Usa la muerte real para comprobar también la transición de fin de partida.
            typeof(GameWorld).GetMethod("Damage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(_practice, new object?[] { human, 1000000f, human.Id, null });
            AutosaveSurvival("finished");
            Check(_practice.MatchEnded && !HasSurvivalSave, "La partida terminada conserva un autoguardado recuperable.");
            ReturnToLauncher();
            ContinueSurvival(); Check(!Loading && !_playing, "Una partida terminada reaparece desde el respaldo.");
            _survivalSaveSmoke = false;
            var tree = GetTree(); var errors = _engineLog;
            QueueFree(); _ = FinishSurvivalSaveSmokeAfterCleanup(tree, errors);
        }
        catch (Exception error)
        { GD.PushError("SURVIVAL_SAVE_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); _survivalSaveSmoke = false; }
        finally
        {
            if (!_survivalSaveSmoke && !_playing && System.IO.Directory.Exists(_survivalSaveSmokePath))
                System.IO.Directory.Delete(_survivalSaveSmokePath, true);
        }
        return false;
    }

    private static async Task FinishSurvivalSaveSmokeAfterCleanup(SceneTree tree, EngineLogBridge errors)
    {
        try
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            GC.Collect(); GC.WaitForPendingFinalizers();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (errors.ErrorCount > 0) throw new InvalidOperationException("Godot registró errores al liberar la prueba de guardado.");
            GD.Print("SURVIVAL_SAVE_SMOKE_OK: autoguardado, estado, aliados, identidad y mapa; Android conserva carga y partida al perder foco; finalizar invalida el punto y su respaldo; limpieza final verificada.");
            tree.Quit();
        }
        catch (Exception error) { GD.PushError("SURVIVAL_SAVE_SMOKE_FAILED: limpieza: " + error); tree.Quit(1); }
    }
}

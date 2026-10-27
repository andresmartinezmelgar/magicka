using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private SurvivalSaveStore? _survivalSaves;
    private SurvivalSaveStore SurvivalSaves => _survivalSaves ??= new(ProjectSettings.GlobalizePath("user://survival-save.json"));
    private SurvivalSave? _resumingSurvival;
    private Control? _survivalSaveActions;
    private ElementTextLabel? _survivalSaveStatus;
    private PanelContainer? _responsivePauseModal;
    private ScrollContainer? _responsivePauseScroll;
    private const double SurvivalAutosaveSeconds = 20;
    private double _survivalAutosaveClock;
    private bool _survivalAutosaveEnded;
    private string _autosavedSurvivalRun = "";
    private long _autosavedSurvivalTick = -1;
    internal bool HasSurvivalSave => SurvivalSaves.Exists;
    private bool CanSaveSurvival => _playing && _connection == null && _practice is { IsDefense: true, MatchEnded: false } &&
        _practice.Players.TryGetValue(_localId, out var player) && !player.Bot && player.Deaths < DefenseRules.PlayerLives;

    private void AddSurvivalSaveActions(VBoxContainer box)
    {
        var actions = new GridContainer { Name = "SurvivalSaveActions", Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        actions.AddThemeConstantOverride("h_separation", 10); actions.AddThemeConstantOverride("v_separation", 8);
        _survivalSaveActions = actions; actions.Visible = false; box.AddChild(actions);
        var save = Ui.Button("Guardar partida", () => SaveLocalSurvival(false)); save.Name = "SaveSurvival";
        var leave = Ui.Button("Guardar y salir", () => SaveLocalSurvival(true)); leave.Name = "SaveAndLeaveSurvival";
        save.SizeFlagsHorizontal = leave.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actions.AddChild(save); actions.AddChild(leave);
        _survivalSaveStatus = Ui.Label("", 15, Ui.Muted);
        _survivalSaveStatus.Name = "SurvivalSaveStatus"; _survivalSaveStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_survivalSaveStatus); _survivalSaveStatus.Visible = false;
    }

    private void UpdateSurvivalSaveActions()
    {
        if (_survivalSaveActions == null || _survivalSaveStatus == null) return;
        _survivalSaveActions.Visible = CanSaveSurvival; _survivalSaveStatus.Visible = CanSaveSurvival;
        _survivalSaveStatus.Text = "Autoguardado local cada 20 s y al cambiar de aplicación. Continuar recupera el último punto disponible.";
        UpdatePauseResponsive();
    }

    private void UpdatePauseResponsive()
    {
        var size = GetViewport().GetVisibleRect().Size;
        float width = Math.Max(250, Math.Min(750, size.X - 48));
        if (_responsivePauseModal != null) _responsivePauseModal.CustomMinimumSize = new(width, 0);
        if (_responsivePauseScroll != null)
            _responsivePauseScroll.CustomMinimumSize = new(width - 32, Math.Max(100, size.Y - 152));
        if (_survivalSaveActions is GridContainer actions)
            actions.Columns = ResponsiveLayout.Columns(width - 32, 260, 2, 10);
    }

    private bool SaveLocalSurvival(bool leave)
    {
        if (!CanSaveSurvival) return false;
        try
        {
            // Al pausar ya se han soltado los controles; ningún hechizo humano queda atascado al continuar.
            WriteSurvivalCheckpoint(); CaptureLocalBestiary();
            if (leave)
            {
                ReturnToLauncher(); _launcher.ShowStatus("Supervivencia guardada en este dispositivo.");
            }
            else if (_survivalSaveStatus != null) _survivalSaveStatus.Text = "Partida guardada en este dispositivo. Puedes continuar o salir.";
            Diagnostics.Info("survival.saved", "Supervivencia guardada en local.");
            return true;
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            if (_survivalSaveStatus != null) _survivalSaveStatus.Text = "No se pudo guardar la partida. Sigue abierta; vuelve a intentarlo.";
            Diagnostics.Warn("survival.save_failed", "No se pudo guardar la supervivencia local.", error);
            return false;
        }
    }

    private void WriteSurvivalCheckpoint()
    {
        var save = _practice!.SaveSurvival(_localId, _localBestiaryRun, _localBestiaryUid);
        SurvivalSaves.Write(save);
        _autosavedSurvivalRun = _localBestiaryRun; _autosavedSurvivalTick = _practice.Tick;
        _survivalAutosaveClock = 0;
        _survivalAutosaveEnded = false;
    }

    private void AutosaveSurvival(string reason)
    {
        try
        {
            if (_connection == null && _practice is { IsDefense: true, MatchEnded: true } &&
                _autosavedSurvivalRun.Length > 0 && _autosavedSurvivalRun == _localBestiaryRun)
            {
                SurvivalSaves.Discard(); _autosavedSurvivalRun = ""; _autosavedSurvivalTick = -1;
                return;
            }
            if (!CanSaveSurvival || !Guid.TryParseExact(_localBestiaryRun, "N", out _)) return;
            WriteSurvivalCheckpoint();
            Diagnostics.Info("survival.autosaved", "Supervivencia autoguardada.",
                new Dictionary<string, object?> { ["reason"] = reason, ["tick"] = _autosavedSurvivalTick });
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            // Un fallo de almacenamiento no expulsa de la partida ni sustituye el respaldo válido.
            Diagnostics.Warn("survival.autosave_failed", "No se pudo autoguardar la supervivencia.", error);
            if (GodotObject.IsInstanceValid(_survivalSaveStatus))
                _survivalSaveStatus!.Text = "No se pudo autoguardar. Comprueba el espacio libre y usa Guardar partida.";
        }
    }

    private void TickSurvivalAutosave(double delta)
    {
        if (_connection != null || !_playing || _practice?.IsDefense != true) return;
        _survivalAutosaveClock += Math.Clamp(delta, 0, 1);
        if (_practice.MatchEnded)
        {
            if (!_survivalAutosaveEnded || _survivalAutosaveClock >= SurvivalAutosaveSeconds)
            {
                _survivalAutosaveEnded = true; _survivalAutosaveClock = 0;
                AutosaveSurvival("finished");
            }
            return;
        }
        if (_survivalAutosaveClock < SurvivalAutosaveSeconds) return;
        _survivalAutosaveClock = 0;
        // El equipo también puede cambiar en pausa, sin que avance el tick de simulación.
        AutosaveSurvival("interval");
    }

    public void ContinueSurvival()
    {
        if (_playing || Loading || !HasSurvivalSave) return;
        try
        {
            // Leer y validar todo antes de sustituir la vista del launcher.
            var restored = SurvivalSaves.Load();
            CancelPendingConnection();
            var old = _connection; _connection = null; if (old != null) _ = old.DisposeAsync();
            _practiceSession = null; _practice = restored.World; _localId = restored.Save.LocalPlayerId; _localSequence = 0;
            _resumingSurvival = restored.Save;
            _serverName = restored.RecoveredBackup ? "Supervivencia · copia recuperada" : "Supervivencia · partida guardada";
            StartGame(_practice.Snapshot());
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            _launcher.ShowError("No se pudo abrir la partida guardada. El archivo falta, está dañado o pertenece a una versión incompatible.");
            Diagnostics.Warn("survival.load_failed", "No se pudo recuperar la supervivencia local.", error);
        }
    }
}

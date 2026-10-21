using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private SurvivalSaveStore? _survivalSaves;
    private SurvivalSaveStore SurvivalSaves => _survivalSaves ??= new(ProjectSettings.GlobalizePath("user://survival-save.json"));
    private SurvivalSave? _resumingSurvival;
    private Control? _survivalSaveActions;
    private Label? _survivalSaveStatus;
    private PanelContainer? _responsivePauseModal;
    private ScrollContainer? _responsivePauseScroll;
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
        _survivalSaveStatus.Text = "Un punto de continuación local. Cargarlo lo consume; debes guardar de nuevo para volver a continuar.";
        UpdatePauseResponsive();
    }

    private void UpdatePauseResponsive()
    {
        var size = GetViewport().GetVisibleRect().Size;
        float width = Math.Max(250, Math.Min(750, size.X - 48));
        if (_responsivePauseModal != null) _responsivePauseModal.CustomMinimumSize = new(width, 0);
        if (_responsivePauseScroll != null)
            _responsivePauseScroll.CustomMinimumSize = new(width - 32, Math.Max(180, size.Y - 96));
        if (_survivalSaveActions is GridContainer actions)
            actions.Columns = ResponsiveLayout.Columns(width - 32, 260, 2, 10);
    }

    private bool SaveLocalSurvival(bool leave)
    {
        if (!CanSaveSurvival) return false;
        try
        {
            // Al pausar ya se han soltado los controles; ningún hechizo humano queda atascado al continuar.
            var save = _practice!.SaveSurvival(_localId, _localBestiaryRun, _localBestiaryUid);
            SurvivalSaves.Write(save); CaptureLocalBestiary();
            CaptureStatistics(MatchRecordStatus.Suspended, true);
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
            _practice.ReconcileMatchStatistics(_statisticsArchive?.Find(_practice.MatchStatisticsRunId));
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

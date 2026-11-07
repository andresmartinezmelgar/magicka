using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private sealed record TutorialStep(string Title, string Text, Func<WorldSnapshot, bool>? Goal = null,
        Func<V2?>? Marker = null, Action? Prepare = null);

    private const string TutorialProgressFile = "tutorial-progress";
    private TutorialChapter _tutorialProgress;
    private GameWorld.TutorialScenario? _tutorial;
    private TutorialGuide? _tutorialGuide;
    private HBoxContainer? _tutorialGuideHost;
    private PanelContainer? _tutorialMarker;
    private EquipmentPanel? _tutorialInventory;
    private TutorialStep[] _tutorialSteps = [];
    private int _tutorialStepIndex;
    private bool _tutorialStepReady, _tutorialRetry, _tutorialSaveFailed;
    internal bool TutorialCompleted => _tutorialProgress == TutorialChapter.Completed;
    private bool TutorialActive => _tutorial != null;
    private bool TutorialHoldsCombat => TutorialActive &&
        (_tutorialStepReady || _tutorialRetry || _tutorial!.Chapter == TutorialChapter.Inventory);

    private void InitializeTutorial()
    {
        int stored = DevicePreferences.Read<int>(TutorialProgressFile);
        _tutorialProgress = Enum.IsDefined((TutorialChapter)stored) ? (TutorialChapter)stored : TutorialChapter.Basics;
    }

    internal bool CanStartRegularMatch() => !TutorialActive;

    internal void OpenTutorial()
    {
        if (TutorialActive || _playing || Loading || _reconnecting) return;
        if (GodotObject.IsInstanceValid(_equipmentPanel) && !_equipmentPanel!.IsQueuedForDeletion())
        { _equipmentPanel.Close(OpenTutorial); return; }
        CloseAccountProfile();
        CloseMenuHelp();
        CloseBestiary();
        CloseMatchStatistics();
        CloseItemShop();
        CancelPendingConnection();
        StartTutorialChapter(TutorialCompleted ? TutorialChapter.Basics : _tutorialProgress);
    }

    private void StartTutorialChapter(TutorialChapter chapter)
    {
        ClearTutorialControls();
        _defensePanel?.QueueFree(); _defensePanel = null;
        _practiceSession = null; _resumingSurvival = null;
        var scenario = new GameWorld.TutorialScenario(chapter, _launcher.PlayerName);
        _tutorial = scenario; _practice = scenario.World;
        _localId = scenario.Player.Id; _localSequence = 0; _serverName = "Tutorial";
        _tutorialSteps = BuildTutorialSteps(scenario);

        _tutorialGuideHost = new HBoxContainer { Name = "TutorialGuideHost", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _ui.AddChild(_tutorialGuideHost);
        _tutorialGuideHost.AnchorLeft = _tutorialGuideHost.AnchorRight = 0.5f;
        _tutorialGuideHost.OffsetTop = Ui.IsMobile ? 6 : 12;
        _tutorialGuide = new TutorialGuide(); _tutorialGuideHost.AddChild(_tutorialGuide);
        _tutorialGuide.Build(AdvanceTutorial, RepeatTutorialChapter);
        ResizeTutorialGuide();
        GetViewport().SizeChanged += ResizeTutorialGuide;

        _tutorialMarker = new PanelContainer { Name = "TutorialObjective", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _tutorialMarker.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("12283d"), 5, Ui.Gold));
        var marker = Ui.Label("OBJETIVO", 13, Ui.Gold);
        marker.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tutorialMarker.AddChild(marker); _ui.AddChild(_tutorialMarker);

        BeginTutorialStep(0);
        _renderer.Clear();
        StartGame(_snapshot!);
    }

    private void ResizeTutorialGuide()
    {
        if (_tutorialGuideHost == null) return;
        float width = GetViewport().GetVisibleRect().Size.X;
        float panelWidth = Math.Min(Math.Max(0, width - 20), Math.Clamp(width * (Ui.IsMobile ? 0.64f : 0.48f), 290, 620));
        _tutorialGuideHost.OffsetLeft = -panelWidth / 2;
        _tutorialGuideHost.OffsetRight = panelWidth / 2;
    }

    private void ClearTutorialControls()
    {
        if (_tutorialGuideHost != null) GetViewport().SizeChanged -= ResizeTutorialGuide;
        _tutorialGuideHost?.QueueFree(); _tutorialGuideHost = null; _tutorialGuide = null;
        _tutorialMarker?.QueueFree(); _tutorialMarker = null;
        _tutorialInventory?.QueueFree(); _tutorialInventory = null;
    }

    private void EndTutorial()
    {
        ClearTutorialControls();
        _tutorial = null;
        _tutorialSteps = [];
    }

    private void BeginTutorialStep(int index)
    {
        StopInput();
        _ignoreLeftUntilRelease = true;
        _tutorialStepIndex = index;
        var step = _tutorialSteps[index];
        _tutorialStepReady = step.Goal == null;
        _tutorialRetry = _tutorialSaveFailed = false;
        step.Prepare?.Invoke();
        _snapshot = _practice!.Snapshot();
        RefreshTutorialGuide();
    }

    private void RefreshTutorialGuide()
    {
        if (_tutorial == null || _tutorialGuide == null) return;
        var step = _tutorialSteps[_tutorialStepIndex];
        bool reopenInventory = _tutorial.Chapter == TutorialChapter.Inventory && _tutorialStepIndex > 0 && _tutorialInventory == null;
        bool lastStep = _tutorialStepIndex == _tutorialSteps.Length - 1;
        string next = _tutorialRetry ? "Reintentar" : reopenInventory ? "Abrir inventario" :
            _tutorialSaveFailed ? "Guardar de nuevo" :
            _tutorial.Chapter == TutorialChapter.Inventory && _tutorialStepIndex == 0 ? "Abrir inventario" :
            lastStep && _tutorial.Chapter == TutorialChapter.Inventory ? "Terminar tutorial" : "Continuar";
        string body = _tutorialRetry ? "Repite la práctica para completar el objetivo del capítulo." :
            _tutorialSaveFailed ? "No se pudo guardar el tutorial en este dispositivo. Vuelve a intentar el guardado." : step.Text;
        _tutorialGuide.ShowStep($"Tutorial {(int)_tutorial.Chapter + 1}/6 · {_tutorialStepIndex + 1}/{_tutorialSteps.Length} · {step.Title}",
            body, next, _tutorialStepReady || _tutorialRetry || reopenInventory,
            retry: !_tutorialRetry && !_tutorialStepReady && _tutorialStepIndex > 0);
    }

    private void RepeatTutorialChapter()
    {
        if (_tutorial != null && _playing && !Loading) StartTutorialChapter(_tutorial.Chapter);
    }

    private void AdvanceTutorial()
    {
        if (_tutorial == null || !_playing || Loading) return;
        if (_tutorialRetry) { RepeatTutorialChapter(); return; }
        if (_tutorial.Chapter == TutorialChapter.Inventory && _tutorialStepIndex > 0 && _tutorialInventory == null)
        { OpenTutorialInventory(); return; }
        if (!_tutorialStepReady) return;
        if (_tutorialStepIndex + 1 < _tutorialSteps.Length)
        {
            BeginTutorialStep(_tutorialStepIndex + 1);
            if (_tutorial.Chapter == TutorialChapter.Inventory && _tutorialStepIndex == 1) OpenTutorialInventory();
            return;
        }
        var next = (TutorialChapter)((int)_tutorial.Chapter + 1);
        if (!TutorialCompleted)
        {
            bool stored = DevicePreferences.Write(TutorialProgressFile, (int)next);
            if (!stored && next == TutorialChapter.Completed)
            { _tutorialSaveFailed = true; RefreshTutorialGuide(); return; }
            _tutorialProgress = next;
        }
        if (next == TutorialChapter.Completed)
        {
            ReturnToLauncher();
            _launcher.UpdateTutorialLink();
        }
        else StartTutorialChapter(next);
    }

    private void OpenTutorialInventory()
    {
        if (_tutorialInventory != null || _snapshot is not { Defense: { } defense } snapshot || _tutorialGuide == null) return;
        var player = defense.Defenders.Single(d => d.PlayerId == _localId);
        var wizard = snapshot.Players.Single(w => w.Id == _localId);
        var inventory = new EquipmentPanel();
        _ui.AddChild(inventory); _tutorialInventory = inventory;
        inventory.BuildDefense(this, player, wizard, false, Submit);
        inventory.MountTutorialGuide(_tutorialGuide);
        _tutorialGuideHost!.Hide();
        inventory.Closed += () =>
        {
            if (_tutorialInventory != inventory) return;
            _tutorialInventory = null;
            _tutorialGuide!.Reparent(_tutorialGuideHost!, false);
            RefreshTutorialGuide();
        };
        RefreshTutorialGuide();
    }

    private void UpdateTutorial(WorldSnapshot snapshot)
    {
        if (_tutorial == null || _tutorialGuide == null || _tutorialGuideHost == null || _tutorialMarker == null) return;
        bool visible = !_paused && !_help.Visible && !ControlsOpen && !InspectingEquipment &&
            !ViewingDamageSummary && _defensePanel?.ShopOpen != true;
        _tutorialGuide.Visible = visible;
        _tutorialGuideHost.Visible = visible && _tutorialInventory == null;
        _tutorialMarker.Hide();
        if (!visible) return;
        if (_tutorialGuideHost.Visible && _tutorialGuideHost.GetIndex() != _ui.GetChildCount() - 1)
            _ui.MoveChild(_tutorialGuideHost, -1);
        if (_tutorialInventory != null && snapshot.Defense is { } defense)
            _tutorialInventory.UpdateDefenseInventory(defense.Defenders.Single(d => d.PlayerId == _localId),
                snapshot.Players.Single(w => w.Id == _localId), snapshot.MatchEnded);

        var step = _tutorialSteps[_tutorialStepIndex];
        if (!_tutorialStepReady && !_tutorialRetry)
        {
            if (step.Goal?.Invoke(snapshot) == true)
            {
                StopInput();
                _tutorialStepReady = true;
                RefreshTutorialGuide();
            }
            else if (snapshot.MatchEnded)
            {
                _tutorialRetry = true;
                RefreshTutorialGuide();
            }
            else if (_tutorial.Chapter == TutorialChapter.Basics && (!_tutorial.Player.Alive || _tutorial.Target?.Alive == false))
            {
                if (step.Prepare != null) step.Prepare();
                else _tutorial.ResetExercise();
                _snapshot = _practice!.Snapshot();
            }
        }
        if (_tutorialStepReady || _tutorialRetry || step.Marker?.Invoke() is not { } at) return;
        var screen = _renderer.Camera.UnprojectPosition(ArenaRenderer.World(at, 1.8f));
        var size = GetViewport().GetVisibleRect().Size;
        _tutorialMarker.Position = new(Math.Clamp(screen.X - _tutorialMarker.Size.X / 2, 4, Math.Max(4, size.X - _tutorialMarker.Size.X - 4)),
            Math.Clamp(screen.Y - _tutorialMarker.Size.Y, 4, Math.Max(4, size.Y - _tutorialMarker.Size.Y - 4)));
        _tutorialMarker.Show();
    }

    private bool TutorialBlocksPointer(Vector2 point) => _tutorialGuide?.Blocks(point) == true;
}

using System.Collections.Concurrent;
using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    public void ToggleHelp()
    {
        if (!_playing && !_help.Visible) { ToggleMenuHelp(); return; }
        _help.Visible = !_help.Visible;
        if (_help.Visible) _ui.MoveChild(_help, -1);
        if (_help.Visible) _help.SetMatch(_snapshot);
        if (_playing && _help.Visible) StopInput();
        SynchronizeDefenseInterface();
        UpdateTouchVisibility();
    }

    private void TogglePause()
    {
        _paused = !_paused; _pause.Visible = _paused;
        if (_paused) _ui.MoveChild(_pause, -1);
        if (_paused) UpdateSpellShortcutMenu();
        if (_relicMenuButton != null) _relicMenuButton.Visible = HasRelics;
        UpdateSurvivalSaveActions();
        if (_paused) StopInput();
        SynchronizeDefenseInterface();
        UpdateTouchVisibility();
    }

    private Control BuildPauseModal(out VBoxContainer content)
    {
        var root = new Control { Name = "PauseMenu", Theme = Ui.Theme() }; Ui.Full(root);
        var shade = new ColorRect { Color = Ui.ModalDim }; root.AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); root.AddChild(center); Ui.Full(center);
        var panel = new PanelContainer(); _responsivePauseModal = panel;
        panel.AddThemeStyleboxOverride("panel", Ui.ModalPanel()); center.AddChild(panel);
        var frame = new VBoxContainer(); frame.AddThemeConstantOverride("separation", 8); panel.AddChild(frame);
        var header = new HBoxContainer(); frame.AddChild(header);
        var title = Ui.Label("PARTIDA", 22, Ui.Gold); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title); header.AddChild(Ui.ExitButton("Continuar", TogglePause, "PauseResume"));
        var scroll = new ContentScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _responsivePauseScroll = scroll; frame.AddChild(scroll);
        content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 16); scroll.AddChild(content);
        return root;
    }

    private Control BuildPause()
    {
        var root = BuildPauseModal(out var box);
        _defenseInterfaceHint = Ui.Label("En red, la partida continúa mientras estás en este menú.", 17, Ui.Muted);
        _defenseInterfaceHint.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_defenseInterfaceHint);
        AddSurvivalSaveActions(box);
        AddSpellShortcutMenu(box);
        BuildAudioControls(box);
        _relicMenuButton = Ui.Button("Reliquias recogidas", OpenRelics);
        _relicMenuButton.Name = "PauseRelics"; _relicMenuButton.Visible = false; box.AddChild(_relicMenuButton);
        box.AddChild(Ui.Button("Volver al launcher", () => ReturnToLauncher()));
        box.AddChild(Ui.Button("Exportar diagnóstico", ExportDiagnostics));
        _diagnosticStatus = Ui.Label("", 15, Ui.Muted); _diagnosticStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_diagnosticStatus);
        box.AddChild(Ui.Button("Salir del juego", () => GetTree().Quit()));
        GetViewport().SizeChanged += UpdatePauseResponsive;
        UpdatePauseResponsive();
        return root;
    }
}

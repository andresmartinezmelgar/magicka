using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class RoomLobby
{
    private void UpdateRoomResponsive()
    {
        if (_roomFormGrid == null) return;
        float width = GetViewport().GetVisibleRect().Size.X - (Ui.IsMobile ? 65 : 115);
        _roomFormGrid.Columns = ResponsiveLayout.Columns(_roomFormGrid.Size.X > 0 ? _roomFormGrid.Size.X : width, 260, 3, 18);
        bool compact = GetViewport().GetVisibleRect().Size.Y <= 600 || width < 720;
        if (_mobileListActions != null)
            _mobileListActions.Columns = ResponsiveLayout.Columns(width, compact ? 110 : 175, 4, 12);
        if (_mobileCreateActions != null)
            _mobileCreateActions.Columns = ResponsiveLayout.Columns(width, compact ? 160 : Ui.IsMobile ? 195 : 230, 3, 12);
        foreach (var group in new[] { _mobileListActions, _mobileCreateActions })
            if (group != null)
                foreach (var button in group.GetChildren().OfType<Button>())
                {
                    button.CustomMinimumSize = new(0, compact ? Ui.IsMobile ? 60 : 48 : Ui.IsMobile ? 66 : 48);
                    button.AddThemeFontSizeOverride("font_size", compact ? 16 : 20);
                    if (button.Name == "RoomLobbyBack") button.Text = compact ? "Volver" : "Volver al launcher";
                }
        _status.CustomMinimumSize = new(0, compact ? 0 : 36);
        _status.AddThemeFontSizeOverride("font_size", compact ? 14 : 15);
    }

    private static SpinBox Number(int min, int max, int value) => new() { MinValue = min, MaxValue = max,
        Value = value, Step = 1, CustomMinimumSize = new(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };

    private static void Row(GridContainer grid, string title, Control control)
    {
        var field = new VBoxContainer { Name = "RoomField" + control.GetClass(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        field.AddThemeConstantOverride("separation", 4);
        grid.AddChild(field);
        var label = Ui.Label(title, 15, Ui.Muted);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        field.AddChild(label);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (control is OptionButton choice)
        {
            choice.FitToLongestItem = false;
            choice.ClipText = true;
            choice.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
        if (control is CheckBox check) check.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        field.AddChild(control);
    }

    private void ActionButton(Node parent, string text, Action action, bool primary = false)
    {
        var button = Ui.Button(text, action, primary); button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        parent.AddChild(button); _buttons.Add(button);
        if (text is "Entrar" or "Crear sala" or "Crear y entrar") _matchButtons.Add(button);
    }

    private void SwitchPane(bool create)
    {
        if (create && _serverStarting) { ShowStartup(); return; }
        _listPane.Visible = !create;
        _createPane.Visible = create;
        if (_mobileListActions != null) _mobileListActions.Visible = !create;
        if (_mobileCreateActions != null) _mobileCreateActions.Visible = create;
        if (_mobileScroll != null) _mobileScroll.ScrollVertical = 0;
        _status.Text = "";
    }

    private void ShowObjective()
    {
        var room = Selected();
        // Evita reutilizar sin querer la clave de otra sala tras cambiar de selección.
        if ((room?.Id ?? "") != _selectedRoomId)
        {
            _selectedRoomId = room?.Id ?? "";
            if (_joinPassword != null) _joinPassword.Text = "";
        }
        _objective.Text = room == null ? "Selecciona una sala para ver su objetivo."
            : MatchRules.Objective(room.Mode, room.ScoreLimit, room.Tickets, room.Map) +
                (room.PasswordRequired ? "\nEsta sala tiene una clave propia; no es la del servidor." : "\nSala sin contraseña propia.");
    }

    private void UpdateRewardsHint()
    {
        if (_rewardHint == null) return;
        _rewardHint.Text = !_rewardsConfigured
            ? "SIN RECOMPENSAS: el servidor no tiene Firebase habilitado. Configura el servidor con cuentas para probar premios."
            : (GameMode)_mode.GetSelectedId() == GameMode.Defense
                ? "Defensa: un objeto cada cinco minutos activos por humano con Google, hasta cuatro. Los bots aliados están permitidos."
                : _bots.Value > 0
                ? "SIN RECOMPENSAS: las partidas con IA no conceden premios."
                : (GameMode)_mode.GetSelectedId() == GameMode.Arena
                    ? "Sin premios por equipos: selecciona Guerra o Duelo para recompensas PvP."
                    : "Al finalizar la serie: 50 oro por victoria, 25 por derrota y bonos equipados. Botín para el ganador. Google, equipos humanos equilibrados (1v1–4v4) y 15 s activos.";
    }

    private void ShowCreationObjective()
    {
        var mode = (GameMode)_mode.GetSelectedId(); var map = (ArenaMapId)_map.GetSelectedId();
        var definition = ArenaMap.Get(map);
        if (map == ArenaMapId.Bastion) { _mode.Select(_mode.GetItemIndex((int)GameMode.Defense)); mode = GameMode.Defense; }
        for (int i = 0; i < _mode.ItemCount; i++)
        {
            var choice = (GameMode)_mode.GetItemId(i);
            _mode.SetItemDisabled(i, map != ArenaMapId.Bastion && choice != GameMode.Defense && !definition.SupportsMode(choice));
        }
        if (!definition.SupportsMode(mode)) { mode = GameMode.Warfare; _mode.Select(_mode.GetItemIndex((int)mode)); }
        _capacity.MinValue = mode == GameMode.Defense ? 1 : 2;
        _capacity.MaxValue = mode == GameMode.Defense ? 4 : 16;
        _bots.MaxValue = _capacity.Value - 1;
        _bestOf.Disabled = _busy || mode is GameMode.Defense or GameMode.Arena;
        _teamChoice.Disabled = _busy || mode == GameMode.Defense;
        _friendly.Disabled = _busy || mode == GameMode.Defense;
        if (mode == GameMode.Defense) _friendly.ButtonPressed = false;
        if (mode == GameMode.Duel) _capacity.Value = 2;
        if (mode == GameMode.Defense) _bestOf.Select(0);
        _capacity.Editable = !_busy && mode != GameMode.Duel;
        _tickets.Editable = !_busy && mode == GameMode.Warfare && definition.HoldSeconds == 0 && !definition.SequentialCapture;
        _score.Editable = !_busy && mode is GameMode.Duel or GameMode.Arena;
        _creationObjective.Text = MatchRules.Objective(mode, (int)_score.Value, (int)_tickets.Value, map);
        UpdateRewardsHint();
    }
}

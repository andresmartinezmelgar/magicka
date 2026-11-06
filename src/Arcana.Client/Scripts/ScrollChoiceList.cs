using Godot;

namespace Arcana.Client;

internal partial class ScrollChoiceList : ContentScrollContainer
{
    private readonly VBoxContainer _rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private int _lastTapped = -1;
    private bool _disabled;

    public int ItemCount => _rows.GetChildCount();
    public int SelectedIndex { get; private set; } = -1;
    public float ContentHeight => _rows.GetCombinedMinimumSize().Y;
    public bool ActivateOnTap { get; init; }
    public event Action<int>? ItemSelected;
    public event Action<int>? ItemActivated;

    public ScrollChoiceList()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        _rows.AddThemeConstantOverride("separation", 3);
        AddChild(_rows);
        ScrollStarted += () => _lastTapped = -1;
    }

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            foreach (Button row in _rows.GetChildren()) row.Disabled = value;
        }
    }

    public void AddItem(string text, Texture2D? icon = null, bool disabled = false)
    {
        int index = ItemCount;
        var row = new Button
        {
            Text = text, Icon = icon, TooltipText = text, Alignment = HorizontalAlignment.Left,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ToggleMode = true, Disabled = disabled || Disabled,
            CustomMinimumSize = new(0, Ui.UsesTouchScroll ? 48 : 30)
        };
        row.AddThemeConstantOverride("icon_max_width", 24);
        bool doubleClick = false;
        row.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
                doubleClick = click.DoubleClick;
            if (!row.Disabled && input is not InputEventMouse) HandleKeyboard(row, index, input);
        };
        row.Pressed += () =>
        {
            bool activate = ActivateOnTap || doubleClick && _lastTapped == index;
            _lastTapped = index;
            Choose(index, activate);
        };
        _rows.AddChild(row);
    }

    public void Clear()
    {
        foreach (Node row in _rows.GetChildren())
        {
            _rows.RemoveChild(row);
            row.QueueFree();
        }
        SelectedIndex = _lastTapped = -1;
        ScrollVertical = 0;
    }

    public void Select(int index)
    {
        if (SelectedIndex >= 0) _rows.GetChild<Button>(SelectedIndex).SetPressedNoSignal(false);
        SelectedIndex = index;
        if (index >= 0) _rows.GetChild<Button>(index).SetPressedNoSignal(true);
    }

    public void RevealSelection()
    {
        if (SelectedIndex >= 0) EnsureControlVisible(_rows.GetChild<Button>(SelectedIndex));
    }

    public void FocusSelection()
    {
        var row = SelectedIndex >= 0 ? _rows.GetChild<Button>(SelectedIndex) : null;
        if (row == null || row.Disabled)
            row = _rows.GetChildren().OfType<Button>().FirstOrDefault(candidate => !candidate.Disabled);
        if (row == null) return;
        row.GrabFocus();
        EnsureControlVisible(row);
    }

    private void Choose(int index, bool activate)
    {
        bool changed = index != SelectedIndex;
        Select(index);
        if (changed) ItemSelected?.Invoke(index);
        if (activate) ItemActivated?.Invoke(index);
    }

    private void HandleKeyboard(Button row, int index, InputEvent input)
    {
        if (input.IsActionPressed("ui_accept"))
        {
            row.AcceptEvent();
            Choose(index, true);
            return;
        }

        int direction = input.IsActionPressed("ui_down", true) ? 1 : input.IsActionPressed("ui_up", true) ? -1 : 0;
        if (direction == 0) return;
        for (int next = index + direction; next >= 0 && next < ItemCount; next += direction)
        {
            var candidate = _rows.GetChild<Button>(next);
            if (candidate.Disabled) continue;
            row.AcceptEvent();
            Choose(next, false);
            candidate.GrabFocus();
            RevealSelection();
            return;
        }
    }
}

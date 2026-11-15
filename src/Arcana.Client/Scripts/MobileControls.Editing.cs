using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class MobileControls
{
    private TouchEditorOverlay? _editorOverlay;
    private string _selectedEditorControl = "";
    private bool _resizingControl;
    private Rect2 _resizeStart;
    private Vector2 _resizePointerStart;
    private float _resizeStartScale;
    public event Action<string, float>? EditorSelectionChanged;
    internal string SelectedEditorControl => _selectedEditorControl;
    internal float SelectedEditorScale => Settings.ControlScale(_selectedEditorControl);

    private void BuildEditorOverlay()
    {
        _editorOverlay = new TouchEditorOverlay { Name = "TouchSelectedControl", ZIndex = 20,
            MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_editorOverlay); Ui.Full(_editorOverlay);
    }
    private void ClearEditorSelection()
    {
        _selectedEditorControl = _editingControl = ""; _editFinger = -1; _resizingControl = false;
        if (_editorOverlay != null) _editorOverlay.Hide();
        EditorSelectionChanged?.Invoke("", 1);
    }
    private void UpdateEditorSelection()
    {
        if (_editorOverlay == null) return;
        _editorOverlay.Visible = EditorMode && _rectangles.TryGetValue(_selectedEditorControl, out _);
        if (!_editorOverlay.Visible) return;
        _editorOverlay.Target = _rectangles[_selectedEditorControl]; _editorOverlay.QueueRedraw();
    }
    internal string EditorControlName(string key)
    {
        if (key == "Move") return "Joystick de movimiento";
        if (key == "Aim") return "Joystick de apuntado";
        if (GodotObject.IsInstanceValid(_equipmentHud) && _equipmentHud!.Buttons.ContainsKey(key))
            return "Equipo · " + EquipmentCatalogue.SlotName(Enum.Parse<EquipmentSlot>(key["SurvivalEquipped_".Length..]));
        return _actions.FirstOrDefault(a => a.Panel.Name == key)?.Text.Text.Split('\n')[0] ?? key;
    }
    internal bool SelectEditorControl(string key)
    {
        if (!EditorMode || !_rectangles.ContainsKey(key)) return false;
        _selectedEditorControl = key; UpdateEditorSelection();
        EditorSelectionChanged?.Invoke(EditorControlName(key), Settings.ControlScale(key)); return true;
    }
    private bool EditControl(int finger, Vector2 point, bool down)
    {
        if (!down)
        {
            if (_editFinger != finger) return false;
            _editFinger = -1; _editingControl = ""; _resizingControl = false; LayoutEdited?.Invoke(); return true;
        }
        if (_editFinger != -1 || PointerBlocked?.Invoke(point) == true) return false;
        point -= GlobalPosition;
        if (_editorOverlay?.Visible == true && _editorOverlay.Grip.Grow(4).HasPoint(point))
        {
            _editingControl = _selectedEditorControl; _editFinger = finger; _resizingControl = true;
            _resizeStart = _rectangles[_editingControl]; _resizePointerStart = point;
            _resizeStartScale = Settings.ControlScale(_editingControl); return true;
        }
        var hit = _rectangles.FirstOrDefault(pair => pair.Value.HasPoint(point));
        if (hit.Key == null || !SelectEditorControl(hit.Key)) return false;
        _editingControl = hit.Key; _editFinger = finger; _editOffset = point - hit.Value.GetCenter(); return true;
    }
    private bool DragControl(int finger, Vector2 point)
    {
        if (_editFinger != finger || _editingControl.Length == 0) return false;
        point -= GlobalPosition;
        if (_resizingControl)
        {
            float ratio = 1 + (point - _resizePointerStart).Dot(_resizeStart.Size) / _resizeStart.Size.LengthSquared();
            ResizeEditorControl(_editingControl, _resizeStartScale * ratio, _resizeStart.Position);
        }
        else MoveEditedControl(point - _editOffset);
        return true;
    }

    private List<ScreenRect> EditorObstacles(string key)
    {
        var occupied = Reserved();
        occupied.AddRange(_rectangles.Where(pair => pair.Key != key).Select(pair => TouchHudLayout.Rect(pair.Value)));
        if (MapRectangle is { } map) occupied.Add(TouchHudLayout.Rect(new Rect2(map.Position - GlobalPosition, map.Size)));
        return occupied;
    }
    private bool PlaceEdited(string key, Rect2 wanted, out Rect2 destination)
    {
        var obstacles = EditorObstacles(key);
        bool fits = key is "Move" or "Aim"
            ? ScreenPlacement.TryPlaceJoystick(TouchHudLayout.Rect(wanted), Screen, obstacles, out var safe)
            : ScreenPlacement.TryPlace(TouchHudLayout.Rect(wanted), Screen, obstacles, out safe);
        destination = TouchHudLayout.Rect(safe); return fits;
    }
    private void RememberCurrentLayout()
    {
        // Fija los demás controles antes de separar una cuadrícula o cambiar un único tamaño.
        Settings = Settings with { LayoutScale = _fitScale };
        Settings.Positions.Remove(SpellGridPosition);
        foreach (var (key, rect) in _rectangles)
            Settings.Positions[key] = new(rect.GetCenter().X / Size.X, rect.GetCenter().Y / Size.Y);
        if (MapRectangle is { } map)
        {
            var center = map.GetCenter() - GlobalPosition;
            Settings.Positions[MapPosition] = new(center.X / Size.X, center.Y / Size.Y);
        }
    }
    private void MoveEditedControl(Vector2 point)
    {
        var current = _rectangles[_editingControl];
        if (!PlaceEdited(_editingControl, new(point - current.Size / 2, current.Size), out var destination)) return;
        RememberCurrentLayout(); _rectangles[_editingControl] = destination;
        Settings.Positions[_editingControl] = new(destination.GetCenter().X / Size.X, destination.GetCenter().Y / Size.Y);
        ApplyRectangles();
    }
    internal bool ResizeEditorControl(string key, float nextScale, Vector2? anchor = null)
    {
        if (!EditorMode || !_rectangles.TryGetValue(key, out var current) || !float.IsFinite(nextScale)) return false;
        nextScale = Math.Clamp(nextScale, TouchSettings.MinimumControlScale, TouchSettings.MaximumControlScale);
        var size = current.Size * (nextScale / Settings.ControlScale(key));
        var wanted = new Rect2(anchor ?? current.GetCenter() - size / 2, size);
        if (!PlaceEdited(key, wanted, out var destination)) return false;
        RememberCurrentLayout(); Settings.ControlScales[key] = nextScale; _rectangles[key] = destination;
        Settings.Positions[key] = new(destination.GetCenter().X / Size.X, destination.GetCenter().Y / Size.Y);
        ApplyRectangles(); EditorSelectionChanged?.Invoke(EditorControlName(key), nextScale); LayoutEdited?.Invoke(); return true;
    }
    internal bool ResizeSelectedControl(float scale) => ResizeEditorControl(_selectedEditorControl, scale);
}

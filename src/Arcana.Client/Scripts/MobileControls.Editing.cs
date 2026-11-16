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
    internal IEnumerable<string> EditableKeys => _rectangles.Keys.Concat(_hudAreas.Where(a => a.Widget != null).Select(a => a.Name))
        .Concat(MapRectangle != null ? new[] { MapPosition } : []).Distinct(StringComparer.Ordinal);
    private bool EditorRectangle(string key, out Rect2 rect)
    {
        if (_rectangles.TryGetValue(key, out rect)) return true;
        if (key == MapPosition && MapRectangle is { } map) { rect = new(map.Position - GlobalPosition, map.Size); return true; }
        foreach (var area in _hudAreas) if (area.Name == key && area.Widget != null)
        { rect = new(area.Bounds.Position - GlobalPosition, area.Bounds.Size); return true; }
        rect = default; return false;
    }

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
        _editorOverlay.Visible = EditorMode && EditorRectangle(_selectedEditorControl, out _);
        if (!_editorOverlay.Visible) return;
        EditorRectangle(_selectedEditorControl, out var selected); _editorOverlay.Target = selected; _editorOverlay.QueueRedraw();
    }
    internal string EditorControlName(string key)
    {
        if (key == "Move") return "Joystick de movimiento";
        if (key == "Aim") return "Joystick de apuntado";
        if (key == "HudTopScore") return "Temporizador, vidas y objetivo";
        if (key == "PlayerStatusPanel") return "Nombre, vida, escudo y energía";
        if (key == "SpellControlsPanel") return "Mezcla seleccionada y carga";
        if (key == "MagickFeedback") return "Avisos de hechizos e interrupciones";
        if (key == "ActiveBuffs") return "Información de reliquias";
        if (key == "HudLootNotice") return "Avisos de objetos y recompensas";
        if (key == "HudEventFeed") return "Registro de eventos";
        if (key == "HudServerInfo") return "Nombre del servidor y modo";
        if (key == "HudControlHints") return "Recordatorio de controles";
        if (key == MapPosition) return "Minimapa";
        if (GodotObject.IsInstanceValid(_equipmentHud) && _equipmentHud!.Buttons.ContainsKey(key))
            return "Equipo · " + EquipmentCatalogue.SlotName(Enum.Parse<EquipmentSlot>(key["SurvivalEquipped_".Length..]));
        return _actions.FirstOrDefault(a => a.Panel.Name == key)?.Text.Text.Split('\n')[0] ?? key;
    }
    internal bool SelectEditorControl(string key)
    {
        if (!EditorMode || !EditorRectangle(key, out _)) return false;
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
            EditorRectangle(_editingControl, out _resizeStart); _resizePointerStart = point;
            _resizeStartScale = Settings.ControlScale(_editingControl); return true;
        }
        var hit = EditableKeys.Select(k => { EditorRectangle(k, out var r); return new KeyValuePair<string, Rect2>(k, r); })
            .FirstOrDefault(pair => pair.Value.HasPoint(point));
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
        var occupied = _hudAreas.Where(a => !a.Floating && a.Name != key && !Settings.Hidden(a.Name) && a.Name != "ActiveBuffs")
            .Select(a => TouchHudLayout.Rect(new Rect2(a.Bounds.Position - GlobalPosition, a.Bounds.Size))).ToList();
        occupied.AddRange(_rectangles.Where(pair => pair.Key != key && !Settings.Hidden(pair.Key)).Select(pair => TouchHudLayout.Rect(pair.Value)));
        if (key != MapPosition && !Settings.Hidden(MapPosition) && MapRectangle is { } map)
            occupied.Add(TouchHudLayout.Rect(new Rect2(map.Position - GlobalPosition, map.Size)));
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
        foreach (var area in _hudAreas.Where(a => a.Widget != null))
        {
            var center = area.Bounds.GetCenter() - GlobalPosition;
            Settings.Positions[area.Name] = new(center.X / Size.X, center.Y / Size.Y);
        }
        if (MapRectangle is { } map)
        {
            var center = map.GetCenter() - GlobalPosition;
            Settings.Positions[MapPosition] = new(center.X / Size.X, center.Y / Size.Y);
        }
    }
    private void MoveEditedControl(Vector2 point)
    {
        if (!EditorRectangle(_editingControl, out var current)) return;
        if (!PlaceEdited(_editingControl, new(point - current.Size / 2, current.Size), out var destination)) return;
        RememberCurrentLayout();
        if (_rectangles.ContainsKey(_editingControl)) _rectangles[_editingControl] = destination;
        Settings.Positions[_editingControl] = new(destination.GetCenter().X / Size.X, destination.GetCenter().Y / Size.Y);
        ApplyEditedHud(_editingControl, destination); ApplyRectangles();
    }
    internal bool ResizeEditorControl(string key, float nextScale, Vector2? anchor = null)
    {
        if (!EditorMode || !EditorRectangle(key, out var current) || !float.IsFinite(nextScale)) return false;
        nextScale = Math.Clamp(nextScale, TouchSettings.MinimumControlScale, TouchSettings.MaximumControlScale);
        var size = current.Size * (nextScale / Settings.ControlScale(key));
        var wanted = new Rect2(anchor ?? current.GetCenter() - size / 2, size);
        if (!PlaceEdited(key, wanted, out var destination)) return false;
        RememberCurrentLayout(); Settings.ControlScales[key] = nextScale;
        if (_rectangles.ContainsKey(key)) _rectangles[key] = destination;
        Settings.Positions[key] = new(destination.GetCenter().X / Size.X, destination.GetCenter().Y / Size.Y);
        ApplyEditedHud(key, destination); ApplyRectangles(); EditorSelectionChanged?.Invoke(EditorControlName(key), nextScale); LayoutEdited?.Invoke(); return true;
    }
    private void ApplyEditedHud(string key, Rect2 destination)
    {
        if (key == MapPosition && destination.Size.X > 0) MapRectangle = new(destination.Position + GlobalPosition, destination.Size);
        var areas = _hudAreas.ToList(); _hudCustomization.Apply(areas, Settings, Size, EditorMode); _hudAreas = areas.ToArray();
    }
    internal bool SetSelectedHidden(bool hidden)
    {
        if (!EditorMode || !EditorRectangle(_selectedEditorControl, out _)) return false;
        RememberCurrentLayout();
        if (hidden) Settings.HiddenControls.Add(_selectedEditorControl); else Settings.HiddenControls.Remove(_selectedEditorControl);
        ApplyEditedHud(_selectedEditorControl, default); ApplyRectangles();
        EditorSelectionChanged?.Invoke(EditorControlName(_selectedEditorControl), SelectedEditorScale); LayoutEdited?.Invoke(); return true;
    }
    internal bool ResizeSelectedControl(float scale) => ResizeEditorControl(_selectedEditorControl, scale);
}

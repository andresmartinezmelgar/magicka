using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class MobileControls
{
    private float _fitScale = 1;
    private TouchHudArea[] _hudAreas = [];
    private Rect2? _preferredMap;
    private readonly Dictionary<string, Rect2> _rectangles = new(StringComparer.Ordinal);
    internal Rect2? MapRectangle { get; private set; }
    internal IReadOnlyDictionary<string, Rect2> LayoutRectangles => _rectangles;
    internal IReadOnlyList<TouchHudArea> HudAreas => _hudAreas;
    internal float FittedScale => _fitScale;

    internal void SetHudAreas(IReadOnlyList<TouchHudArea> areas, Rect2? map)
    {
        bool same = _preferredMap == map && _hudAreas.Length == areas.Count;
        if (same) for (int i = 0; i < areas.Count; i++) if (areas[i] != _hudAreas[i]) { same = false; break; }
        if (same) return;
        _hudAreas = areas.ToArray(); _preferredMap = map; Layout();
    }

    private static Rect2 ButtonRect(TouchAction action) => new(action.Panel.Position, action.Panel.Size * action.Panel.Scale);
    private ScreenRect Screen => new(0, 0, Size.X, Size.Y);
    private List<ScreenRect> Reserved() => _hudAreas.Select(a => TouchHudLayout.Rect(new Rect2(a.Bounds.Position - GlobalPosition, a.Bounds.Size))).ToList();

    private Dictionary<string, Rect2> Wanted(bool custom)
    {
        float width = Size.X, height = Size.Y, s = BaseScale;
        var result = new Dictionary<string, Rect2>(StringComparer.Ordinal);
        void Add(string name, Vector2 center, Vector2 size)
        {
            if (Settings.LeftHanded) center.X = width - center.X;
            if (custom && Settings.Positions.TryGetValue(name, out var saved)) center = new(saved.X * width, saved.Y * height);
            result[name] = new(center - size / 2, size);
        }
        Add("Move", new(142 * s, height - 125 * s), Vector2.One * StickRadius * 2);
        Add("Aim", new(width - 142 * s, height - 125 * s), Vector2.One * StickRadius * 2);
        void Button(TouchAction action, Vector2 position, Vector2 size)
        {
            var pivot = _utilities.Contains(action) ? new Vector2(width - 8 * s, 8 * s) :
                _magicks.Contains(action) ? new Vector2(width / 2, height - 8 * s) : new Vector2(width - 8 * s, height - 238 * s);
            float spacing = Settings.Spacing < 1 ? 1 + (Settings.Spacing - 1) * .1f : Settings.Spacing;
            var center = pivot + (position + size * s / 2 - pivot) * Settings.Size * spacing * _fitScale;
            // Escalar el panel entero conserva su mínimo real, iconos, texto y área pulsable.
            action.Panel.Scale = Vector2.One * ControlScale;
            action.Panel.Size = size;
            if (_magicks.Contains(action)) FitMagickCaption(action);
            Add(action.Panel.Name, center, action.Panel.Size * ControlScale);
        }
        for (int i = 0; i < _utilities.Count; i++) Button(_utilities[i], new(width - (382 - i * 124) * s, 22 * s), new(118, 62));
        for (int i = 0; i < 8; i++) Button(_actions[i], new(width - (246 - i % 2 * 116) * s, height - (572 - i / 2 * 80) * s), new(108, 72));
        Button(_self, new(width - 382 * s, height - 452 * s), new(120, 62));
        Button(_nova, new(width - 382 * s, height - 382 * s), new(120, 62));
        for (int i = 0; i < _magicks.Count; i++) Button(_magicks[i], new(width / 2 - (244 - i * 124) * s, height - 90 * s), new(120, 72));
        return result;
    }

    private bool Pack(Dictionary<string, Rect2> wanted, List<ScreenRect> occupied, out Dictionary<string, Rect2> placed)
    {
        placed = new(StringComparer.Ordinal);
        foreach (var (name, rect) in wanted)
        {
            if (!ScreenPlacement.TryPlace(TouchHudLayout.Rect(rect), Screen, occupied, out var safe)) return false;
            placed[name] = TouchHudLayout.Rect(safe); occupied.Add(safe);
        }
        return true;
    }

    private void Layout()
    {
        if (_actions.Count == 0 || Size.X < 1 || Size.Y < 1) return;
        // Se ejecuta al configurar, redimensionar o cambiar una zona del HUD; nunca en cada dibujo.
        for (_fitScale = 1; _fitScale >= .45f; _fitScale -= .05f)
        {
            var reserved = Reserved();
            Rect2? map = null;
            if (_preferredMap is { } desired)
            {
                // El mapa conserva su hueco aunque se arrastre un botón: se calcula con el diseño base.
                if (!Pack(Wanted(false), new(reserved), out var defaults)) continue;
                var occupied = new List<ScreenRect>(reserved);
                occupied.AddRange(defaults.Values.Select(TouchHudLayout.Rect));
                var local = new Rect2(desired.Position - GlobalPosition, desired.Size);
                if (!ScreenPlacement.TryPlace(TouchHudLayout.Rect(local), Screen, occupied, out var safeMap)) continue;
                map = TouchHudLayout.Rect(safeMap); reserved.Add(safeMap);
            }
            if (!Pack(Wanted(true), reserved, out var placed)) continue;
            _rectangles.Clear(); foreach (var item in placed) _rectangles.Add(item.Key, item.Value);
            foreach (string name in Settings.Positions.Keys.ToArray())
                if (placed.TryGetValue(name, out var adjusted))
                    Settings.Positions[name] = new(adjusted.GetCenter().X / Size.X, adjusted.GetCenter().Y / Size.Y);
            MapRectangle = map is { } area ? new Rect2(area.Position + GlobalPosition, area.Size) : null;
            ApplyRectangles(); return;
        }
        throw new InvalidOperationException("No hay espacio para los controles y las zonas del HUD.");
    }

    private void ApplyRectangles()
    {
        foreach (var action in _actions) action.Panel.Position = _rectangles[action.Panel.Name].Position;
        _moveHome = _rectangles["Move"].GetCenter(); _aimCenter = _rectangles["Aim"].GetCenter();
        if (_moveFinger < 0) _moveCenter = _moveHome;
        else _moveCenter = FreeMoveCenter(_moveOrigin);
        QueueRedraw();
    }

    private Vector2 FreeMoveCenter(Vector2 point)
    {
        var occupied = Reserved();
        occupied.AddRange(_rectangles.Where(p => p.Key != "Move").Select(p => TouchHudLayout.Rect(p.Value)));
        if (MapRectangle is { } map) occupied.Add(TouchHudLayout.Rect(new Rect2(map.Position - GlobalPosition, map.Size)));
        var wanted = new ScreenRect(point.X - StickRadius, point.Y - StickRadius, StickRadius * 2, StickRadius * 2);
        return ScreenPlacement.TryPlace(wanted, Screen, occupied, out var safe)
            ? TouchHudLayout.Rect(safe).GetCenter() : _moveHome;
    }

    private void MoveEditedControl(Vector2 point)
    {
        var current = _rectangles[_editingControl];
        var occupied = Reserved();
        occupied.AddRange(_rectangles.Where(p => p.Key != _editingControl).Select(p => TouchHudLayout.Rect(p.Value)));
        if (MapRectangle is { } map) occupied.Add(TouchHudLayout.Rect(new Rect2(map.Position - GlobalPosition, map.Size)));
        if (!ScreenPlacement.TryPlace(TouchHudLayout.Rect(new Rect2(point - current.Size / 2, current.Size)), Screen, occupied, out var safe)) return;
        _rectangles[_editingControl] = TouchHudLayout.Rect(safe);
        var center = _rectangles[_editingControl].GetCenter();
        Settings.Positions[_editingControl] = new(center.X / Size.X, center.Y / Size.Y);
        ApplyRectangles();
    }
}

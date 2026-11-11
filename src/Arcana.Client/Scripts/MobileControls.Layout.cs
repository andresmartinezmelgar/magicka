using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class MobileControls
{
    private float _fitScale = 1;
    private TouchHudArea[] _hudAreas = [];
    private Rect2? _preferredMap;
    private readonly Dictionary<string, Rect2> _rectangles = new(StringComparer.Ordinal);
    private const string SpellGridPosition = "AutomaticSpellGrid";
    private TouchAction[] SpellGridActions => [_defenseShortcut.Action, _attackShortcut.Action, _self, _nova];
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
            if (!action.Panel.Visible) return;
            var pivot = _utilities.Contains(action) ? new Vector2(width - 8 * s, 8 * s) :
                _magicks.Contains(action) ? new Vector2(width / 2, height - 8 * s) : new Vector2(width - 8 * s, height - 238 * s);
            float spacing = Settings.Spacing < 1 ? 1 + (Settings.Spacing - 1) * .1f : Settings.Spacing;
            var center = pivot + (position + size * s / 2 - pivot) * Settings.Size * spacing * _fitScale;
            // Escalar el panel entero conserva su mínimo real, iconos, texto y área pulsable.
            action.Panel.Scale = Vector2.One * ControlScale;
            action.Panel.Size = size;
            if (_magicks.Contains(action)) FitActionCaption(action);
            Add(action.Panel.Name, center, action.Panel.Size * ControlScale);
        }
        for (int i = 0; i < _utilities.Count; i++) Button(_utilities[i], new(width - (382 - i * 124) * s, 22 * s), new(118, 62));
        for (int i = 0; i < 8; i++) Button(_actions[i], new(width - (246 - i % 2 * 116) * s, height - (572 - i / 2 * 80) * s), new(108, 72));
        if (AutomaticSpells) AddSpellGrid(result, custom);
        else
        {
            Button(_self, new(width - 382 * s, height - 452 * s), new(120, 62));
            Button(_nova, new(width - 382 * s, height - 382 * s), new(120, 62));
        }
        for (int i = 0; i < _magicks.Count; i++) Button(_magicks[i], new(width / 2 - (244 - i * 124) * s, height - 90 * s), new(120, 72));
        return result;
    }

    private void AddSpellGrid(Dictionary<string, Rect2> wanted, bool custom)
    {
        var actions = SpellGridActions;
        var cellSize = new Vector2(120, 68);
        var cell = cellSize * ControlScale;
        float gap = Math.Max(ScreenPlacement.MinimumGap, 12 * ControlScale * Settings.Spacing);
        var size = cell * 2 + Vector2.One * gap;
        var origin = new Vector2(Settings.LeftHanded ? ScreenPlacement.MinimumGap : Size.X - size.X - ScreenPlacement.MinimumGap,
            Size.Y - 125 * BaseScale - StickRadius - ScreenPlacement.MinimumGap - size.Y);
        if (custom && Settings.Positions.TryGetValue(SpellGridPosition, out var saved))
            origin = new Vector2(saved.X * Size.X, saved.Y * Size.Y) - size / 2;
        else if (custom && Settings.Positions.TryGetValue(actions[0].Panel.Name, out var previous))
            origin = new Vector2(previous.X * Size.X, previous.Y * Size.Y) - cell / 2;
        for (int i = 0; i < actions.Length; i++)
        {
            var panel = actions[i].Panel;
            panel.Scale = Vector2.One * ControlScale;
            panel.Size = cellSize;
            wanted[panel.Name] = new(origin + new Vector2(i % 2 * (cell.X + gap), i / 2 * (cell.Y + gap)), cell);
        }
    }

    private Rect2 SpellGridBounds(IReadOnlyDictionary<string, Rect2> rectangles)
    {
        var first = rectangles[_defenseShortcut.Action.Panel.Name];
        var last = rectangles[_nova.Panel.Name];
        return new(first.Position, last.End - first.Position);
    }

    private bool Pack(Dictionary<string, Rect2> wanted, List<ScreenRect> occupied, out Dictionary<string, Rect2> placed)
    {
        placed = new(StringComparer.Ordinal);
        foreach (var (name, rect) in wanted)
        {
            if (placed.ContainsKey(name)) continue;
            if (AutomaticSpells && name == _defenseShortcut.Action.Panel.Name)
            {
                // Reservar el bloque completo impide que el ajuste separe las dos filas.
                var bounds = SpellGridBounds(wanted);
                if (!ScreenPlacement.TryPlace(TouchHudLayout.Rect(bounds), Screen, occupied, out var grid)) return false;
                var offset = TouchHudLayout.Rect(grid).Position - bounds.Position;
                foreach (var action in SpellGridActions)
                {
                    var cell = wanted[action.Panel.Name];
                    placed[action.Panel.Name] = new(cell.Position + offset, cell.Size);
                }
                occupied.Add(grid);
                continue;
            }
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
            TouchAction[] grid = AutomaticSpells ? SpellGridActions : [];
            // Sobre mí y En área conservan sus posiciones personalizadas del modo manual.
            foreach (string name in Settings.Positions.Keys.ToArray())
                if (!grid.Any(a => a.Panel.Name == name) && placed.TryGetValue(name, out var adjusted))
                    Settings.Positions[name] = new(adjusted.GetCenter().X / Size.X, adjusted.GetCenter().Y / Size.Y);
            if (AutomaticSpells && Settings.Positions.ContainsKey(SpellGridPosition))
            {
                var center = SpellGridBounds(placed).GetCenter();
                Settings.Positions[SpellGridPosition] = new(center.X / Size.X, center.Y / Size.Y);
            }
            MapRectangle = map is { } area ? new Rect2(area.Position + GlobalPosition, area.Size) : null;
            ApplyRectangles(); return;
        }
        throw new InvalidOperationException("No hay espacio para los controles y las zonas del HUD.");
    }

    private void ApplyRectangles()
    {
        foreach (var action in _actions.Where(a => a.Panel.Visible)) action.Panel.Position = _rectangles[action.Panel.Name].Position;
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
        var grid = SpellGridActions;
        bool movingGrid = AutomaticSpells && grid.Any(a => a.Panel.Name == _editingControl);
        var bounds = movingGrid ? SpellGridBounds(_rectangles) : current;
        var occupied = Reserved();
        occupied.AddRange(_rectangles.Where(p => movingGrid ? !grid.Any(a => a.Panel.Name == p.Key) : p.Key != _editingControl)
            .Select(p => TouchHudLayout.Rect(p.Value)));
        if (MapRectangle is { } map) occupied.Add(TouchHudLayout.Rect(new Rect2(map.Position - GlobalPosition, map.Size)));
        var wanted = new Rect2(bounds.Position + point - current.GetCenter(), bounds.Size);
        if (!ScreenPlacement.TryPlace(TouchHudLayout.Rect(wanted), Screen, occupied, out var safe)) return;
        var destination = TouchHudLayout.Rect(safe);
        if (movingGrid)
        {
            var offset = destination.Position - bounds.Position;
            foreach (var action in grid)
            {
                var cell = _rectangles[action.Panel.Name];
                _rectangles[action.Panel.Name] = new(cell.Position + offset, cell.Size);
            }
        }
        else _rectangles[_editingControl] = destination;
        var center = destination.GetCenter();
        Settings.Positions[movingGrid ? SpellGridPosition : _editingControl] = new(center.X / Size.X, center.Y / Size.Y);
        ApplyRectangles();
    }
}

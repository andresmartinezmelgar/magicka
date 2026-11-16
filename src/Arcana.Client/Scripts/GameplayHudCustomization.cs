using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Transformaciones locales de los paneles informativos. No cambian la simulación.</summary>
internal sealed class GameplayHudCustomization
{
    private sealed record Baseline(Control Widget, Rect2 Bounds, Vector2 Size, Vector2 Scale, bool Visible);
    private readonly Dictionary<string, Baseline> _baselines = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly HashSet<string> _transformed = new(StringComparer.Ordinal);
    private Vector2 _screen;
    internal void Apply(List<TouchHudArea> areas, TouchSettings settings, Vector2 screen, bool editor)
    {
        if (_screen != screen) { _baselines.Clear(); _screen = screen; }
        for (int i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            if (area.Widget is not { } widget || !GodotObject.IsInstanceValid(widget)) continue;
            bool custom = settings.Positions.ContainsKey(area.Name) || settings.ControlScales.ContainsKey(area.Name);
            if (!custom)
            {
                // Los avisos y resúmenes conservan su colocación dinámica hasta que el
                // usuario los edita. Capturar su caja vacía desplazaría el aviso al llenarse.
                if (_transformed.Remove(area.Name) && _baselines.TryGetValue(area.Name, out var original))
                { widget.Scale = original.Scale; widget.GlobalPosition = original.Bounds.Position; }
                if (editor) { widget.Show(); widget.Modulate = new(1, 1, 1, settings.Hidden(area.Name) ? .3f : 1); }
                else if (settings.Hidden(area.Name)) { _hidden.Add(area.Name); widget.Hide(); }
                else if (_hidden.Remove(area.Name)) widget.Show();
                areas[i] = area with { Bounds = widget.GetGlobalRect() };
                continue;
            }
            if (!_baselines.TryGetValue(area.Name, out var baseline) || baseline.Widget != widget)
            {
                // Los Container ordenan sus hijos al final del primer fotograma.
                // Esperar evita fijar su mínimo provisional como tamaño permanente.
                if (!new Rect2(Vector2.Zero, screen).Grow(.1f).Encloses(area.Bounds) ||
                    widget.GetGlobalRect().Size.DistanceTo(area.Bounds.Size) > 1) continue;
                _baselines[area.Name] = baseline = new(widget, area.Bounds, widget.Size, widget.Scale, widget.Visible);
            }
            float scale = settings.ControlScale(area.Name);
            if (baseline.Size.X <= 0 || baseline.Size.Y <= 0) continue;
            var size = baseline.Bounds.Size * (widget.Size / baseline.Size) * scale;
            var center = settings.Positions.TryGetValue(area.Name, out var saved)
                ? new Vector2(saved.X * screen.X, saved.Y * screen.Y) : baseline.Bounds.GetCenter();
            widget.Scale = baseline.Scale * scale;
            widget.GlobalPosition = center - size / 2;
            _transformed.Add(area.Name);
            if (editor) { widget.Visible = true; widget.Modulate = new(1, 1, 1, settings.Hidden(area.Name) ? .3f : 1); }
            else if (settings.Hidden(area.Name)) { _hidden.Add(area.Name); widget.Hide(); }
            else if (_hidden.Remove(area.Name) && baseline.Visible) widget.Show();
            areas[i] = area with { Bounds = new(center - size / 2, size) };
        }
    }
}

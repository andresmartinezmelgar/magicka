using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private readonly List<TouchHudArea> _touchHudAreas = [];
    private void AppendEditorTouchAreas(List<TouchHudArea> areas)
    {
        float s = Math.Min(1, GetViewport().GetVisibleRect().Size.Y / 720f);
        if (_defensePanel != null) areas.Add(new("Supervivencia", _defensePanel.SummaryRectForVerification));
        else
        {
            var metrics = new Rect2(22 * s, 194 * s + ScreenPlacement.MinimumGap, 288 * s, NetworkMetricsHeight * s);
            areas.Add(new("Conexión / FPS / ping", metrics));
            if (_practicePanel != null)
            {
                float top = metrics.End.Y + ScreenPlacement.MinimumGap;
                float height = Math.Max(150 * s, GetViewport().GetVisibleRect().Size.Y - 207 * s - ScreenPlacement.MinimumGap - top);
                areas.Add(new("Laboratorio", new Rect2(22 * s, top, 320 * s, height)));
            }
        }
    }
    private void AppendModeTouchAreas(List<TouchHudArea> areas)
    {
        if (_defensePanel != null) areas.Add(new("Supervivencia", _defensePanel.SummaryRectForVerification));
        else
        {
            areas.Add(new("Conexión / FPS / ping", _networkPanel.GetGlobalRect()));
            if (_practicePanel != null) areas.Add(new("Laboratorio", _practicePanel.GetGlobalRect()));
        }
    }

    private void UpdateTouchLayout()
    {
        if (_touch == null || _hud == null) return;
        float s = Math.Min(1, GetViewport().GetVisibleRect().Size.Y / 720f);
        _networkPanel.Scale = Vector2.One * s;
        _networkPanel.Position = new(22 * s, _hud.TouchPlayerRectangle.End.Y + ScreenPlacement.MinimumGap);
        _networkPanel.Size = new(288, NetworkMetricsHeight);
        _practicePanel?.FitTouchLayout(_networkPanel.GetGlobalRect().End.Y + ScreenPlacement.MinimumGap);
        _touchHudAreas.Clear(); AppendModeTouchAreas(_touchHudAreas);
        TouchHudLayout.Apply(_touch, _hud, _touchHudAreas);
    }
}

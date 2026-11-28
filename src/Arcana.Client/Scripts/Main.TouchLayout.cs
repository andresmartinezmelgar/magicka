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
        if (_defensePanel != null) areas.Add(new("Supervivencia", _defensePanel.SummaryRectForVerification,
            (Control)_defensePanel.FindChild("DefenseSummary", true, false)));
        else
        {
            areas.Add(new("Conexión / FPS / ping", _networkPanel.GetGlobalRect(), _networkPanel));
            if (_practicePanel != null) areas.Add(new("Laboratorio", _practicePanel.GetGlobalRect(), _practicePanel));
        }
    }

    private void UpdateTouchLayout()
    {
        if (_hud == null) return;
        var equipment = _defensePanel?.EquippedHud;
        UpdateGameplayHudVisibility();
        LayoutInformationalPanels();
        _hud.PrepareRewardNoticeLayout();
        if (_touch == null)
        {
            if (equipment == null) return;
            _touchHudAreas.Clear(); AppendModeTouchAreas(_touchHudAreas); _hud.AppendTouchAreas(_touchHudAreas);
            if (_hud.CurrentMapRectangle is { } map) _touchHudAreas.Add(new("Mapa", map));
            equipment.LayoutStandalone(LocalTouchSettings, GetViewport().GetVisibleRect().Size,
                _touchHudAreas.Select(area => TouchHudLayout.Rect(area.Bounds)).ToArray());
            return;
        }
        _touch.SetEquipmentHud(equipment);
        _touchHudAreas.Clear(); AppendModeTouchAreas(_touchHudAreas);
        TouchHudLayout.Apply(_touch, _hud, _touchHudAreas);
    }

    private void LayoutInformationalPanels()
    {
        if (_hud == null || _defensePanel != null) return;
        var areas = new List<TouchHudArea>(); _hud.AppendTouchAreas(areas);
        var occupied = areas.Where(area => area.Name != "HudLootNotice" && area.Widget?.IsVisibleInTree() == true &&
            (area.Widget is not ElementTextLabel label || label.Text.Length > 0))
            .Select(area => TouchHudLayout.Rect(area.Widget!.GetGlobalRect())).ToList();
        if (_hud.CurrentMapRectangle is { } map) occupied.Add(TouchHudLayout.Rect(map));
        float scale = _touch != null ? Math.Min(1, GetViewport().GetVisibleRect().Size.Y / 720f) : 1;
        _networkPanel.Scale = Vector2.One * scale; _networkPanel.Size = new(288, NetworkMetricsHeight);
        var wanted = new ScreenRect(22 * scale, _hud.TouchPlayerRectangle.End.Y + ScreenPlacement.MinimumGap,
            _networkPanel.Size.X * scale, _networkPanel.Size.Y * scale);
        if (ScreenPlacement.TryPlace(wanted, TouchHudLayout.Rect(GetViewport().GetVisibleRect()), occupied, out var safe))
            _networkPanel.GlobalPosition = new(safe.X, safe.Y);
        occupied.Add(TouchHudLayout.Rect(_networkPanel.GetGlobalRect()));
        _practicePanel?.FitTouchLayout(_networkPanel.GetGlobalRect().End.Y + ScreenPlacement.MinimumGap, occupied);
    }

    private void PlaceRewardNoticeAfterLayout()
    {
        if (_hud?.RewardNoticePending != true || LocalTouchSettings.Hidden("HudLootNotice")) return;
        // Medir los rectángulos finales, después de aplicar las posiciones y
        // escalas guardadas y de colocar los joysticks y las piezas de equipo.
        var areas = new List<TouchHudArea>();
        AppendModeTouchAreas(areas); _hud.AppendTouchAreas(areas);
        var occupied = areas.Where(area => area.Name != "HudLootNotice" &&
            area.Widget?.IsVisibleInTree() == true &&
            (area.Widget is not ElementTextLabel label || label.Text.Length > 0))
            .Select(area => area.Widget!.GetGlobalRect()).ToList();
        if (_hud.CurrentMapRectangle is { } map) occupied.Add(map);
        if (_touch != null)
            occupied.AddRange(_touch.LayoutRectangles.Where(pair => !_touch.Settings.Hidden(pair.Key))
                .Select(pair => new Rect2(pair.Value.Position + _touch.GlobalPosition, pair.Value.Size)));
        if (_defensePanel != null)
            occupied.AddRange(_defensePanel.EquippedHud.Buttons.Values.Where(button => button.IsVisibleInTree()).Select(button => button.GetGlobalRect()));
        _hud.KeepRewardNoticeClear(occupied);
    }
}

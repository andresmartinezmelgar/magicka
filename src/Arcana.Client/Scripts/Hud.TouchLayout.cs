using Godot;

namespace Arcana.Client;

public partial class Hud
{
    private Rect2 _touchMapHome;
    internal Rect2? PreferredTouchMap => _mapPanel.Visible ? _touchMapHome : null;
    internal Rect2 TouchPlayerRectangle => _playerPanel.GetGlobalRect();

    private void ResizeTouchHud()
    {
        Vector2 screen = GetViewportRect().Size;
        float s = Math.Min(1, screen.Y / 720f);
        bool defense = _lastSnapshot?.Defense != null;
        _playerPanel.Scale = Vector2.One * s / GameplayScale;
        _playerPanel.Position = new Vector2(22 * s, 18 * s) / GameplayScale;
        _playerPanel.CustomMinimumSize = _playerPanel.Size = new(310, 176);
        // La información central ocupa el espacio entre vida y utilidades incluso en pantallas estrechas.
        var topRow = (HBoxContainer)_topScore.GetParent();
        topRow.OffsetLeft = (332 * s + Arcana.Core.ScreenPlacement.MinimumGap) / GameplayScale;
        topRow.OffsetRight = -(382 * s + Arcana.Core.ScreenPlacement.MinimumGap) / GameplayScale;
        topRow.OffsetTop = Math.Max(8, 12 * s) / GameplayScale;
        topRow.OffsetBottom = 120 * s / GameplayScale;
        foreach (int i in new[] { 0, 2 })
        {
            var spacer = topRow.GetChild<Control>(i);
            spacer.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; spacer.CustomMinimumSize = Vector2.Zero;
        }
        _topScore.CustomMinimumSize = Vector2.Zero; _topScore.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _match.AddThemeFontSizeOverride("font_size", Math.Max(12, (int)(24 * s)));
        _match.CustomMinimumSize = new(0, 42 * s);
        _teams.AddThemeConstantOverride("separation", (int)(18 * s));
        foreach (var label in _teams.GetChildren().OfType<ElementTextLabel>()) label.AddThemeFontSizeOverride("font_size", Math.Max(12, (int)(24 * s)));
        _teams.CustomMinimumSize = new(0, 42 * s);
        _objective.AddThemeFontSizeOverride("font_size", Math.Max(10, (int)(16 * s)));
        _objective.CustomMinimumSize = new(0, 46 * s);
        _mapPanel.Visible = !defense;
        _mapPanel.Scale = Vector2.One * s;
        _mapPanel.Size = _mapPanel.CustomMinimumSize = new(205, 151);
        _touchMapHome = new(new Vector2(screen.X - 235 * GameplayScale * s, 102 * s), new Vector2(205, 151) * GameplayScale * s);
        _spellsPanel.Scale = Vector2.One * s;
        _spellsPanel.CustomMinimumSize = new(320, 0);
        float spellsHeight = _spellsPanel.GetCombinedMinimumSize().Y;
        _spellsPanel.Size = new(320, spellsHeight);
        var spellsSize = _spellsPanel.Size * GameplayScale * s;
        var spellsPosition = new Vector2((screen.X - spellsSize.X) / 2, screen.Y - 100 * s - spellsSize.Y);
        _spellsPanel.Position = spellsPosition / GameplayScale;
        float width = 320 * s;
        _buffs.Size = new(width, 44 * s);
        float rowHeight = 44 * s + Arcana.Core.ScreenPlacement.MinimumGap / GameplayScale;
        _buffs.Position = new((screen.X / GameplayScale - width) / 2, spellsPosition.Y / GameplayScale - rowHeight);
        _buffs.AddThemeFontSizeOverride("font_size", Math.Max(10, (int)(16 * s)));
        _buffs.MaxLinesVisible = 2; _buffs.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _actionNotice.Size = new(width, 44 * s);
        _actionNotice.Position = new(_buffs.Position.X, _buffs.Position.Y - rowHeight);
        _actionNotice.AddThemeFontSizeOverride("font_size", Math.Max(11, (int)(18 * s)));
        _actionNotice.MaxLinesVisible = 2; _actionNotice.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }

    internal void AppendTouchAreas(List<TouchHudArea> areas)
    {
        // Se reserva la altura de avisos y bonos aunque estén vacíos para evitar saltos durante el combate.
        foreach (Control control in new Control[] { _playerPanel, _topScore, _spellsPanel, _actionNotice })
            areas.Add(new(control.Name, control.GetGlobalRect()));
        if (_lastSnapshot?.Defense == null) areas.Add(new(_buffs.Name, _buffs.GetGlobalRect()));
    }

    internal void PlaceTouchMap(Rect2 rect) => _mapPanel.Position = (rect.Position - GlobalPosition) / GameplayScale;
    internal void HideEditorPlayer() => _playerPanel.Hide();
}

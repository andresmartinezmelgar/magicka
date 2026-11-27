using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Hud
{
    private static void IgnoreChildren(Node root)
    {
        foreach (var child in root.GetChildren())
        { if (child is Control c && child is not (Button or ScrollContainer or ScrollBar)) c.MouseFilter = MouseFilterEnum.Ignore; IgnoreChildren(child); }
    }

    private void ResizeLayout()
    {
        Size = GetViewportRect().Size / GameplayScale;
        // El resultado y la clasificación siguen centrados al cambiar la ventana.
        if (_scorePanel == null) return; // Build invoca ResizeLayout antes de crear los hijos.
        float available = Math.Max(240, Size.X - 36);
        float tableWidth = Math.Min(760, available);
        _scorePanel.CustomMinimumSize = new(tableWidth, 0);
        _scorePanel.Size = new Vector2(tableWidth, Math.Max(150, Math.Min(430, Size.Y - 60)));
        // FullRect ajusta el centrador cuando cambia el viewport; no forzar Size.
        _scoreScroll.CustomMinimumSize = new(0, Math.Max(72, Math.Min(300, Size.Y - 145)));
        float messageWidth = Math.Min(800, available);
        float bannerHeight = Math.Min(180, Math.Max(90, Size.Y * 0.42f));
        _bannerBackdrop.CustomMinimumSize = new Vector2(messageWidth, bannerHeight);
        _topScore.CustomMinimumSize = new Vector2(Math.Min(690, available), 0);
        _objective.CustomMinimumSize = new Vector2(0, Size.X < 800 ? 76 : 46);
        _server.Size = new(Math.Max(70, Math.Min(300, (Size.X - 60) / 3)), 58);
        _controls.Visible = !MobileLayout && Size.X >= 1250;
        _controls.Position = new(Size.X - 440, 24);
        float feedWidth = Math.Min(420, available);
        _feed.Position = new(Size.X - feedWidth - 30, Math.Min(245, Math.Max(80, Size.Y - 110)));
        _feed.Size = new(feedWidth, 100);
        _mapPanel.Visible = Size.X >= (MobileLayout ? 850 : 700);
        _mapPanel.Position = MobileLayout ? new(Size.X - 235, 136) : new(Size.X - 235, 72);
        float spellsWidth = Math.Min(MobileLayout ? 320 : 690, Math.Max(100, Size.X - 36));
        float spellsHeight = Math.Max(54, _spellsPanel.GetCombinedMinimumSize().Y);
        float touchScale = Math.Min(1, Size.Y * GameplayScale / 720f);
        float spellsBottom = MobileLayout ? 100 * touchScale / GameplayScale : 26 / GameplayScale;
        float spellsLeft = (Size.X - spellsWidth) / 2;
        if (!MobileLayout && Size.X >= 1250)
            spellsLeft = Math.Max(spellsLeft, 345 / GameplayScale);
        float spellsTop = Math.Max(120, Size.Y - spellsHeight - spellsBottom);
        _spellsPanel.Position = new(spellsLeft, spellsTop);
        _spellsPanel.Size = new(spellsWidth, spellsHeight);
        float buffsWidth = Math.Min(690, available);
        _buffs.Position = new((Size.X - buffsWidth) / 2, Math.Max(100, spellsTop - 52));
        _buffs.Size = new(buffsWidth, 44);
        _actionNotice.Position = new((Size.X - buffsWidth) / 2, Math.Max(100, spellsTop - (_buffs.Visible ? 120 : 72)));
        _actionNotice.Size = new(buffsWidth, 60);
        if (!MobileLayout)
            _playerPanel.Position = Size.X < 1250
                ? new Vector2(24, 145) / GameplayScale
                : new Vector2(24, Size.Y * GameplayScale - 282) / GameplayScale;
        else ResizeTouchHud();
        PlaceRewardNotice();
    }

    private void PlaceRewardNotice()
    {
        // Medir después de fijar el ancho: dos líneas fijas cortaban nombres,
        // afijos y cantidades. La tarjeta crece con su contenido real.
        float s = MobileLayout || _lastSnapshot?.Defense != null ? Math.Min(1, GetViewportRect().Size.Y / 720f) / GameplayScale : 1;
        _rewardBackdrop.Scale = Vector2.One * s;
        float cardWidth = Math.Min(310, Math.Max(1, (Size.X - 16) / s));
        _rewardBackdrop.CustomMinimumSize = new(cardWidth, 0);
        _rewardBackdrop.Size = new(cardWidth, 62);
        float cardHeight = Math.Max(62, _reward.GetMinimumSize().Y + 16);
        _rewardBackdrop.CustomMinimumSize = _rewardBackdrop.Size = new(cardWidth, cardHeight);
        var player = _playerPanel.GetGlobalRect();
        var status = _rewardStatusBounds ?? player;
        var cardSize = _rewardBackdrop.Size * s * GameplayScale;
        var wanted = new ScreenRect(status.Position.X, Math.Max(status.End.Y, player.End.Y) + ScreenPlacement.MinimumGap,
            cardSize.X, cardSize.Y);
        var occupied = new List<ScreenRect> { TouchHudLayout.Rect(status) };
        foreach (Control control in new Control[] { _playerPanel, _topScore, _spellsPanel, _mapPanel, _actionNotice, _buffs })
            if (control.IsVisibleInTree()) occupied.Add(TouchHudLayout.Rect(control.GetGlobalRect()));
        // Buscar también a los lados cuando no cabe debajo ni encima. Limitar
        // las coordenadas al viewport sin buscar hueco volvía a solapar el panel.
        _rewardFits = ScreenPlacement.TryPlace(wanted, TouchHudLayout.Rect(GetViewportRect()), occupied, out var safe);
        if (_rewardFits) _rewardBackdrop.GlobalPosition = new(safe.X, safe.Y);
        UpdateRewardVisibility();
    }

    internal void KeepRewardNoticeClear(IReadOnlyList<Rect2> obstacles)
    {
        if (!RewardNoticePending) return;
        float height = Math.Max(62, _reward.GetMinimumSize().Y + 16);
        if (!Mathf.IsEqualApprox(height, _rewardBackdrop.Size.Y))
            _rewardBackdrop.CustomMinimumSize = _rewardBackdrop.Size = new(_rewardBackdrop.Size.X, height);
        _rewardFits = ScreenPlacement.TryPlace(TouchHudLayout.Rect(_rewardBackdrop.GetGlobalRect()),
            TouchHudLayout.Rect(GetViewportRect()), obstacles.Select(TouchHudLayout.Rect).ToArray(), out var safe);
        if (_rewardFits) _rewardBackdrop.GlobalPosition = new(safe.X, safe.Y);
        UpdateRewardVisibility();
    }

    private static ProgressBar Bar(Color color, float max, int height)
    {
        var bar = new ProgressBar { MaxValue = max, ShowPercentage = false, CustomMinimumSize = new(0, height) };
        var track = Ui.Panel(new("101a2b"), 4, color.Darkened(0.45f));
        var fill = Ui.Panel(color, 4, color.Lightened(0.18f));
        foreach (var style in new[] { track, fill })
            style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 0;
        bar.AddThemeStyleboxOverride("background", track); bar.AddThemeStyleboxOverride("fill", fill); return bar;
    }

    private static Control Meter(ProgressBar bar, string textName, out ElementTextLabel label, int fontSize = 14)
    {
        var root = new Control { CustomMinimumSize = bar.CustomMinimumSize, MouseFilter = MouseFilterEnum.Ignore };
        root.AddChild(bar); Ui.Full(bar);
        label = Ui.Label("", fontSize); label.Name = textName;
        label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeConstantOverride("outline_size", 4); root.AddChild(label); Ui.Full(label);
        return root;
    }
}

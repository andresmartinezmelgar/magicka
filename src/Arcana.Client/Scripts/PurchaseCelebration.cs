using Godot;

namespace Arcana.Client;

/// <summary>Una celebración breve y sin captura de gestos para una compra ya confirmada.</summary>
internal partial class PurchaseCelebration : Control
{
    private const float Duration = 1.65f;
    private readonly Vector2[] _star = new Vector2[10];
    private PanelContainer _card = null!;
    private Color _accent;
    private float _elapsed;

    internal static void Play(Control owner, string itemName, Color accent)
    {
        if (owner.GetNodeOrNull<PurchaseCelebration>("PurchaseCelebration") is { } previous)
        { owner.RemoveChild(previous); previous.QueueFree(); }
        var effect = new PurchaseCelebration { Name = "PurchaseCelebration", MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new(1, 1, 1, 0) };
        owner.AddChild(effect); Ui.Full(effect); effect.Build(itemName, accent);
    }

    private void Build(string itemName, Color accent)
    {
        _accent = accent;
        _card = new PanelContainer { Name = "PurchaseCelebrationCard", MouseFilter = MouseFilterEnum.Ignore };
        var style = Ui.Panel(new("10253c"), 14, accent);
        style.BorderWidthTop = style.BorderWidthBottom = style.BorderWidthLeft = style.BorderWidthRight = 2;
        style.ContentMarginLeft = style.ContentMarginRight = 20;
        style.ContentMarginTop = style.ContentMarginBottom = 14;
        _card.AddThemeStyleboxOverride("panel", style); AddChild(_card);
        var body = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 5); _card.AddChild(body);
        foreach (var line in new[] { ("COMPRA COMPLETADA", 21, Ui.Gold), (itemName, 16, Ui.Text) })
        {
            var label = Ui.Label(line.Item1, line.Item2, line.Item3);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.MouseFilter = MouseFilterEnum.Ignore; body.AddChild(label);
        }
        Resized += LayoutCard; LayoutCard();
    }

    private void LayoutCard()
    {
        if (_card == null) return;
        float width = Math.Min(460, Math.Max(1, Size.X - 48));
        _card.CustomMinimumSize = new(width, 0); _card.Size = new(width, 0);
        _card.Position = new((Size.X - width) / 2, Math.Clamp(Size.Y * .24f, 80, 200));
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed >= Duration) { SetProcess(false); QueueFree(); return; }
        float fade = Math.Min(1, _elapsed / .12f) * Math.Min(1, (Duration - _elapsed) / .35f);
        Modulate = new(1, 1, 1, fade); QueueRedraw();
    }

    public override void _Draw()
    {
        if (_card == null) return;
        float progress = Math.Clamp(_elapsed / Duration, 0, 1);
        float spread = 70 * (1 - (1 - progress) * (1 - progress));
        var center = _card.Position + _card.Size / 2;
        for (int i = 0; i < 18; i++)
        {
            float angle = i * MathF.Tau / 18;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var point = center + direction * (_card.Size / 2 + Vector2.One * (10 + spread));
            point.Y += 24 * progress * progress;
            float size = (4 + i % 3 * 1.5f) * (.8f + .2f * MathF.Sin(_elapsed * 12 + i));
            float rotation = angle + progress * (i % 2 == 0 ? 2 : -2);
            for (int vertex = 0; vertex < _star.Length; vertex++)
            {
                float phase = rotation + vertex * MathF.Tau / _star.Length;
                float radius = size * (vertex % 2 == 0 ? 1 : .42f);
                _star[vertex] = point + new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * radius;
            }
            DrawColoredPolygon(_star, i % 3 == 0 ? Colors.White : i % 3 == 1 ? Ui.Gold : _accent);
        }
    }
}

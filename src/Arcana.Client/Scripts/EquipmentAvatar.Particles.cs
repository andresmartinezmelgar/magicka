using Godot;

namespace Arcana.Client;

public partial class EquipmentAvatar
{
    private const int MotesPerQualityPoint = 4, MaximumMotes = 120;
    private readonly Color[] _moteColors = new Color[MaximumMotes];
    private GradientTexture2D? _moteTexture;
    private Gradient? _moteGradient;
    private int _moteCount;
    private double _moteClock, _moteRedraw;

    private void RefreshAuraParticles()
    {
        _moteCount = 0;
        // Solo cuentan las piezas equipadas: Plata=1 ... Mítico=5.
        // Cada pieza aporta su color al mismo campo de partículas del mago.
        foreach (var slot in Order)
        {
            if (!_halos.TryGetValue(slot, out var item)) continue;
            int particles = ((int)item.Tier + 1) * MotesPerQualityPoint;
            var color = EquipmentPanel.TierColor(item.Tier);
            for (int i = 0; i < particles && _moteCount < MaximumMotes; i++)
                _moteColors[_moteCount++] = color;
        }
        RefreshMoteProcessing();
    }

    private void RefreshMoteProcessing() => SetProcess(_moteCount > 0 && IsVisibleInTree());

    public override void _Process(double delta)
    {
        _moteClock += Math.Min(delta, .1);
        _moteRedraw += delta;
        if (_moteRedraw < 1.0 / 24) return;
        _moteRedraw %= 1.0 / 24;
        QueueRedraw();
    }

    private void DrawAuraParticles()
    {
        if (_moteCount == 0) return;
        if (_moteTexture == null)
        {
            _moteGradient = new Gradient
            {
                Offsets = [0, .16f, .45f, 1],
                Colors = [new(1, 1, 1, .9f), new(1, 1, 1, .5f), new(1, 1, 1, .12f), new(1, 1, 1, 0)]
            };
            _moteTexture = new GradientTexture2D { Gradient = _moteGradient, Width = 16, Height = 16,
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new(.5f, .5f), FillTo = new(1, .5f) };
        }
        var bounds = SpriteBounds();
        var center = bounds.Position + bounds.Size * new Vector2(.5f, .53f);
        var radii = bounds.Size * new Vector2(.40f, .47f);
        float time = (float)_moteClock, scale = Math.Clamp(bounds.Size.Y / 280, .65f, 1.3f);
        for (int i = 0; i < _moteCount; i++)
        {
            // Semillas estables: cambiar de selección no vuelve a sortear el efecto.
            float seed = i * 2.399963f;
            float cycle = time * .14f + i * .618034f;
            float phase = cycle - Mathf.Floor(cycle);
            float fade = Mathf.Sin(phase * Mathf.Pi);
            fade *= fade;
            float angle = seed + time * .13f;
            float drift = .98f + .16f * Mathf.Sin(seed * 1.7f + time * .4f);
            var position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radii * drift;
            position.Y -= Mathf.Sin(seed + time * .55f) * bounds.Size.Y * .018f;
            float radius = scale * (2 + .6f * Mathf.Sin(seed * 2.3f));
            var color = new Color(_moteColors[i], fade * (.42f + .16f * Mathf.Sin(seed)));
            DrawTextureRect(_moteTexture, new Rect2(position - Vector2.One * radius, Vector2.One * radius * 2), false, color);
        }
    }

    private void DisposeAuraParticles()
    {
        VisibilityChanged -= RefreshMoteProcessing;
        _moteTexture?.Dispose(); _moteTexture = null;
        _moteGradient?.Dispose(); _moteGradient = null;
    }
}

using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Radio estable; el parpadeo de una mina nunca oculta su área de efecto o activación.</summary>
public partial class AreaFieldVisual : Node3D
{
    private MeshInstance3D? _trigger;
    private readonly Sprite3D?[] _approvedAreaArt = new Sprite3D?[2];
    internal AreaVisuals.Pattern Pattern { get; private set; }
    internal float Radius { get; private set; }
    internal float TriggerRadius { get; private set; }

    internal void Build(FieldSnapshot field)
    {
        Position = ArenaRenderer.World(field.Position, 0.055f); Radius = field.Radius; TriggerRadius = field.TriggerRadius;
        Pattern = AreaVisuals.GroundPattern(field.Kind);
        bool mine = field.Kind is "mine" or "healing_mine";
        Color? tint = mine ? field.Kind == "healing_mine" ? Ui.Health : Ui.SpellColor(field.Elements) : null;
        var area = AreaVisuals.Piece(this, Pattern, tint); area.Name = "AreaBoundary";
        area.Scale = new(Radius, 1, Radius);
        if (mine)
        {
            // Flat, concentric rings on the actual ground. Unlike a billboard, the
            // sprite does not float in front of the mage or hide the playfield.
            int layers = ArcanaSpriteAtlas.AffinityLayers(field.Elements);
            for (int i = 0; i < layers; i++)
            {
                var texture = ArcanaSpriteAtlas.Defense(
                    ArcanaSpriteAtlas.DefenseForm.Area, field.Elements, i);
                if (texture == null) continue;
                var sprite = new Sprite3D
                {
                    Name = "AreaRuneVisual" + i,
                    Texture = texture,
                    Position = new(0, 0.032f + i * 0.012f, 0),
                    RotationDegrees = new(-90, 0, 0),
                    PixelSize = field.Radius * 1.86f / 160f,
                    Shaded = false,
                    Modulate = new Color(1, 1, 1, i == 0 ? 0.68f : 0.38f)
                };
                AddChild(sprite);
                _approvedAreaArt[i] = sprite;
            }
            _trigger = AreaVisuals.Piece(this, field.Kind == "healing_mine" ? AreaVisuals.Pattern.HealingTrigger : AreaVisuals.Pattern.MineTrigger, tint);
            _trigger.Name = "MineCore"; _trigger.Position = new(0, 0.025f, 0);
            _trigger.Scale = new(TriggerRadius, 0.8f + SpellVisualStrength.For(field.Elements).Charges * 0.2f, TriggerRadius);
        }
    }
    internal void Update(FieldSnapshot field, float time)
    {
        Visible = field.Lifetime > 0;
        // El núcleo se eleva al armarse; ambos radios siguen visibles durante la preparación.
        if (_trigger != null) _trigger.Position = new(0, field.Armed ? 0.055f : 0.025f + MathF.Sin(time * 8) * 0.015f, 0);
        for (int i = 0; i < _approvedAreaArt.Length; i++)
        {
            if (_approvedAreaArt[i] is not { } sprite) continue;
            sprite.Visible = field.Lifetime > 0;
            sprite.Modulate = new Color(1, 1, 1,
                (i == 0 ? 0.68f : 0.38f) * (field.Armed ? 1f : 0.55f));
        }
    }
}

using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Radio estable; el parpadeo de una mina nunca oculta su área de efecto o activación.</summary>
public partial class AreaFieldVisual : Node3D
{
    private MeshInstance3D? _trigger;
    private Sprite3D? _approvedMineArt;
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
            Texture2D? image = ArcanaSpriteAtlas.Mine(field.Elements, field.Kind == "healing_mine");
            if (image != null)
            {
                _approvedMineArt = new Sprite3D
                {
                    Name = "GeneratedMineArtwork",
                    Texture = image,
                    Position = new(0, 0.20f, 0),
                    PixelSize = Math.Min(field.Radius * 0.65f, 1.65f) / 96f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    Shaded = false, Modulate = new Color(1, 1, 1, 0.58f)
                };
                AddChild(_approvedMineArt);
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
        if (_approvedMineArt != null)
        {
            _approvedMineArt.Visible = field.Lifetime > 0;
            _approvedMineArt.Modulate = new Color(1, 1, 1, field.Armed ? 0.62f : 0.34f);
            _approvedMineArt.Position = new(0, 0.20f + MathF.Sin(time * 2.5f) * 0.04f, 0);
        }
    }
}

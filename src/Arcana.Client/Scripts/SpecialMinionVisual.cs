using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Un aura eléctrica compartida y una defensa frontal de coste acotado; se reutiliza con el minion.</summary>
public partial class SpecialMinionVisual : Node3D
{
    private Node3D _electricity = null!, _shield = null!;
    private MeshInstance3D[] _arcs = [];
    private DefenseSpecialAbility _lastAbility;
    internal bool ShieldVisible => _shield.Visible;
    internal static Color BodyColor(DefenseSpecialAbility ability) =>
        new(ability == DefenseSpecialAbility.FrontalShield ? "263047" : "432342");
    private static Color AuraColor(DefenseSpecialAbility ability) =>
        new(ability == DefenseSpecialAbility.FrontalShield ? "6ddfff" : "e9a0ff");

    internal void Build(MinionKind kind)
    {
        Name = "SpecialMinionEffects";
        float radius = kind == MinionKind.Boss ? 1.6f : kind is MinionKind.Brute or MinionKind.Tank ? 1.2f : .9f;
        float height = kind == MinionKind.Boss ? 3.3f : MinionArt.CasterType(kind) != null ? 2.4f
            : Math.Max(1.3f, MinionArt.BodyHeight(kind) * 2);
        _electricity = new Node3D { Name = "SpecialElectricity" }; AddChild(_electricity);
        _arcs = new MeshInstance3D[3];
        for (int i = 0; i < _arcs.Length; i++)
        {
            var arc = AreaVisuals.Piece(_electricity, AreaVisuals.Pattern.LightningAura);
            arc.Name = "SpecialElectricArc" + i;
            arc.Scale = new(radius, 1, i == 0 ? radius : height * .55f);
            arc.Position = new(0, i == 0 ? .08f : height * .5f, 0);
            if (i > 0) arc.Rotation = new(MathF.PI / 2, (i - 1) * MathF.PI / 2, 0);
            _arcs[i] = arc;
        }
        _shield = new Node3D { Name = "SpecialFrontalShield", Visible = false }; AddChild(_shield);
        var skin = new StandardMaterial3D
        {
            AlbedoColor = new Color(.3f, .8f, 1, .32f), EmissionEnabled = true, Emission = new("6ddfff"),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        for (int i = 0; i < 5; i++)
        {
            float angle = (i - 2) * MathF.PI * 2 / 15;
            _shield.AddChild(new MeshInstance3D
            {
                Name = "FrontalShieldPane" + i,
                Mesh = new BoxMesh { Size = new(2 * (radius + .2f) * MathF.Tan(MathF.PI / 15), height * .75f, .055f) },
                Position = new(MathF.Sin(angle) * (radius + .2f), height * .5f, MathF.Cos(angle) * (radius + .2f)),
                Rotation = new(0, angle, 0), MaterialOverride = skin,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }
        Visible = false;
    }

    internal void Update(MinionSnapshot minion, float time)
    {
        Visible = minion.SpecialAbility != DefenseSpecialAbility.None;
        if (!Visible) { _shield.Hide(); return; }
        if (_lastAbility != minion.SpecialAbility)
        {
            foreach (var arc in _arcs) arc.MaterialOverride = AreaVisuals.Skin(AuraColor(minion.SpecialAbility));
            _lastAbility = minion.SpecialAbility;
        }
        _electricity.Rotation = new(0, time * .65f, 0);
        _shield.Visible = minion.SpecialAbility == DefenseSpecialAbility.FrontalShield &&
            minion.FrontalShieldSeconds > 0 && minion.Frozen <= 0 && !minion.Stunned;
        _shield.Rotation = new(0, MathF.Atan2(minion.Facing.X, minion.Facing.Y), 0);
    }
}

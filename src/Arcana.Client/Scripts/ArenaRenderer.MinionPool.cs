using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private static int PreparedMinionCount(MinionKind kind) => kind switch
    { MinionKind.Melee => 64, MinionKind.Archer => 24, MinionKind.Mage or MinionKind.Tank => 16,
        MinionKind.Captain or MinionKind.Shaman => 10, MinionKind.Warlock or MinionKind.Cryomancer => 12,
        MinionKind.Sentinel => 8, MinionKind.Boss => 2, MinionKind.Runner => 24, MinionKind.Prism => 8, MinionKind.Oracle => 2,
        MinionKind.Brute => 12, MinionKind.Bomber => 14, _ => 0 };

    private void ReturnMinionVisual(MinionVisual visual)
    {
        visual.Root.Visible = false;
        var pool = _freeMinions[(int)visual.Kind];
        if (pool.Count >= PreparedMinionCount(visual.Kind) * 2)
        { visual.Root.QueueFree(); _minionCreated[(int)visual.Kind]--; }
        else pool.Push(visual);
    }

    private MinionVisual CreateMinionVisual(MinionSnapshot m)
    {
        var root = new Node3D { Name = "Minion" + m.Id, Position = World(m.Position) }; AddChild(root);
        var v = new MinionVisual { Root = root, Kind = m.Kind };
        if (MinionArt.CasterType(m.Kind) != null)
        {
            v.Mage = new WizardVisual(); root.AddChild(v.Mage); v.Mage.Build(MinionWizard(m), false, compactEnemy: true);
            v.Mage.Position = Vector3.Zero;
        }
        else
        {
            if (!_minionShapes.TryGetValue(m.Kind, out var shape))
            {
                shape = MinionArt.Shape(m.Kind);
                _minionShapes[m.Kind] = shape;
            }
            float height = MinionArt.BodyHeight(m.Kind);
            float healthHeight = m.Kind == MinionKind.Boss ? 4.05f : m.Kind == MinionKind.Brute ? 2.25f : m.Kind == MinionKind.Sentinel ? 2.65f : m.Kind == MinionKind.Tank ? 1.65f : m.Kind == MinionKind.Prism ? 2.05f : m.Kind == MinionKind.Oracle ? 3.15f : 1.3f;
            v.Body = Mesh(root, shape, new(0, height, 0), MinionColor(m.Kind));
            var details = MinionArt.Decorate(root, m.Kind, m.VulnerableElement, color => Material(color));
            v.Details = details.Root; v.Tinted = details.Tinted;
            v.Vitals = new Node3D { Name = "EnemyVitals", Position = new(0, healthHeight, 0) }; root.AddChild(v.Vitals);
            var healthTrack = Mesh(v.Vitals, new BoxMesh { Size = new(1.3f, 0.1f, 0.025f) }, Vector3.Zero, new("17232d"), unshaded: true);
            v.Health = Mesh(v.Vitals, new BoxMesh { Size = new(1.25f, 0.08f, 0.04f) }, new(0, 0, 0.01f), Ui.Red, unshaded: true);
            v.ShieldTrack = Mesh(v.Vitals, new BoxMesh { Size = new(1.3f, 0.12f, 0.025f) }, new(0, 0.17f, 0), new("132a3b"), unshaded: true);
            v.Shield = Mesh(v.Vitals, new BoxMesh { Size = new(1.25f, 0.08f, 0.04f) }, new(0, 0.17f, 0.01f), Ui.EnemyShield, unshaded: true);
            v.Shield.Name = "EnemyShield"; v.ShieldTrack.Name = "EnemyShieldTrack";
            v.Shield.Visible = v.ShieldTrack.Visible = m.Shield > 0;
            foreach (var bar in new[] { healthTrack, v.Health, v.ShieldTrack, v.Shield }) bar.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        if (m.Kind == MinionKind.Prism)
        {
            v.ElementLabel = new Label3D { Name = "PrismElement", Position = new(0, 2.3f, 0), FontSize = 24,
                PixelSize = 0.012f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
            root.AddChild(v.ElementLabel);
        }
        if (m.Kind == MinionKind.Oracle)
        {
            v.SupportArea = AreaVisuals.Piece(root, AreaVisuals.Pattern.ShieldAura, new("8dbdff"));
            v.SupportArea.Name = "OracleShieldArea"; v.SupportArea.Position = new(0, 0.07f, 0);
            v.SupportArea.Scale = new(DefenseRules.OracleAuraRadius, 1, DefenseRules.OracleAuraRadius);
        }
        if (v.Mage == null)
        {
            v.ArcaneMark = AreaVisuals.Piece(root, AreaVisuals.Pattern.ArcaneAura);
            v.ArcaneMark.Name = "ArcaneMark"; v.ArcaneMark.Position = new(0, .08f, 0);
            float size = m.Kind == MinionKind.Boss ? 1.5f : .8f;
            v.ArcaneMark.Scale = new(size, 1, size); v.ArcaneMark.Visible = m.ArcaneMarked;
            v.LightningStun = AreaVisuals.Piece(root, AreaVisuals.Pattern.LightningAura);
            v.LightningStun.Name = "LightningStun"; v.LightningStun.Position = new(0, v.Vitals!.Position.Y + .4f, 0);
            v.LightningStun.Scale = new(.35f * size, 1, .35f * size); v.LightningStun.Visible = m.Stunned;
        }
        v.Aura = Mesh(root, new TorusMesh { InnerRadius = 0.88f, OuterRadius = 0.97f, Rings = 20, RingSegments = 6 },
            new(0, 0.06f, 0), Ui.Gold, 0.5f);
        if (DefenseRules.AreaAttackRadius(m.Kind) is > 0 and var radius)
        {
            v.Warning = new EnemyAttackWarning();
            root.AddChild(v.Warning);
            v.Warning.Build(radius);
        }
        _minionCreated[(int)m.Kind]++; DefenseMinionBuilds++;
        return v;
    }

    private IEnumerable<(string Name, Action Work)> PrepareDefenseMinions(V2 center)
    {
        foreach (var kind in Enum.GetValues<MinionKind>())
            for (int batch = 0; batch < PreparedMinionCount(kind); batch += 8)
                yield return ("Preparando la horda y sus animaciones…", () =>
                {
                    foreach (var pool in _freeMinions) foreach (var visual in pool) visual.Root.Visible = false;
                    int count = Math.Min(8, PreparedMinionCount(kind) - _minionCreated[(int)kind]);
                    for (int n = 0; n < count; n++)
                    {
                        var sample = new MinionSnapshot(8000 + n, kind, center + new V2((n % 4 - 1.5f) * 3, n / 4 * 3),
                            new(1, 0), 1000, 1000, 0, 0, 0, 0, false, [], false);
                        var visual = CreateMinionVisual(sample);
                        visual.Mage?.Update(MinionWizard(sample), 1 / 60f, 0, Camera, 0, 0);
                        if (visual.Mage != null) visual.Mage.Position = Vector3.Zero;
                        _freeMinions[(int)kind].Push(visual);
                    }
                    FocusLoadingView(center);
                });
    }
}

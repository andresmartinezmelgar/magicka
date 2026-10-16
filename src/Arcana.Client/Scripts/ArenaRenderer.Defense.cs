using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private sealed class MinionVisual
    {
        public required Node3D Root;
        public MeshInstance3D? Body, Health, Aura;
        public WizardVisual? Mage;
    }
    private readonly Dictionary<int, MinionVisual> _minionVisuals = [];
    private readonly Dictionary<int, Node3D> _defenseLootVisuals = [];
    private readonly Dictionary<(int Player, int Aura), (MeshInstance3D Mesh, TorusMesh Ring)> _defenseAuraVisuals = [];
    internal int DefenseMinionVisualCount => _minionVisuals.Count;
    private Node3D? _castleVisual;
    private Label3D? _castleLabel;

    private void ClearDefenseVisuals()
    {
        foreach (var v in _minionVisuals.Values) v.Root.QueueFree(); _minionVisuals.Clear();
        foreach (var v in _defenseLootVisuals.Values) v.QueueFree(); _defenseLootVisuals.Clear();
        foreach (var v in _defenseAuraVisuals.Values) v.Mesh.QueueFree(); _defenseAuraVisuals.Clear();
        _castleVisual?.QueueFree(); _castleVisual = null; _castleLabel = null;
    }

    private static WizardSnapshot MinionWizard(MinionSnapshot m) => new(m.Id, "Mago invasor", 1, true, m.Position,
        m.Facing, m.Health, m.MaxHealth, 0, [], [], true, 0, m.Wet, m.Burning, m.Chill, m.Frozen, 0,
        100, [0, 0, 0, 0], 0, 0, 0, m.Casting, WizardType: WizardType.Pyromancer, CastingElements: m.Elements);

    private void RenderDefense(WorldSnapshot snapshot, int localId, float dt)
    {
        if (snapshot.Defense is not { } defense) return;
        if (_castleVisual == null)
        {
            _castleVisual = new Node3D { Name = "DefenseCastle", Position = World(defense.CastlePosition) }; AddChild(_castleVisual);
            Mesh(_castleVisual, new BoxMesh { Size = new(5.3f, 0.6f, 5.3f) }, new(0, 0.3f, 0), new("526579"));
            Mesh(_castleVisual, new BoxMesh { Size = new(3.8f, 3, 3.8f) }, new(0, 2, 0), new("8194a4"));
            Mesh(_castleVisual, new BoxMesh { Size = new(1, 1.8f, 0.2f) }, new(0, 1.2f, 1.98f), new("18283b"));
            foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                Mesh(_castleVisual, new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.8f, Height = 4.4f, RadialSegments = 10 }, new(x * 1.9f, 2.3f, z * 1.9f), new("607b94"));
                Mesh(_castleVisual, new CylinderMesh { TopRadius = 0, BottomRadius = 0.95f, Height = 1.2f, RadialSegments = 8 }, new(x * 1.9f, 5.1f, z * 1.9f), Ui.Blue);
            }
            _castleLabel = new Label3D { Position = new(0, 6.3f, 0), FontSize = 38, PixelSize = 0.018f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 10, OutlineModulate = new("070c15"), NoDepthTest = true };
            _castleVisual.AddChild(_castleLabel);
        }
        _castleLabel!.Text = $"BASTIÓN\n{Math.Ceiling(defense.CastleHealth):0} / {defense.CastleMaxHealth:0}";
        _castleLabel.Modulate = defense.CastleHealth < defense.CastleMaxHealth * 0.3f ? Ui.Red : Ui.Blue;
        foreach (var m in defense.Minions)
        {
            if (!_minionVisuals.TryGetValue(m.Id, out var v))
            {
                var root = new Node3D { Name = "Minion" + m.Id, Position = World(m.Position) }; AddChild(root);
                v = new() { Root = root };
                if (m.Kind == MinionKind.Mage)
                {
                    v.Mage = new WizardVisual(); root.AddChild(v.Mage); v.Mage.Build(MinionWizard(m), false);
                    v.Mage.Position = Vector3.Zero;
                }
                else
                {
                    Godot.Mesh shape = m.Kind == MinionKind.Tank ? new BoxMesh { Size = new(1.6f, 1.45f, 1.6f) } : m.Kind == MinionKind.Archer ? new BoxMesh { Size = new(0.95f, 0.7f, 0.95f) }
                        : new CylinderMesh { TopRadius = m.Kind == MinionKind.Captain ? 0.8f : 0.55f,
                            BottomRadius = m.Kind == MinionKind.Captain ? 0.8f : 0.55f, Height = 0.65f, RadialSegments = 16 };
                    v.Body = Mesh(root, shape, new(0, m.Kind == MinionKind.Tank ? 0.8f : 0.5f, 0), MinionColor(m.Kind));
                    Mesh(root, new BoxMesh { Size = new(1.3f, 0.1f, 0.12f) }, new(0, m.Kind == MinionKind.Tank ? 1.65f : 1.3f, 0), new("17232d"));
                    v.Health = Mesh(root, new BoxMesh { Size = new(1.25f, 0.12f, 0.13f) }, new(0, m.Kind == MinionKind.Tank ? 1.65f : 1.3f, 0.01f), Ui.Red);
                }
                v.Aura = Mesh(root, new TorusMesh { InnerRadius = 0.88f, OuterRadius = 0.97f, Rings = 20, RingSegments = 6 },
                    new(0, 0.06f, 0), Ui.Gold, 0.5f);
                _minionVisuals[m.Id] = v;
            }
            v.Root.Position = v.Root.Position.Lerp(World(m.Position), Math.Min(1, dt * 18));
            if (v.Mage != null)
            {
                v.Mage.Update(MinionWizard(m), dt, (float)_time, Camera, snapshot.Time, InterpolationDelay);
                v.Mage.Position -= World(m.Position); // La raíz ya está en coordenadas del mundo.
            }
            if (v.Health != null) v.Health.Scale = new(Math.Max(0.001f, m.Health / m.MaxHealth), 1, 1);
            if (v.Body != null) v.Body.MaterialOverride = Material(m.Frozen > 0 ? new("8bdeff") : m.Burning > 0 ? Ui.ElementColor(Element.Fire) : MinionColor(m.Kind));
            v.Aura!.Visible = m.Empowered || m.Kind == MinionKind.Captain;
        }
        foreach (int id in _minionVisuals.Keys.Where(id => !defense.Minions.Any(m => m.Id == id)).ToArray())
        { _minionVisuals[id].Root.QueueFree(); _minionVisuals.Remove(id); }
        foreach (var drop in defense.Drops)
        {
            if (drop.OwnerId != 0 && drop.OwnerId != localId) continue;
            if (!_defenseLootVisuals.TryGetValue(drop.Id, out var visual))
            {
                visual = new Node3D { Name = "DefenseDrop" + drop.Id, Position = World(drop.Position) + new Vector3(0, 0.35f, 0) }; AddChild(visual);
                var color = drop.Kind == DefenseDropKind.Gold ? Ui.Gold : drop.Kind == DefenseDropKind.Relic ? RelicVisual.ColorFor(drop.Buff) : new Color("b6a1ff");
                Mesh(visual, drop.Kind == DefenseDropKind.Gold ? new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.22f, Height = 0.15f }
                    : new BoxMesh { Size = new(0.45f, 0.45f, 0.45f) }, Vector3.Zero, color, 0.6f);
                _defenseLootVisuals[drop.Id] = visual;
            }
            visual.Rotation = new(0, (float)_time, 0);
        }
        foreach (int id in _defenseLootVisuals.Keys.Where(id => !defense.Drops.Any(d => d.Id == id)).ToArray())
        { _defenseLootVisuals[id].QueueFree(); _defenseLootVisuals.Remove(id); }
        foreach (var defender in defense.Defenders)
        {
            var player = snapshot.Players.FirstOrDefault(p => p.Id == defender.PlayerId);
            if (player == null) continue;
            for (int i = 0; i < 4; i++)
            {
                var key = (defender.PlayerId, i);
                if (!_defenseAuraVisuals.TryGetValue(key, out var aura))
                {
                    var ring = new TorusMesh { Rings = 48, RingSegments = 5 };
                    var mesh = Mesh(this, ring, Vector3.Zero, i == 0 ? Ui.ElementColor(Element.Fire) : i == 1 ? Ui.ElementColor(Element.Lightning) : i == 2 ? Ui.Health : Ui.Gold, 0.5f);
                    aura = (mesh, ring); _defenseAuraVisuals[key] = aura;
                }
                int level = i == 3 ? defender.BerserkerRemaining > 0 ? 1 : 0 : defender.Auras[i];
                aura.Mesh.Visible = player.Alive && level > 0;
                if (level > 0)
                {
                    float radius = i == 3 ? 1.6f : DefenseRules.AuraRadius(level);
                    if (Math.Abs(aura.Ring.OuterRadius - radius) > 0.01f)
                    { aura.Ring.InnerRadius = radius - 0.08f; aura.Ring.OuterRadius = radius; }
                    aura.Mesh.Position = (_wizards.TryGetValue(player.Id, out var wizard) ? wizard.Position : World(player.Position)) + new Vector3(0, 0.08f + i * 0.03f, 0);
                }
            }
        }
        foreach (var key in _defenseAuraVisuals.Keys.Where(k => !defense.Defenders.Any(d => d.PlayerId == k.Player)).ToArray())
        { _defenseAuraVisuals[key].Mesh.QueueFree(); _defenseAuraVisuals.Remove(key); }
    }

    private static Color MinionColor(MinionKind kind) => kind switch
    { MinionKind.Tank => new("5c9d91"), MinionKind.Archer => new("d6a36b"), MinionKind.Captain => new("a36de0"), _ => new("db636b") };
}

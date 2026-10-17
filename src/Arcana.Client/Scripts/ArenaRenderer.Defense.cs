using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private sealed class MinionVisual
    {
        public required Node3D Root;
        public MinionKind Kind;
        public MeshInstance3D? Body, Health, Aura, Warning;
        public WizardVisual? Mage;
    }
    private readonly Dictionary<int, MinionVisual> _minionVisuals = [];
    private readonly Stack<MinionVisual>[] _freeMinions = Enum.GetValues<MinionKind>().Select(_ => new Stack<MinionVisual>()).ToArray();
    private readonly int[] _minionCreated = new int[Enum.GetValues<MinionKind>().Length];
    private readonly Dictionary<MinionKind, Godot.Mesh> _minionShapes = [];
    private readonly HashSet<int> _visibleMinionIds = [];
    private readonly List<int> _missingMinionIds = [];
    internal int DefenseMinionBuilds { get; private set; }
    internal int PreparedMinionCapacity => _minionCreated.Sum();
    private readonly Dictionary<int, DefenseLootVisual> _defenseLootVisuals = [];
    private readonly Dictionary<(int Player, int Aura), (MeshInstance3D Mesh, TorusMesh Ring)> _defenseAuraVisuals = [];
    internal int DefenseMinionVisualCount => _minionVisuals.Count;
    internal IReadOnlyDictionary<int, DefenseLootVisual> DefenseLootVisuals => _defenseLootVisuals;

    private void ClearDefenseVisuals()
    {
        foreach (var v in _minionVisuals.Values) ReturnMinionVisual(v); _minionVisuals.Clear();
        foreach (var pool in _freeMinions) foreach (var v in pool) v.Root.Visible = false;
        foreach (var v in _defenseLootVisuals.Values) v.QueueFree(); _defenseLootVisuals.Clear();
        foreach (var v in _defenseAuraVisuals.Values) v.Mesh.QueueFree(); _defenseAuraVisuals.Clear();
    }

    private static WizardSnapshot MinionWizard(MinionSnapshot m) => new(m.Id, DefenseRules.MinionName(m.Kind), 1, true, m.Position,
        m.Facing, m.Health, m.MaxHealth, 0, [], [], true, 0, m.Wet, m.Burning, m.Chill, m.Frozen, 0,
        100, [0, 0, 0, 0], 0, 0, 0, m.Casting,
        WizardType: m.Kind == MinionKind.Shaman ? WizardType.Geomancer : WizardType.Pyromancer, CastingElements: m.Elements);

    private void RenderDefense(WorldSnapshot snapshot, int localId, float dt)
    {
        if (snapshot.Defense is not { } defense) return;
        _visibleMinionIds.Clear(); _missingMinionIds.Clear();
        foreach (var m in defense.Minions) if (NearSurvival(m.Position)) _visibleMinionIds.Add(m.Id);
        foreach (int id in _minionVisuals.Keys) if (!_visibleMinionIds.Contains(id)) _missingMinionIds.Add(id);
        foreach (int id in _missingMinionIds) { ReturnMinionVisual(_minionVisuals[id]); _minionVisuals.Remove(id); }
        foreach (var m in defense.Minions.Where(m => NearSurvival(m.Position)))
        {
            if (!_minionVisuals.TryGetValue(m.Id, out var v))
            {
                var pool = _freeMinions[(int)m.Kind];
                v = pool.Count > 0 ? pool.Pop() : CreateMinionVisual(m);
                v.Root.Name = "Minion" + m.Id; v.Root.Position = World(m.Position); v.Root.Visible = true;
                v.Mage?.ResetForReuse(MinionWizard(m));
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
            v.Aura!.Visible = m.Empowered || m.Kind == MinionKind.Captain || m.SpeedBoostSeconds > 0 || m.Kind == MinionKind.Boss;
            v.Aura.MaterialOverride = Material(m.SpeedBoostSeconds > 0 ? new("65e5ff") : Ui.Gold, 0.5f);
            if (v.Warning != null)
                v.Warning.Visible = m.AttackWarningSeconds > 0;
        }
        foreach (var drop in defense.Drops)
        {
            if (!NearSurvival(drop.Position) || drop.OwnerId != 0 && drop.OwnerId != localId) continue;
            if (!_defenseLootVisuals.TryGetValue(drop.Id, out var visual))
            {
                visual = new DefenseLootVisual(); AddChild(visual);
                var color = drop.Kind == DefenseDropKind.Gold ? Ui.Gold : drop.Kind == DefenseDropKind.Relic ? RelicVisual.ColorFor(drop.Buff)
                    : EquipmentPanel.TierColor(drop.Tier ?? ItemTier.Silver);
                visual.Build(drop, Material(color, 0.2f, true), Material(new("edba49"), 0.2f));
                _defenseLootVisuals[drop.Id] = visual;
            }
            visual.Update(_time);
        }
        foreach (int id in _defenseLootVisuals.Keys.Where(id => !defense.Drops.Any(d => d.Id == id && NearSurvival(d.Position) && (d.OwnerId == 0 || d.OwnerId == localId))).ToArray())
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
                aura.Mesh.Visible = player.Alive && level > 0 && NearSurvival(player.Position);
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

    private static int PreparedMinionCount(MinionKind kind) => kind switch
    { MinionKind.Melee => 64, MinionKind.Archer => 24, MinionKind.Mage or MinionKind.Tank => 16,
        MinionKind.Captain or MinionKind.Shaman => 10, MinionKind.Boss => 2, MinionKind.Runner => 24,
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
        if (m.Kind is MinionKind.Mage or MinionKind.Shaman)
        {
            v.Mage = new WizardVisual(); root.AddChild(v.Mage); v.Mage.Build(MinionWizard(m), false);
            v.Mage.Position = Vector3.Zero;
        }
        else
        {
            if (!_minionShapes.TryGetValue(m.Kind, out var shape))
            {
                shape = m.Kind switch
                {
                    MinionKind.Boss => new BoxMesh { Size = new(2.6f, 2.7f, 2.6f) },
                    MinionKind.Brute => new BoxMesh { Size = new(1.8f, 1.9f, 1.8f) },
                    MinionKind.Tank => new BoxMesh { Size = new(1.6f, 1.45f, 1.6f) },
                    MinionKind.Archer => new BoxMesh { Size = new(0.95f, 0.7f, 0.95f) },
                    MinionKind.Runner => new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.45f, Height = 1.1f, RadialSegments = 12 },
                    MinionKind.Bomber => new SphereMesh { Radius = 0.6f, Height = 1.2f, RadialSegments = 12, Rings = 8 },
                    _ => new CylinderMesh { TopRadius = m.Kind == MinionKind.Captain ? 0.8f : 0.55f,
                        BottomRadius = m.Kind == MinionKind.Captain ? 0.8f : 0.55f, Height = 0.65f, RadialSegments = 16 }
                };
                _minionShapes[m.Kind] = shape;
            }
            float height = m.Kind == MinionKind.Boss ? 1.4f : m.Kind == MinionKind.Brute ? 1.05f : m.Kind == MinionKind.Tank ? 0.8f : 0.6f;
            float healthHeight = m.Kind == MinionKind.Boss ? 3.15f : m.Kind == MinionKind.Brute ? 2.25f : m.Kind == MinionKind.Tank ? 1.65f : 1.3f;
            v.Body = Mesh(root, shape, new(0, height, 0), MinionColor(m.Kind));
            Mesh(root, new BoxMesh { Size = new(1.3f, 0.1f, 0.12f) }, new(0, healthHeight, 0), new("17232d"));
            v.Health = Mesh(root, new BoxMesh { Size = new(1.25f, 0.12f, 0.13f) }, new(0, healthHeight, 0.01f), Ui.Red);
        }
        if (m.Kind == MinionKind.Boss)
            root.AddChild(new Label3D { Text = "COLOSO", Position = new(0, 3.7f, 0), FontSize = 34, PixelSize = 0.015f,
                Modulate = Ui.Gold, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
        v.Aura = Mesh(root, new TorusMesh { InnerRadius = 0.88f, OuterRadius = 0.97f, Rings = 20, RingSegments = 6 },
            new(0, 0.06f, 0), Ui.Gold, 0.5f);
        if (DefenseRules.AreaAttackRadius(m.Kind) is > 0 and var radius)
            v.Warning = Mesh(root, new TorusMesh { InnerRadius = radius - 0.14f, OuterRadius = radius, Rings = 32, RingSegments = 5 },
                new(0, 0.09f, 0), Ui.Red, 0.6f);
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

    private static Color MinionColor(MinionKind kind) => kind switch
    { MinionKind.Boss => new("d6ae57"), MinionKind.Tank => new("5c9d91"), MinionKind.Archer => new("d6a36b"), MinionKind.Captain => new("a36de0"),
        MinionKind.Runner => new("d5ee69"), MinionKind.Brute => new("8796af"), MinionKind.Bomber => new("ff913d"), _ => new("db636b") };
}

using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private sealed class MinionVisual
    {
        public required Node3D Root;
        public MinionKind Kind;
        public MeshInstance3D? Body, Health, Aura, Warning, Shield, SupportArea;
        public Node3D? Details;
        public MeshInstance3D[] Tinted = [];
        public Label3D? ElementLabel;
        public Element? LabelElement;
        public WizardVisual? Mage;
        public MinionSnapshot? Sample;
        public WizardSnapshot? Wizard;
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
    private readonly Dictionary<(DefenseDropKind, BuffKind, EquipmentSlot?, ItemTier?), Stack<DefenseLootVisual>> _freeLoot = [];
    private readonly Dictionary<int, (DefenseDropKind, BuffKind, EquipmentSlot?, ItemTier?)> _lootKeys = [];
    private readonly HashSet<int> _visibleLootIds = [];
    private int _freeLootCount;
    internal int DefenseLootBuilds { get; private set; }
    private static readonly float[] MinionCooldowns = [0, 0, 0, 0];
    private static (DefenseDropKind, BuffKind, EquipmentSlot?, ItemTier?) LootKey(DefenseDropSnapshot d) => (d.Kind, d.Buff, d.Slot, d.Tier);
    private void ReturnLoot(int id)
    {
        var visual = _defenseLootVisuals[id]; visual.Visible = false;
        if (_freeLootCount >= DefenseRules.MaxDrops) visual.QueueFree();
        else
        {
            var key = _lootKeys[id];
            if (!_freeLoot.TryGetValue(key, out var pool)) _freeLoot[key] = pool = new();
            pool.Push(visual); _freeLootCount++;
        }
        _defenseLootVisuals.Remove(id); _lootKeys.Remove(id);
    }
    private readonly Dictionary<(int Player, int Aura), MeshInstance3D> _defenseAuraVisuals = [];
    internal IReadOnlyDictionary<(int Player, int Aura), MeshInstance3D> DefenseAuraVisuals => _defenseAuraVisuals;
    internal int DefenseMinionVisualCount => _minionVisuals.Count;
    internal IReadOnlyDictionary<int, DefenseLootVisual> DefenseLootVisuals => _defenseLootVisuals;

    private void ClearDefenseVisuals()
    {
        foreach (var v in _minionVisuals.Values) ReturnMinionVisual(v); _minionVisuals.Clear();
        foreach (var pool in _freeMinions) foreach (var v in pool) v.Root.Visible = false;
        foreach (int id in _defenseLootVisuals.Keys.ToArray()) ReturnLoot(id);
        foreach (var v in _defenseAuraVisuals.Values) v.QueueFree(); _defenseAuraVisuals.Clear();
    }

    private static WizardSnapshot MinionWizard(MinionSnapshot m) => new(m.Id, DefenseRules.MinionName(m.Kind), 1, true, m.Position,
        m.Facing, m.Health, m.MaxHealth, m.Shield, [], [], true, 0, m.Wet, m.Burning, m.Chill, m.Frozen, 0,
        100, MinionCooldowns, 0, 0, 0, m.Casting,
        ShieldMax: m.ShieldMax, ShieldSeconds: m.Shield > 0 ? 1 : 0,
        WizardType: MinionArt.CasterType(m.Kind) ?? WizardType.Pyromancer,
            CastingElements: m.Elements);

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
                if (!ReferenceEquals(v.Sample, m)) { v.Sample = m; v.Wizard = MinionWizard(m); }
                v.Mage.Update(v.Wizard!, dt, (float)_time, Camera, snapshot.Time, InterpolationDelay);
                v.Mage.Position -= World(m.Position); // La raíz ya está en coordenadas del mundo.
            }
            if (v.Health != null) v.Health.Scale = new(Math.Max(0.001f, m.Health / m.MaxHealth), 1, 1);
            Color bodyColor = m.VulnerableElement is { } weakness ? Ui.ElementColor(weakness)
                : m.Frozen > 0 ? new("8bdeff") : m.Burning > 0 ? Ui.ElementColor(Element.Fire) : MinionColor(m.Kind);
            bool fixedColor = m.Kind is MinionKind.Prism or MinionKind.Oracle;
            if (v.Body != null) v.Body.MaterialOverride = Material(bodyColor, 0, fixedColor);
            foreach (var part in v.Tinted) part.MaterialOverride = Material(bodyColor.Darkened(0.18f), 0, fixedColor);
            if (v.Details != null) v.Details.Rotation = new(0, MathF.Atan2(m.Facing.X, m.Facing.Y), 0);
            if (v.ElementLabel != null && m.VulnerableElement is { } shown && shown != v.LabelElement)
            { v.ElementLabel.Text = SpellCatalogue.Name([shown]); v.ElementLabel.Modulate = Ui.ElementColor(shown); v.LabelElement = shown; }
            if (v.Shield != null) v.Shield.Visible = m.Shield > 0;
            v.Aura!.Visible = m.Empowered || m.Kind is MinionKind.Captain or MinionKind.Sentinel or MinionKind.Warlock or MinionKind.Cryomancer or MinionKind.Prism or MinionKind.Oracle || m.SpeedBoostSeconds > 0 || m.Kind == MinionKind.Boss;
            v.Aura.MaterialOverride = Material(m.SpeedBoostSeconds > 0 ? new("65e5ff") :
                m.Kind is MinionKind.Warlock or MinionKind.Cryomancer or MinionKind.Sentinel
                    ? MinionColor(m.Kind) : Ui.Gold, 0.5f);
            if (v.Warning != null)
                v.Warning.Visible = m.AttackWarningSeconds > 0;
        }
        _visibleLootIds.Clear();
        foreach (var drop in defense.Drops)
        {
            if (!NearSurvival(drop.Position) || drop.OwnerId != 0 && drop.OwnerId != localId) continue;
            _visibleLootIds.Add(drop.Id);
            if (!_defenseLootVisuals.TryGetValue(drop.Id, out var visual))
            {
                var key = LootKey(drop);
                if (_freeLoot.TryGetValue(key, out var pool) && pool.Count > 0)
                { visual = pool.Pop(); _freeLootCount--; visual.Reset(drop); }
                else
                {
                visual = new DefenseLootVisual(); AddChild(visual); DefenseLootBuilds++;
                var color = drop.Kind == DefenseDropKind.Gold ? Ui.Gold : drop.Kind == DefenseDropKind.Relic ? RelicVisual.ColorFor(drop.Buff)
                    : EquipmentPanel.TierColor(drop.Tier ?? ItemTier.Silver);
                visual.Build(drop, Material(color, 0.2f, true), Material(new("edba49"), 0.2f));
                }
                _defenseLootVisuals[drop.Id] = visual; _lootKeys[drop.Id] = key;
            }
            visual.Update(_time);
        }
        foreach (int id in _defenseLootVisuals.Keys.Where(id => !_visibleLootIds.Contains(id)).ToArray()) ReturnLoot(id);
        foreach (var defender in defense.Defenders)
        {
            var player = snapshot.Players.FirstOrDefault(p => p.Id == defender.PlayerId);
            if (player == null) continue;
            for (int i = 0; i < 4; i++)
            {
                var key = (defender.PlayerId, i);
                if (!_defenseAuraVisuals.TryGetValue(key, out var aura))
                {
                    aura = AreaVisuals.Piece(this, AreaVisuals.AuraPattern(i));
                    aura.Name = $"DefenseAura{defender.PlayerId}_{i}"; _defenseAuraVisuals[key] = aura;
                }
                int level = i == 3 ? defender.BerserkerRemaining > 0 ? 1 : 0 : defender.Auras[i];
                aura.Visible = player.Alive && level > 0 && NearSurvival(player.Position);
                if (level > 0)
                {
                    float radius = i == 3 ? 1.6f : DefenseRules.AuraRadius(level);
                    aura.Scale = new(radius, 1, radius);
                    aura.Position = (_wizards.TryGetValue(player.Id, out var wizard) ? wizard.Position : World(player.Position)) + new Vector3(0, 0.08f + i * 0.03f, 0);
                }
            }
        }
        foreach (var key in _defenseAuraVisuals.Keys.Where(k => !defense.Defenders.Any(d => d.PlayerId == k.Player)).ToArray())
        { _defenseAuraVisuals[key].QueueFree(); _defenseAuraVisuals.Remove(key); }
    }

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
            v.Mage = new WizardVisual(); root.AddChild(v.Mage); v.Mage.Build(MinionWizard(m), false);
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
            float shieldRadius = m.Kind == MinionKind.Boss ? 1.55f : m.Kind == MinionKind.Sentinel ? 1.15f : 0.8f;
            v.Shield = Mesh(root, new TorusMesh { InnerRadius = shieldRadius - 0.06f, OuterRadius = shieldRadius, Rings = 16, RingSegments = 4 },
                new(0, 0.14f, 0), new("8dbdff"), 0.65f);
            v.Shield.Name = "EnemyShield"; v.Shield.Visible = m.Shield > 0;
            Mesh(root, new BoxMesh { Size = new(1.3f, 0.1f, 0.12f) }, new(0, healthHeight, 0), new("17232d"));
            v.Health = Mesh(root, new BoxMesh { Size = new(1.25f, 0.12f, 0.13f) }, new(0, healthHeight, 0.01f), Ui.Red);
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
        if (m.Kind == MinionKind.Boss)
            root.AddChild(new Label3D { Text = "COLOSO", Position = new(0, 4.55f, 0), FontSize = 34, PixelSize = 0.015f,
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

    private static Color MinionColor(MinionKind kind) => MinionArt.ColorFor(kind);

    internal void VerifyEliteModels()
    {
        foreach (var element in DefenseRules.PrismElements.ToArray())
        {
            var visual = _minionVisuals.Values.First(v => v.Sample?.VulnerableElement == element ||
                v.Kind == MinionKind.Prism && v.LabelElement == element);
            if (visual.Body?.MaterialOverride is not StandardMaterial3D skin || skin.AlbedoColor != Ui.ElementColor(element) ||
                skin.ShadingMode != BaseMaterial3D.ShadingModeEnum.Unshaded)
                throw new InvalidOperationException("El color del prisma no coincide con su punto débil: " + element);
        }
        var sentinel = _minionVisuals.Values.First(v => v.Kind == MinionKind.Sentinel);
        var boss = _minionVisuals.Values.First(v => v.Kind == MinionKind.Boss);
        var oracle = _minionVisuals.Values.First(v => v.Kind == MinionKind.Oracle);
        if (sentinel.Details?.FindChild("KnightShield", true, false) is not MeshInstance3D ||
            boss.Details?.FindChild("ColossusHorn1", true, false) is not MeshInstance3D || boss.Body?.Mesh is not CapsuleMesh)
            throw new InvalidOperationException("El guardián y el coloso no tienen sus siluetas nuevas.");
        if (oracle.SupportArea == null || oracle.SupportArea.Scale.X != DefenseRules.OracleAuraRadius ||
            oracle.Shield is not { Visible: true } || _fields.Values.All(v => v is not OracleStrikeVisual))
            throw new InvalidOperationException("Falta el aura, escudo o bombardeo del oráculo.");
    }
}

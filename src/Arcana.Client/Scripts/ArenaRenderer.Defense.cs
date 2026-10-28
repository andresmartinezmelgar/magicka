using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private sealed class MinionVisual
    {
        public required Node3D Root;
        public MinionKind Kind;
        public MeshInstance3D? Body, Health, Aura, Warning, Shield, ShieldTrack, SupportArea, ArcaneMark;
        public Node3D? Details, Vitals;
        public MeshInstance3D[] Tinted = [];
        public Label3D? ElementLabel;
        public Element? LabelElement;
        public WizardVisual? Mage;
        public MinionSnapshot? Sample;
        public WizardSnapshot? Wizard;
        public Color? BodyColor, AuraColor;
        public Basis? VitalsBasis;
        public V2? DetailFacing;
        public float HealthFraction = -1, ShieldFraction = -1;
        public bool? ShieldVisible, AuraVisible, WarningVisible;
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
            CastingElements: m.Elements) { ArcaneMarked = m.ArcaneMarked };

    private void RenderDefense(WorldSnapshot snapshot, int localId, float dt)
    {
        if (snapshot.Defense is not { } defense) return;
        _visibleMinionIds.Clear(); _missingMinionIds.Clear();
        foreach (var m in defense.Minions) if (NearSurvival(m.Position)) _visibleMinionIds.Add(m.Id);
        foreach (int id in _minionVisuals.Keys) if (!_visibleMinionIds.Contains(id)) _missingMinionIds.Add(id);
        foreach (int id in _missingMinionIds) { ReturnMinionVisual(_minionVisuals[id]); _minionVisuals.Remove(id); }
        var vitalsBasis = Camera.GlobalBasis;
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
            if (v.Mage != null)
            {
                if (!ReferenceEquals(v.Sample, m)) { v.Sample = m; v.Wizard = MinionWizard(m); }
                v.Mage.Update(v.Wizard!, dt, (float)_time, Camera, snapshot.Time, InterpolationDelay);
                // Un único interpolador mueve también sus auras y avisos de ataque.
                v.Root.Position = v.Mage.Position; v.Mage.Position = Vector3.Zero;
            }
            else { v.Root.Position = v.Root.Position.Lerp(World(m.Position), Math.Min(1, dt * 18)); v.Sample = m; }
            if (v.ArcaneMark != null) v.ArcaneMark.Visible = m.ArcaneMarked;
            if (v.Vitals != null && v.VitalsBasis != vitalsBasis)
            { v.Vitals.Basis = vitalsBasis; v.VitalsBasis = vitalsBasis; }
            if (v.Health != null)
            {
                float health = Math.Clamp(m.Health / Math.Max(1, m.MaxHealth), 0, 1);
                if (health != v.HealthFraction)
                {
                    v.Health.Scale = new(Math.Max(0.001f, health), 1, 1);
                    v.Health.Position = new(-(1 - health) * 0.625f, 0, 0.01f); v.HealthFraction = health;
                }
            }
            Color bodyColor = m.VulnerableElement is { } weakness ? Ui.ElementColor(weakness)
                : m.Frozen > 0 ? new("8bdeff") : m.Burning > 0 ? Ui.ElementColor(Element.Fire) : MinionColor(m.Kind);
            bool fixedColor = m.Kind is MinionKind.Prism or MinionKind.Oracle;
            if (bodyColor != v.BodyColor)
            {
                if (v.Body != null) v.Body.MaterialOverride = Material(bodyColor, 0, fixedColor);
                foreach (var part in v.Tinted) part.MaterialOverride = Material(bodyColor.Darkened(0.18f), 0, fixedColor);
                v.BodyColor = bodyColor;
            }
            if (v.Details != null && v.DetailFacing != m.Facing)
            { v.Details.Rotation = new(0, MathF.Atan2(m.Facing.X, m.Facing.Y), 0); v.DetailFacing = m.Facing; }
            if (v.ElementLabel != null && m.VulnerableElement is { } shown && shown != v.LabelElement)
            { v.ElementLabel.Text = SpellCatalogue.Name([shown]); v.ElementLabel.Modulate = Ui.ElementColor(shown); v.LabelElement = shown; }
            if (v.Shield != null)
            {
                bool shielded = m.Shield > 0;
                if (shielded != v.ShieldVisible)
                { v.Shield.Visible = v.ShieldTrack!.Visible = shielded; v.ShieldVisible = shielded; }
                if (shielded)
                {
                    float fraction = Math.Clamp(m.Shield / (m.ShieldMax > 0 ? m.ShieldMax : m.Shield), 0, 1);
                    if (fraction != v.ShieldFraction)
                    {
                        v.Shield.Scale = new(fraction, 1, 1);
                        v.Shield.Position = new(-(1 - fraction) * 0.625f, 0.17f, 0.01f); v.ShieldFraction = fraction;
                    }
                }
            }
            bool auraVisible = m.Empowered || m.Kind is MinionKind.Captain or MinionKind.Sentinel or MinionKind.Warlock or MinionKind.Cryomancer or MinionKind.Prism or MinionKind.Oracle || m.SpeedBoostSeconds > 0 || m.Kind == MinionKind.Boss;
            if (auraVisible != v.AuraVisible) { v.Aura!.Visible = auraVisible; v.AuraVisible = auraVisible; }
            Color auraColor = m.SpeedBoostSeconds > 0 ? new("65e5ff") :
                m.Kind is MinionKind.Warlock or MinionKind.Cryomancer or MinionKind.Sentinel
                    ? MinionColor(m.Kind) : Ui.Gold;
            if (auraColor != v.AuraColor) { v.Aura!.MaterialOverride = Material(auraColor, 0.5f); v.AuraColor = auraColor; }
            if (v.Warning != null)
            {
                bool warning = m.AttackWarningSeconds > 0;
                if (warning != v.WarningVisible) { v.Warning.Visible = warning; v.WarningVisible = warning; }
            }
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
            for (int i = 0; i <= DefenseRules.AuraCount; i++)
            {
                var key = (defender.PlayerId, i);
                if (!_defenseAuraVisuals.TryGetValue(key, out var aura))
                {
                    aura = AreaVisuals.Piece(this, AreaVisuals.AuraPattern(i));
                    aura.Name = $"DefenseAura{defender.PlayerId}_{i}"; _defenseAuraVisuals[key] = aura;
                }
                int level = i == DefenseRules.AuraCount ? defender.BerserkerRemaining > 0 ? 1 : 0 : defender.Auras[i];
                aura.Visible = player.Alive && level > 0 && NearSurvival(player.Position);
                if (level > 0)
                {
                    float radius = i == DefenseRules.AuraCount ? 1.6f : DefenseRules.AuraRadius(level);
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
        }
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

    internal void VerifyEnemyShieldIndicators()
    {
        foreach (var visual in _minionVisuals.Values)
        {
            var sample = visual.Sample ?? throw new InvalidOperationException("Falta la muestra del enemigo.");
            if (visual.Mage?.NameVisible == true || visual.Root.GetChildren().OfType<Label3D>().Any(l => l.Visible && l.Text == "COLOSO"))
                throw new InvalidOperationException("Un enemigo conserva su nombre durante Supervivencia.");
            if ((visual.Mage?.ArcaneMarkVisible ?? visual.ArcaneMark?.Visible ?? false) != sample.ArcaneMarked)
                throw new InvalidOperationException("La marca arcana no aparece, no se oculta o se conserva al reutilizar un enemigo.");
            if (visual.Mage == null)
            {
                float health = Math.Clamp(sample.Health / Math.Max(1, sample.MaxHealth), 0, 1);
                Color expected = sample.VulnerableElement is { } weakness ? Ui.ElementColor(weakness) : sample.Frozen > 0
                    ? new("8bdeff") : sample.Burning > 0 ? Ui.ElementColor(Element.Fire) : MinionColor(sample.Kind);
                if (visual.Health == null || Math.Abs(visual.Health.Scale.X - Math.Max(0.001f, health)) > 0.001f ||
                    Math.Abs(visual.Health.Position.X + (1 - health) * 0.625f) > 0.001f ||
                    visual.Body?.MaterialOverride is not StandardMaterial3D body || body.AlbedoColor != expected)
                    throw new InvalidOperationException("La vida o el color del enemigo conserva un estado anterior: " + sample.Kind);
            }
            var fill = visual.Mage?.FindChild("ShieldFill", true, false) as MeshInstance3D ?? visual.Shield!;
            var track = visual.Mage?.FindChild("ShieldTrack", true, false) as MeshInstance3D ?? visual.ShieldTrack!;
            bool shielded = sample.Shield > 0;
            if (fill.Visible != shielded || track.Visible != shielded)
                throw new InvalidOperationException("La barra de escudo no aparece o no se oculta: " + sample.Kind);
            if (shielded)
            {
                float expected = Math.Clamp(sample.Shield / (sample.ShieldMax > 0 ? sample.ShieldMax : sample.Shield), 0, 1);
                if (Math.Abs(fill.Scale.X - expected) > 0.001f || fill.MaterialOverride is not StandardMaterial3D material || material.AlbedoColor != Ui.EnemyShield)
                    throw new InvalidOperationException("La barra no refleja la integridad del escudo: " + sample.Kind);
            }
            if (visual.Mage != null && (visual.Mage.FindChild("ShieldAmount", true, false) is not Label3D { Visible: false } ||
                visual.Mage.FindChild("ProtectionBubble", true, false) is not MeshInstance3D { Visible: false } ||
                visual.Mage.FindChild("StyleBPersonalWardRing", true, false) is not MeshInstance3D { Visible: false }))
                throw new InvalidOperationException("El mago enemigo acumula cifras, burbujas o anillos de escudo.");
        }
    }
}

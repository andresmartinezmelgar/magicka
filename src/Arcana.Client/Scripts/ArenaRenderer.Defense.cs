using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private sealed class MinionVisual
    {
        public required Node3D Root;
        public MinionKind Kind;
        public MeshInstance3D? Body, Health, Aura, Shield, ShieldTrack, SupportArea, ArcaneMark, LightningStun;
        public EnemyAttackWarning? Warning;
        public Node3D? Details, Vitals;
        public MeshInstance3D[] Tinted = [];
        public Label3D? ElementLabel;
        public Element? LabelElement;
        public WizardVisual? Mage;
        public SpecialMinionVisual? Special;
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
    private readonly List<int> _missingLootIds = [];
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
    private readonly HashSet<int> _auraDefenderIds = [];
    private readonly List<(int Player, int Aura)> _missingAuraKeys = [];
    internal IReadOnlyDictionary<(int Player, int Aura), MeshInstance3D> DefenseAuraVisuals => _defenseAuraVisuals;
    internal int DefenseMinionVisualCount => _minionVisuals.Count;
    internal IReadOnlyDictionary<int, DefenseLootVisual> DefenseLootVisuals => _defenseLootVisuals;

    private void ClearDefenseVisuals()
    {
        foreach (var v in _minionVisuals.Values) ReturnMinionVisual(v); _minionVisuals.Clear();
        foreach (var pool in _freeMinions) foreach (var v in pool) v.Root.Visible = false;
        _missingLootIds.Clear(); _missingLootIds.AddRange(_defenseLootVisuals.Keys);
        foreach (int id in _missingLootIds) ReturnLoot(id);
        _missingLootIds.Clear(); _missingAuraKeys.Clear(); _auraDefenderIds.Clear();
        foreach (var v in _defenseAuraVisuals.Values) v.QueueFree(); _defenseAuraVisuals.Clear();
    }

    private static WizardSnapshot MinionWizard(MinionSnapshot m) => new(m.Id, DefenseRules.MinionName(m.Kind), 1, true, m.Position,
        m.Facing, m.Health, m.MaxHealth, m.Shield, [], [], true, 0, m.Wet, m.Burning, m.Chill, m.Frozen, 0,
        100, MinionCooldowns, 0, 0, 0, m.Casting,
        ShieldMax: m.ShieldMax, ShieldSeconds: m.Shield > 0 ? 1 : 0,
        WizardType: MinionArt.CasterType(m.Kind) ?? WizardType.Pyromancer,
            CastingElements: m.Elements) { ArcaneMarked = m.ArcaneMarked, Stunned = m.Stunned, WaterSlowed = m.WaterSlowed };

    private void RenderDefense(WorldSnapshot snapshot, int localId, float dt)
    {
        if (snapshot.Defense is not { } defense) return;
        _visibleMinionIds.Clear(); _missingMinionIds.Clear();
        foreach (var m in defense.Minions) if (NearSurvival(m.Position)) _visibleMinionIds.Add(m.Id);
        foreach (int id in _minionVisuals.Keys) if (!_visibleMinionIds.Contains(id)) _missingMinionIds.Add(id);
        foreach (int id in _missingMinionIds) { ReturnMinionVisual(_minionVisuals[id]); _minionVisuals.Remove(id); }
        var vitalsBasis = Camera.GlobalBasis;
        foreach (var m in defense.Minions)
        {
            if (!_visibleMinionIds.Contains(m.Id)) continue;
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
            else { v.Root.Position = m.Stunned ? World(m.Position) : v.Root.Position.Lerp(World(m.Position), Math.Min(1, dt * 18)); v.Sample = m; }
            if (m.SpecialAbility != DefenseSpecialAbility.None && v.Special == null)
            {
                v.Special = new SpecialMinionVisual(); v.Root.AddChild(v.Special); v.Special.Build(m.Kind);
            }
            v.Special?.Update(m, (float)_time);
            v.Mage?.SetEnemyTint(m.SpecialAbility == DefenseSpecialAbility.None ? null : SpecialMinionVisual.BodyColor(m.SpecialAbility));
            if (v.ArcaneMark != null) v.ArcaneMark.Visible = m.ArcaneMarked;
            if (v.LightningStun != null)
            {
                v.LightningStun.Visible = m.Stunned;
                if (m.Stunned) v.LightningStun.Rotation = new(0, (float)_time * 8, 0);
            }
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
                : m.Frozen > 0 ? new("8bdeff") : m.Burning > 0 ? Ui.ElementColor(Element.Fire)
                : m.SpecialAbility != DefenseSpecialAbility.None ? SpecialMinionVisual.BodyColor(m.SpecialAbility) : MinionColor(m.Kind);
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
                if (warning)
                {
                    v.Warning.Position = World(m.Position) - v.Root.Position;
                    v.Warning.ShowRemaining(m.AttackWarningSeconds, DefenseRules.AttackWindup(m.Kind));
                }
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
        _missingLootIds.Clear();
        foreach (int id in _defenseLootVisuals.Keys)
            if (!_visibleLootIds.Contains(id)) _missingLootIds.Add(id);
        foreach (int id in _missingLootIds) ReturnLoot(id);
        _auraDefenderIds.Clear();
        foreach (var defender in defense.Defenders)
        {
            _auraDefenderIds.Add(defender.PlayerId);
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
        _missingAuraKeys.Clear();
        foreach (var key in _defenseAuraVisuals.Keys)
            if (!_auraDefenderIds.Contains(key.Player)) _missingAuraKeys.Add(key);
        foreach (var key in _missingAuraKeys)
        { _defenseAuraVisuals[key].QueueFree(); _defenseAuraVisuals.Remove(key); }
    }

    private static Color MinionColor(MinionKind kind) => MinionArt.ColorFor(kind);

}

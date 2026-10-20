using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class WizardVisual
{
    private readonly List<MeshInstance3D> _protectionBands = [];
    private StandardMaterial3D _bandMaterial = null!;
    private MeshInstance3D _spawnProtection = null!;
    private Color? _bandColor, _bandEmission;
    private Node3D _stoneArmor = null!;
    private readonly List<MeshInstance3D> _stonePlates = [];
    private readonly List<MeshInstance3D> _arcaneShieldCrystals = [];
    private readonly List<MeshInstance3D> _arcaneShieldRings = [];
    private Sprite3D? _approvedPersonalShield;
    private StandardMaterial3D _arcaneShieldAccent = null!;
    private Material? _stoneArmorMaterial;
    internal int StoneArmorPlates => _stoneArmor.Visible ? _stonePlates.Count(p => p.Visible) : 0;
    internal int ProtectionBandCount => _protectionBands.Count(b => b.Visible);
    internal bool SpawnProtectionVisible => _spawnProtection.Visible;

    private void BuildProtectionBands()
    {
        _bandMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(Ui.Shield, 0.65f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true, Emission = Ui.Shield, EmissionEnergyMultiplier = 0.25f
        };
        for (int i = 0; i < 2; i++)
        {
            var band = Mesh(this, new TorusMesh { InnerRadius = 1.04f, OuterRadius = 1.09f,
                Rings = 32, RingSegments = 6 }, new(0, 1.45f, 0), _bandMaterial);
            band.Visible = false; _protectionBands.Add(band);
        }
        _spawnProtection = Mesh(this, new TorusMesh { InnerRadius = 0.97f, OuterRadius = 1.08f,
            Rings = 32, RingSegments = 6 }, new(0, 0.075f, 0),
            new StandardMaterial3D { AlbedoColor = new("fff2a3"),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _spawnProtection.Visible = false;
        // Visual-only decoration; shield rules remain authoritative on the server.
        _arcaneShieldAccent = new StandardMaterial3D
        {
            AlbedoColor = new Color(Ui.Shield, 0.7f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true, Emission = Ui.Shield, EmissionEnergyMultiplier = 0.35f
        };
        for (int i = 0; i < 8; i++)
        {
            var crystal = Mesh(this, new PrismMesh { Size = new(0.14f, 0.31f, 0.14f) },
                Vector3.Zero, _arcaneShieldAccent);
            crystal.Name = "ShieldRuneCrystal" + i;
            crystal.Visible = false;
            _arcaneShieldCrystals.Add(crystal);
        }
        for (int i = 0; i < 2; i++)
        {
            var ring = Mesh(this, new TorusMesh { InnerRadius = 1.09f, OuterRadius = 1.12f,
                    Rings = 36, RingSegments = 5 }, new(0, 1.45f, 0), _arcaneShieldAccent);
            ring.Name = "ShieldArcaneOrbit" + i;
            ring.Visible = false;
            _arcaneShieldRings.Add(ring);
        }
        _approvedPersonalShield = new Sprite3D
        {
            Name = "ApprovedPersonalWard",
            Position = new(0, 1.43f, 0),
            PixelSize = 3.18f / 96f,
            Shaded = false,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1f, 1f, 1f, 0.57f),
            Visible = false
        };
        AddChild(_approvedPersonalShield);
        _stoneArmor = new Node3D { Name = "StoneArmor", Visible = false }; AddChild(_stoneArmor);
        _stoneArmorMaterial = StoneVisuals.Surface([Element.Earth]);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6;
            var plate = Mesh(_stoneArmor, StoneVisuals.Shape(i),
                new(Mathf.Cos(angle) * 0.82f, 0.62f + i % 2 * 0.45f, Mathf.Sin(angle) * 0.82f),
                _stoneArmorMaterial);
            plate.Name = "StonePlate" + i;
            plate.Scale = new(0.30f, 0.43f, 0.21f);
            plate.Rotation = new(0.12f * i, -angle, 0.15f);
            _stonePlates.Add(plate);
        }
    }

    private void UpdateProtectionBands(WizardSnapshot player, float time)
    {
        bool stone = player.Alive && player.Shield > 0 && player.Ward.Contains(Element.Earth) && player.Frozen <= 0;
        _stoneArmor.Visible = stone;
        if (stone)
        {
            float fraction = Math.Clamp(player.Shield / Ui.ShieldMaximum(player), 0, 1);
            int plates = 3 + Mathf.CeilToInt(fraction * 3);
            _stoneArmor.Rotation = new(0, time * 0.18f, 0);
            var material = StoneVisuals.Surface(player.Ward);
            bool changed = _stoneArmorMaterial != material;
            for (int i = 0; i < _stonePlates.Count; i++)
            {
                _stonePlates[i].Visible = i < plates;
                if (changed) _stonePlates[i].MaterialOverride = material;
            }
            _stoneArmorMaterial = material;
        }
        bool invulnerable = player.Alive && player.InvulnerableSeconds > 0;
        int bands = invulnerable ? 2 : player.Alive && player.Shield > 0 ? player.ShieldCharges - 1 : 0;
        Color color = invulnerable ? new("fff2a3") : player.Ward.Length > 0 ? Ui.SpellColor(player.Ward) : Ui.Shield;
        float integrity = player.Shield > 0 ? Math.Clamp(player.Shield / Ui.ShieldMaximum(player), 0, 1) : 1;
        Color bandColor = new(color, invulnerable ? 0.78f : 0.25f + integrity * 0.4f);
        if (_bandColor != bandColor) { _bandMaterial.AlbedoColor = bandColor; _bandColor = bandColor; }
        if (_bandEmission != color) { _bandMaterial.Emission = color; _bandEmission = color; }
        for (int i = 0; i < _protectionBands.Count; i++)
        {
            var band = _protectionBands[i]; band.Visible = i < bands;
            if (i >= bands) continue;
            band.Rotation = new(0.42f + i * 0.65f, time * (i == 0 ? 0.35f : -0.28f), i * 0.3f);
            band.Scale = new(1, 1, 1.3f);
        }
        // Element-specific motifs: regeneration, frost, arcane and offensive infusions.
        bool activeWard = player.Alive && player.Shield > 0 && player.Frozen <= 0;
        bool regenerate = player.Ward.Contains(Element.Life);
        bool frost = player.Ward.Contains(Element.Cold);
        bool arcane = player.Ward.Contains(Element.Arcane);
        bool elemental = regenerate || frost || arcane || player.Ward.Contains(Element.Fire) ||
                         player.Ward.Contains(Element.Water) || player.Ward.Contains(Element.Lightning);
        Color accent = regenerate ? Ui.ElementColor(Element.Life)
            : frost ? Ui.ElementColor(Element.Cold)
            : arcane ? Ui.ElementColor(Element.Arcane)
            : player.Ward.Contains(Element.Fire) ? Ui.ElementColor(Element.Fire)
            : player.Ward.Contains(Element.Water) ? Ui.ElementColor(Element.Water)
            : player.Ward.Contains(Element.Lightning) ? Ui.ElementColor(Element.Lightning) : Ui.Shield;
        _arcaneShieldAccent.AlbedoColor = new Color(accent, 0.25f + integrity * 0.52f);
        _arcaneShieldAccent.Emission = accent;
        int crystalCount = !activeWard || !elemental ? 0
            : Math.Min(_arcaneShieldCrystals.Count, 4 + Math.Max(0, player.ShieldCharges - 1) * 2);
        for (int i = 0; i < _arcaneShieldCrystals.Count; i++)
        {
            var crystal = _arcaneShieldCrystals[i];
            crystal.Visible = i < crystalCount;
            if (!crystal.Visible) continue;
            float a = i * Mathf.Tau / crystalCount + time * (arcane ? 0.60f : 0.22f);
            float radius = frost ? 1.03f : 1.10f;
            crystal.Position = new(Mathf.Cos(a) * radius,
                1.45f + MathF.Sin(a * (frost ? 1 : 2) + time * 1.3f) * (frost ? 0.55f : 0.35f),
                Mathf.Sin(a) * radius);
            crystal.Rotation = new(time * 0.3f, a, frost ? 0 : time * 0.4f);
            crystal.Scale = Vector3.One * (frost ? 1.35f : regenerate ? 0.8f : 1f);
        }
        for (int i = 0; i < _arcaneShieldRings.Count; i++)
        {
            var ring = _arcaneShieldRings[i];
            ring.Visible = activeWard && elemental && (i == 0 || player.ShieldCharges > 1);
            if (!ring.Visible) continue;
            ring.Rotation = new(0.4f + i * 0.65f, time * (i == 0 ? 0.48f : -0.38f), i * 0.45f);
        }
        // Original bubble and combat hitboxes stay unchanged.
        if (_approvedPersonalShield != null)
        {
            var art = ArcanaSpriteAtlas.Personal(player.Ward);
            if (_approvedPersonalShield.Texture != art) _approvedPersonalShield.Texture = art;
            _approvedPersonalShield.Visible = activeWard && art != null;
            if (_approvedPersonalShield.Visible)
            {
                _approvedPersonalShield.Modulate = new Color(1f, 1f, 1f, 0.35f + 0.30f * integrity);
                _approvedPersonalShield.Scale = Vector3.One * (1f + MathF.Sin(time * 2.8f) * 0.022f);
            }
        }
        _spawnProtection.Visible = invulnerable;
        if (invulnerable) _spawnProtection.Scale = Vector3.One * (1 + Mathf.Sin(time * 12) * 0.035f);
    }
}

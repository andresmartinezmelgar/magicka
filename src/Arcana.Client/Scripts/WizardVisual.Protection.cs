using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class WizardVisual
{
    private readonly List<MeshInstance3D> _protectionBands = [];
    private StandardMaterial3D _bandMaterial = null!;
    private MeshInstance3D _spawnProtection = null!;
    private Color? _bandColor, _bandEmission;
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
    }

    private void UpdateProtectionBands(WizardSnapshot player, float time)
    {
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
        _spawnProtection.Visible = invulnerable;
        if (invulnerable) _spawnProtection.Scale = Vector3.One * (1 + Mathf.Sin(time * 12) * 0.035f);
    }
}

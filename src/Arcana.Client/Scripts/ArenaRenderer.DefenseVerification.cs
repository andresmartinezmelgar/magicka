using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
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
            if ((visual.Mage?.LightningStunVisible ?? visual.LightningStun?.Visible ?? false) != sample.Stunned)
                throw new InvalidOperationException("El indicador de aturdimiento no se actualiza al reutilizar un enemigo.");
            if (visual.Mage == null)
            {
                float health = Math.Clamp(sample.Health / Math.Max(1, sample.MaxHealth), 0, 1);
                Color expected = sample.VulnerableElement is { } weakness ? sample.SpecialAbility == DefenseSpecialAbility.None
                    ? Ui.ElementColor(weakness) : SpecialMinionVisual.BodyColor(sample.SpecialAbility, weakness) : sample.Frozen > 0
                    ? new("8bdeff") : sample.Burning > 0 ? Ui.ElementColor(Element.Fire)
                    : sample.SpecialAbility != DefenseSpecialAbility.None ? SpecialMinionVisual.BodyColor(sample.SpecialAbility) : MinionColor(sample.Kind);
                if (visual.Health == null || Math.Abs(visual.Health.Scale.X - Math.Max(0.001f, health)) > 0.001f ||
                    Math.Abs(visual.Health.Position.X + (1 - health) * 0.625f) > 0.001f ||
                    visual.Body?.MaterialOverride is not StandardMaterial3D body || body.AlbedoColor != expected)
                    throw new InvalidOperationException("La vida o el color del enemigo conserva un estado anterior: " + sample.Kind);
            }
            bool special = sample.SpecialAbility != DefenseSpecialAbility.None;
            bool frontal = sample.SpecialAbility == DefenseSpecialAbility.FrontalShield && sample.FrontalShieldSeconds > 0 && sample.Frozen <= 0 && !sample.Stunned;
            if (visual.Mage != null && visual.Mage.EnemyTintForVerification != (special ? SpecialMinionVisual.BodyColor(sample.SpecialAbility) : (Color?)null))
                throw new InvalidOperationException("El mago conserva el color de un especial después de reutilizarse.");
            if ((visual.Special?.Visible ?? false) != special || (visual.Special?.ShieldVisible ?? false) != frontal)
                throw new InvalidOperationException("El aura o escudo frontal conserva un estado anterior al reutilizar un especial.");
            if ((visual.Special?.DeathRadiusVisible ?? false) != (sample.SpecialAbility == DefenseSpecialAbility.DeathExplosion))
                throw new InvalidOperationException("El alcance de detonación no coincide con la habilidad del especial.");
            if (special && (visual.Special!.FindChild("SpecialElectricity", true, false) is not MeshInstance3D { Mesh: not null } ||
                visual.Special.FindChild("SpecialFrontalShield", true, false)!.GetChildCount() != SpecialMinionVisual.FrontalPaneCount))
                throw new InvalidOperationException("Faltan los rayos o las cinco placas del especial.");
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

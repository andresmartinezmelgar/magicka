using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckStoneVisuals()
    {
        for (int variant = 0; variant < 4; variant++)
        {
            var mesh = StoneVisuals.Shape(variant);
            if (mesh != StoneVisuals.Shape(variant + 4) || mesh.GetSurfaceCount() != 1) return false;
            using var arrays = mesh.SurfaceGetArrays(0);
            var vertices = arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Godot.Mesh.ArrayType.Normal].AsVector3Array();
            if (vertices.Length != 240 || normals.Length != vertices.Length) return false;
            for (int i = 0; i < vertices.Length; i += 3)
            {
                var a = vertices[i]; var b = vertices[i + 1]; var c = vertices[i + 2];
                if (!a.IsFinite() || !b.IsFinite() || !c.IsFinite() ||
                    a.Length() > 1.001f || b.Length() > 1.001f || c.Length() > 1.001f ||
                    (b - a).Cross(c - a).Dot(normals[i]) >= -0.00001f || normals[i].Dot(a + b + c) <= 0) return false;
            }
        }
        var empty = VisualFixture.Create("none") with { Players = [], Walls = [], Fields = [], Beams = [], Projectiles = [], Events = [] };
        Element[][] recipes = [[Element.Earth], [Element.Earth, Element.Earth, Element.Earth],
            [Element.Earth, Element.Fire], [Element.Earth, Element.Cold], [Element.Earth, Element.Water]];
        foreach (var elements in recipes)
        {
            _runtime.Renderer.Clear();
            _runtime.Renderer.Render(empty with { Projectiles = [new(1, 1, V2.Zero, new(1, 0), elements, 0.3f)] }, 0, 1/60.0, true);
            var core = _runtime.Renderer.ProjectileVisuals[1].GetNode<MeshInstance3D>("StoneCore");
            if (core.Mesh is not ArrayMesh || !core.Scale.IsEqualApprox(Vector3.One * 0.3f) ||
                core.MaterialOverride != StoneVisuals.Surface(elements)) return false;
            float imbued = ((ShaderMaterial)core.MaterialOverride).GetShaderParameter("imbued").AsSingle();
            if (imbued != (elements.Any(e => e != Element.Earth) ? 1 : 0)) return false;
        }
        _runtime.Renderer.Clear();
        _runtime.Renderer.Render(empty with { Projectiles = [new(1, 1, V2.Zero, new(1, 0), [Element.Water, Element.Cold], 0.16f)] }, 0, 1/60.0, true);
        var ice = _runtime.Renderer.ProjectileVisuals[1].GetNode<MeshInstance3D>("IceCore");
        if (_runtime.Renderer.Effects.EnhancedSpells
            ? ice.Mesh != ElementChannelVisuals.Mesh(ElementChannelVisuals.Ice)
            : ice.Mesh != ElementChannelVisuals.LegacyIce) return false;
        _runtime.Renderer.Clear();
        var wizard = VisualFixture.Create("stone").Players[0] with { Shield = 650, ShieldMax = 650, Frozen = 0 };
        void Render(WizardSnapshot player) => _runtime.Renderer.Render(empty with { Players = [player] }, player.Id, 1/60.0, true);
        Render(wizard);
        var visual = _runtime.Renderer.Wizards[wizard.Id];
        if (visual.StoneArmorPlates != 6) return false;
        Render(wizard with { Shield = 40 }); if (visual.StoneArmorPlates != 4) return false;
        foreach (var inactive in new[] { wizard with { Shield = 0 }, wizard with { Alive = false },
            wizard with { Frozen = 1 }, wizard with { Ward = [] } })
        { Render(inactive); if (visual.StoneArmorPlates != 0) return false; }
        Render(wizard); if (visual.StoneArmorPlates != 6) return false;
        _runtime.Renderer.Clear();
        if (_runtime.Renderer.ProjectileVisuals.Count != 0 || _runtime.Renderer.Wizards.Count != 0) return false;
        GD.Print("STONE_VISUALS_OK: cuatro mallas cerradas reutilizadas, normales, radio, cinco recetas, hielo, integridad de armadura y limpieza.");
        return true;
    }
}

using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckStoneAuras()
    {
        Color Expected(Element element) => element switch
        {
            Element.Fire => new("ff3026"), Element.Water => new(Ui.IsMobile ? "2985ff" : "2791ff"),
            Element.Cold => new(Ui.IsMobile ? "1ddecb" : "10d7cd"), Element.Life => new(Ui.IsMobile ? "30ed69" : "2ce273"),
            Element.Arcane => new(Ui.IsMobile ? "ff3b9d" : "ff369f"), _ => throw new InvalidDataException("Elemento inesperado en una roca.")
        };
        var empty = VisualFixture.Create("none") with { Players = [], Walls = [], Fields = [], Beams = [], Projectiles = [], Events = [] };
        var recipes = SpellCatalogue.All.Where(c => c.Recipe.Shape == SpellShape.Rock).ToArray();
        var covered = new HashSet<Element>(); int mixed = 0;
        var probe = new CombatEffects(); AddChild(probe);
        bool ColoredParticle(Element element) => probe.GetChildren().OfType<MeshInstance3D>().Any(m => m.Visible &&
            m.MaterialOverride is StandardMaterial3D material && material.AlbedoColor.IsEqualApprox(Expected(element)) &&
            (element == Element.Fire ? m.Mesh is CylinderMesh : element == Element.Cold ? m.Mesh is PrismMesh : m.Mesh is SphereMesh));
        foreach (var combination in recipes)
        {
            var elements = combination.Elements;
            var secondary = elements.Where(e => e != Element.Earth).Distinct().OrderBy(e => e).ToArray();
            _runtime.Renderer.Clear();
            var shot = empty with { Projectiles = [new(1, 1, V2.Zero, new(1, 0), elements, 0.3f)] };
            _runtime.Renderer.Render(shot, 0, 1/60.0, true);
            var root = _runtime.Renderer.ProjectileVisuals[1];
            var core = root.GetNode<MeshInstance3D>("StoneCore");
            var aura = root.GetNodeOrNull<MeshInstance3D>("StoneAura");
            if (secondary.Length == 0) { if (aura != null) return false; continue; }
            if (aura == null || aura.Mesh != core.Mesh || aura.Scale.X <= core.Scale.X || aura.Scale.X > 0.43f ||
                aura.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off || aura.MaterialOverride is not ShaderMaterial skin) return false;
            if (!skin.GetShaderParameter("accent").AsColor().IsEqualApprox(Expected(secondary[0])) ||
                !skin.GetShaderParameter("secondary_accent").AsColor().IsEqualApprox(Expected(secondary[^1])) ||
                skin.GetShaderParameter("mixed").AsSingle() != (secondary.Length == 2 ? 1 : 0)) return false;
            if (secondary.Length == 2) mixed++;
            covered.UnionWith(secondary);
            // La receta repetida reutiliza el material; la piedra pura nunca hereda el aura anterior.
            var material = aura.MaterialOverride;
            _runtime.Renderer.Clear(); _runtime.Renderer.Render(shot, 0, 1/60.0, true);
            if (_runtime.Renderer.ProjectileVisuals[1].GetNode<MeshInstance3D>("StoneAura").MaterialOverride != material) return false;
            probe.Clear(); probe.Advance(shot, 0.04f, true); probe.Advance(shot, 0.04f, true);
            if (secondary.Any(e => !ColoredParticle(e))) return false;
            probe.Clear(); probe.Play(new(1, "impact", V2.Zero, elements, ""));
            if (secondary.Any(e => !ColoredParticle(e))) return false;
            if (probe.ActiveParticles > CombatEffects.EmissionBudget || probe.ActiveParticles > CombatEffects.ParticleLimit) return false;
        }
        bool valid = covered.SetEquals([Element.Fire, Element.Water, Element.Cold, Element.Life, Element.Arcane]) && mixed > 0;
        _runtime.Renderer.Clear(); probe.Clear(); probe.QueueFree();
        if (valid) GD.Print($"STONE_AURAS_OK: {recipes.Length} recetas, cinco colores, {mixed} mezclas bicolor, estelas, impactos, radio y materiales reutilizados.");
        return valid;
    }
}

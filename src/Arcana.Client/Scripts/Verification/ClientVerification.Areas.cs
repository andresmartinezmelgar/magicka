using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckAreaAndPassiveVisuals()
    {
        var renderer = _runtime.Renderer;
        renderer.Clear();
        var fields = VisualFixture.AreaAndPassiveScene(10, "areas");
        renderer.Render(fields, 1, 1/60.0, true);
        bool valid = renderer.Fields.Count == 6;
        foreach (var f in fields.Fields)
        {
            var area = (AreaFieldVisual)renderer.Fields[f.Id];
            var boundary = area.GetNode<MeshInstance3D>("AreaBoundary");
            bool mine = f.Kind is "mine" or "healing_mine";
            valid &= area.Pattern == AreaVisuals.GroundPattern(f.Kind) && area.Radius == f.Radius &&
                boundary.Scale.X == f.Radius && boundary.Scale.Z == f.Radius &&
                boundary.Mesh == AreaVisuals.Shape(area.Pattern) && boundary.Mesh.GetSurfaceCount() == 1 &&
                area.GetChildren().OfType<MeshInstance3D>().Count() == (mine ? 2 : 1) &&
                area.GetChildren().OfType<MeshInstance3D>().All(m => m.Visible && m.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off);
            if (mine)
            {
                var centerMarker = area.GetNode<MeshInstance3D>("MineCore");
                var effectArt = area.GetNode<Sprite3D>("AreaRuneVisual0");
                // La distancia de activación (0,85) no es el alcance del efecto (2,4).
                valid &= area.TriggerRadius == f.TriggerRadius &&
                    centerMarker.Scale.X == AreaFieldVisual.MineCenterMarkerRadius &&
                    centerMarker.Scale.Z == AreaFieldVisual.MineCenterMarkerRadius &&
                    effectArt.Texture != null &&
                    Mathf.IsEqualApprox(effectArt.PixelSize * 160f * 0.5f, f.Radius) &&
                    effectArt.RotationDegrees.X == -90f &&
                    boundary.Scale.X == f.Radius && boundary.Scale.Z == f.Radius;
            }
        }
        var original = renderer.Fields.ToDictionary(p => p.Key, p => p.Value);
        for (int i = 1; i <= 30; i++) renderer.Render(fields with { Tick = 10+i }, 1, 1/60.0, true);
        valid &= original.All(p => ReferenceEquals(renderer.Fields[p.Key], p.Value) && p.Value.Scale == Vector3.One);
        foreach (var pattern in Enum.GetValues<AreaVisuals.Pattern>())
        {
            var mesh = AreaVisuals.Shape(pattern);
            var faces = mesh.GetFaces();
            valid &= mesh.GetSurfaceCount() == 1 && faces.Length is > 0 and <= 384*3 &&
                faces.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
            if (pattern != AreaVisuals.Pattern.LightningLink)
                valid &= faces.All(v => new Vector2(v.X, v.Z).Length() <= 1.001f);
        }
        renderer.Render(fields with { Tick = 41, Fields = [] }, 1, 1/60.0, true);
        valid &= renderer.Fields.Count == 0;

        renderer.Clear();
        var auras = VisualFixture.AreaAndPassiveScene(1, "auras");
        renderer.Render(auras, 1, 1/60.0, true);
        var auraNodes = renderer.DefenseAuraVisuals.ToDictionary(p => p.Key, p => p.Value);
        foreach (var defender in auras.Defense!.Defenders)
        for (int i = 0; i < 4; i++)
        {
            var mesh = auraNodes[(defender.PlayerId,i)];
            bool active = i == 3 ? defender.BerserkerRemaining > 0 : defender.Auras[i] > 0;
            valid &= mesh.Visible == active && mesh.Mesh == AreaVisuals.Shape(AreaVisuals.AuraPattern(i));
            if (active) valid &= mesh.Scale.X == (i == 3 ? 1.6f : DefenseRules.AuraRadius(defender.Auras[i]));
        }
        var upgraded = auras with { Tick = 2, Defense = auras.Defense with
            { Defenders = auras.Defense.Defenders.Select(d => d with { Auras = [10,10,10], BerserkerRemaining = 0 }).ToArray() } };
        renderer.Render(upgraded, 1, 1/60.0, true);
        foreach (var p in auraNodes)
            valid &= ReferenceEquals(renderer.DefenseAuraVisuals[p.Key],p.Value) &&
                (p.Key.Aura == 3 ? !p.Value.Visible : p.Value.Visible && p.Value.Scale.X == DefenseRules.AuraRadius(10));
        renderer.Render(upgraded with { Tick = 3,
            Players = upgraded.Players.Select(p => p with { Alive = false, Health = 0 }).ToArray() }, 1, 1/60.0, true);
        valid &= renderer.DefenseAuraVisuals.Values.All(m => !m.Visible);
        renderer.Clear(); valid &= renderer.DefenseAuraVisuals.Count == 0;

        var probe = new CombatEffects(); AddChild(probe);
        var empty = fields with { Players = [], Fields = [], Events = [] };
        probe.Play(new(1,"nova",V2.Zero,[Element.Cold],"Pulso glacial",5));
        probe.Play(new(2,"nova",V2.Zero,[Element.Fire,Element.Arcane],"Berserker",1.8f));
        valid &= probe.ActiveRings == 2 && probe.GetChildren().OfType<MeshInstance3D>().Any(m =>
            m.Visible && m.Mesh == AreaVisuals.Shape(AreaVisuals.Pattern.FrostPulse));
        probe.Clear();
        probe.Play(new(3,"explosion",V2.Zero,[Element.Earth,Element.Fire],"Meteorito",3));
        valid &= probe.ActiveParticles == 22 && probe.ActiveRings == 1;
        probe.Clear();
        probe.Play(new(4,"passive_chain",V2.Zero,[Element.Lightning],""));
        probe.Play(new(5,"hurt",new(2,0),[Element.Lightning],"Relámpago"));
        probe.Play(new(6,"hurt",new(2,3),[Element.Lightning],"Relámpago"));
        var links = probe.GetChildren().OfType<MeshInstance3D>().Where(m => m.Visible &&
            m.Mesh == AreaVisuals.Shape(AreaVisuals.Pattern.LightningLink) && Math.Abs(m.Position.Y-0.85f)<0.001f).ToArray();
        valid &= probe.ActiveParticles == 6 && links.Length == 2;
        for (int i = 0; i < links.Length; i++)
        {
            Vector3 start = i == 0 ? new(0,0.85f,0) : new(2,0.85f,0);
            Vector3 end = i == 0 ? new(2,0.85f,0) : new(2,0.85f,3);
            valid &= links[i].Position.IsEqualApprox(start) && (links[i].Transform * Vector3.Right).IsEqualApprox(end);
        }
        probe.Clear();
        probe.Play(new(7,"passive_chain",V2.Zero,[Element.Lightning],""));
        probe.Advance(empty,0.05f,false);
        probe.Play(new(8,"hurt",new(2,0),[Element.Lightning],"Relámpago"));
        valid &= probe.ActiveParticles == 3 && !probe.GetChildren().OfType<MeshInstance3D>().Any(m => m.Visible &&
            m.Mesh == AreaVisuals.Shape(AreaVisuals.Pattern.LightningLink) && Math.Abs(m.Position.Y-0.85f)<0.001f);
        for (int frame = 0; frame < 40; frame++)
        {
            for (int chain = 0; chain < 8; chain++)
            {
                probe.Play(new(9,"passive_chain",V2.Zero,[Element.Lightning],""));
                for (int i = 0; i < 6; i++) probe.Play(new(10+i,"hurt",new(i+1,0),[Element.Lightning],"Relámpago"));
            }
            probe.Advance(empty,0.05f,true);
            valid &= probe.ActiveParticles <= CombatEffects.ParticleLimit && probe.ActiveRings <= CombatEffects.RingLimit &&
                probe.EmittedLastFrame <= CombatEffects.EmissionBudget;
        }
        for (int i = 0; i < 30; i++) probe.Advance(empty,0.05f,false);
        valid &= probe.ActiveCount == 0; probe.Clear(); probe.QueueFree(); renderer.Clear();
        if (valid) GD.Print("AREA_PASSIVE_VISUALS_OK: radio de efecto visible, marcador de activación central, 1/2 superficies, mallas compartidas, auras acumuladas, muerte, meteorito, hielo, furia, enlaces reales, descarte, límites y limpieza.");
        return valid;
    }
}

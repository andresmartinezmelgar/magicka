using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool CheckShieldMeteorVisuals()
    {
        var empty = VisualFixture.Create("none") with { Players=[], Walls=[], Fields=[], Beams=[], Projectiles=[], Events=[] };
        var recipes = SpellCatalogue.All.Where(c => c.Recipe.Has(Element.Shield) && !c.Recipe.Healing && !c.Recipe.Has(Element.Arcane)).ToArray();
        _renderer.Clear();
        foreach (var combination in recipes)
        {
            var wall = new WallSnapshot(1,1,new(-3,0),new(3,0),650,7,combination.Elements);
            _renderer.Render(empty with { Tick=1, Walls=[wall] },0,1/60.0,true);
            bool elemental = combination.Elements.Any(e => e is Element.Water or Element.Fire or Element.Cold or Element.Lightning);
            var visual = _renderer.Walls[1];
            if (visual.Stone != combination.Recipe.Has(Element.Earth) || visual.HasElementSurface != elemental) return false;
            _renderer.Render(empty with { Tick=2, Walls=[wall with { Health=0, Lifetime=0 }] },0,1/60.0,true);
            if (visual.Visible) return false;
            _renderer.Render(empty with { Tick=3 },0,1/60.0,true);
            if (_renderer.Walls.Count != 0) return false;
        }
        var field = new FieldSnapshot(1,1,V2.Zero,4,1.5f,[Element.Earth,Element.Fire,Element.Fire],"meteor");
        _renderer.Render(empty with { Tick=4, Fields=[field] },0,1/60.0,true);
        var meteor = (MeteorVisual)_renderer.Fields[1]; float height = meteor.Height;
        for (int i=1;i<=84;i++)
        {
            _renderer.Render(empty with { Tick=i+4, Fields=[field with { Lifetime=1.5f-i/60f }] },0,1/60.0,true);
            if (meteor.Height > height + 0.0001f || meteor.Height < 0.65f) return false;
            height = meteor.Height;
        }
        if (Math.Abs(height-0.65f)>0.001f || meteor.Radius != 4) return false;
        var probe = new CombatEffects(); AddChild(probe);
        probe.Play(new(1,"explosion",V2.Zero,field.Elements,"Meteoro",4));
        bool valid = probe.ActiveParticles == 30 && probe.ActiveRings == 3;
        var crowded = empty with { Fields=Enumerable.Range(1,40).Select(i=>field with { Id=i, Lifetime=0.3f }).ToArray(),
            Walls=Enumerable.Range(1,40).Select(i=>new WallSnapshot(i,1,new(-3,0),new(3,0),650,7,[Element.Shield,Element.Fire,Element.Lightning])).ToArray() };
        for (int i=0;i<40;i++)
        {
            probe.Play(new(i+2,"explosion",V2.Zero,field.Elements,"Meteoro",4));
            probe.Advance(crowded,0.05f,true);
            valid &= probe.ActiveParticles <= CombatEffects.ParticleLimit && probe.ActiveRings <= CombatEffects.RingLimit &&
                probe.EmittedLastFrame <= CombatEffects.EmissionBudget;
        }
        for (int i=0;i<30;i++) probe.Advance(empty,0.05f,false);
        valid &= probe.ActiveCount == 0; probe.Clear(); probe.QueueFree(); _renderer.Clear();
        valid &= _renderer.Fields.Count == 0 && _renderer.Walls.Count == 0;
        if (valid) GD.Print($"SHIELD_METEOR_OK: {recipes.Length} combinaciones de barrera, caducidad, caída, radio de 4 m, impacto y saturación");
        return valid;
    }
}

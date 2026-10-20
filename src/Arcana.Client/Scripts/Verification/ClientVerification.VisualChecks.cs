using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckShieldMeteorVisuals()
    {
        var empty = VisualFixture.Create("none") with { Players=[], Walls=[], Fields=[], Beams=[], Projectiles=[], Events=[] };
        var recipes = SpellCatalogue.All.Where(c => c.Recipe.Has(Element.Shield) && !c.Recipe.Healing && !c.Recipe.Has(Element.Arcane)).ToArray();
        _runtime.Renderer.Clear();
        foreach (var combination in recipes)
        {
            var wall = new WallSnapshot(1,1,new(-3,0),new(3,0),650,7,combination.Elements);
            _runtime.Renderer.Render(empty with { Tick=1, Walls=[wall] },0,1/60.0,true);
            bool elemental = combination.Elements.Any(e => e is Element.Water or Element.Fire or Element.Cold or Element.Lightning);
            var visual = _runtime.Renderer.Walls[1];
            if (visual.Stone != combination.Recipe.Has(Element.Earth) || visual.HasElementSurface != elemental ||
                visual.Charges != combination.Elements.Length) return false;
            if (visual.Stone && (visual.StoneDrawMeshes != 3 || visual.StoneDrawMeshes >= visual.StoneSourceMeshes ||
                visual.StoneSourceVertices <= 0 || visual.StoneBatchedVertices != visual.StoneSourceVertices)) return false;
            if (!visual.Stone)
            {
                var geometry = visual.GetChildren().OfType<Node3D>().First();
                var color = geometry.GetNode<MeshInstance3D>("BarrierArchColor");
                if (geometry.HasNode("BarrierArchOutline") || color.Mesh.GetSurfaceCount() != 1 ||
                    color.Mesh.GetAabb().Size.Z < 6 || color.Mesh.GetAabb().Size.Y < 0.65f ||
                    geometry.GetChildren().OfType<MeshInstance3D>().Count() > 32) return false;
            }
            _runtime.Renderer.Render(empty with { Tick=2, Walls=[wall with { Health=0, Lifetime=0 }] },0,1/60.0,true);
            if (visual.Visible) return false;
            _runtime.Renderer.Render(empty with { Tick=3 },0,1/60.0,true);
            if (_runtime.Renderer.Walls.Count != 0) return false;
        }
        float StoneVolume(Element[] elements)
        {
            _runtime.Renderer.Clear();
            _runtime.Renderer.Render(empty with { Tick = 3, Walls = [new(1, 1, new(-3, 0), new(3, 0), 1300, 10, elements)] }, 0, 1/60.0, true);
            var geometry = _runtime.Renderer.Walls[1].GetChildren().OfType<Node3D>().First();
            return geometry.GetChildren().OfType<MeshInstance3D>().Where(m => m.Mesh is ArrayMesh)
                .Sum(m => { var size = m.Mesh.GetAabb().Size; return size.X * size.Y * size.Z; });
        }
        float basicVolume = StoneVolume([Element.Shield, Element.Earth]);
        float reinforcedVolume = StoneVolume([Element.Shield, Element.Earth, Element.Earth]);
        if (reinforcedVolume < basicVolume * 1.8f) return false;
        _runtime.Renderer.Clear();
        var wizard = VisualFixture.Create("full").Players[0];
        for (int charges = 1; charges <= 3; charges++)
        {
            _runtime.Renderer.Render(empty with { Players = [wizard with { ShieldCharges = charges }] }, wizard.Id, 1/60.0, true);
            var personal = _runtime.Renderer.Wizards[wizard.Id];
            if (personal.ProtectionBandCount != charges - 1 || personal.SpawnProtectionVisible ||
                personal.GetChildren().OfType<Sprite3D>().Any(n => n.Name.ToString().StartsWith("PersonalWardVisual")) ||
                personal.GetChildren().OfType<MeshInstance3D>().Any(n =>
                    n.Name.ToString().StartsWith("ShieldRuneCrystal") ||
                    n.Name.ToString().StartsWith("ShieldArcaneOrbit"))) return false;
        }
        _runtime.Renderer.Render(empty with { Players = [wizard with { Shield = 0, InvulnerableSeconds = 1 }] }, wizard.Id, 1/60.0, true);
        if (!_runtime.Renderer.Wizards[wizard.Id].SpawnProtectionVisible) return false;
        _runtime.Renderer.Render(empty with { Players = [wizard with { Shield = 0, InvulnerableSeconds = 0 }] }, wizard.Id, 1/60.0, true);
        if (_runtime.Renderer.Wizards[wizard.Id].SpawnProtectionVisible || _runtime.Renderer.Wizards[wizard.Id].ProtectionBandCount != 0) return false;
        _runtime.Renderer.Clear();
        _runtime.Renderer.Render(empty with { Mode = GameMode.Arena, Players = [wizard with {
            Position = new(-18, 0), Health = 0, Alive = false, Shield = 0, InvulnerableSeconds = 0 }] }, wizard.Id, 1/60.0, true);
        _runtime.Renderer.Render(empty with { Mode = GameMode.Arena, Players = [wizard with {
            Position = new(18, 0), Shield = 0, InvulnerableSeconds = 1 }] }, wizard.Id, 1/60.0, true);
        if (Math.Abs(_runtime.Renderer.Camera.Position.X - 7) > 0.001f || !_runtime.Renderer.Wizards[wizard.Id].SpawnProtectionVisible) return false;
        foreach (var mode in Enum.GetValues<GameMode>())
        {
            _runtime.Renderer.Clear();
            _runtime.Renderer.Render(empty with { Mode = mode, Time = 0, Players = [wizard with { Position = new(-5, 0), Deaths = 0 }] }, wizard.Id, 1/60.0, true);
            // Reaparición cercana sin recibir el estado muerto: tampoco se interpola la vida anterior.
            _runtime.Renderer.Render(empty with { Mode = mode, Time = 0.05, Players = [wizard with { Position = new(-4, 0), Deaths = 1 }] }, wizard.Id, 1/60.0, true);
            if (_runtime.Renderer.Wizards[wizard.Id].Position != ArenaRenderer.World(new(-4, 0)) ||
                Math.Abs(_runtime.Renderer.Camera.Position.X + 4) > 0.001f) return false;
        }
        _runtime.Renderer.Clear();
        var field = new FieldSnapshot(1,1,V2.Zero,4,1.5f,[Element.Earth,Element.Fire,Element.Fire],"meteor");
        _runtime.Renderer.Render(empty with { Tick=4, Fields=[field] },0,1/60.0,true);
        var meteor = (MeteorVisual)_runtime.Renderer.Fields[1]; float height = meteor.Height;
        for (int i=1;i<=84;i++)
        {
            _runtime.Renderer.Render(empty with { Tick=i+4, Fields=[field with { Lifetime=1.5f-i/60f }] },0,1/60.0,true);
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
        valid &= probe.ActiveCount == 0; probe.Clear(); probe.QueueFree(); _runtime.Renderer.Clear();
        valid &= _runtime.Renderer.Fields.Count == 0 && _runtime.Renderer.Walls.Count == 0;
        if (valid) GD.Print($"SHIELD_METEOR_OK: {recipes.Length} combinaciones de barrera, muro reforzado, bandas de escudo, protección y cámara de reaparición, caducidad, caída, radio de 4 m, impacto y saturación");
        return valid;
    }
}

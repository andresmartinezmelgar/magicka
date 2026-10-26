using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private WorldSnapshot MapFixture(ArenaMapId id)
    {
        var map = ArenaMap.Get(id); _runtime.ServerName = map.Name;
        var source = VisualFixture.Create("none");
        bool hold = id == ArenaMapId.Citadel;
        var items = map.HasPickups ? new GameWorld(new() { Map = id, PickupSeed = 271 }).Snapshot().Pickups!
            .Select(i => i.Id == 1 ? i with { Active = false, Respawn = 6 } : i).ToArray() : [];
        return source with { Map = id, HoldingTeam = hold ? 0 : -1, HoldSeconds = hold ? 7.5f : 0,
            Players = source.Players.Select(p => p with { Position = _previewMap != null ? new V2(0, -1) : map.Spawn(p.Team, p.Id),
                MaxHealth = id == ArenaMapId.Ascension ? 1030 : id == ArenaMapId.Relics && p.Id == 1 ? 1100 : 1000,
                Health = id == ArenaMapId.Ascension ? 1020 : p.Health,
                Buffs = id == ArenaMapId.Relics && p.Id == 1 ? [new(BuffKind.Power, 2), new(BuffKind.Vitality, 1)] : [] }).ToArray(),
            Pickups = items,
            Points = map.ControlPoints.Select((p, i) => new PointSnapshot(i, p, hold ? 0 : -1, hold ? -1 : 0)).ToArray() };
    }
    private void CheckMaps(WorldSnapshot snapshot)
    {
        var choice = (OptionButton)_runtime.Launcher.FindChild("PracticeMapChoice", true, false);
        var objective = (Label)_runtime.Hud!.FindChild("MatchObjective", true, false);
        var map = ArenaMap.Get(snapshot.Map);
        bool valid = choice.ItemCount == Enum.GetValues<ArenaMapId>().Length && _runtime.Renderer.CurrentMap == snapshot.Map && _runtime.Renderer.Points.Count == map.ControlPoints.Count &&
            _runtime.Renderer.SceneryDrawMeshes < _runtime.Renderer.ScenerySourceMeshes &&
            _runtime.Renderer.ScenerySourceVertices > 0 && _runtime.Renderer.ScenerySourceVertices == _runtime.Renderer.SceneryBatchedVertices &&
            _runtime.Renderer.SceneryTheme == map.Theme && (map.Theme == ArenaTheme.City ? _runtime.Renderer.SceneryBuildings >= 12 : _runtime.Renderer.SceneryTrees >= 20) &&
            _runtime.Renderer.GetChildren().Count(n => n.Name == "MapGeometry") == 1 &&
            snapshot.Points.All(p => _runtime.Renderer.Points[p.Id].Position == ArenaRenderer.World(p.Position));
        if (snapshot.Map == ArenaMapId.Citadel) valid &= objective.Text.Contains($"{snapshot.HoldSeconds:0.0} / 15 s") && Math.Abs(_runtime.Renderer.Camera.Position.X) > 40;
        else if (map.SequentialCapture) valid &= objective.Text.Contains("cinco") && !objective.Text.Contains("tickets") &&
            _runtime.Renderer.FindChildren("TeleportPortal*", "", true, false).Count == map.Teleports.Count * 2;
        else if (map.IsPractice) valid &= objective.Text.Contains("Sin puntos");
        else valid &= objective.Text.Contains("tickets");
        if (snapshot.Map == ArenaMapId.Ascension)
        {
            var health = (ProgressBar)_runtime.Hud.FindChild("HealthBar", true, false);
            valid &= objective.Text.Contains("+10") && objective.Text.Contains("10 s") && health.MaxValue == 1030 && health.Value == 1020;
        }
        if (map.HasPickups)
            valid &= _runtime.Renderer.Pickups.Count == 14 && !_runtime.Renderer.Pickups[1].Available && _runtime.Renderer.Pickups.Values.Count(i => i.Available) == 13
                && snapshot.Pickups!.All(i => _runtime.Renderer.Pickups[i.Id].Position == ArenaRenderer.World(i.Position));
        else valid &= _runtime.Renderer.Pickups.Count == 0;
        if (snapshot.Map == ArenaMapId.Relics)
        {
            var buffs = (Label)_runtime.Hud.FindChild("ActiveBuffs", true, false);
            valid &= !buffs.IsVisibleInTree() && buffs.Text.Length == 0 && objective.Text.Contains("hasta morir");
        }
        var local = snapshot.Players.Single(p => p.Id == _runtime.LocalId);
        valid &= _runtime.Renderer.Wizards[_runtime.LocalId].ElementSlotCount == 3 && _runtime.Renderer.Wizards[_runtime.LocalId].DisplayedElements.SequenceEqual(local.DisplayElements);
        foreach (var point in _runtime.Renderer.Points.Values)
        {
            var circle = point.GetNode<MeshInstance3D>("CaptureMagicCircle");
            var texture = ((StandardMaterial3D)circle.MaterialOverride).AlbedoTexture;
            valid &= texture != null && texture.GetWidth() == 512 && texture.GetHeight() == 512 &&
                !point.HasNode("CaptureContrast") && !point.HasNode("CaptureLabel") && point.HasNode("CaptureProgress");
        }
        if (_renderFrames >= 210) valid &= CheckCaptureCircleVisuals();
        if (!valid) { GD.PushError($"MAPS_SMOKE_FAILED: {map.Name}, cámara, selector, contador o geometría incorrectos"); GetTree().Quit(1); return; }
        GD.Print($"MAP_CASE_OK: {map.Name}, {map.Area} unidades²");
        if (_renderFrames >= 210) CompleteSmoke("MAPS_SMOKE_OK: seis mapas, cambios entre frente y laboratorio, geometría agrupada, portales, objetos y captura");
    }

    private bool CheckCaptureCircleVisuals()
    {
        var probe = new CapturePointVisual(); AddChild(probe); probe.Build(0, V2.Zero);
        var ring = (ShaderMaterial)probe.GetNode<MeshInstance3D>("CaptureProgress").MaterialOverride;
        bool valid = !probe.HasNode("CaptureLabel");
        probe.Update(new(0, V2.Zero, 0, -0.2f), 1, 0);
        valid &= Math.Abs(probe.DisplayedProgress + 0.2f) < 0.001f && ring.GetShaderParameter("team_color").AsColor().IsEqualApprox(Ui.Blue);
        probe.Update(new(0, V2.Zero, -1, -0.35f, true), 1, 0.1f);
        valid &= probe.Contested && Math.Abs(probe.DisplayedProgress + 0.35f) < 0.001f;
        float brightness = ring.GetShaderParameter("brightness").AsSingle();
        probe.Update(new(0, V2.Zero, -1, -0.35f, true), 1, 0.6f);
        valid &= Math.Abs(ring.GetShaderParameter("progress").AsSingle() - 0.35f) < 0.001f &&
            Math.Abs(ring.GetShaderParameter("brightness").AsSingle() - brightness) > 0.1f;
        probe.Update(new(0, V2.Zero, 1, 0.4f), 1, 0);
        valid &= !probe.Contested && probe.DisplayedProgress > 0 && ring.GetShaderParameter("team_color").AsColor().IsEqualApprox(Ui.Red);
        probe.Update(new(0, V2.Zero, 0, 0), 1, 0);
        valid &= Math.Abs(probe.DisplayedProgress + 1) < 0.001f;
        probe.Reset();
        valid &= probe.DisplayedProgress == 0 && !probe.Contested && !probe.HasNode("CaptureLabel") &&
            ring.GetShaderParameter("progress").AsSingle() == 0;
        probe.QueueFree();
        if (valid) GD.Print("CAPTURE_SPRITES_OK: seis recursos, neutralizacion, equipos, disputa sin variar porcentaje y reinicio limpio.");
        return valid;
    }
    private void CheckHudCase()
    {
        var expected = HudCases[(_renderFrames - 1) / 3];
        var health = (ProgressBar)_runtime.Hud!.FindChild("HealthBar", true, false);
        var charge = (ProgressBar)_runtime.Hud.FindChild("ChargeBar", true, false);
        var shield = (ProgressBar)_runtime.Hud.FindChild("ShieldBar", true, false);
        var text = (Label)_runtime.Hud.FindChild("ShieldValue", true, false);
        var playerPanel = (Control)_runtime.Hud.FindChild("PlayerStatusPanel", true, false);
        var minimap = (Control)_runtime.Hud.FindChild("MiniMapPanel", true, false);
        var metrics = (Control)_runtime.UiRoot.FindChild("NetworkMetricsPanel", true, false);
        bool correct = !metrics.GetGlobalRect().Intersects(minimap.GetGlobalRect()) &&
            GetViewport().GetVisibleRect().Encloses(metrics.GetGlobalRect()) &&
            _runtime.UiRoot.GetChildren().ToList().IndexOf(metrics) > _runtime.UiRoot.GetChildren().ToList().IndexOf(_runtime.Hud) &&
            _runtime.Hud.Scale.IsEqualApprox(Vector2.One * Hud.GameplayScale) && (_runtime.Hud.Size * Hud.GameplayScale).IsEqualApprox(GetViewport().GetVisibleRect().Size) &&
            playerPanel.GetGlobalTransform().Scale.IsEqualApprox(Vector2.One) && playerPanel.GetGlobalRect().Size.X >= 310 &&
            Math.Abs(playerPanel.GetGlobalRect().Position.X - 24) < 0.01f &&
            health.Value == 830 && health.MaxValue == 1000 && charge.Value == 0 && shield.Value == expected.Shield && shield.MaxValue == expected.Maximum &&
            (expected.Shield == 0 ? text.Text.Contains("INACTIVO") : text.Text.Contains($"{expected.Shield:0} / {expected.Maximum:0}") && text.Text.Contains(" s"));
        if (!correct) { GD.PushError($"HUD_SMOKE_FAILED: {expected.State}, {shield.Value}/{shield.MaxValue}, {text.Text}"); GetTree().Quit(1); return; }
        GD.Print($"HUD_CASE_OK: {expected.State}, {shield.Value}/{shield.MaxValue}");
        if (expected.State == HudCases[^1].State) CompleteSmoke("HUD_SMOKE_OK: 6 estados de escudo, vida, capacidad y duración");
    }
    private void CheckCombinations()
    {
        int index = (_renderFrames - 1) / 200;
        if (index >= CombinationCases.Length) return;
        var item = CombinationCases[index];
        if ((_renderFrames - 1) % 200 == 0) _runtime.SpellBook.SelectCombination(item.Keys, item.Mode, item.Kind);
        if (_renderFrames % 200 != 0) return;
        try
        {
            var state = _runtime.SpellBook.Demo.World.Snapshot(); SnapshotValidation.Check(state);
            if (!_runtime.SpellBook.IsVisibleInTree() || state.Tick < 180)
                throw new InvalidDataException("La pestaña de combinaciones está oculta o la demostración no avanza.");
            if (item.Kind == DemonstrationKind.Combination)
            {
                var damageText = (Label)_runtime.SpellBook.FindChild("SpellDamageValues", true, false);
                if (!damageText.IsVisibleInTree() || damageText.Text != SpellBalance.Format(SpellCatalogue.FromKeys(item.Keys)!.Recipe, item.Mode))
                    throw new InvalidDataException("F1 no muestra los valores de daño de la combinación y forma de lanzamiento.");
            }
            var effects = _runtime.SpellBook.Preview.Effects;
            bool valid = effects.PooledCount <= CombatEffects.ParticleLimit + CombatEffects.RingLimit;
            if (index == 0) valid &= !effects.SeenKinds.Contains("splash");
            if (index is >= 1 and <= 3) valid &= effects.SeenKinds.Contains("splash") && state.Fields.Length > 0;
            if (index == 4) valid &= state.Players[0].RegenerationRate == 38 && state.Players[0].Health > 500;
            if (index == 5) valid &= effects.SeenKinds.Contains("explosion");
            if (index == 6) valid &= effects.SeenKinds.Contains("mine_heal");
            if (index == 7) valid &= effects.SeenKinds.Contains("beam_merge");
            if (index == 8) valid &= effects.SeenKinds.Contains("beam_refract");
            if (index == 9) valid &= _runtime.SpellBook.Preview.Points[1].DisplayedProgress < -0.5f;
            if (!valid) throw new InvalidDataException("No se representó el comportamiento esperado.");
            GD.Print($"COMBINATION_CASE_OK: {item.Keys} {item.Mode} {item.Kind}");
            if (index == CombinationCases.Length - 1)
            CompleteSmoke($"COMBINATIONS_SMOKE_OK: {SpellCatalogue.All.Length} combinaciones y 10 escenas verificadas");
        }
        catch (Exception e) { GD.PushError($"COMBINATIONS_SMOKE_FAILED: {index}, {e.Message}"); GetTree().Quit(1); }
    }
    private void CheckEffects()
    {
        var effects = _runtime.Renderer.Effects;
        bool valid = effects.ActiveParticles <= CombatEffects.ParticleLimit && effects.ActiveRings <= CombatEffects.RingLimit &&
            effects.PooledCount <= CombatEffects.ParticleLimit + CombatEffects.RingLimit && effects.EmittedLastFrame <= CombatEffects.EmissionBudget;
        if (_renderFrames == 130) valid &= effects.ActiveCount > 0 && effects.SeenKinds.Count == 12 &&
            ((Label)_runtime.Hud!.FindChild("WizardAffinity", true, false)).Text == "Piromante";
        if (_renderFrames == 240)
        {
            valid &= effects.ActiveCount == 0 && effects.SeenCharges.SetEquals([1,2,3]);
            if (valid) valid = CheckSpellStrength();
            if (valid) valid = CheckShieldMeteorVisuals();
            if (valid) valid = CheckFlamethrowerVisuals();
            if (valid) valid = CheckElementChannelVisuals();
            if (valid) valid = CheckMythicEyesVisuals();
            if (valid) valid = CheckStoneVisuals();
            if (valid) valid = CheckStoneAuras();
            if (valid) valid = CheckAreaAndPassiveVisuals();
            if (valid) valid = CheckInstanceBatches();
            _runtime.Renderer.Clear(); valid &= effects.ActiveCount == 0 && _runtime.Renderer.ActiveBeamSegments == 0;
            if (valid) CompleteSmoke("EFFECTS_SMOKE_OK: 1/2/3 cargas, elementos repetidos, 12 eventos, límites, expiración, reutilización y limpieza");
        }
        if (!valid) { GD.PushError($"EFFECTS_SMOKE_FAILED: frame={_renderFrames}, active={effects.ActiveCount}, particles={effects.ActiveParticles}, rings={effects.ActiveRings}, pool={effects.PooledCount}, emitted={effects.EmittedLastFrame}, kinds={string.Join(",", effects.SeenKinds)}, charges={string.Join(",", effects.SeenCharges)}, affinity={((Label)_runtime.Hud!.FindChild("WizardAffinity", true, false)).Text}"); GetTree().Quit(1); }
    }
    private bool CheckSpellStrength()
    {
        var probe = new CombatEffects(); AddChild(probe);
        var empty = _runtime.Snapshot! with { Players = [], Projectiles = [], Beams = [], Fields = [], Walls = [], Points = [], Events = [], Pickups = [] };
        var impacts = new int[3]; var trails = new int[3]; var waves = new int[3];
        bool valid = true;
        for (int charges = 1; charges <= 3; charges++)
        {
            var elements = Enumerable.Repeat(Element.Earth, charges).ToArray();
            probe.Clear(); probe.Play(new(1,"impact",V2.Zero,elements,"")); impacts[charges-1] = probe.ActiveParticles;
            probe.Clear(); probe.Play(new(1,"nova",V2.Zero,elements,"",4)); waves[charges-1] = probe.ActiveRings;
            probe.Clear();
            var shot = empty with { Projectiles = [new(1,1,V2.Zero,new(1,0),elements,0.3f)] };
            probe.Advance(shot,0.04f,true); probe.Advance(shot,0.04f,true); trails[charges-1] = probe.ActiveParticles;
            _runtime.Renderer.Render(shot,0,1/60.0,true);
            var meshes = _runtime.Renderer.ProjectileVisuals[1].GetChildren().OfType<MeshInstance3D>().ToArray();
            valid &= meshes.Length == charges && meshes[0].Name == "StoneCore" && meshes[0].Mesh is ArrayMesh &&
                meshes[0].Scale.IsEqualApprox(Vector3.One * 0.3f);
            _runtime.Renderer.Clear();
        }
        valid &= impacts[0] > 0 && impacts[1] > impacts[0] && impacts[2] > impacts[1] && impacts[2] >= impacts[0]*2.5f;
        valid &= trails[0] > 0 && trails[1] > trails[0] && trails[2] > trails[1] && waves[0] == 1 && waves[1] == 1 && waves[2] == 2;
        foreach (var shape in new[] { SpellShape.Beam, SpellShape.Spray, SpellShape.Force, SpellShape.Lightning })
        {
            int previous = 0;
            for (int charges = 1; charges <= 3; charges++)
            {
                var elements = Enumerable.Repeat(Element.Arcane,charges).ToArray();
                _runtime.Renderer.Render(empty with { Beams = [new(1,new(-3,0),new(3,0),elements,shape)] },0,1/60.0,true);
                valid &= _runtime.Renderer.ActiveBeamSegments > previous; previous = _runtime.Renderer.ActiveBeamSegments;
            }
        }
        // Un hechizo ya lanzado conserva su nivel aunque se preparen tres elementos para el siguiente.
        var steady = empty with { Beams = [new(1, new(-3, 0), new(3, 0), [Element.Arcane], SpellShape.Beam)] };
        _runtime.Renderer.Render(steady, 0, 1/60.0, true);
        _runtime.Renderer.Render(steady with { Beams = [steady.Beams[0] with { To = new(7, 0) }] }, 0, 1/60.0, true);
        valid &= _runtime.Renderer.BeamGeometryUpdates == 0 && _runtime.Renderer.ActiveBeamSegments > 0;
        probe.Clear();
        var caster = _runtime.Snapshot!.Players[0] with { Burning=0,Wet=0,Chill=0,Frozen=0,Haste=0,RegenerationRate=0,
            Casting=true,Charge=0,Selected=[Element.Fire,Element.Fire,Element.Fire],CastingElements=[Element.Arcane] };
        for (int i=0;i<3;i++) probe.Advance(empty with { Players=[caster] },0.05f,true);
        valid &= probe.ActiveParticles == 1;
        probe.Clear();
        for (int frame=0;frame<40;frame++)
        {
            for (int i=0;i<20;i++) probe.Play(new(frame*20+i,"nova",V2.Zero,[Element.Fire,Element.Fire,Element.Fire],"",4));
            probe.Advance(empty,0.05f,true);
            valid &= probe.ActiveParticles <= CombatEffects.ParticleLimit && probe.ActiveRings <= CombatEffects.RingLimit &&
                probe.EmittedLastFrame <= CombatEffects.EmissionBudget;
        }
        int allocated = probe.PooledCount; probe.Clear(); probe.Play(new(1,"impact",V2.Zero,[Element.Fire],""));
        valid &= probe.ActiveParticles > 0 && probe.PooledCount == allocated;
        for (int i=0;i<25;i++) probe.Advance(empty,0.05f,false);
        valid &= probe.ActiveCount == 0; probe.QueueFree();
        if (valid) GD.Print($"SPELL_STRENGTH_OK: impactos {string.Join('/',impacts)}, estelas {string.Join('/',trails)}, anillos {string.Join('/',waves)}, 4 formas y saturación comprobadas");
        return valid;
    }
    private void CheckSprites(WorldSnapshot snapshot)
    {
        bool valid = _runtime.Renderer.Wizards.Count == 10;
        foreach (var player in snapshot.Players)
        {
            if (!_runtime.Renderer.Wizards.TryGetValue(player.Id, out var wizard)) { valid = false; continue; }
            var sprite = wizard.Sprite;
            var atlas = WizardSprites.Load(player.WizardType);
            valid &= sprite.Texture == atlas.Texture && sprite.IsVisibleInTree() == player.Alive &&
                sprite.Frame is >= 0 and < 16 && sprite.Offset == atlas.Offset(sprite.Frame);
            valid &= wizard.ElementSlotCount == 3 && wizard.ElementSlotsVisible == player.Alive;
            if (player.Alive && player.Frozen <= 0) _spriteCoverage.Add((player.WizardType, player.Team, sprite.Frame / 4, sprite.Frame % 4));
            if (player.Frozen > 0) valid &= sprite.Frame % 4 == 0;
            if (_renderFrames == 7) valid &= sprite.Frame % 4 == 3; // Evento instantáneo, sin Casting en el snapshot.
            var material = (ShaderMaterial)sprite.MaterialOverride;
            valid &= material.GetShaderParameter("team_color").AsColor() == (player.Team == 0 ? Ui.Blue : Ui.Red);
        }
        if (_renderFrames == 270)
        {
            // Cinco atlas compartidos, ambos equipos, cuatro direcciones y tres poses usadas.
            valid &= _spriteCoverage.Count == 120;
            valid &= CheckWizardMotion();
            valid &= CheckWizardFacing();
            valid &= CheckTranslationInput();
            valid &= CheckRespawnInput();
            _runtime.Renderer.Clear(); valid &= _runtime.Renderer.Wizards.Count == 0;
            if (valid) CompleteSmoke("SPRITES_SMOKE_OK: 5 atlas, 120 combinaciones, equipos, anclajes, congelación, muerte, reaparición y limpieza");
        }
        if (!valid) { GD.PushError($"SPRITES_SMOKE_FAILED: frame {_renderFrames}, cobertura {_spriteCoverage.Count}"); GetTree().Quit(1); }
    }
}

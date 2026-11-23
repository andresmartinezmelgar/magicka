using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class CombatEffects
{
    public void Advance(WorldSnapshot snapshot, float delta, bool playing)
    {
        float dt = Math.Clamp(delta, 0, 0.05f);
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var p = _sparks[i]; p.Life -= dt;
            if (p.Life <= 0)
            {
                if (p.Shape == FlameVisuals.Shape) _activeFlames--;
                if (p.Shape >= FlameVisuals.Shape) _activeChannels--;
                if (p.Shape == ElementChannelVisuals.Steam) _activeSteam--;
                if (p.Node != null) p.Node.Visible = false;
                _freeSparks.Push(p); _sparks[i] = _sparks[^1]; _sparks.RemoveAt(_sparks.Count - 1); continue;
            }
            if (p.Shape >= FlameVisuals.Shape) AdvanceChannel(p);
            else if (p.FixedSpan)
                p.Node!.Scale = new(p.Size.X, 1, Math.Max(0.04f, p.Life / p.Duration));
            else
            {
                p.Velocity -= Vector3.Up * p.Gravity * dt; p.Position += p.Velocity * dt;
                p.Scale = p.Size * Math.Max(0.04f, p.Life / p.Duration); p.Angle += dt * 4;
                if (p.Shape == 4)
                { p.Node!.Position = p.Position; p.Node.Scale = p.Scale; p.Node.RotateY(dt * 4); }
            }
        }
        for (int i = _waves.Count - 1; i >= 0; i--)
        {
            var w = _waves[i]; w.Life -= dt;
            if (w.Life <= 0)
            {
                w.Node.Visible = false; _freeWaves.Push(w); _waves[i] = _waves[^1]; _waves.RemoveAt(_waves.Count - 1); continue;
            }
            float radius = Mathf.Lerp(w.Start, w.End, 1 - w.Life / w.Duration);
            w.Node.Scale = new(radius, Math.Max(0.03f, w.Life / w.Duration) * 0.6f, radius);
        }
        AdvanceWaterJets(snapshot, dt, playing);
        if (!playing) { FinishFrame(); return; }
        EmitElementChannels(snapshot, dt);
        _trailClock += dt; _statusClock += dt;
        if (_trailClock >= 0.065f)
        {
            _trailClock %= 0.065f;
            foreach (var p in snapshot.Projectiles)
            {
                bool stone = p.Elements.Contains(Element.Earth);
                var imbuements = stone ? StoneVisuals.Imbuements(p.Elements) : [];
                var strength = SpellVisualStrength.For(p.Elements); SeenCharges.Add(strength.Charges);
                var color = !stone && p.Elements.Contains(Element.Cold) ? ElementChannelVisuals.IceBody : Ui.SpellColor(p.Elements);
                var forward = ArenaRenderer.World(p.Direction);
                var side = forward.Cross(Vector3.Up);
                for (int i = 0; i < strength.TrailSamples; i++)
                {
                    float offset = (i - (strength.TrailSamples - 1) * 0.5f) * 0.085f;
                    Element? infusion = imbuements.Length > 0 && i > 0 ? imbuements[(i - 1) % imbuements.Length] : null;
                    Emit(ArenaRenderer.World(p.Position, 0.9f) + side * offset,
                        infusion is { } element ? StoneVisuals.AuraColor(element) : stone ? StoneVisuals.ChipColor(i) : color,
                        forward * -1.3f + side * offset, strength.Lifetime,
                        infusion is { } imbued ? StoneVisuals.ParticleShape(imbued) : stone ? 3 : p.Elements.Contains(Element.Cold) ? 1 : 0,
                        strength.SparkSize, stone && infusion == null ? 3 : 0);
                }
            }
            foreach (var b in snapshot.Beams)
            {
                if (UsesChannelParticles(b)) continue;
                var strength = SpellVisualStrength.For(b.Elements); SeenCharges.Add(strength.Charges);
                bool cone = b.Shape == SpellShape.Spray;
                bool steam = ElementChannelVisuals.IsSteam(b.Elements);
                bool fire = cone && b.Elements.Contains(Element.Fire) && !steam;
                bool frost = cone && b.Elements.Contains(Element.Cold);
                bool water = cone && b.Elements.Contains(Element.Water);
                var forward = ArenaRenderer.World(b.To - b.From).Normalized();
                var side = forward.Cross(Vector3.Up).Normalized();
                var baseColor = steam ? new Color("dce8ec") : Ui.SpellColor(b.Elements);
                int samples = strength.ChannelSamples + (cone ? strength.Charges : 0);
                for (int i = 0; i < samples; i++)
                {
                    float t = (i + (float)_random.NextDouble()) / samples;
                    float width = cone ? V2.Distance(b.From, b.To) * 0.18f * t : 0;
                    float lateral = ((float)_random.NextDouble() * 2 - 1) * width;
                    var at = ArenaRenderer.World(V2.Lerp(b.From, b.To, t), 1)
                        + side * lateral;
                    Color accent = steam ? new Color("edf1f3") : fire ? new Color("ffd78b") : frost ? new Color("d5f5ff")
                        : water ? new Color("92eaff") : baseColor;
                    Color particleColor = i % 3 == 0 ? accent : baseColor;
                    Emit(at, particleColor, forward * (cone ? 1.55f : 0.35f)
                        + side * (cone ? lateral * 0.38f : 0) + Vector3.Up * (steam ? .65f : fire ? 0.45f : 0.06f),
                        steam ? .4f : cone ? 0.30f : 0.20f,
                        fire ? 2 : frost ? 1 : 0,
                        strength.SparkSize * (cone ? 0.92f : 0.7f), fire ? -0.15f : 0);
                }
            }
            foreach (var meteor in snapshot.Fields)
            {
                if (meteor.Kind != "meteor") continue;
                var at = ArenaRenderer.World(meteor.Position) + MeteorVisual.FlightPosition(meteor.Lifetime);
                var trail = MeteorVisual.TrailDirection(meteor.Lifetime);
                for (int i = 0; i < 3; i++)
                    Emit(at + trail * (0.6f + i * 0.8f) + Direction() * 0.25f,
                        i == 2 ? new Color(0.23f, 0.19f, 0.16f, 0.26f) : i == 0 ? new("ffe4ad") : Ui.ElementColor(Element.Fire),
                        trail * 0.9f, i == 2 ? 0.55f : 0.38f, i == 2 ? 0 : 2, i == 2 ? 0.28f : 0.12f, 0);
            }
        }
        if (_statusClock >= 0.11f)
        {
            _statusClock %= 0.11f;
            foreach (var p in snapshot.Players)
            {
                if (!p.Alive) continue;
                var at = ArenaRenderer.World(p.Position, 0.55f);
                if (p.Burning > 0) for (int i = 0; i < 3; i++) Emit(at + Direction() * 0.35f, Ui.ElementColor(Element.Fire), Vector3.Up * 1.5f, 0.55f, 2, 0.24f, -0.3f);
                if (p.Wet > 0) Emit(at + Vector3.Up * 0.9f + Direction() * 0.3f, Ui.ElementColor(Element.Water), Vector3.Down * 1.6f, 0.5f, 0, 0.16f);
                if (p.Chill > 20 || p.Frozen > 0) Emit(at + Direction() * 0.5f, Ui.ElementColor(Element.Cold), Direction() * 0.6f, 0.6f, 1, 0.15f, 0.2f);
                if (p.Haste > 0) Emit(at, Ui.Energy, -ArenaRenderer.World(p.Facing) * 1.8f, 0.4f, 0, 0.24f, 0);
                if (p.RegenerationRate > 0) Emit(at + Direction() * 0.4f, Ui.Health, Vector3.Up * 1.3f, 0.7f, 0, 0.20f, -0.2f);
                if (p.Casting)
                {
                    var elements = p.CastingElements is { Length: > 0 } ? p.CastingElements : p.Selected;
                    if (elements.Length == 0) continue;
                    if (HasElementChannel(snapshot, p.Id)) continue;
                    var strength = SpellVisualStrength.For(elements); SeenCharges.Add(strength.Charges);
                    var imbuements = elements.Contains(Element.Earth) ? StoneVisuals.Imbuements(elements) : [];
                    for (int i = 0; i < strength.ChannelSamples; i++)
                    {
                        Element? infusion = imbuements.Length > 0 && i > 0 ? imbuements[(i - 1) % imbuements.Length] : null;
                        Emit(at + Vector3.Up * 0.55f + Direction() * 0.22f,
                            infusion is { } element ? StoneVisuals.AuraColor(element) : elements.Contains(Element.Earth) ? StoneVisuals.ChipColor(i) : Ui.SpellColor(elements),
                            Direction() * 0.4f, 0.24f, infusion is { } imbued ? StoneVisuals.ParticleShape(imbued) : elements.Contains(Element.Earth) ? 3 : 0,
                            strength.SparkSize * 0.75f, 0);
                    }
                }
            }
            foreach (var field in snapshot.Fields)
            {
                if (field.Kind is not ("burning_ground" or "frost_ground" or "water_pool")) continue;
                var strength = SpellVisualStrength.For(field.Elements);
                for (int i = 0; i < strength.ChannelSamples; i++)
                {
                    var at = ArenaRenderer.World(field.Position, 0.15f) + Direction() * (field.Radius * 0.7f);
                    Emit(at, Ui.SpellColor(field.Elements), Vector3.Up * (field.Kind == "burning_ground" ? 1.4f : 0.35f),
                        0.35f, field.Kind == "burning_ground" ? 2 : field.Kind == "frost_ground" ? 1 : 0, strength.SparkSize, 0);
                }
            }
            foreach (var wall in snapshot.Walls)
            {
                if (wall.Health <= 0 || wall.Lifetime <= 0) continue;
                bool fire = wall.Elements.Contains(Element.Fire), water = wall.Elements.Contains(Element.Water);
                bool steam = fire && water, ice = water && wall.Elements.Contains(Element.Cold);
                var at = ArenaRenderer.World(V2.Lerp(wall.A, wall.B, (float)_random.NextDouble()), 0.4f);
                if (steam)
                    Emit(at + Vector3.Up * 0.6f, new Color(0.73f, 0.85f, 0.88f, 0.3f), Vector3.Up * 0.8f, 0.55f, 0, 0.22f, 0);
                else if (fire)
                    Emit(at + Vector3.Up * (wall.Elements.Contains(Element.Earth) ? 1.1f : 0.4f), Ui.ElementColor(Element.Fire), Vector3.Up * 1.5f, 0.4f, 2, 0.12f, 0);
                if (water && !steam && !ice)
                    Emit(at - Vector3.Up * 0.25f, Ui.IsMobile ? Ui.ElementColor(Element.Cold) : new("a0e6ff"), Direction() * 0.75f, 0.28f, 0, 0.08f, 2);
                if (wall.Elements.Contains(Element.Lightning))
                    Emit(at + Vector3.Up * 0.6f, Ui.IsMobile ? Ui.ElementColor(Element.Lightning) : new("c9cdff"), Direction() * 1.7f, 0.16f, 1, 0.055f, 0);
            }
        }
        FinishFrame();
    }

    private void FinishFrame()
    {
        foreach (var batch in _particleBatches.Values) batch.Begin();
        _flameBatch?.Begin();
        foreach (var batch in _channelBatches.Values) batch.Begin();
        foreach (var spark in _sparks)
        {
            if (spark.Shape >= FlameVisuals.Shape)
            {
                float age = 1 - spark.Life / spark.Duration;
                Basis basis; float opacity;
                if (spark.Shape == FlameVisuals.Shape)
                {
                    var up = spark.FlameForward.Cross(spark.FlameSide);
                    basis = new(spark.FlameSide * spark.Scale.X, up * spark.Scale.Y, spark.FlameForward * spark.Scale.Z);
                    opacity = .48f;
                }
                else
                {
                    float angle = spark.Shape == ElementChannelVisuals.Ice ? spark.FlameSeed * Mathf.Tau + age
                        : spark.Shape == ElementChannelVisuals.Water ? MathF.Atan2(spark.FlameForward.Dot(Billboard.Y), spark.FlameForward.Dot(Billboard.X)) : 0;
                    var rotation = Billboard * new Basis(Vector3.Back, angle);
                    basis = new(rotation.X * spark.Scale.X, rotation.Y * spark.Scale.Y, rotation.Z * spark.Scale.Z);
                    opacity = spark.Shape == ElementChannelVisuals.Ice ? .94f : spark.Shape == ElementChannelVisuals.Water ? .74f : .22f;
                }
                spark.Batch!.Add(new Transform3D(basis, spark.Position),
                    new(age, spark.FlameSeed, spark.FlamePower, opacity));
            }
            else spark.Batch?.Add(new Transform3D(new Basis(Vector3.Up, spark.Angle).Scaled(spark.Scale), spark.Position));
        }
        foreach (var batch in _particleBatches.Values) batch.Commit();
        _flameBatch?.Commit();
        foreach (var batch in _channelBatches.Values) batch.Commit();
        EmittedLastFrame = _emittedThisFrame; _emittedThisFrame = 0;
        // No unir tandas distintas si una base de red descartó la cabecera de origen.
        _chainFrom = null; _chainLinks = 0;
    }

    public void Clear()
    {
        foreach (var p in _sparks) { if (p.Node != null) p.Node.Visible = false; _freeSparks.Push(p); } _sparks.Clear();
        foreach (var batch in _particleBatches.Values) batch.Clear();
        ClearChannelParticles(); ClearWaterJets();
        foreach (var w in _waves) { w.Node.Visible = false; _freeWaves.Push(w); } _waves.Clear();
        _trailClock = _statusClock = 0; _emittedThisFrame = EmittedLastFrame = 0;
        _chainFrom = null; _chainLinks = 0;
        SeenKinds.Clear(); SeenCharges.Clear();
    }
}

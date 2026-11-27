using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private void DrawProjectiles(WorldSnapshot snapshot, float dt)
    {
        foreach (var projectile in snapshot.Projectiles)
        {
            if (!_projectiles.TryGetValue(projectile.Id, out var root))
            {
                root = new Node3D { Position = World(projectile.Position, 0.9f) }; AddChild(root);
                bool stone = projectile.Elements.Contains(Element.Earth);
                bool ice = !stone && projectile.Elements.Contains(Element.Cold);
                var imbuements = stone ? StoneVisuals.Imbuements(projectile.Elements) : [];
                var color = ice ? ElementChannelVisuals.IceBody : Ui.SpellColor(projectile.Elements);
                if (stone)
                {
                    root.AddChild(new MeshInstance3D { Name = "StoneCore", Mesh = StoneVisuals.Shape(projectile.Id),
                        Scale = Vector3.One * projectile.Radius, MaterialOverride = StoneVisuals.Surface(projectile.Elements) });
                    if (StoneVisuals.Aura(projectile.Elements) is { } aura)
                        root.AddChild(new MeshInstance3D { Name = "StoneAura", Mesh = StoneVisuals.Shape(projectile.Id),
                            Scale = Vector3.One * (projectile.Radius + 0.10f + (projectile.Elements.Length - 2) * 0.025f),
                            MaterialOverride = aura, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                }
                else if (ice)
                {
                    var core = Mesh(root, ElementChannelVisuals.LegacyIce, Vector3.Zero, color, .35f);
                    core.Name = "IceCore";
                    core.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
                else Mesh(root, new SphereMesh { Radius = projectile.Radius, Height = projectile.Radius * 2 }, Vector3.Zero, color, 0.35f);
                var strength = SpellVisualStrength.For(projectile.Elements);
                for (int i = 0; i < strength.Charges - 1; i++)
                {
                    color = imbuements.Length > 0 ? StoneVisuals.AuraColor(imbuements[i % imbuements.Length]) :
                        ice ? ElementChannelVisuals.IceBody : Ui.SpellColor(projectile.Elements);
                    color.A = imbuements.Length > 0 ? 0.62f : 0.3f;
                    float radius = projectile.Radius + 0.09f + i * 0.045f;
                    var accent = Mesh(root, new TorusMesh { InnerRadius = radius - 0.025f, OuterRadius = radius,
                        Rings = 24, RingSegments = 5 }, Vector3.Zero, color, 0.25f);
                    accent.Rotation = new(0.9f + i * 0.7f, 0, i * 0.6f);
                    accent.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
                _projectiles[projectile.Id] = root;
            }
            root.Position = root.Position.Lerp(World(projectile.Position, 0.9f), 1 - Mathf.Exp(-35 * dt));
            root.Rotation += new Vector3(dt * 4, dt * 3, dt * 2);
            if (projectile.Elements.Contains(Element.Cold) && !projectile.Elements.Contains(Element.Earth))
            {
                var core = root.GetNode<MeshInstance3D>("IceCore");
                Godot.Mesh mesh = _effects.EnhancedSpells ? ElementChannelVisuals.Mesh(ElementChannelVisuals.Ice) : ElementChannelVisuals.LegacyIce;
                Godot.Material material = _effects.EnhancedSpells ? ElementChannelVisuals.ProjectileIceMaterial() : Material(ElementChannelVisuals.IceBody, .35f);
                if (core.Mesh != mesh) core.Mesh = mesh;
                if (core.MaterialOverride != material) core.MaterialOverride = material;
                core.Basis = _effects.EnhancedSpells
                    ? (root.GlobalBasis.Inverse() * Camera.GlobalBasis * new Basis(Vector3.Back, (float)_time * 2 + projectile.Id)).Scaled(Vector3.One * .38f)
                    : Basis.Identity;
            }
        }
        if (_fresh || snapshot.Defense != null) RemoveMissing(_projectiles, snapshot.Projectiles, static p => p.Id);
    }

    private void DrawWalls(WorldSnapshot snapshot, float delta)
    {
        foreach (var wall in snapshot.Walls)
        {
            if (!_walls.TryGetValue(wall.Id, out var visual))
            {
                visual = new BarrierVisual(); AddChild(visual); visual.Build(wall); _walls[wall.Id] = visual;
            }
            visual.Update(wall, delta, (float)_time);
        }
        if (_fresh || snapshot.Defense != null) RemoveMissing(_walls, snapshot.Walls, static p => p.Id);
    }

    private void DrawFields(WorldSnapshot snapshot, float delta)
    {
        foreach (var field in snapshot.Fields)
        {
            if (field.Kind == DefenseRules.SpecialExplosionKind)
            {
                if (!_fields.TryGetValue(field.Id, out var existingExplosion))
                {
                    var warning = new EnemyAttackWarning { Name = "SpecialDeathWarning", Position = World(field.Position) };
                    AddChild(warning); warning.Build(field.Radius); _fields[field.Id] = existingExplosion = warning;
                }
                var deathWarning = (EnemyAttackWarning)existingExplosion;
                deathWarning.Visible = field.Lifetime > 0;
                if (deathWarning.Visible) deathWarning.ShowRemaining(field.Lifetime - .2f, DefenseRules.SpecialExplosionDelay);
                continue;
            }
            if (field.Kind == DefenseRules.ColossusFissureKind)
            {
                if (!_fields.TryGetValue(field.Id, out var existingWarning))
                {
                    var warning = new EnemyAttackWarning { Position = World(field.Position) };
                    AddChild(warning); warning.Build(field.Radius);
                    _fields[field.Id] = existingWarning = warning;
                }
                var fissure = (EnemyAttackWarning)existingWarning;
                fissure.Visible = field.Lifetime > 0;
                if (fissure.Visible) fissure.ShowRemaining(field.Lifetime - 0.2f, DefenseRules.ColossusFissureWindup);
                continue;
            }
            if (field.Kind == "oracle_bombardment")
            {
                if (!_fields.TryGetValue(field.Id, out var existingStrike))
                {
                    var strike = new OracleStrikeVisual(); AddChild(strike); strike.Build(field);
                    _fields[field.Id] = existingStrike = strike;
                }
                ((OracleStrikeVisual)existingStrike).Update(field, (float)_time);
                continue;
            }
            if (field.Kind is "meteor" or "defense_meteor")
            {
                if (!_fields.TryGetValue(field.Id, out var existing))
                {
                    var meteor = new MeteorVisual(); AddChild(meteor); meteor.Build(field);
                    _fields[field.Id] = existing = meteor;
                }
                ((MeteorVisual)existing).Update(field, delta, (float)_time);
                continue;
            }
            if (!_fields.TryGetValue(field.Id, out var root))
            {
                var area = new AreaFieldVisual(); AddChild(area); area.Build(field);
                _fields[field.Id] = root = area;
            }
            ((AreaFieldVisual)root).Update(field, (float)_time);
        }
        if (_fresh || snapshot.Defense != null) RemoveMissing(_fields, snapshot.Fields, static p => p.Id);
    }
}

using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>La barrera sigue el segmento de colisión del servidor; solo anima su superficie.</summary>
public partial class BarrierVisual : Node3D
{
    private static Shader? _surfaceShader;
    private ShaderMaterial? _surface;
    private readonly Node3D _geometry = new();
    private readonly StandardMaterial3D _rim = new();
    private Color _color;
    private float _previousHealth, _maximumHealth, _flash, _age;
    internal int Charges { get; private set; }
    internal int StoneRows { get; private set; }
    internal float StoneThickness { get; private set; }
    internal int StoneSourceMeshes { get; private set; }
    internal int StoneDrawMeshes { get; private set; }
    internal int StoneSourceVertices { get; private set; }
    internal int StoneBatchedVertices { get; private set; }
    private float _reinforcement;
    private readonly Sprite3D?[] _approvedWallArt = new Sprite3D?[2];

    public void Build(WallSnapshot wall)
    {
        Position = ArenaRenderer.World((wall.A + wall.B) / 2);
        var direction = wall.B - wall.A;
        Rotation = new(0, Mathf.Atan2(direction.X, direction.Y), 0);
        AddChild(_geometry);
        bool stone = wall.Elements.Contains(Element.Earth);
        Stone = stone;
        int charges = SpellVisualStrength.For(wall.Elements).Charges;
        Charges = charges;
        _reinforcement = Math.Max(0, charges - (stone ? 2 : 1));
        StoneRows = stone ? 2 + (int)_reinforcement : 0;
        StoneThickness = stone ? 0.62f + _reinforcement * 0.20f
            + Math.Max(0, wall.Elements.Count(e => e == Element.Earth) - 1) * 0.20f : 0;
        float length = V2.Distance(wall.A, wall.B);
        _previousHealth = wall.Health; _maximumHealth = stone ? 1300 : 650;
        _color = Ui.SpellColor(wall.Elements);
        _rim.AlbedoColor = _color;
        _rim.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _rim.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _rim.EmissionEnabled = true; _rim.Emission = _color;
        _rim.EmissionEnergyMultiplier = 0.05f;

        if (stone)
        {
            int blocks = Math.Clamp((int)MathF.Ceiling(length / 0.72f), 3, 16);
            var stoneMaterials = Enumerable.Range(0, 3).Select(i => StoneVisuals.Surface(wall.Elements, i)).ToArray();
            var stoneTools = new SurfaceTool[stoneMaterials.Length];
            for (int i = 0; i < stoneTools.Length; i++)
            { stoneTools[i] = new(); stoneTools[i].Begin(Godot.Mesh.PrimitiveType.Triangles); }
            void StonePiece(Vector3 size, int seed, Vector3 at, int material)
            {
                using var mesh = StoneMesh(size, seed);
                using var arrays = mesh.SurfaceGetArrays(0);
                StoneSourceVertices += arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array().Length;
                StoneSourceMeshes++;
                stoneTools[material].AppendFrom(mesh, 0, new Transform3D(Basis.Identity, at));
            }
            try
            {
                // La pared básica conserva dos hiladas; una tercera carga añade masa y contrafuertes.
                for (int row = 0; row < StoneRows; row++)
                for (int i = 0; i < blocks + row % 2; i++)
                {
                    float start = Math.Max(0, (i - row % 2 * 0.5f) / blocks) * length;
                    float end = Math.Min(1, (i + 1 - row % 2 * 0.5f) / blocks) * length;
                    float height = row == 0 ? 0.82f : 0.73f + 0.14f * MathF.Sin(i * 2.31f + wall.Id);
                    StonePiece(new(StoneThickness - (row == 0 ? 0 : 0.08f), height, end - start + 0.025f),
                            i + row * 31 + wall.Id,
                        new(0, 0.04f + row * 0.76f + height / 2, (start + end - length) / 2),
                        (i + row) % stoneMaterials.Length);
                }
                if (_reinforcement > 0)
                    foreach (int end in new[] { -1, 1 })
                    {
                        StonePiece(new(StoneThickness + 0.24f, 1.92f, 0.44f), wall.Id + end * 41,
                            new(0, 1, end * (length / 2 - 0.24f)), 2);
                    }
                // Los bloques de una pared no se mueven entre sí: conserva sus
                // vértices y sombras, dibujándolos juntos por material.
                for (int i = 0; i < stoneTools.Length; i++)
                {
                    var combined = stoneTools[i].Commit();
                    using var arrays = combined.SurfaceGetArrays(0);
                    StoneBatchedVertices += arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array().Length;
                    var block = Piece(combined, Vector3.Zero, stoneMaterials[i]);
                    block.Name = "StoneBatch" + i;
                    block.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                    StoneDrawMeshes++;
                }
            }
            finally { foreach (var tool in stoneTools) tool.Dispose(); }
        }
        else
        {
            _surfaceShader ??= new Shader { Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
                uniform vec4 barrier_color : source_color;
                uniform float span = 4.4;
                uniform float detail = 1.0;
                uniform float integrity = 1.0;
                uniform float fade = 1.0;
                uniform float hit_flash = 0.0;
                uniform float clock = 0.0;
                void fragment() {
                    float reinforcement = detail - 1.0;
                    vec2 p = vec2(UV.x * span, UV.y * 2.0) * (2.6 + detail * 0.25);
                    vec2 tile = vec2(1.0, 1.73205);
                    vec2 a = mod(p, tile) - tile * 0.5;
                    vec2 b = mod(p - tile * 0.5, tile) - tile * 0.5;
                    vec2 cell = dot(a, a) < dot(b, b) ? a : b;
                    float hex = max(abs(cell.x), dot(abs(cell), vec2(0.5, 0.866025)));
                    float grid = smoothstep(0.43, 0.49, hex);
                    float edge = 1.0 - smoothstep(0.0, 0.028, min(min(UV.x, 1.0 - UV.x), min(UV.y, 1.0 - UV.y)));
                    float sweep = pow(0.5 + 0.5 * sin(UV.y * 7.0 - clock * 2.4), 12.0);
                    float scars = pow(0.5 + 0.5 * sin(UV.x * 37.0 + UV.y * 19.0), 28.0) * (1.0 - integrity);
                    float runes = pow(abs(sin(UV.x * span * 5.0 + clock * 0.22)), 18.0)
                        * pow(abs(sin(UV.y * 14.0 + UV.x * 9.0)), 8.0);
                    ALBEDO = mix(barrier_color.rgb * 0.72, barrier_color.rgb, grid * 0.7 + edge * 0.3);
                    ALBEDO = mix(ALBEDO, vec3(1.0), hit_flash * 0.18);
                    EMISSION = barrier_color.rgb * (0.075 + reinforcement * 0.025
                        + runes * 0.28 + hit_flash * 0.14);
                    ALPHA = fade * clamp(0.42 + edge * 0.48 + grid * 0.24
                        + reinforcement * 0.06 + sweep * 0.05 + scars * 0.20 + hit_flash * 0.12, 0.0, 0.94);
                }
                """ };
            _surface = new ShaderMaterial { Shader = _surfaceShader };
            _surface.SetShaderParameter("barrier_color", _color);
            _surface.SetShaderParameter("span", length);
            _surface.SetShaderParameter("detail", charges);
            using var face = new SurfaceTool(); face.Begin(Godot.Mesh.PrimitiveType.Triangles);
            const int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments, b = (i + 1) / (float)segments;
                void Vertex(float u, float v)
                {
                    face.SetUV(new(u, v));
                    float height = 1.55f + _reinforcement * 0.18f + MathF.Sin(u * MathF.PI) * 0.65f;
                    face.AddVertex(new(0, 0.07f + height * v, (u - 0.5f) * length));
                }
                Vertex(a, 0); Vertex(b, 0); Vertex(a, 1);
                Vertex(a, 1); Vertex(b, 0); Vertex(b, 1);
            }
            face.GenerateNormals();
            Piece(face.Commit(), Vector3.Zero, _surface);
            // El borde usa únicamente el color elemental del escudo.
            using var edges = new SurfaceTool(); edges.Begin(Godot.Mesh.PrimitiveType.Triangles);
            for (int i = 0; i < segments; i++)
            {
                Vector3 Top(float u) => new(0, 1.62f + _reinforcement * 0.18f + MathF.Sin(u * MathF.PI) * 0.65f,
                    (u - 0.5f) * length);
                var a = Top(i / (float)segments); var b = Top((i + 1) / (float)segments);
                float angle = -Mathf.Atan2(b.Y - a.Y, b.Z - a.Z);
                float span = a.DistanceTo(b) + 0.035f;
                var transform = new Transform3D(new Basis(Vector3.Right, angle), (a + b) / 2);
                using var edge = new BoxMesh { Size = new(0.26f, 0.075f, span) };
                edges.AppendFrom(edge, 0, transform);
            }
            Piece(edges.Commit(), Vector3.Zero, _rim).Name = "BarrierArchColor";
            foreach (int end in new[] { -1, 1 })
            {
                float height = 1.55f + _reinforcement * 0.18f;
                Piece(new PrismMesh { Size = new(0.15f + _reinforcement * 0.04f, height, 0.17f) },
                    new(0.13f + _reinforcement * 0.02f, 0.045f + height / 2, end * length / 2), _rim);
                Piece(new PrismMesh { Size = new(0.15f + _reinforcement * 0.04f, height, 0.17f) },
                    new(-0.13f - _reinforcement * 0.02f, 0.045f + height / 2, end * length / 2), _rim);
            }
            for (int band = 0; band < charges - 1; band++)
                Piece(new BoxMesh { Size = new(0.08f, 0.055f, length) },
                    new(0, 0.58f + band * 0.64f, 0), _rim);
        }
        Piece(new BoxMesh { Size = new(0.19f, 0.055f, length) }, new(0, 0.08f, 0), _rim);
        // Runic crystals trace the wall silhouette; the collider remains untouched.
        int glyphs = charges == 1 ? 3 : charges == 2 ? 5 : 7;
        for (int i = 0; i < glyphs; i++)
        {
            float z = ((i + 1) / (float)(glyphs + 1) - 0.5f) * length;
            float x = stone ? StoneThickness / 2 + 0.055f : 0.065f;
            var glyph = Piece(new PrismMesh { Size = new(0.11f, 0.28f + (i % 2) * 0.11f, 0.18f) },
                new(x, 0.91f + (i % 2) * 0.2f, z), _rim);
            glyph.Name = "BarrierRuneCrystal" + i;
            glyph.Rotation = new(MathF.PI / 4, i * 0.35f, 0);
        }
        // 3D-aligned wall graphics: never paste a whole scene or second character.
        // Layer 0 and 1 reproduce both affinities of valid EXY combinations.
        int layers = ArcanaSpriteAtlas.AffinityLayers(wall.Elements);
        for (int i = 0; i < layers; i++)
        {
            var texture = ArcanaSpriteAtlas.Defense(ArcanaSpriteAtlas.DefenseForm.Wall, wall.Elements, i);
            if (texture == null) continue;
            var sprite = new Sprite3D
            {
                Name = "WallRuneVisual" + i,
                Texture = texture,
                Position = new(stone ? StoneThickness / 2 + 0.08f + i * 0.015f : 0.095f + i * 0.015f,
                    1.22f, 0),
                Rotation = new(0, Mathf.Pi / 2f, 0),
                PixelSize = 1f / 160f,
                Scale = new(Math.Min(length * 0.94f, 4.5f), 2.48f, 1),
                Shaded = false,
                Modulate = new Color(1, 1, 1, i == 0 ? 0.62f : 0.36f)
            };
            _geometry.AddChild(sprite);
            _approvedWallArt[i] = sprite;
        }
        BuildElements(wall, length, stone);
        BuildStyleBCrests(length, stone);
    }

    private static ArrayMesh StoneMesh(Vector3 size, int seed)
    {
        Vector3[] corners = [new(-1,-1,-1),new(1,-1,-1),new(1,1,-1),new(-1,1,-1),
            new(-1,-1,1),new(1,-1,1),new(1,1,1),new(-1,1,1)];
        for (int i = 0; i < corners.Length; i++)
        {
            var point = corners[i];
            float inset = 0.04f + 0.06f * (0.5f + 0.5f * MathF.Sin(seed * 2.3f + i * 4.1f));
            corners[i] = new Vector3(point.X * (1 - inset), point.Y * (1 - inset * 0.45f), point.Z)
                * size / 2;
        }
        int[][] faces = [[0,1,2,3],[5,4,7,6],[4,0,3,7],[1,5,6,2],[3,2,6,7],[4,5,1,0]];
        using var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        foreach (var face in faces)
        {
            Vector3 center = (corners[face[0]] + corners[face[1]] + corners[face[2]] + corners[face[3]]) / 4;
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[face[i]], b = corners[face[(i + 1) % 4]];
                // Godot usa triángulos horarios; la normal debe apuntar hacia fuera.
                surface.SetNormal((b - a).Cross(center - a).Normalized() * -1);
                surface.AddVertex(a); surface.AddVertex(b); surface.AddVertex(center);
            }
        }
        return surface.Commit();
    }

    private MeshInstance3D Piece(Godot.Mesh shape, Vector3 at, Godot.Material material)
    {
        var mesh = new MeshInstance3D { Mesh = shape, Position = at, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _geometry.AddChild(mesh); return mesh;
    }

    public void Update(WallSnapshot wall, float delta, float time)
    {
        _age += delta;
        _flash = Math.Max(0, _flash - delta * 3);
        if (wall.Health < _previousHealth - 0.01f) _flash = 0.6f;
        _previousHealth = wall.Health;
        float fade = Math.Clamp(wall.Lifetime / 0.8f, 0, 1);
        float height = Mathf.Lerp(0.25f, 1, Math.Clamp(_age * 8, 0, 1));
        _geometry.Scale = new(1, height * (_surface == null ? Math.Max(0.02f, fade) : 1), 1);
        _rim.AlbedoColor = new(_color.Lightened(_flash * 0.18f), fade);
        if (_surface != null)
        {
            _surface.SetShaderParameter("integrity", Math.Clamp(wall.Health / _maximumHealth, 0, 1));
            _surface.SetShaderParameter("fade", fade);
            _surface.SetShaderParameter("hit_flash", _flash);
            _surface.SetShaderParameter("clock", time);
        }
        for (int i = 0; i < _approvedWallArt.Length; i++)
        {
            if (_approvedWallArt[i] is not { } sprite) continue;
            sprite.Modulate = new Color(1, 1, 1, (i == 0 ? 0.62f : 0.36f) * fade);
        }
        UpdateElements(fade, time);
        UpdateStyleBCrests(time);
        Visible = wall.Health > 0 && wall.Lifetime > 0;
    }
}

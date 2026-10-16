using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Botín pequeño y legible: siluetas orientadas a cámara y moneda con grosor real.</summary>
public partial class DefenseLootVisual : Node3D
{
    private static Texture2D? _atlas;
    private static CylinderMesh? _coinMesh;
    private static TorusMesh? _haloMesh;
    private readonly Node3D _body = new() { Name = "LootBody" };
    private bool _coin;
    private int _id;
    internal int IconFrame { get; private set; }
    internal Vector3 BodyRotation => _body.Rotation;
    internal bool SpinsVertically => _coin;

    internal void Build(DefenseDropSnapshot drop, Material haloMaterial, Material coinMaterial)
    {
        _id = drop.Id; _coin = drop.Kind == DefenseDropKind.Gold;
        Name = "DefenseDrop" + drop.Id;
        Position = ArenaRenderer.World(drop.Position);
        AddChild(_body);
        // El mismo atlas y las mismas mallas sirven para todas las recogidas de la partida.
        _atlas ??= GD.Load<Texture2D>("res://Assets/Loot/pickups.svg");
        IconFrame = drop.Kind switch
        {
            DefenseDropKind.Gold => 11,
            DefenseDropKind.Equipment => (int)(drop.Slot ?? EquipmentSlot.Chest),
            _ => 4 + (int)drop.Buff
        };
        if (_coin)
        {
            _coinMesh ??= new CylinderMesh { TopRadius = 0.30f, BottomRadius = 0.30f,
                Height = 0.10f, RadialSegments = 20 };
            _body.AddChild(new MeshInstance3D { Name = "CoinEdge", Mesh = _coinMesh,
                RotationDegrees = new(90, 0, 0), MaterialOverride = coinMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            // Caras verticales a ambos lados: el giro sobre Y alterna cara y canto.
            for (int face = -1; face <= 1; face += 2)
            {
                var sprite = Icon(0.0049f, false);
                sprite.Position = new(0, 0, face * 0.052f);
                sprite.Rotation = new(0, face < 0 ? Mathf.Pi : 0, 0);
                _body.AddChild(sprite);
            }
        }
        else _body.AddChild(Icon(drop.Kind == DefenseDropKind.Equipment ? 0.0105f : 0.0095f, true));

        _haloMesh ??= new TorusMesh { InnerRadius = 0.29f, OuterRadius = 0.34f, Rings = 24, RingSegments = 4 };
        AddChild(new MeshInstance3D { Name = "LootHalo", Mesh = _haloMesh,
            Position = new(0, 0.055f, 0), Scale = new(1, 0.25f, 1), MaterialOverride = haloMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    private Sprite3D Icon(float size, bool billboard) => new()
    {
        Name = "LootIcon", Texture = _atlas, Hframes = 6, Vframes = 2, Frame = IconFrame,
        PixelSize = size, Billboard = billboard ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled,
        Shaded = false, DoubleSided = false, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        AlphaCut = SpriteBase3D.AlphaCutMode.Discard, AlphaScissorThreshold = 0.12f,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
    };

    internal void Update(double time)
    {
        float phase = (float)time * 2.4f + _id * 0.73f;
        _body.Position = new(0, (_coin ? 0.52f : 0.80f) + Mathf.Sin(phase) * 0.08f, 0);
        // Las piezas y mejoras no giran de canto: su símbolo siempre se puede reconocer.
        if (_coin) _body.Rotation = new(0, (float)time * 2.2f + _id * 0.73f, 0);
    }
}

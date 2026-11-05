using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Recortes de las piezas y su posición en cada pose del mago Equilibrado.</summary>
internal sealed class WizardOutfitParts
{
    private const string Index = "res://Assets/Wizards/balanced-parts.json";
    private static WizardOutfitParts? _loaded;
    private static readonly EquipmentSlot[] Slots = [EquipmentSlot.Boots, EquipmentSlot.Chest, EquipmentSlot.Hat, EquipmentSlot.Weapon];
    private readonly Image _pixels;
    private readonly Specification _spec;
    private readonly Dictionary<(EquipmentSlot, EquipmentFamily?), Piece> _pieces;

    private sealed record Region(int X, int Y, int Width, int Height, int AtX, int AtY);
    private sealed record GripOffset(int X, int Y);
    private sealed record Piece(EquipmentSlot Slot, EquipmentFamily? Family, Region?[] Frames,
        WizardSprites.FrameSpec[]? CastOrigins = null, GripOffset[]? GripOffsets = null);
    private sealed record Specification(string Texture, int Width, int Height, Region?[] Body, Piece[] Pieces);

    private WizardOutfitParts(Specification spec, Image pixels)
    {
        _spec = spec; _pixels = pixels;
        _pieces = spec.Pieces.ToDictionary(p => (p.Slot, p.Family));
        if (Slots.Any(slot => !_pieces.ContainsKey((slot, null))))
            throw new InvalidDataException("Faltan piezas base del mago Equilibrado.");
        ValidateFrames(spec.Body);
        foreach (var piece in spec.Pieces)
        {
            ValidateFrames(piece.Frames);
            if (piece.Slot == EquipmentSlot.Weapon && piece.CastOrigins?.Length != WizardSprites.Rows)
                throw new InvalidDataException("Faltan anclajes de lanzamiento del arma.");
            if (piece.GripOffsets is { } grips && grips.Length != piece.Frames.Length)
                throw new InvalidDataException("Faltan posiciones de la mano para las poses del pecho.");
        }
    }

    internal static WizardOutfitParts Load()
    {
        if (_loaded != null) return _loaded;
        var spec = JsonSerializer.Deserialize<Specification>(Godot.FileAccess.GetFileAsString(Index))
            ?? throw new InvalidDataException("Falta el índice de piezas del mago Equilibrado.");
        var pixels = new Image();
        try
        {
            if (pixels.LoadWebpFromBuffer(Godot.FileAccess.GetFileAsBytes(spec.Texture)) != Error.Ok)
                throw new InvalidDataException($"No se pudo cargar {spec.Texture}.");
            pixels.Convert(Image.Format.Rgba8);
            _loaded = new(spec, pixels);
            return _loaded;
        }
        catch { pixels.Dispose(); throw; }
    }

    private void ValidateFrames(Region?[] frames)
    {
        if (frames.Length != WizardSprites.Columns * WizardSprites.Rows)
            throw new InvalidDataException("Número incorrecto de poses para una pieza del mago.");
        foreach (var region in frames)
            if (region != null && (region.Width <= 0 || region.Height <= 0 ||
                region.X < 0 || region.Y < 0 || region.X + region.Width > _pixels.GetWidth() || region.Y + region.Height > _pixels.GetHeight() ||
                region.AtX < 0 || region.AtY < 0 || region.AtX + region.Width > _spec.Width || region.AtY + region.Height > _spec.Height))
                throw new InvalidDataException("Recorte de pieza fuera del atlas del mago.");
    }

    private EquipmentFamily? Supported(EquipmentSlot slot, EquipmentFamily? family) =>
        _pieces.ContainsKey((slot, family)) ? family : null;

    internal EquipmentOutfit Normalize(EquipmentOutfit outfit) => new(
        Supported(EquipmentSlot.Boots, outfit.Boots), Supported(EquipmentSlot.Hat, outfit.Hat),
        Supported(EquipmentSlot.Chest, outfit.Chest), Supported(EquipmentSlot.Weapon, outfit.Weapon));

    internal WizardSprites.FrameSpec[] CastOrigins(EquipmentOutfit outfit)
    {
        var origins = _pieces[(EquipmentSlot.Weapon, outfit.Weapon)].CastOrigins!;
        var grips = _pieces[(EquipmentSlot.Chest, outfit.Chest)].GripOffsets;
        if (grips == null) return origins;
        return origins.Select((point, row) =>
        {
            var grip = grips[row * WizardSprites.Columns + 3];
            return new WizardSprites.FrameSpec(point.AnchorX + grip.X, point.AnchorY + grip.Y);
        }).ToArray();
    }

    internal Image Assemble(WizardSprites.AtlasSpec atlas, EquipmentOutfit outfit)
    {
        if (atlas.Width != _spec.Width || atlas.Height != _spec.Height)
            throw new InvalidDataException("El tamaño de las piezas no coincide con el atlas del mago.");
        var image = Image.CreateEmpty(atlas.Width, atlas.Height, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var chest = _pieces[(EquipmentSlot.Chest, outfit.Chest)];
        // El arma se dibuja detrás de la mano. Cada ranura aporta una única pieza.
        Paint(image, _pieces[(EquipmentSlot.Weapon, outfit.Weapon)].Frames, chest.GripOffsets);
        Paint(image, _pieces[(EquipmentSlot.Boots, outfit.Boots)].Frames);
        Paint(image, chest.Frames);
        Paint(image, _spec.Body);
        Paint(image, _pieces[(EquipmentSlot.Hat, outfit.Hat)].Frames);
        image.GenerateMipmaps();
        return image;
    }

    private void Paint(Image image, Region?[] frames, GripOffset[]? grips = null)
    {
        for (int i = 0; i < frames.Length; i++)
            if (frames[i] is { } r)
            {
                var grip = grips?[i];
                image.BlendRect(_pixels, new Rect2I(r.X, r.Y, r.Width, r.Height),
                    new Vector2I(r.AtX + (grip?.X ?? 0), r.AtY + (grip?.Y ?? 0)));
            }
    }

    internal static void ClearCache()
    {
        _loaded?._pixels.Dispose();
        _loaded = null;
    }
}

/// <summary>Una textura reutilizable por vista. Se recompone únicamente al cambiar el equipo.</summary>
internal sealed class WizardOutfitTexture : IDisposable
{
    private ImageTexture? _texture;
    private EquipmentOutfit _requested, _painted;
    private bool _custom;
    internal WizardSprites.FrameSpec[]? CastOrigins { get; private set; }

    internal Texture2D Get(WizardSprites.Atlas atlas, EquipmentOutfit outfit)
    {
        if (atlas.Spec.Type != WizardType.Balanced)
        {
            CastOrigins = null;
            _requested = default;
            _custom = false;
            return atlas.Texture;
        }
        if (_requested == outfit) return _custom ? _texture! : atlas.Texture;

        var parts = WizardOutfitParts.Load();
        var look = parts.Normalize(outfit);
        if (look != default && look != _painted)
        {
            using var art = parts.Assemble(atlas.Spec, look);
            if (_texture == null) _texture = ImageTexture.CreateFromImage(art);
            else _texture.Update(art);
            _painted = look;
        }
        CastOrigins = look == default ? null : parts.CastOrigins(look);
        _custom = look != default;
        _requested = outfit;
        return _custom ? _texture! : atlas.Texture;
    }

    public void Dispose()
    {
        _texture?.Dispose(); _texture = null;
        _requested = _painted = default;
        _custom = false; CastOrigins = null;
    }
}

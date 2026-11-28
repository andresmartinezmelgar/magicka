using System.Security.Cryptography;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckWizardDirections()
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidDataException("GIROS_FAILED: " + message); }
        static V2 Direction(int index)
        {
            float angle = index * MathF.Tau / WizardSprites.Directions;
            return new(MathF.Sin(angle), MathF.Cos(angle));
        }

        foreach (int fps in new[] { 30, 60, 120 })
        {
            var facing = new WizardFacing(); facing.Reset(new(0, 1));
            for (int direction = 0; direction < WizardSprites.Directions; direction++)
            {
                for (int frame = 0; frame < fps / 3; frame++) facing.Advance(Direction(direction), 1f / fps, false);
                Check(facing.Direction == direction, $"Falta la orientación {direction} a {fps} FPS.");
            }
            facing.Reset(Direction(23));
            for (int frame = 0; frame < fps / 4; frame++)
            {
                int direction = facing.Advance(Direction(1), 1f / fps, false);
                Check(direction is 23 or 0 or 1, "El giro de 345° a 15° da una vuelta completa.");
            }
            Check(facing.Direction == 1, "No termina el giro corto al cruzar 0°.");
            facing.Advance(Direction(12), 1, false, locked: true);
            Check(facing.Direction == 1, "La congelación o el aturdimiento permiten seguir girando.");
            facing.Advance(V2.Zero, 1, false);
            Check(facing.Direction == 1, "Un vector nulo inventa una dirección.");
            facing.Reset(new(0, 1));
            for (int frame = 0; frame < fps / 10; frame++) facing.Advance(new(1, 0), 1f / fps, false);
            Check(facing.Direction == 6, "La duración del giro depende de los FPS.");
        }

        foreach (var type in Enum.GetValues<WizardType>())
        {
            var atlas = WizardSprites.Load(type);
            Check(atlas.Spec.Frames.Length == 96 && atlas.Spec.CastOrigins?.Length == 24 &&
                atlas.Texture.GetWidth() == 1280 && atlas.Texture.GetHeight() == 1920, "Formato de atlas direccional incorrecto.");
            var distinct = new HashSet<string>();
            using var image = atlas.Texture.GetImage();
            for (int direction = 0; direction < WizardSprites.Directions; direction++)
            {
                for (int pose = 0; pose < WizardSprites.Poses; pose++)
                {
                    int frame = direction * WizardSprites.Poses + pose;
                    using var cell = image.GetRegion(new Rect2I(frame % WizardSprites.Columns * WizardDirectionalAtlas.FrameSize,
                        frame / WizardSprites.Columns * WizardDirectionalAtlas.FrameSize, WizardDirectionalAtlas.FrameSize, WizardDirectionalAtlas.FrameSize));
                    var used = cell.GetUsedRect();
                    Check(used.Size.X > 0 && used.Size.Y > 0, $"Sprite vacío: {type}/{direction}/{pose}.");
                    var feet = atlas.Spec.Frames[frame];
                    Check(float.IsFinite(feet.AnchorX + feet.AnchorY) && feet.AnchorX >= 0 && feet.AnchorY >= 0 &&
                        feet.AnchorX <= WizardDirectionalAtlas.FrameSize && feet.AnchorY <= WizardDirectionalAtlas.FrameSize,
                        "Los pies no caben en la celda.");
                    if (pose == 0) distinct.Add(Convert.ToHexString(SHA256.HashData(cell.GetData())));
                }
                var cast = atlas.Spec.CastOrigins![direction];
                Check(float.IsFinite(cast.AnchorX + cast.AnchorY) && cast.AnchorX >= 0 && cast.AnchorY >= 0 &&
                    cast.AnchorX < WizardDirectionalAtlas.FrameSize && cast.AnchorY < WizardDirectionalAtlas.FrameSize,
                    "El lanzamiento queda fuera del atlas.");
            }
            Check(distinct.Count == WizardSprites.Directions, "Las vistas intermedias repiten sprites de otras direcciones.");
        }
        GD.Print("WIZARD_DIRECTIONS_OK: 24 vistas distintas por mago, 480 poses, giro corto, control de masas, anclajes y 30/60/120 FPS.");
        return true;
    }
}

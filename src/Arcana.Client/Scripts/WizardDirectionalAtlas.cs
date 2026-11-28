using Godot;

namespace Arcana.Client;

/// <summary>
/// Genera las vistas intermedias del sprite ya vestido. Los anclajes de pies,
/// rostro y báculo se deforman juntos antes de mezclar las dos vistas vecinas.
/// El resultado se reutiliza al girar: no se generan imágenes por fotograma.
/// </summary>
internal static class WizardDirectionalAtlas
{
    internal const int Directions = 24, Poses = 4, Columns = 8, Rows = 12, FrameSize = 160;
    private const float Padding = 24;
    private static readonly int[] SourceRows = [0, 3, 2, 1]; // Sur, este, norte, oeste.

    internal static int SourceRow(int direction) => SourceRows[((direction + 3) / 6) % 4];

    private static Vector2 Point(WizardSprites.FrameSpec value) => new(value.AnchorX, value.AnchorY);
    private static WizardSprites.FrameSpec Point(Vector2 value) => new(value.X, value.Y);
    private readonly record struct Pixel(float R, float G, float B, float A, float Distance)
    {
        internal static Pixel Mix(Pixel a, Pixel b, float t) => new(
            a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t, a.Distance + (b.Distance - a.Distance) * t);
    }

    private readonly record struct Rig(Vector2 Feet, Vector2 Head, Vector2 Staff);
    private readonly record struct Mapping(Rig Source, Vector2 Head, Vector2 Staff)
    {
        internal Vector2 Delta(Vector2 point)
        {
            // Los pesos singulares fijan los tres anclajes, incluida la punta
            // real del arma equipada. No se duplica el báculo durante el giro.
            float feet = 1 / Math.Max(.00001f, point.LengthSquared());
            float head = 1 / Math.Max(.00001f, (point - Head).LengthSquared());
            float staff = 1 / Math.Max(.00001f, (point - Staff).LengthSquared());
            return ((Source.Head - Head) * head + (Source.Staff - Staff) * staff) / (feet + head + staff);
        }

        internal Vector2 Project(Vector2 sourcePoint)
        {
            var local = sourcePoint - Source.Feet;
            var result = local;
            for (int i = 0; i < 6; i++) result = local - Delta(result);
            return result;
        }
    }

    internal static Image Bake(Image source, WizardSprites.AtlasSpec spec,
        WizardSprites.FrameSpec[] castOrigins, WizardSprites.FrameSpec[]? staffAnchors,
        out WizardSprites.AtlasSpec result)
    {
        if (castOrigins.Length != WizardSprites.SourceRows ||
            staffAnchors != null && staffAnchors.Length != WizardSprites.SourceRows * Poses)
            throw new InvalidDataException("Anclajes de origen incorrectos al generar los giros del mago.");
        source.Convert(Image.Format.Rgba8);
        byte[] input = source.GetData();
        int sourceWidth = source.GetWidth(), sourceHeight = source.GetHeight();
        if (sourceWidth != spec.Width || sourceHeight != spec.Height || input.Length < sourceWidth * sourceHeight * 4)
            throw new InvalidDataException("La imagen de origen no coincide con sus anclajes.");
        float cellWidth = sourceWidth / (float)WizardSprites.SourceColumns;
        float cellHeight = sourceHeight / (float)WizardSprites.SourceRows;
        if (Math.Abs(cellWidth - cellHeight) > .01f)
            throw new InvalidDataException("Las celdas del mago deben ser cuadradas.");
        var distance = SilhouetteDistance(input, sourceWidth, sourceHeight);
        var transparent = new Pixel(0, 0, 0, 0, -cellWidth);
        float pixels = (cellWidth + Padding * 2) / FrameSize;
        int width = Columns * FrameSize, height = Rows * FrameSize;
        var output = new byte[width * height * 4];
        var feet = new WizardSprites.FrameSpec[Directions * Poses];
        var eyes = new WizardSprites.FrameSpec[Directions * Poses][];
        var origins = new WizardSprites.FrameSpec[Directions];

        Rig SourceRig(int frame)
        {
            var foot = Point(spec.Frames[frame]);
            var face = spec.EyeAnchors![frame];
            var head = face.Length == 0 ? new Vector2(0, -cellHeight * .51f)
                : face.Select(Point).Aggregate(Vector2.Zero, (sum, p) => sum + p) / face.Length - foot;
            var staff = staffAnchors != null ? Point(staffAnchors[frame]) - foot
                : Point(castOrigins[frame / Poses]) - Point(spec.Frames[frame / Poses * Poses + 3]);
            return new(foot, head, staff);
        }

        for (int direction = 0; direction < Directions; direction++)
        {
            int sector = direction / 6;
            float blend = direction % 6 / 6f;
            int rowA = SourceRows[sector], rowB = SourceRows[(sector + 1) % 4];
            for (int pose = 0; pose < Poses; pose++)
            {
                int frame = direction * Poses + pose, frameA = rowA * Poses + pose, frameB = rowB * Poses + pose;
                var rigA = SourceRig(frameA); var rigB = SourceRig(frameB);
                var targetFeet = rigA.Feet.Lerp(rigB.Feet, blend);
                var targetHead = rigA.Head.Lerp(rigB.Head, blend);
                var targetStaff = rigA.Staff.Lerp(rigB.Staff, blend);
                var mappingA = new Mapping(rigA, targetHead, targetStaff);
                var mappingB = new Mapping(rigB, targetHead, targetStaff);
                Vector2 ToCell(Vector2 point) => (point + targetFeet + Vector2.One * Padding) / pixels;
                feet[frame] = Point(ToCell(Vector2.Zero));
                if (pose == 3) origins[direction] = Point(ToCell(targetStaff));
                int primary = blend < .5f ? frameA : frameB;
                var mapping = blend < .5f ? mappingA : mappingB;
                eyes[frame] = spec.EyeAnchors![primary].Select(p => Point(ToCell(mapping.Project(Point(p))))).ToArray();
                int destX = frame % Columns * FrameSize, destY = frame / Columns * FrameSize;

                for (int y = 0; y < FrameSize; y++)
                for (int x = 0; x < FrameSize; x++)
                {
                    var point = new Vector2((x + .5f) * pixels - Padding, (y + .5f) * pixels - Padding) - targetFeet;
                    var a = Sample(rigA.Feet + point + (blend == 0 ? Vector2.Zero : mappingA.Delta(point)), rowA, pose);
                    var b = blend == 0 ? a : Sample(rigB.Feet + point + mappingB.Delta(point), rowB, pose);
                    var color = blend == 0 ? a : Pixel.Mix(a, b, blend);
                    int index = ((destY + y) * width + destX + x) * 4;
                    if (color.A < 1 / 255f) continue;
                    output[index] = Byte(color.R / color.A);
                    output[index + 1] = Byte(color.G / color.A);
                    output[index + 2] = Byte(color.B / color.A);
                    // Se interpola el contorno, además del color: la nueva vista
                    // tiene una silueta sólida y conserva el brillo suave del arte.
                    float alpha = blend == 0 ? color.A : Math.Max(Math.Clamp(.5f + color.Distance / pixels, 0, 1) * Math.Max(a.A, b.A),
                        (a.A < .45f ? a.A * (1 - blend) : 0) + (b.A < .45f ? b.A * blend : 0));
                    output[index + 3] = Byte(alpha * 255);
                }
            }
        }

        result = spec with { Width = width, Height = height, PixelSize = spec.PixelSize * pixels,
            Frames = feet, CastOrigins = origins, EyeAnchors = eyes };
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, output);
        image.FixAlphaEdges();
        image.GenerateMipmaps();
        return image;

        Pixel Sample(Vector2 local, int row, int pose)
        {
            if (local.X < 0 || local.Y < 0 || local.X >= cellWidth || local.Y >= cellHeight) return transparent;
            float left = pose * cellWidth, top = row * cellHeight;
            float sx = left + local.X - .5f, sy = top + local.Y - .5f;
            int x = (int)MathF.Floor(sx), y = (int)MathF.Floor(sy);
            float tx = sx - x, ty = sy - y;
            return Pixel.Mix(Pixel.Mix(Read(x, y), Read(x + 1, y), tx),
                Pixel.Mix(Read(x, y + 1), Read(x + 1, y + 1), tx), ty);

            Pixel Read(int px, int py)
            {
                if (px < 0 || py < 0 || px >= sourceWidth || py >= sourceHeight ||
                    px + .5f < left || px + .5f >= left + cellWidth || py + .5f < top || py + .5f >= top + cellHeight) return transparent;
                int offset = (py * sourceWidth + px) * 4;
                float alpha = input[offset + 3] / 255f;
                return new(input[offset] * alpha, input[offset + 1] * alpha, input[offset + 2] * alpha, alpha,
                    distance[py * sourceWidth + px]);
            }
        }
    }

    private static byte Byte(float value) => (byte)Math.Clamp((int)(value + .5f), 0, 255);

    private static float[] SilhouetteDistance(byte[] rgba, int width, int height)
    {
        var mask = new bool[width * height]; var result = new float[mask.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = rgba[i * 4 + 3] >= 128;
        const float diagonal = 1.41421356f;
        for (int row = 0; row < WizardSprites.SourceRows; row++)
        for (int column = 0; column < WizardSprites.SourceColumns; column++)
        {
            int left = (int)MathF.Ceiling(column * width / (float)WizardSprites.SourceColumns - .5f);
            int right = (int)MathF.Ceiling((column + 1) * width / (float)WizardSprites.SourceColumns - .5f);
            int top = (int)MathF.Ceiling(row * height / (float)WizardSprites.SourceRows - .5f);
            int bottom = (int)MathF.Ceiling((row + 1) * height / (float)WizardSprites.SourceRows - .5f);
            bool Inside(int x, int y) => x >= left && x < right && y >= top && y < bottom && mask[y * width + x];
            for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            {
                int index = y * width + x; bool value = mask[index];
                result[index] = value != Inside(x - 1, y) || value != Inside(x + 1, y) ||
                    value != Inside(x, y - 1) || value != Inside(x, y + 1) ? .5f : 1000000;
            }
            for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            {
                int index = y * width + x; float value = result[index];
                if (x > left) value = Math.Min(value, result[index - 1] + 1);
                if (y > top)
                {
                    value = Math.Min(value, result[index - width] + 1);
                    if (x > left) value = Math.Min(value, result[index - width - 1] + diagonal);
                    if (x + 1 < right) value = Math.Min(value, result[index - width + 1] + diagonal);
                }
                result[index] = value;
            }
            for (int y = bottom - 1; y >= top; y--)
            for (int x = right - 1; x >= left; x--)
            {
                int index = y * width + x; float value = result[index];
                if (x + 1 < right) value = Math.Min(value, result[index + 1] + 1);
                if (y + 1 < bottom)
                {
                    value = Math.Min(value, result[index + width] + 1);
                    if (x > left) value = Math.Min(value, result[index + width - 1] + diagonal);
                    if (x + 1 < right) value = Math.Min(value, result[index + width + 1] + diagonal);
                }
                result[index] = value;
            }
            for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                if (!mask[y * width + x]) result[y * width + x] *= -1;
        }
        return result;
    }
}

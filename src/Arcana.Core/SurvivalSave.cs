using System.Text.Json;

namespace Arcana.Core;

/// <summary>Un único punto de continuación local. No contiene credenciales ni premios de cuenta.</summary>
public sealed record SurvivalSave(int Version, DateTimeOffset SavedAt, string RunId, string BestiaryUid,
    int LocalPlayerId, JsonElement State)
{
    public const int CurrentVersion = 1;
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true, MaxDepth = 32 };
}

public sealed record RestoredSurvival(GameWorld World, SurvivalSave Save, bool RecoveredBackup);

/// <summary>Escritura atómica y copia del último guardado válido; nunca utiliza la red.</summary>
public sealed class SurvivalSaveStore(string path)
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    public bool Exists => File.Exists(path) || File.Exists(path + ".bak");

    private static SurvivalSave Read(string file)
    {
        if (new FileInfo(file).Length > MaximumBytes) throw new InvalidDataException("El guardado es demasiado grande.");
        return JsonSerializer.Deserialize<SurvivalSave>(File.ReadAllBytes(file), SurvivalSave.Json)
            ?? throw new InvalidDataException("El guardado está vacío.");
    }

    public RestoredSurvival Load()
    {
        Exception? failure = null;
        foreach (string file in new[] { path, path + ".bak" })
        {
            if (!File.Exists(file)) continue;
            try
            {
                var save = Read(file);
                return new(GameWorld.RestoreSurvival(save), save, file != path);
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
            { failure ??= error; }
        }
        throw new InvalidDataException("No se pudo leer una partida de supervivencia compatible.", failure);
    }

    public void Write(SurvivalSave save)
    {
        // También valida la estructura antes de sustituir el punto de continuación anterior.
        GameWorld.ValidateSurvivalSave(save);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(save, SurvivalSave.Json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("El guardado es demasiado grande.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        if (File.Exists(path))
        {
            try
            {
                GameWorld.ValidateSurvivalSave(Read(path));
                File.Copy(path, path + ".bak.tmp", true);
                File.Move(path + ".bak.tmp", path + ".bak", true);
            }
            catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException)
            { /* Si se recuperó la copia, no sustituirla por un archivo corrupto. */ }
        }
        File.Move(temporary, path, true);
    }
}

/// <summary>Flujo de azar con estado explícito para continuar sin reiniciar la secuencia de la horda.</summary>
internal sealed class SurvivalRandom : Random
{
    public ulong State { get; set; }
    public SurvivalRandom(int seed) => State = unchecked((ulong)(long)seed) ^ 0x9e3779b97f4a7c15UL;
    private ulong Draw()
    {
        ulong x = State; x ^= x >> 12; x ^= x << 25; x ^= x >> 27; State = x;
        return unchecked(x * 2685821657736338717UL);
    }
    protected override double Sample() => (Draw() >> 11) * (1.0 / (1UL << 53));
    public override double NextDouble() => Sample();
    public override float NextSingle() => (float)((Draw() >> 40) * (1.0 / (1UL << 24)));
    public override int Next() => Next(int.MaxValue);
    public override int Next(int maxValue) => Next(0, maxValue);
    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(maxValue));
        ulong range = (ulong)((long)maxValue - minValue);
        if (range == 0) return minValue;
        ulong threshold = unchecked(0UL - range) % range, value;
        do { value = Draw(); } while (value < threshold);
        return (int)(minValue + (long)(value % range));
    }
    public override void NextBytes(byte[] buffer) { ArgumentNullException.ThrowIfNull(buffer); NextBytes(buffer.AsSpan()); }
    public override void NextBytes(Span<byte> buffer)
    { for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)Draw(); }
}

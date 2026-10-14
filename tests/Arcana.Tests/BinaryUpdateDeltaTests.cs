using Arcana.Core;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class BinaryUpdateDeltaTests
{
    private static readonly byte[] Base = [1, 2, 3, 4, 5, 6];
    private static readonly byte[] Target = [1, 2, 3, 9, 8, 5, 6];
    private static void Patch(string path, Action<BinaryWriter> instructions, int count = 3)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new BinaryWriter(gzip);
        writer.Write("ARCDLT01"u8.ToArray());
        foreach (string version in new[] { "0.17.16", "0.17.17" })
        { var bytes = Encoding.UTF8.GetBytes(version); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
        writer.Write((long)Base.Length); writer.Write((long)Target.Length);
        writer.Write(SHA256.HashData(Base)); writer.Write(SHA256.HashData(Target));
        writer.Write(count);
        instructions(writer);
    }
    private static void Instructions(BinaryWriter w, byte changed = 9)
    {
        w.Write((byte)0); w.Write(0L); w.Write(3L);
        w.Write((byte)1); w.Write(2L); w.Write(new byte[] { changed, 8 });
        w.Write((byte)0); w.Write(4L); w.Write(2L);
    }
    private static void WithFixture(Action<string, string, string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "arcana-binary-delta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "base.apk"), patch = Path.Combine(root, "update.delta"), target = Path.Combine(root, "target.apk");
            File.WriteAllBytes(source, Base); Patch(patch, w => Instructions(w));
            body(source, patch, target);
        }
        finally { Directory.Delete(root, true); }
    }
    public static void Run(Action<string, Action> test)
    {
        test("Parche APK: reconstruccion exacta con copia y datos nuevos", () => WithFixture((source, patch, target) =>
        {
            BinaryUpdateDelta.Apply(source, patch, target, "0.17.16", "0.17.17", SHA256.HashData(Target), Target.Length);
            if (!File.ReadAllBytes(target).SequenceEqual(Target) || !File.ReadAllBytes(source).SequenceEqual(Base))
                throw new Exception("El resultado o la base son incorrectos.");
        }));
        test("Parche APK: una base alterada o de otra version conserva el destino", () => WithFixture((source, patch, target) =>
        {
            File.WriteAllText(target, "existing-destination");
            foreach (string version in new[] { "0.17.15", "0.17.16" })
            {
                if (version == "0.17.16") File.WriteAllBytes(source, [6, 5, 4, 3, 2, 1]);
                bool rejected = false;
                try { BinaryUpdateDelta.Apply(source, patch, target, version, "0.17.17", SHA256.HashData(Target), Target.Length); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected || File.ReadAllText(target) != "existing-destination") throw new Exception("Se acepto otra base.");
            }
        }));
        test("Parche APK: instrucciones fuera de limites nunca instalan un resultado parcial", () => WithFixture((source, patch, target) =>
        {
            foreach (var (opcode, offset, length) in new[] { ((byte)0, -1L, 3L), ((byte)0, 5L, 3L), ((byte)0, 0L, 8L), ((byte)7, 0L, 1L) })
            {
                Patch(patch, w => { w.Write(opcode); if (opcode == 0) w.Write(offset); w.Write(length); }, 1);
                bool rejected = false;
                try { BinaryUpdateDelta.Apply(source, patch, target, "0.17.16", "0.17.17", SHA256.HashData(Target), Target.Length); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected || File.Exists(target) || File.Exists(target + ".part")) throw new Exception("Parche inseguro o parcial aceptado.");
            }
        }));
        test("Parche APK: SHA final, truncacion y cancelacion se comprueban antes de sustituir", () => WithFixture((source, patch, target) =>
        {
            Patch(patch, w => Instructions(w, changed: 7));
            bool rejected = false;
            try { BinaryUpdateDelta.Apply(source, patch, target, "0.17.16", "0.17.17", SHA256.HashData(Target), Target.Length); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected || File.Exists(target)) throw new Exception("APK con firma/bytes distintos aceptada.");
            File.WriteAllBytes(patch, File.ReadAllBytes(patch)[..20]);
            rejected = false;
            try { BinaryUpdateDelta.Apply(source, patch, target, "0.17.16", "0.17.17", SHA256.HashData(Target), Target.Length); }
            catch (IOException) { rejected = true; }
            if (!rejected || File.Exists(target + ".part")) throw new Exception("Parche truncado aceptado.");
            Patch(patch, w => Instructions(w));
            using var stop = new CancellationTokenSource(); stop.Cancel();
            rejected = false;
            try { BinaryUpdateDelta.Apply(source, patch, target, "0.17.16", "0.17.17", SHA256.HashData(Target), Target.Length, stop.Token); }
            catch (OperationCanceledException) { rejected = true; }
            if (!rejected || File.Exists(target)) throw new Exception("Cancelacion ignorada.");
        }));
    }
    public static void Interoperability(string folder, Action<string, Action> test)
    {
        test("Parche publicado en Python: cliente C# reconstruye APK byte por byte", () =>
        {
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "android-result.json")));
            string target = Path.Combine(folder, "rebuilt-by-client.apk");
            BinaryUpdateDelta.Apply(Path.Combine(folder, "base.apk"), Path.Combine(folder, "android.delta"), target,
                "0.17.16", "0.17.17", Convert.FromHexString(result.RootElement.GetProperty("sha256").GetString()!),
                result.RootElement.GetProperty("length").GetInt64());
            if (!File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "target.apk"))))
                throw new Exception("El cliente no reconstruye el archivo firmado original.");
            File.Delete(target);
        });
    }
}

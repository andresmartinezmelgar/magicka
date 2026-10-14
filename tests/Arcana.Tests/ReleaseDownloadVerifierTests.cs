using Arcana.Core;

static class ReleaseDownloadVerifierTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Actualizador: compara SHA256 exacto de Windows y Android", () =>
        {
            string digest = new string('a', 64);
            string manifest = digest + "  ArcanaArena-v0.17.16-Windows-x64.zip\n" +
                new string('b', 64) + "  ArcanaArena-v0.17.16-Android-arm64.apk\n";
            if (Convert.ToHexString(ReleaseDownloadVerifier.ReadExpectedHash(manifest,
                "ArcanaArena-v0.17.16-Windows-x64.zip")) != digest.ToUpperInvariant())
                throw new Exception("El hash no coincide.");
        });
        test("Actualizador: rechaza manifiesto ausente, manipulado o duplicado", () =>
        {
            string entry = new string('a', 64) + "  ArcanaArena-v0.17.16-Windows-x64.zip\n";
            foreach (var (manifest, filename) in new[]
            {
                ("", "ArcanaArena-v0.17.16-Windows-x64.zip"),
                (entry + entry, "ArcanaArena-v0.17.16-Windows-x64.zip"),
                (new string('z', 64) + "  ArcanaArena-v0.17.16-Windows-x64.zip", "ArcanaArena-v0.17.16-Windows-x64.zip"),
                (entry, "../ArcanaArena-v0.17.16-Windows-x64.zip")
            })
            {
                bool rejected = false;
                try { ReleaseDownloadVerifier.ReadExpectedHash(manifest, filename); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected) throw new Exception("Manifiesto o nombre inseguro aceptado.");
            }
        });
    }
}

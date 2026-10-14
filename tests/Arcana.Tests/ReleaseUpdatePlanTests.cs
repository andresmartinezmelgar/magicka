using Arcana.Core;
using System.Security.Cryptography;
using System.Text.Json;

static class ReleaseUpdatePlanTests
{
    private const string Root = "https://github.com/andresmartinezmelgar/magicka/releases/download/v0.17.17/";
    private const string Windows = "ArcanaArena-v0.17.17-Windows-x64.zip";
    private const string Android = "ArcanaArena-v0.17.17-Android-arm64.apk";
    private static object Asset(string name, string? url = null, long bytes = 100) =>
        new { name, browser_download_url = url ?? Root + name, size = bytes };
    private static ReleaseUpdatePlan? Plan(object[] assets, bool android = false,
        string tag = "v0.17.17", bool draft = false, bool prerelease = false, string local = "0.17.16")
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, assets }));
        return ReleaseUpdatePlan.Read(json.RootElement, local, android);
    }
    public static void Run(Action<string, Action> test)
    {
        test("Actualizacion diferencial: elige la base exacta y conserva el paquete completo", () =>
        {
            string name = "ArcanaArena-v0.17.17-Windows-x64-from-v0.17.16.zip";
            var plan = Plan([Asset(Windows), Asset(name, bytes: 20), Asset("SHA256SUMS-Windows.txt")]);
            if (plan?.Differential != true || plan.Filename != name || plan.FullPackage?.Filename != Windows || plan.BaseVersion != "0.17.16")
                throw new Exception("El parche no coincide con la version instalada.");
            string android = "ArcanaArena-v0.17.17-Android-arm64-from-v0.17.16.delta";
            if (Plan([Asset(Android), Asset(android, bytes: 20), Asset("SHA256SUMS-Android.txt")], true)?.Differential != true)
                throw new Exception("No se selecciono el parche Android.");
        });
        test("Actualizacion diferencial: sin base compatible o ahorro se utiliza descarga completa", () =>
        {
            foreach (var asset in new[] {
                Asset("ArcanaArena-v0.17.17-Windows-x64-from-v0.17.15.zip", bytes: 20),
                Asset("ArcanaArena-v0.17.17-Windows-x64-from-v0.17.16.zip", bytes: 90),
                Asset("ArcanaArena-v0.17.17-Windows-x64-from-v0.17.16.zip", "https://evil.example/patch.zip", 20) })
                if (Plan([Asset(Windows), asset, Asset("SHA256SUMS.txt")])?.Filename != Windows)
                    throw new Exception("No se conservo la alternativa completa.");
        });
        test("Actualizacion: descarga solo el paquete de su plataforma y de la version anunciada", () =>
        {
            object[] assets = [Asset(Windows), Asset(Android), Asset("SHA256SUMS-Windows.txt"), Asset("SHA256SUMS-Android.txt")];
            if (Plan(assets)?.Filename != Windows || Plan(assets, true)?.Filename != Android)
                throw new Exception("Plataforma incorrecta.");
            if (Plan([Asset("ArcanaArena-v0.17.16-Windows-x64.zip"), Asset("SHA256SUMS.txt")]) != null)
                throw new Exception("Se acepto un archivo de otra version.");
        });
        test("Actualizacion: espera el ZIP cuando la Release solo ha publicado Android", () =>
        {
            object[] assets = [Asset(Android), Asset("SHA256SUMS-Android.txt")];
            if (Plan(assets) != null || Plan(assets, true) == null) throw new Exception("Release incompleta mal interpretada.");
        });
        test("Actualizacion: no reinstala, retrocede ni instala borradores o previews", () =>
        {
            object[] assets = [Asset(Windows), Asset("SHA256SUMS.txt")];
            if (Plan(assets, local: "0.17.17") != null || Plan(assets, local: "0.18.0") != null ||
                Plan(assets, draft: true) != null || Plan(assets, prerelease: true) != null ||
                Plan(assets, tag: "../../unsafe") != null) throw new Exception("Version no publicable aceptada.");
        });
        test("Actualizacion: manifiesto ausente o assets duplicados no se descargan", () =>
        {
            if (Plan([Asset(Windows)]) != null ||
                Plan([Asset(Windows), Asset(Windows), Asset("SHA256SUMS.txt")]) != null ||
                Plan([Asset(Windows), Asset("SHA256SUMS.txt"), Asset("SHA256SUMS.txt")]) != null)
                throw new Exception("Se acepto una descarga ambigua o sin integridad.");
        });
        test("Actualizacion: rechaza enlaces ajenos al repositorio, tag, HTTPS o puerto oficial", () =>
        {
            foreach (string url in new[] {
                Root.Replace("https:", "http:") + Windows,
                Root.Replace("github.com", "github.com.evil.example") + Windows,
                Root.Replace("github.com", "github.com:8443") + Windows,
                Root.Replace("magicka", "another-repository") + Windows,
                Root.Replace("v0.17.17", "v0.17.18") + Windows,
                Root + Windows + "?token=unexpected",
                Root.Replace("github.com", "user@github.com") + Windows })
                if (Plan([Asset(Windows, url), Asset("SHA256SUMS.txt")]) != null)
                    throw new Exception("Enlace no oficial aceptado: " + url);
        });
        test("Actualizacion: rechaza tamaños vacios, excesivos y metadatos invalidos", () =>
        {
            foreach (long bytes in new[] { 0L, -1L, ReleaseUpdatePlan.MaximumBytes + 1 })
                if (Plan([Asset(Windows, bytes: bytes), Asset("SHA256SUMS.txt")]) != null)
                    throw new Exception("Tamaño incorrecto aceptado.");
            using var json = JsonDocument.Parse("{\"tag_name\":\"v0.17.17\",\"assets\":[{}]}");
            if (ReleaseUpdatePlan.Read(json.RootElement, "0.17.16", false) != null)
                throw new Exception("Metadatos incompletos aceptados.");
        });
        test("Actualizacion: reutiliza cache verificada y rechaza archivos truncados o alterados", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), "arcana-update-cache-" + Guid.NewGuid().ToString("N"));
            byte[] content = [1, 2, 3, 4], hash = SHA256.HashData(content);
            try
            {
                File.WriteAllBytes(path, content);
                if (!ReleaseDownloadVerifier.IsVerifiedFileAsync(path, 4, hash).GetAwaiter().GetResult())
                    throw new Exception("Cache valida rechazada.");
                File.WriteAllBytes(path, [1, 2, 3]);
                if (ReleaseDownloadVerifier.IsVerifiedFileAsync(path, 4, hash).GetAwaiter().GetResult())
                    throw new Exception("Archivo truncado aceptado.");
                File.WriteAllBytes(path, [1, 2, 3, 5]);
                if (ReleaseDownloadVerifier.IsVerifiedFileAsync(path, 4, hash).GetAwaiter().GetResult())
                    throw new Exception("Archivo alterado aceptado.");
                File.Delete(path);
                if (ReleaseDownloadVerifier.IsVerifiedFileAsync(path, 4, hash).GetAwaiter().GetResult())
                    throw new Exception("Archivo inexistente aceptado.");
            }
            finally { File.Delete(path); }
        });
    }
}

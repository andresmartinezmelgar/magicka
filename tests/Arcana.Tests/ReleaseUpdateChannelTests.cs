using Arcana.Core;
using System.Text.Json;

internal static class ReleaseUpdateChannelTests
{
    private const string Tester = "qRUza2DieeTypFqBIRA5KhbtXoY2";
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static object Release(int build = 12, string version = "0.18.1", string branch = "feature/defensa-oleadas",
        bool draft = false, bool preview = true, bool hashes = true, string? host = null, bool androidOnly = false)
    {
        string tag = $"preview-v{version}-r{build}";
        string root = (host ?? "https://github.com/andresmartinezmelgar/magicka") + "/releases/download/" + tag + "/";
        object Asset(string name) => new { name, browser_download_url = root + name, size = 1024 };
        var assets = new List<object> { Asset($"ArcanaArena-v{version}-Android-arm64.apk") };
        if (!androidOnly) assets.Add(Asset($"ArcanaArena-v{version}-Windows-x64.zip"));
        if (hashes) assets.Add(Asset("SHA256SUMS.txt"));
        return new { tag_name = tag, draft, prerelease = preview, target_commitish = new string('a', 40),
            body = ReleaseUpdateChannel.BranchMarker + branch + "\n" + ReleaseChanges.Start +
                "\n- Novedades de la prueba " + build + ".\n" + ReleaseChanges.End, assets };
    }
    private static ReleaseUpdatePlan? Select(object releases, string uid = Tester, bool signedIn = true,
        string installed = "0.18.0", long installedBuild = 0, bool android = true)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(releases));
        return ReleaseUpdateChannel.Select(document.RootElement, uid, signedIn, installed, installedBuild, android);
    }

    public static void Run(Action<string, Action> test)
    {
        test("Canal de pruebas: solo los dos UID autenticados tienen acceso al selector", () =>
        {
            foreach (string uid in new[] { Tester, "hg7F74kCNVQU3tU2itdjWu9SkyS2" })
            {
                Check(ReleaseUpdateChannel.PreviewAllowed(uid, true), "UID autorizado rechazado.");
                Check(!ReleaseUpdateChannel.PreviewAllowed(uid, false), "La caché sin sesión habilitó pruebas.");
            }
            foreach (string? uid in new[] { null, "", "otro", Tester.ToLowerInvariant(), Tester + " " })
                Check(!ReleaseUpdateChannel.PreviewAllowed(uid, true), "UID ajeno aceptado.");
        });
        test("Canal de pruebas: selecciona la compilación más nueva compatible de una rama", () =>
        {
            var plan = Select(new[] { Release(13), Release(15), Release(11) });
            Check(plan is { Preview: true, BuildNumber: 15, SourceBranch: "feature/defensa-oleadas", Differential: false } &&
                plan.Filename.EndsWith("Android-arm64.apk"), "No eligió la prueba Android más reciente.");
            Check(plan!.Changes.SequenceEqual(new[] { "Novedades de la prueba 15." }), "Mezcló notas de otras compilaciones.");
            var windows = Select(new[] { Release() }, android: false);
            Check(windows?.Filename.EndsWith("Windows-x64.zip") == true, "Plataforma equivocada.");
        });
        test("Canal de pruebas: una compilación nueva de igual versión se instala una sola vez", () =>
        {
            Check(Select(new[] { Release(20) }, installed: "0.18.1", installedBuild: 19) != null, "Ignoró la nueva compilación.");
            Check(Select(new[] { Release(20) }, installed: "0.18.1", installedBuild: 20) == null, "Volvió a ofrecer la compilación instalada.");
            Check(Select(new[] { Release(19) }, installed: "0.18.1", installedBuild: 20) == null, "Retrocedió de compilación.");
            Check(Select(new[] { Release(21, "0.17.24") }, installed: "0.18.1", installedBuild: 20) == null, "Ofreció una APK de versión inferior.");
        });
        test("Canal de pruebas: excluye main, borradores y releases estables", () =>
        {
            foreach (object release in new[] { Release(branch: "main"), Release(branch: "refs/heads/main"), Release(draft: true), Release(preview: false) })
                Check(Select(new[] { release }) == null, "Origen no experimental aceptado.");
            Check(Select(new[] { Release(branch: "otra-rama") })?.SourceBranch == "otra-rama", "El canal quedó ligado a una sola rama.");
        });
        test("Canal estable: usuarios ajenos y sesiones cerradas rechazan preliminares", () =>
        {
            foreach (var pair in new[] { (Tester, false), ("normal", true), ("", false) })
                Check(Select(Release(), pair.Item1, pair.Item2) == null && Select(new[] { Release() }, pair.Item1, pair.Item2) == null,
                    "Se filtró una prueba a una cuenta estable.");
            string root = "https://github.com/andresmartinezmelgar/magicka/releases/download/v0.18.1/";
            var stable = new { tag_name = "v0.18.1", draft = false, prerelease = false, assets = new[] {
                new { name = "ArcanaArena-v0.18.1-Android-arm64.apk", browser_download_url = root + "ArcanaArena-v0.18.1-Android-arm64.apk", size = 200 },
                new { name = "SHA256SUMS.txt", browser_download_url = root + "SHA256SUMS.txt", size = 80 } } };
            Check(Select(stable, "normal") is { Preview: false }, "El canal estable dejó de actualizarse.");
        });
        test("Canal de pruebas: conserva la validación de origen e integridad", () =>
        {
            Check(Select(new[] { Release(hashes: false) }) == null, "Aceptó una prueba sin SHA-256.");
            Check(Select(new[] { Release(host: "https://evil.example") }) == null, "Aceptó una descarga externa.");
            Check(Select(new[] { Release(androidOnly: true) }, android: false) == null, "Windows recibió una APK.");
            Check(Select(new object[] { new { tag_name = "preview-invalid" }, Release(13) })?.BuildNumber == 13,
                "Una entrada incompleta impidió seleccionar una válida.");
        });
    }
}

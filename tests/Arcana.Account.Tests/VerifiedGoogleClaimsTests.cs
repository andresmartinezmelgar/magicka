using Arcana.Server;
using Newtonsoft.Json;

internal static class VerifiedGoogleClaimsTests
{
    internal static void Run()
    {
        foreach (string address in new[] { "127.0.0.1:27015", "localhost:27015", "ws://[::1]:27015/play" })
            if (!Arcana.Core.PublicAccountServer.NeedsDiscovery(address, true) || Arcana.Core.PublicAccountServer.NeedsDiscovery(address, false))
                throw new Exception("La dirección local no se sustituye exclusivamente en móvil.");
        if (Arcana.Core.PublicAccountServer.NeedsDiscovery("wss://example.trycloudflare.com/play", true) ||
            Arcana.Core.PublicAccountServer.NeedsDiscovery("192.168.1.20:27015", true))
            throw new Exception("El descubrimiento reemplaza un servidor remoto elegido por el jugador.");
        IReadOnlyDictionary<string, object> Decode(string json) => JsonConvert.DeserializeObject<Dictionary<string, object>>(json)!;
        var identity = GoogleAccountVerifier.ReadVerifiedIdentity("verified-user", Decode("{\"firebase\":{\"sign_in_provider\":\"google.com\",\"identities\":{\"google.com\":[\"google-user\"]}},\"name\":\"Mago\\nAzul\"}"));
        if (identity.Uid != "verified-user" || identity.Name != "MagoAzul") throw new Exception("No se acepta el formato real de claims de Firebase Admin.");
        var unnamed = GoogleAccountVerifier.ReadVerifiedIdentity("verified-user", Decode("{\"firebase\":{\"sign_in_provider\":\"google.com\"}}"));
        if (unnamed.Name != "Mago") throw new Exception("El nombre opcional impide completar Google Login.");
        foreach (string json in new[] { "{}", "{\"firebase\":null}", "{\"firebase\":[]}", "{\"firebase\":{\"sign_in_provider\":\"password\"}}", "{\"firebase\":{\"sign_in_provider\":[]}}", "{\"firebase\":{\"sign_in_provider\":null}}" })
        {
            bool rejected = false;
            try { GoogleAccountVerifier.ReadVerifiedIdentity("verified-user", Decode(json)); }
            catch (UnauthorizedAccessException) { rejected = true; }
            if (!rejected) throw new Exception("Se acepta un proveedor no Google o un claim mal formado.");
        }
        // El servidor incluido debe leer ARCANA_FIREBASE_CONFIG sin exigir
        // FIREBASE_PROJECT_ID manual; la opción de túnel es voluntaria.
        string testDirectory = Path.Combine(Path.GetTempPath(), "arcana-firebase-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Arcana.Core.LocalFirebaseHosting.DirectoryPath(testDirectory);
            Directory.CreateDirectory(dir);
            string configuration = Path.Combine(dir, Arcana.Core.LocalFirebaseHosting.ConfigFileName);
            string credential = Path.Combine(dir, Arcana.Core.LocalFirebaseHosting.CredentialFileName);
            File.WriteAllText(configuration, """
                {"projectId":"demo-arcana-accounts","apiKey":"test-key","authDomain":"demo-arcana-accounts.firebaseapp.com",
                "publicUrl":"http://127.0.0.1:27015","autoPublicHosting":true}
                """);
            File.WriteAllText(credential, "{\"type\":\"service_account\",\"testing\":true}");
            var start = new System.Diagnostics.ProcessStartInfo("test-server");
            if (!Arcana.Core.LocalFirebaseHosting.Configure(start, testDirectory))
                throw new Exception("No se activó Firebase al encontrar server.local.json.");
            if (start.Environment["ARCANA_FIREBASE_CONFIG"] != configuration ||
                start.Environment["GOOGLE_APPLICATION_CREDENTIALS"] != credential ||
                start.Environment["ARCANA_AUTO_PUBLIC_HOST"] != "true")
                throw new Exception("No se propagó la configuración Firebase al servidor.");
            var options = Arcana.Server.AccountOptions.Read(key => key == "ARCANA_FIREBASE_CONFIG"
                ? start.Environment["ARCANA_FIREBASE_CONFIG"] : null);
            if (options.ProjectId != "demo-arcana-accounts" || !options.Enabled ||
                options.PublicUrl != "http://127.0.0.1:27015")
                throw new Exception("El servidor no lee el JSON local de Firebase.");
            File.Delete(configuration);
            if (Arcana.Core.LocalFirebaseHosting.Configure(start, testDirectory))
                throw new Exception("Se activó Firebase aunque ya no había configuración.");
        }
        finally { if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true); }
        Console.WriteLine("FIREBASE_LOCAL_HOSTING_CONFIG_OK: configuración, credencial y túnel opcional.");
        Console.WriteLine("VERIFIED_GOOGLE_CLAIMS_OK: formato Firebase Admin, nombre opcional, sanitización y rechazo de proveedores inválidos.");
    }
}

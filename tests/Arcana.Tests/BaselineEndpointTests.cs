using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineEndpointTests
{
    public static void RunPublicAccount(Action<string, Action> Test)
    {
        Test("Public account server: valid HTTPS tunnel and expiry", () =>
        {
            var now = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(new { fields = new { url = new { stringValue = "wss://example.trycloudflare.com/play" }, expiresAt = new { timestampValue = now.AddMinutes(2).ToString("O") }, protocol = new { integerValue = Protocol.Version.ToString() } } });
            Check(PublicAccountServer.Parse(json, now) == "wss://example.trycloudflare.com/play", "Valid endpoint rejected.");
        });
        Test("Public account server: reject unsafe URLs, stale leases and protocols", () =>
        {
            var now = DateTimeOffset.UtcNow;
            string Json(string url, DateTimeOffset expiry, int protocol) => JsonSerializer.Serialize(new { fields = new { url = new { stringValue = url }, expiresAt = new { timestampValue = expiry.ToString("O") }, protocol = new { integerValue = protocol.ToString() } } });
            foreach (string url in new[] { "ws://example.trycloudflare.com/play", "wss://evil.example/play", "wss://example.trycloudflare.com.evil.example/play", "wss://user:secret@example.trycloudflare.com/play", "wss://example.trycloudflare.com/play?token=secret", "wss://example.trycloudflare.com:8443/play", "wss://example.trycloudflare.com/other" })
            {
                bool rejected = false;
                try { PublicAccountServer.Parse(Json(url, now.AddMinutes(2), Protocol.Version), now); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Unsafe endpoint accepted: " + url);
            }
            foreach (var entry in new[] { (now.AddSeconds(-1), Protocol.Version), (now.AddMinutes(4), Protocol.Version), (now.AddMinutes(2), Protocol.Version + 1) })
            {
                bool rejected = false;
                try { PublicAccountServer.Parse(Json("wss://example.trycloudflare.com/play", entry.Item1, entry.Item2), now); } catch (IOException) { rejected = true; }
                Check(rejected, "Stale or incompatible announcement accepted.");
            }
        });
    }

    public static void RunLocalHosting(Action<string, Action> Test)
    {
        Test("Firebase en alojamiento local requiere activación explícita y ficheros privados", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "arcana-firebase-local-" + Guid.NewGuid().ToString("N"));
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("Arcana.Server");
                Check(!LocalFirebaseHosting.Configure(start, root), "Firebase se activó sin configuración local.");
                Check(!start.Environment.ContainsKey("GOOGLE_APPLICATION_CREDENTIALS"), "Se usó una credencial sin autorización local.");
                string directory = LocalFirebaseHosting.DirectoryPath(root);
                Directory.CreateDirectory(directory);
                string config = Path.Combine(directory, LocalFirebaseHosting.ConfigFileName);
                string credential = Path.Combine(directory, LocalFirebaseHosting.CredentialFileName);
                File.WriteAllText(config, "{}");
                Check(LocalFirebaseHosting.PublicGameAddress(root) == null, "Un JSON sin URL produjo una dirección de cuenta.");
                bool missing = false;
                try { LocalFirebaseHosting.Configure(start, root); }
                catch (InvalidOperationException) { missing = true; }
                Check(missing, "Firebase se activó sin credencial de servicio.");
                File.WriteAllText(config, "{\"publicUrl\":\"http://localhost:29015\"}");
                Check(LocalFirebaseHosting.PublicGameAddress(root) == "ws://localhost:29015/play",
                    "La dirección local no coincide con el origen de Google Login.");
                File.WriteAllText(config, "{\"publicUrl\":\"https://demo.trycloudflare.com\"}");
                Check(LocalFirebaseHosting.PublicGameAddress(root) == "wss://demo.trycloudflare.com/play",
                    "La URL HTTPS no se transformó en conexión WSS.");
                File.WriteAllText(credential, "{}");
                Check(LocalFirebaseHosting.Configure(start, root), "No se activó con ambas configuraciones.");
                Check(start.Environment["ARCANA_FIREBASE_CONFIG"] == config, "Ruta pública incorrecta.");
                Check(start.Environment["GOOGLE_APPLICATION_CREDENTIALS"] == credential, "Ruta privada incorrecta.");
                Check(start.Environment["ARCANA_REWARD_OUTBOX"] == Path.Combine(directory, "reward-outbox"),
                    "El diario de recompensas no se guardará entre partidas.");
                Check(!File.Exists(Path.Combine(root, "server.local.json")), "Se copió la configuración fuera de la carpeta privada.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
}

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
        Console.WriteLine("VERIFIED_GOOGLE_CLAIMS_OK: formato Firebase Admin, nombre opcional, sanitización y rechazo de proveedores inválidos.");
    }
}

using Arcana.Server;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class AdMobRewardVerifierTests
{
    internal static async Task Run()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string keys = JsonSerializer.Serialize(new { keys = new[] { new { keyId = 7, pem = key.ExportSubjectPublicKeyInfoPem() } } });
        var verifier = new AdMobRewardVerifier(_ => Task.FromResult(keys));
        string uid = "account_123", ticket = Guid.NewGuid().ToString("N"), transaction = Guid.NewGuid().ToString("N");
        string unit = "ca-app-pub-1111111111111111/1234567890";
        string data = $"ad_network=5450213213286189855&ad_unit=1234567890&custom_data={ticket}&reward_amount=1&reward_item=shop_refresh&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}&transaction_id={transaction}&user_id={uid}";
        string Sign(string value) => "?" + value + "&signature=" + WebEncoders.Base64UrlEncode(key.SignData(Encoding.UTF8.GetBytes(value),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) + "&key_id=7";
        var proof = await verifier.Verify(Sign(data), unit);
        if (proof.Uid != uid || proof.TicketId != ticket || proof.TransactionId != transaction) throw new Exception("La firma válida pierde la identidad o la recompensa.");
        async Task Reject(string query, string expected = "ca-app-pub-1111111111111111/1234567890")
        {
            try { await verifier.Verify(query, expected); }
            catch (ArgumentException) { return; }
            throw new Exception("SSV aceptó una confirmación inválida.");
        }
        await Reject(Sign(data).Replace("reward_amount=1", "reward_amount=99"));
        await Reject(Sign(data), "ca-app-pub-1111111111111111/9999999999");
        await Reject("?" + data);
        await Reject(Sign(data + "&user_id=another_account"));
        await Reject(Sign(data).Replace("&key_id=7", "&key_id=7&user_id=another_account"));
        await Reject(Sign(data.Replace(DateTimeOffset.FromUnixTimeMilliseconds(proof.WatchedAt.ToUnixTimeMilliseconds()).ToUnixTimeMilliseconds().ToString(),
            DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds().ToString())));
        Console.WriteLine("ADMOB_SSV_OK: firma ECDSA, identidad, nonce, bloque, fecha, datos alterados y parámetros duplicados.");
    }
}

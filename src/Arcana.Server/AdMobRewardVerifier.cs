using Arcana.Core;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcana.Server;

public sealed record VerifiedAdMobReward(string Uid, string TicketId, string TransactionId, DateTimeOffset WatchedAt);

public sealed class AdMobRewardVerifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, string> _keys = new(StringComparer.Ordinal);
    private DateTimeOffset _keysExpire;
    private readonly Func<CancellationToken, Task<string>> _downloadKeys;
    private readonly TimeProvider _time;
    public AdMobRewardVerifier(Func<CancellationToken, Task<string>>? downloadKeys = null, TimeProvider? time = null)
    {
        _downloadKeys = downloadKeys ?? (token => Http.GetStringAsync("https://www.gstatic.com/admob/reward/verifier-keys.json", token));
        _time = time ?? TimeProvider.System;
    }
    public static RewardedAdsConfiguration? Configuration(IConfiguration config)
    {
        var app = config["ARCANA_ADMOB_APP_ID"] ?? ""; var unit = config["ARCANA_ADMOB_REWARDED_UNIT_ID"] ?? "";
        return config["ARCANA_ADMOB_ENABLED"] == "true" && Regex.IsMatch(app, @"^ca-app-pub-\d{16}~\d{10}$") &&
            Regex.IsMatch(unit, @"^ca-app-pub-\d{16}/\d{10}$") && !app.StartsWith("ca-app-pub-3940256099942544", StringComparison.Ordinal)
            ? new(app, unit) : null;
    }
    public async Task<VerifiedAdMobReward> Verify(string rawQuery, string expectedUnit, CancellationToken token = default)
    {
        string query = rawQuery.TrimStart('?'); int signatureAt = query.IndexOf("&signature=", StringComparison.Ordinal);
        if (query.Length > 8192 || signatureAt < 1) throw new ArgumentException("Callback sin firma.");
        string signed = query[..signatureAt]; var suffix = query[(signatureAt + 1)..].Split('&');
        if (suffix.Length != 2 || !suffix[0].StartsWith("signature=", StringComparison.Ordinal) || !suffix[1].StartsWith("key_id=", StringComparison.Ordinal))
            throw new ArgumentException("Formato de firma inválido.");
        var fields = QueryHelpers.ParseQuery(query);
        if (fields.Any(p => p.Value.Count != 1)) throw new ArgumentException("Parámetros duplicados.");
        string Field(string name) => fields.TryGetValue(name, out var value) ? value.ToString() : throw new ArgumentException("Falta " + name);
        string pem; await _gate.WaitAsync(token);
        try
        {
            if (_time.GetUtcNow() >= _keysExpire || !_keys.ContainsKey(Field("key_id")))
            {
                using var doc = JsonDocument.Parse(await _downloadKeys(token));
                _keys = doc.RootElement.GetProperty("keys").EnumerateArray().ToDictionary(k => k.GetProperty("keyId").ToString(), k => k.GetProperty("pem").GetString()!);
                _keysExpire = _time.GetUtcNow().AddHours(12);
            }
            if (!_keys.TryGetValue(Field("key_id"), out pem!)) throw new ArgumentException("Clave desconocida.");
        }
        finally { _gate.Release(); }
        try
        {
            using var key = ECDsa.Create(); key.ImportFromPem(pem);
            if (!key.VerifyData(Encoding.UTF8.GetBytes(signed), WebEncoders.Base64UrlDecode(Field("signature")), HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence)) throw new ArgumentException("Firma de anuncio inválida.");
        }
        catch (CryptographicException e) { throw new ArgumentException("Firma de anuncio inválida.", e); }
        string unit = Field("ad_unit");
        if (unit != expectedUnit && unit != expectedUnit.Split('/').Last()) throw new ArgumentException("Bloque publicitario incorrecto.");
        if (!long.TryParse(Field("timestamp"), out long millis)) throw new ArgumentException("Fecha inválida.");
        var watched = DateTimeOffset.FromUnixTimeMilliseconds(millis); var now = _time.GetUtcNow();
        if (watched > now.AddMinutes(2) || watched < now.AddHours(-24)) throw new ArgumentException("Confirmación fuera de plazo.");
        var transaction = Field("transaction_id");
        if (transaction.Length is < 8 or > 256) throw new ArgumentException("Transacción inválida.");
        return new(Field("user_id"), Field("custom_data"), transaction, watched);
    }
}

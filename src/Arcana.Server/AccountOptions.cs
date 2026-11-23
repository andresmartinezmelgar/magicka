using Arcana.Core;

namespace Arcana.Server;

public sealed record AccountOptions(string ProjectId, string ApiKey, string AuthDomain, string PublicUrl)
{
    public bool Enabled => ProjectId.Length > 0;
    public static AccountOptions FromEnvironment() => Read(Environment.GetEnvironmentVariable);
    public static AccountOptions Read(Func<string, string?> environment)
    {
        AccountOptions? file = null;
        string path = environment("ARCANA_FIREBASE_CONFIG")?.Trim() ?? "";
        if (path.Length > 0)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 16 * 1024) throw new ArgumentException("El archivo ARCANA_FIREBASE_CONFIG no existe o es demasiado grande.");
            file = Protocol.Deserialize<AccountOptions>(File.ReadAllText(path)) ?? throw new ArgumentException("Configuración Firebase inválida.");
        }
        string Get(string key, string? fallback = null)
        {
            string? value = environment(key)?.Trim();
            return string.IsNullOrEmpty(value) ? fallback?.Trim() ?? "" : value;
        }
        var options = new AccountOptions(Get("FIREBASE_PROJECT_ID", file?.ProjectId), Get("FIREBASE_API_KEY", file?.ApiKey),
            Get("FIREBASE_AUTH_DOMAIN", file?.AuthDomain), Get("ARCANA_PUBLIC_URL", file?.PublicUrl).TrimEnd('/'));
        if (new[] { options.ProjectId, options.ApiKey, options.AuthDomain, options.PublicUrl }.All(s => s.Length == 0)) return options;
        if (new[] { options.ProjectId, options.ApiKey, options.AuthDomain, options.PublicUrl }.Any(s => s.Length == 0) ||
            !Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var url) || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 ||
            url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback) ||
            options.ProjectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || Uri.CheckHostName(options.AuthDomain) != UriHostNameType.Dns)
            throw new ArgumentException("Configura FIREBASE_PROJECT_ID, FIREBASE_API_KEY, FIREBASE_AUTH_DOMAIN y ARCANA_PUBLIC_URL (HTTPS) para activar las cuentas.");
        return options;
    }
}

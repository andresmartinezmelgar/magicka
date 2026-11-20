using System.Security.Cryptography;
using System.Text;
using Arcana.Core;

namespace Arcana.Server;

/// <summary>Cuotas en memoria acotada. No guarda direcciones, tokens ni identificadores en disco.</summary>
public sealed class RequestBudget(TimeProvider? clock = null)
{
    private sealed record Window(long Until, int Count);
    private readonly Dictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public bool Take(string scope, string identity, int maximum, int seconds = 60)
    {
        if (maximum <= 0 || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        string key = scope + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        long now = _clock.GetUtcNow().ToUnixTimeSeconds();
        lock (_gate)
        {
            if (_windows.TryGetValue(key, out var window) && window.Until > now)
            {
                if (window.Count >= maximum) return false;
                _windows[key] = window with { Count = window.Count + 1 }; return true;
            }
            if (_windows.Count >= 4096)
            {
                foreach (var expired in _windows.Where(p => p.Value.Until <= now).Select(p => p.Key).ToArray()) _windows.Remove(expired);
                if (_windows.Count >= 4096) return false;
            }
            _windows[key] = new(now + seconds, 1); return true;
        }
    }
}

public sealed class HttpBudgetExceededException : Exception { }

public static class HttpAbuseProtection
{
    private const string IdentityKey = "Arcana.VerifiedIdentity", QuotaKey = "Arcana.AccountQuota";
    public static void AddArcanaProtection(this IServiceCollection services) => services.AddSingleton<RequestBudget>();
    public static void UseArcanaProtection(this WebApplication app)
    {
        var forwarded = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
        { ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
        string proxy = Environment.GetEnvironmentVariable("ARCANA_TRUSTED_PROXY_IP") ?? "";
        if (proxy.Length > 0)
        {
            if (!System.Net.IPAddress.TryParse(proxy, out var address)) throw new ArgumentException("ARCANA_TRUSTED_PROXY_IP debe ser una IP exacta.");
            forwarded.KnownProxies.Add(address);
        }
        app.UseForwardedHeaders(forwarded);
        var budget = app.Services.GetRequiredService<RequestBudget>();
        var concurrent = new SemaphoreSlim(64, 64);
        // Ajustes de capacidad para instalaciones LAN con varias salas desde una misma dirección.
        int roomLimit = Math.Clamp(app.Configuration.GetValue("ARCANA_ROOM_CREATES_PER_MINUTE", 6), 1, 120);
        int connectLimit = Math.Clamp(app.Configuration.GetValue("ARCANA_CONNECTIONS_PER_MINUTE", 30), 1, 240);
        app.Use(async (context, next) =>
        {
            // El enrutador ignora mayúsculas y la barra final. La cuota debe
            // aplicar exactamente la misma normalización para evitar desvíos.
            string path = (context.Request.Path.Value ?? "").TrimEnd('/').ToLowerInvariant();
            if (path == "/health") { await next(context); return; }
            // RemoteIpAddress procede del transporte. No confiar en X-Forwarded-For del cliente.
            string peer = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            bool createRoom = path == "/rooms" && HttpMethods.IsPost(context.Request.Method);
            int maximum = path == "/play" ? connectLimit : createRoom ? roomLimit : path == "/account/login/start" ? 6
                : path == "/analytics/matches" ? 12 : 600;
            if (!budget.Take("http:" + (path == "/play" ? "connect" : createRoom ? "room-create" : path == "/account/login/start" ? "login" : path == "/analytics/matches" ? "telemetry" : "api"), peer, maximum))
            { await Reject(context); return; }
            // Los WebSocket establecidos ya tienen colas, timeout y cuotas propios.
            bool limited = path != "/play";
            if (limited && !await concurrent.WaitAsync(0, context.RequestAborted)) { await Reject(context); return; }
            try { await next(context); }
            catch (HttpBudgetExceededException) { if (!context.Response.HasStarted) await Reject(context); else context.Abort(); }
            finally { if (limited) concurrent.Release(); }
        });
    }
    private static async Task Reject(HttpContext context)
    {
        context.Response.StatusCode = 429; context.Response.Headers.RetryAfter = "60";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new ApiError("Demasiadas solicitudes. Espera un minuto."), Protocol.Json, context.RequestAborted);
    }
    public static async Task<AccountIdentity?> AuthenticateHttp(this AccountService accounts, HttpContext context)
    {
        if (context.Items.TryGetValue(IdentityKey, out var cached)) return cached as AccountIdentity;
        string auth = context.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || auth.Length > 8200) return null;
        var identity = await accounts.AuthenticateGame(auth[7..], context.RequestAborted);
        if (identity == null) return null;
        if (!context.Items.ContainsKey(QuotaKey))
        {
            var budget = context.RequestServices.GetRequiredService<RequestBudget>();
            string scope = HttpMethods.IsGet(context.Request.Method) ? "account:read" : "account:write";
            if (!budget.Take(scope, identity.Uid, HttpMethods.IsGet(context.Request.Method) ? 120 : 60)) throw new HttpBudgetExceededException();
            context.Items[QuotaKey] = true;
        }
        context.Items[IdentityKey] = identity; return identity;
    }
}

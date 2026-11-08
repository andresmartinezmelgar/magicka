namespace Arcana.Server;

public static class PublicDashboardEndpoints
{
    public static IServiceCollection AddPublicDashboard(this IServiceCollection services)
    {
        services.AddSingleton<PublicDashboardService>();
        services.AddHostedService(provider => provider.GetRequiredService<PublicDashboardService>());
        return services;
    }

    public static void MapPublicDashboard(this WebApplication app)
    {
        foreach (string prefix in new[] { "/api/dashboard", "/analytics/dashboard" })
            foreach (string part in new[] { "history", "live" })
            {
                bool live = part == "live";
                app.MapMethods(prefix + "/" + part, ["GET", "OPTIONS"], async (HttpContext context, PublicDashboardService service) =>
                {
                    // This projection is intentionally public; no credentials, arbitrary queries or writes.
                    var response = context.Response;
                    response.Headers["Access-Control-Allow-Origin"] = "*";
                    response.Headers["Access-Control-Expose-Headers"] = "ETag";
                    if (HttpMethods.IsOptions(context.Request.Method))
                    {
                        response.Headers["Access-Control-Allow-Methods"] = "GET";
                        response.Headers["Access-Control-Allow-Headers"] = "If-None-Match";
                        response.Headers["Access-Control-Max-Age"] = "86400";
                        response.StatusCode = 204; return;
                    }
                    var page = service.Page(live);
                    if (page == null)
                    {
                        response.StatusCode = 503; response.Headers.RetryAfter = "15";
                        response.Headers.CacheControl = "no-store";
                        await response.WriteAsJsonAsync(new { error = "Las estadísticas aún no están disponibles en este servidor." }, context.RequestAborted);
                        return;
                    }
                    response.Headers.CacheControl = "public, max-age=10, must-revalidate";
                    response.Headers.ETag = page.ETag; response.Headers.Vary = "Accept-Encoding";
                    if (context.Request.Headers.IfNoneMatch.ToString().Split(',').Any(tag => tag.Trim() == page.ETag || "W/" + tag.Trim() == page.ETag || tag.Trim() == "*"))
                    { response.StatusCode = 304; return; }
                    bool gzip = context.Request.GetTypedHeaders().AcceptEncoding?.Any(e => e.Value.ToString().Equals("gzip", StringComparison.OrdinalIgnoreCase) && (e.Quality ?? 1) > 0) == true;
                    byte[] bytes = gzip ? page.Gzip : page.Json;
                    if (gzip) response.Headers.ContentEncoding = "gzip";
                    response.ContentType = "application/json; charset=utf-8"; response.ContentLength = bytes.Length;
                    await response.Body.WriteAsync(bytes, context.RequestAborted);
                });
            }
    }
}

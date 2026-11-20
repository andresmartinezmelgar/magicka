using Arcana.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

internal static class HttpAbuseProtectionTests
{
    internal static async Task Run()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddArcanaProtection();
        await using var app = builder.Build();
        // Simular un transporte público que no pertenece al proxy de confianza.
        app.Use(async (context, next) => { context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.20"); await next(context); });
        app.UseArcanaProtection(); app.MapPost("/rooms", () => Results.Ok());
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            for (int i = 0; i < 7; i++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, i % 2 == 0 ? "/rooms" : "/ROOMS/");
                request.Headers.Add("X-Forwarded-For", "203.0.113." + (i + 1));
                using var response = await client.SendAsync(request);
                if (response.StatusCode != (i < 6 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests))
                    throw new Exception("La barra final, mayúsculas o XFF permiten evitar la cuota por transporte.");
                if (i == 6 && response.Headers.RetryAfter?.Delta != TimeSpan.FromMinutes(1)) throw new Exception("Falta la espera indicada al cliente.");
            }
            Console.WriteLine("HTTP_ABUSE_PROTECTION_OK: cuota compartida entre variantes de ruta; XFF ajeno no cambia el remitente.");
        }
        finally { await app.StopAsync(); }
    }
}

using Arcana.Core;
using Microsoft.AspNetCore.Http.Features;
using System.Text;
using System.Text.Json;

namespace Arcana.Server;

public static class MatchStatisticsEndpoints
{
    public static void MapMatchStatistics(this WebApplication app)
    {
        // Límite global sin conservar IP ni identidad del remitente.
        object gate = new(); long window = Environment.TickCount64; int count = 0;
        app.MapPost("/analytics/matches", async (HttpContext context, MatchStatisticsService statistics, AccountService accounts) =>
        {
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            lock (gate)
            {
                long now = Environment.TickCount64;
                if (now - window >= 60000) { window = now; count = 0; }
                if (++count > 60) return Results.StatusCode(429);
            }
            var size = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (size is { IsReadOnly: false }) size.MaxRequestBodySize = MatchStatisticsReport.MaxBytes;
            if (context.Request.ContentLength > MatchStatisticsReport.MaxBytes) return Results.StatusCode(413);
            try
            {
                using var stream = new MemoryStream(); byte[] buffer = new byte[8192]; int read;
                while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
                { if (stream.Length + read > MatchStatisticsReport.MaxBytes) return Results.StatusCode(413); stream.Write(buffer, 0, read); }
                var report = MatchStatisticsReport.Parse(Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length));
                var day = DateOnly.ParseExact(report.Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                if (report.Status == MatchRecordStatus.Playing || report.Revision == 0 || report.Network ||
                    day < today.AddDays(-30) || day > today) return Results.BadRequest();
                if (!accounts.GameEnabled || !statistics.Accept(report)) return Results.StatusCode(503);
                await statistics.Persist(context.RequestAborted);
                return Results.StatusCode(202);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or BadHttpRequestException) { return Results.BadRequest(); }
        });
    }
}

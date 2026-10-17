using Arcana.Core;
using System.Net;
using System.Text.Json;

internal static class ReleaseUpdateQueryTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static HttpResponseMessage Response(object metadata) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(metadata)) };
    private static ReleaseUpdateQueryResult Query(HttpClient http, string uid = "", string installed = "0.19.3", CancellationToken token = default) =>
        ReleaseUpdateQuery.CheckAsync(http, uid, installed, 470, true, token).GetAwaiter().GetResult();

    public static void Run(Action<string, Action> test)
    {
        test("Buscar actualización: consulta oficial, sin credenciales ni descarga automática", () =>
        {
            int requests = 0;
            using var http = new HttpClient(new Handler((request, _) =>
            {
                requests++;
                Check(request.RequestUri!.AbsoluteUri == ReleaseUpdateChannel.StableMetadata && request.Method == HttpMethod.Get,
                    "Se consultó un origen incorrecto.");
                Check(request.Headers.Authorization == null && request.Headers.CacheControl?.NoCache == true &&
                    request.Headers.UserAgent.Count > 0, "Se enviaron credenciales o se reutilizó metadata obsoleta.");
                return Task.FromResult(Response(ReleaseUpdateChannelTests.Stable()));
            }));
            var result = Query(http);
            Check(result.Succeeded && result.Update?.Version == "0.20.0" && requests == 1, "No se encontró la actualización o se descargó antes de elegir.");
        });
        test("Buscar actualización: distingue una versión instalada de errores HTTP y JSON", () =>
        {
            using var current = new HttpClient(new Handler((_, _) => Task.FromResult(Response(ReleaseUpdateChannelTests.Stable()))));
            var result = Query(current, installed: "0.20.0");
            Check(result.Succeeded && result.Update == null, "La versión instalada vuelve a ofrecerse.");
            foreach (var response in new[] { new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") }, Response(new { }), Response(new object[0]) })
            {
                using var http = new HttpClient(new Handler((_, _) => Task.FromResult(response)));
                Check(!Query(http).Succeeded, "Un error de consulta se presentó como ausencia de novedades.");
            }
        });
        test("Buscar actualización: conserva el canal autorizado y acepta una versión promovida a main", () =>
        {
            using var http = new HttpClient(new Handler((request, _) =>
            {
                Check(request.RequestUri!.AbsoluteUri == ReleaseUpdateChannel.PreviewMetadata, "Se perdió la selección de canal.");
                return Task.FromResult(Response(new[] { ReleaseUpdateChannelTests.Stable() }));
            }));
            Check(Query(http, "qRUza2DieeTypFqBIRA5KhbtXoY2").Update is { Preview: false, Version: "0.20.0" },
                "La cuenta autorizada no recibe main desde su versión experimental.");
        });
        test("Buscar actualización: acota respuestas y maneja desconexión y cancelación", () =>
        {
            using var large = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(new string('x', 256 * 1024 + 1)) })));
            Check(!Query(large).Succeeded, "Se aceptó metadata sin límite.");
            using var offline = new HttpClient(new Handler((_, _) => throw new HttpRequestException("offline")));
            Check(!Query(offline).Succeeded, "La desconexión se mostró como ausencia de novedades.");
            using var stop = new CancellationTokenSource(); stop.Cancel();
            bool canceled = false;
            try { Query(offline, token: stop.Token); } catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Una consulta cancelada continuó operando.");
        });
    }
}

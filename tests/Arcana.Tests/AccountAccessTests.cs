using Arcana.Core;
using System.Net;

internal static class AccountAccessTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        test("Configuración Firebase: permisos por cuenta y sin identificadores especiales", () =>
        {
            bool enabled = true;
            using var http = new HttpClient(new Handler(request =>
            {
                Check(request.RequestUri!.AbsolutePath.EndsWith("/users/any-google-user/configuration/account") &&
                    request.Headers.Authorization?.Parameter == "FIREBASE_TOKEN", "Se leyó otra cuenta o sin autenticar.");
                return new(HttpStatusCode.OK) { Content = new StringContent(enabled
                    ? "{\"fields\":{\"isAdmin\":{\"booleanValue\":true},\"matchStatistics\":{\"booleanValue\":true},\"adsRemoved\":{\"booleanValue\":true}}}"
                    : "{\"fields\":{\"isAdmin\":{\"booleanValue\":false},\"matchStatistics\":{\"booleanValue\":false},\"adsRemoved\":{\"booleanValue\":false}}}") };
            }));
            using var reader = new FirebaseProfileReader("demo-arcana", http);
            var access = reader.ReadAccess("any-google-user", "FIREBASE_TOKEN").GetAwaiter().GetResult();
            Check(access is { IsAdmin: true, MatchStatistics: true, AdsRemoved: true } && access.CanReceivePreview,
                "No se aplicaron los permisos remotos.");
            enabled = false;
            access = reader.ReadAccess("any-google-user", "FIREBASE_TOKEN").GetAwaiter().GetResult();
            Check(!access.CanReceivePreview && !access.MatchStatistics && !access.AdsRemoved, "La revocación retuvo permisos previos.");
        });
        test("Configuración Firebase: datos ausentes, denegados o con tipos incorrectos no conceden privilegios", () =>
        {
            foreach (var (status, body) in new[] {
                (HttpStatusCode.NotFound, "{}"), (HttpStatusCode.Forbidden, "{}"),
                (HttpStatusCode.OK, "{\"fields\":{\"isAdmin\":{\"stringValue\":\"true\"},\"previewUpdates\":{\"integerValue\":\"1\"},\"matchStatistics\":null,\"adsRemoved\":true}}"),
                (HttpStatusCode.OK, "{\"fields\":{}}") })
            {
                using var http = new HttpClient(new Handler(_ => new(status) { Content = new StringContent(body) }));
                using var reader = new FirebaseProfileReader("demo-arcana", http);
                var access = reader.ReadAccess("ordinary-user", "TOKEN").GetAwaiter().GetResult();
                Check(access == new AccountAccess(), "Los datos inválidos concedieron un permiso.");
            }
        });
    }
}

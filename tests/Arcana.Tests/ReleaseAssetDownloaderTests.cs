using Arcana.Core;
using System.Net;
using System.Security.Cryptography;
using System.Text;

static class ReleaseAssetDownloaderTests
{
    private const string Root = "https://github.com/andresmartinezmelgar/magicka/releases/download/v0.17.20/";
    private const string Name = "ArcanaArena-v0.17.20-Android-arm64.apk";
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("actualizacion completa verificada");
    private static readonly byte[] Hash = SHA256.HashData(Bytes);
    private static readonly string Manifest = Convert.ToHexString(Hash) + "  " + Name + "\n";
    private static ReleaseUpdatePlan Plan => new("0.17.20", Name, Root + Name, Root + "SHA256SUMS-Android.txt", Bytes.Length);

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static HttpResponseMessage Content(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Metadata(string text = "") => new(HttpStatusCode.OK)
    { Content = new StringContent(text.Length == 0 ? Manifest : text) };
    private static void InFolder(Action<string> action)
    {
        string folder = Path.Combine(Path.GetTempPath(), "arcana-download-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { action(folder); }
        finally { Directory.Delete(folder, recursive: true); }
    }
    private static ReleaseAssetDownloader Downloader(HttpClient metadata, HttpClient download) =>
        new(metadata, download) { RetryDelay = TimeSpan.Zero };
    private static void NoPartial(string folder)
    {
        if (Directory.EnumerateFiles(folder, "*.part-*").Any()) throw new Exception("Quedaron temporales de un intento anterior.");
    }

    public static void Run(Action<string, Action> test)
    {
        test("Descarga: primer manifiesto 503 y primer paquete 404 se recuperan sin pulsar Reintentar", () => InFolder(folder =>
        {
            int manifests = 0, packages = 0, retries = 0;
            var requests = new HashSet<HttpRequestMessage>();
            using var metadata = new HttpClient(new Handler((request, _) =>
            {
                if (!requests.Add(request) || request.Headers.CacheControl?.NoCache != true)
                    throw new Exception("Se reutilizó una petición o una redirección almacenada.");
                return Task.FromResult(++manifests == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Metadata());
            }));
            using var download = new HttpClient(new Handler((request, _) =>
            {
                if (!requests.Add(request)) throw new Exception("Se reutilizó una petición de descarga.");
                return Task.FromResult(++packages == 1 ? new HttpResponseMessage(HttpStatusCode.NotFound) : Content(Bytes));
            }));
            var result = Downloader(metadata, download).DownloadAsync(Plan, folder, retrying: _ => retries++).GetAwaiter().GetResult();
            if (manifests != 2 || packages != 2 || retries != 2 || !File.ReadAllBytes(result.Path).SequenceEqual(Bytes))
                throw new Exception("El primer fallo no se recuperó automáticamente.");
            NoPartial(folder);
        }));
        test("Descarga: EOF prematuro elimina el temporal y reinicia con bytes completos", () => InFolder(folder =>
        {
            int requests = 0;
            using var metadata = new HttpClient(new Handler((_, _) => Task.FromResult(Metadata())));
            using var download = new HttpClient(new Handler((_, _) =>
            {
                var response = Content(++requests == 1 ? Bytes[..5] : Bytes);
                response.Content.Headers.ContentLength = Bytes.Length;
                return Task.FromResult(response);
            }));
            var result = Downloader(metadata, download).DownloadAsync(Plan, folder).GetAwaiter().GetResult();
            if (requests != 2 || !File.ReadAllBytes(result.Path).SequenceEqual(Bytes)) throw new Exception("Descarga truncada aceptada.");
            NoPartial(folder);
        }));
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway })
            test($"Descarga: primer HTTP {(int)status} se recupera con una petición nueva", () => InFolder(folder =>
            {
                int requests = 0;
                using var metadata = new HttpClient(new Handler((_, _) => Task.FromResult(Metadata())));
                using var download = new HttpClient(new Handler((_, _) =>
                    Task.FromResult(++requests == 1 ? new HttpResponseMessage(status) : Content(Bytes))));
                var result = Downloader(metadata, download).DownloadAsync(Plan, folder).GetAwaiter().GetResult();
                if (requests != 2 || !File.ReadAllBytes(result.Path).SequenceEqual(Bytes))
                    throw new Exception("La respuesta temporal requiere reintento manual.");
                NoPartial(folder);
            }));
        test("Descarga: un timeout interno se reintenta mientras el usuario no haya cancelado", () => InFolder(folder =>
        {
            int requests = 0;
            using var metadata = new HttpClient(new Handler((_, _) => Task.FromResult(Metadata())));
            using var download = new HttpClient(new Handler((_, _) =>
            {
                if (++requests == 1) throw new TaskCanceledException("Timeout de conexión.", new TimeoutException());
                return Task.FromResult(Content(Bytes));
            }));
            var result = Downloader(metadata, download).DownloadAsync(Plan, folder).GetAwaiter().GetResult();
            if (requests != 2 || !File.ReadAllBytes(result.Path).SequenceEqual(Bytes)) throw new Exception("No se recuperó el timeout.");
        }));
        test("Descarga: reutiliza un archivo ya verificado sin repetir la transferencia", () => InFolder(folder =>
        {
            File.WriteAllBytes(Path.Combine(folder, Name), Bytes);
            using var metadata = new HttpClient(new Handler((_, _) => Task.FromResult(Metadata())));
            using var download = new HttpClient(new Handler((_, _) => throw new Exception("No debe descargarse otra vez.")));
            var result = Downloader(metadata, download).DownloadAsync(Plan, folder).GetAwaiter().GetResult();
            if (!File.ReadAllBytes(result.Path).SequenceEqual(Bytes)) throw new Exception("Caché verificada rechazada.");
        }));
        test("Descarga: fallo persistente termina tras tres intentos y permite un reintento limpio", () => InFolder(folder =>
        {
            int requests = 0;
            bool offline = true;
            using var metadata = new HttpClient(new Handler((_, _) => Task.FromResult(Metadata())));
            using var download = new HttpClient(new Handler((_, _) =>
            {
                requests++;
                if (offline) throw new HttpRequestException("Sin conexión.");
                return Task.FromResult(Content(Bytes));
            }));
            var downloader = Downloader(metadata, download);
            try { downloader.DownloadAsync(Plan, folder).GetAwaiter().GetResult(); throw new Exception("Red fallida aceptada."); }
            catch (HttpRequestException) { }
            if (requests != ReleaseAssetDownloader.MaximumAttempts || File.Exists(Path.Combine(folder, Name)))
                throw new Exception("Reintentos ilimitados o archivo fallido conservado.");
            offline = false;
            var result = downloader.DownloadAsync(Plan, folder).GetAwaiter().GetResult();
            if (!File.ReadAllBytes(result.Path).SequenceEqual(Bytes)) throw new Exception("El reintento manual quedó bloqueado.");
            NoPartial(folder);
        }));
        test("Descarga: hash incorrecto conserva el destino anterior y nunca se instala", () => InFolder(folder =>
        {
            string target = Path.Combine(folder, Name);
            File.WriteAllText(target, "destino anterior");
            using var metadata = new HttpClient(new Handler((_, _) => Task.FromResult(Metadata())));
            using var download = new HttpClient(new Handler((_, _) => Task.FromResult(Content(new byte[Bytes.Length]))));
            try { Downloader(metadata, download).DownloadAsync(Plan, folder).GetAwaiter().GetResult(); throw new Exception("Hash alterado aceptado."); }
            catch (InvalidDataException) { }
            if (File.ReadAllText(target) != "destino anterior") throw new Exception("Se sustituyó el destino con datos sin verificar.");
            NoPartial(folder);
        }));
        test("Descarga: cancelar no reintenta ni deja un paquete parcial", () => InFolder(folder =>
        {
            int requests = 0, retries = 0;
            using var stop = new CancellationTokenSource();
            using var metadata = new HttpClient(new Handler((_, _) => Task.FromResult(Metadata())));
            using var download = new HttpClient(new Handler((_, token) =>
            {
                requests++; stop.Cancel(); token.ThrowIfCancellationRequested();
                return Task.FromResult(Content(Bytes));
            }));
            try { Downloader(metadata, download).DownloadAsync(Plan, folder, retrying: _ => retries++, token: stop.Token).GetAwaiter().GetResult(); throw new Exception("Cancelación ignorada."); }
            catch (OperationCanceledException) { }
            if (requests != 1 || retries != 0 || File.Exists(Path.Combine(folder, Name))) throw new Exception("Se reintentó la cancelación.");
            NoPartial(folder);
        }));
        test("Descarga: después de fallar la instalación reutiliza el paquete completo sin descargar el parche", () => InFolder(folder =>
        {
            File.WriteAllBytes(Path.Combine(folder, Name), Bytes);
            string patchName = "ArcanaArena-v0.17.20-Android-arm64-from-v0.17.19.delta";
            var patch = new ReleaseUpdatePlan("0.17.20", patchName, Root + patchName, Plan.ChecksumUrl, 2)
            { FullPackage = Plan, BaseVersion = "0.17.19" };
            string manifest = Manifest + Convert.ToHexString(SHA256.HashData(new byte[2])) + "  " + patchName + "\n";
            using var metadata = new HttpClient(new Handler((_, _) => Task.FromResult(Metadata(manifest))));
            using var download = new HttpClient(new Handler((_, _) => throw new Exception("No debe descargarse otra vez.")));
            var result = Downloader(metadata, download).DownloadAsync(patch, folder).GetAwaiter().GetResult();
            if (result.Plan.Differential || result.Plan != Plan || !File.ReadAllBytes(result.Path).SequenceEqual(Bytes))
                throw new Exception("No se reutilizó la APK completa verificada.");
        }));
        test("Descarga: un error de red no cambia el parche por una descarga completa", () => InFolder(folder =>
        {
            string patchName = "ArcanaArena-v0.17.20-Android-arm64-from-v0.17.19.delta";
            var patch = new ReleaseUpdatePlan("0.17.20", patchName, Root + patchName, Plan.ChecksumUrl, 2)
            { FullPackage = Plan, BaseVersion = "0.17.19" };
            string manifest = Manifest + Convert.ToHexString(SHA256.HashData(new byte[2])) + "  " + patchName + "\n";
            int requests = 0;
            using var metadata = new HttpClient(new Handler((_, _) => Task.FromResult(Metadata(manifest))));
            using var download = new HttpClient(new Handler((request, _) =>
            {
                if (request.RequestUri!.AbsoluteUri != patch.DownloadUrl) throw new Exception("Se pidió el paquete completo por un fallo de red.");
                requests++; throw new HttpRequestException("Sin conexión.");
            }));
            try { Downloader(metadata, download).DownloadAsync(patch, folder).GetAwaiter().GetResult(); throw new Exception("Fallo ignorado."); }
            catch (HttpRequestException) { }
            if (requests != ReleaseAssetDownloader.MaximumAttempts || Directory.EnumerateFiles(folder).Any())
                throw new Exception("Se marcaron como incompatibles los datos por un fallo de red.");
        }));
    }
}

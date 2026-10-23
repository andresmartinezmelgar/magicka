using Arcana.Core;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

static class ReleaseResumeTests
{
    private const string Name = "ArcanaArena-v0.29.11-Android-arm64.apk";
    private const string Root = "https://github.com/andresmartinezmelgar/magicka/releases/download/v0.29.11/";
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("Una actualización que sobrevive al cierre de la aplicación.");
    private static ReleaseUpdatePlan Plan => new("0.29.11", Name, Root + Name, Root + "SHA256SUMS-Android.txt", Bytes.Length);
    private static string Manifest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)) + "  " + Name + "\n";
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }
    private sealed class InterruptedStream(byte[] bytes, CancellationTokenSource stop) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position < Length) return base.ReadAsync(buffer, token);
            stop.Cancel();
            return ValueTask.FromCanceled<int>(stop.Token);
        }
    }
    private static void InFolder(Action<string> action)
    {
        string folder = Path.Combine(Path.GetTempPath(), "arcana-resume-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { action(folder); }
        finally { Directory.Delete(folder, recursive: true); }
    }
    private static HttpResponseMessage Full(byte[]? bytes = null) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes ?? Bytes) };
    private static HttpResponseMessage Range(int from, int? to = null)
    {
        int end = to ?? Bytes.Length - 1;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Bytes[from..(end + 1)]) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, end, Bytes.Length);
        response.Headers.ETag = new EntityTagHeaderValue("\"release-a\"");
        return response;
    }
    private static VerifiedReleaseAsset Download(string folder, Func<HttpRequestMessage, HttpResponseMessage> send,
        ReleaseUpdatePlan? plan = null, byte[]? bytes = null, Action<long, long, double>? progress = null)
    {
        using var metadata = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Manifest(bytes ?? Bytes)) }));
        using var packages = new HttpClient(new Handler(send));
        return new ReleaseAssetDownloader(metadata, packages) { RetryDelay = TimeSpan.Zero }
            .DownloadAsync(plan ?? Plan, folder, progress).GetAwaiter().GetResult();
    }
    private static void SavePartial(string folder, int size = 8, ReleaseUpdatePlan? plan = null)
    {
        using var stop = new CancellationTokenSource();
        using var metadata = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Manifest(Bytes)) }));
        using var packages = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(Bytes[..size], stop)) };
            response.Content.Headers.ContentLength = Bytes.Length;
            response.Headers.ETag = new EntityTagHeaderValue("\"release-a\"");
            return response;
        }));
        try
        {
            new ReleaseAssetDownloader(metadata, packages).DownloadAsync(plan ?? Plan, folder, token: stop.Token).GetAwaiter().GetResult();
            throw new Exception("La interrupción no detuvo la descarga.");
        }
        catch (OperationCanceledException) { }
        if (ReleaseUpdateCache.SavedBytes(plan ?? Plan, folder) != size ||
            !File.ReadAllBytes(Path.Combine(folder, Name + ".part")).SequenceEqual(Bytes[..size]))
            throw new Exception("El cierre perdió los bytes descargados.");
    }
    private static void CheckComplete(VerifiedReleaseAsset asset, string folder, byte[]? bytes = null)
    {
        if (!File.ReadAllBytes(asset.Path).SequenceEqual(bytes ?? Bytes) || File.Exists(Path.Combine(folder, Name + ".part")))
            throw new Exception("La reanudación no produjo exactamente el paquete verificado.");
    }

    public static void Run(Action<string, Action> test)
    {
        test("Reanudación: un cliente nuevo continúa los bytes guardados con Range e If-Range", () => InFolder(folder =>
        {
            SavePartial(folder);
            ReleaseUpdateCache.CleanInstalled(folder, "0.29.10");
            long initialProgress = -1;
            var result = Download(folder, request =>
            {
                if (request.Headers.Range?.Ranges.Single().From != 8 || request.Headers.IfRange?.EntityTag?.Tag != "\"release-a\"")
                    throw new Exception("No se pidió únicamente el tramo que faltaba.");
                return Range(8);
            }, progress: (current, _, _) => { if (initialProgress < 0) initialProgress = current; });
            if (initialProgress != 8) throw new Exception("El progreso volvió a cero al reabrir.");
            CheckComplete(result, folder);
        }));
        test("Reanudación: si el servidor ignora Range, 200 sustituye el parcial sin concatenarlo", () => InFolder(folder =>
        {
            SavePartial(folder);
            CheckComplete(Download(folder, request =>
            {
                if (request.Headers.Range == null) throw new Exception("No se intentó reanudar.");
                return Full();
            }), folder);
        }));
        test("Reanudación: rechaza Content-Range incorrecto y conserva el destino anterior", () => InFolder(folder =>
        {
            SavePartial(folder);
            string target = Path.Combine(folder, Name);
            File.WriteAllText(target, "instalador anterior");
            try { Download(folder, _ => Range(9)); throw new Exception("Se aceptó un hueco entre tramos."); }
            catch (InvalidDataException) { }
            if (File.ReadAllText(target) != "instalador anterior" || ReleaseUpdateCache.SavedBytes(Plan, folder) != 0)
                throw new Exception("Se publicó un paquete sin verificar.");
        }));
        test("Reanudación: un ETag cambiado reinicia la transferencia completa", () => InFolder(folder =>
        {
            SavePartial(folder);
            int calls = 0;
            CheckComplete(Download(folder, request =>
            {
                if (++calls == 1) { var response = Range(8); response.Headers.ETag = new("\"release-b\""); return response; }
                if (request.Headers.Range != null) throw new Exception("Se reutilizó un ETag obsoleto.");
                return Full();
            }), folder);
            if (calls != 2) throw new Exception("No se recuperó el cambio de representación.");
        }));
        test("Reanudación: un SHA-256 nuevo invalida el parcial aunque conserve el mismo tamaño", () => InFolder(folder =>
        {
            SavePartial(folder);
            var changed = (byte[])Bytes.Clone(); changed[0] ^= 1;
            CheckComplete(Download(folder, request =>
            {
                if (request.Headers.Range != null) throw new Exception("Se mezclaron publicaciones distintas.");
                return Full(changed);
            }, bytes: changed), folder, changed);
        }));
        test("Reanudación: no mezcla dos compilaciones de pruebas de la misma versión", () => InFolder(folder =>
        {
            SavePartial(folder, plan: Plan with { BuildNumber = 100 });
            var next = Plan with { BuildNumber = 101 };
            if (ReleaseUpdateCache.SavedBytes(next, folder) != 0) throw new Exception("Se ofreció el parcial de otra compilación.");
            CheckComplete(Download(folder, request =>
            {
                if (request.Headers.Range != null) throw new Exception("Se reutilizaron bytes de otra compilación.");
                return Full();
            }, plan: next), folder);
        }));
        test("Reanudación: un prefijo local corrupto se recupera mediante descarga completa", () => InFolder(folder =>
        {
            SavePartial(folder);
            File.WriteAllBytes(Path.Combine(folder, Name + ".part"), new byte[8]);
            int calls = 0;
            CheckComplete(Download(folder, request =>
            {
                if (++calls == 1) return Range(8);
                if (request.Headers.Range != null) throw new Exception("Se conservó el prefijo corrupto.");
                return Full();
            }), folder);
            if (calls != 2) throw new Exception("No se repitió la verificación desde cero.");
        }));
        test("Reanudación: recupera un parcial completo tras cerrar antes de promover el archivo", () => InFolder(folder =>
        {
            SavePartial(folder);
            File.WriteAllBytes(Path.Combine(folder, Name + ".part"), Bytes);
            CheckComplete(Download(folder, _ => throw new Exception("No debe repetir una descarga completa.")), folder);
        }));
        test("Reanudación: HTTP 416 descarta el tramo rechazado y reintenta desde cero", () => InFolder(folder =>
        {
            SavePartial(folder);
            int calls = 0;
            CheckComplete(Download(folder, request =>
            {
                if (++calls == 1) return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
                if (request.Headers.Range != null) throw new Exception("Se repitió el rango rechazado.");
                return Full();
            }), folder);
        }));
        test("Reanudación: admite varios 206 consecutivos sin perder el prefijo", () => InFolder(folder =>
        {
            SavePartial(folder);
            int calls = 0;
            CheckComplete(Download(folder, request =>
            {
                long start = request.Headers.Range!.Ranges.Single().From!.Value;
                if (++calls == 1 && start == 8) return Range(8, 15);
                if (calls == 2 && start == 16) return Range(16);
                throw new Exception("Los rangos no son contiguos.");
            }), folder);
        }));
        test("Limpieza: retira paquetes instalados y temporales antiguos, conserva la descarga futura", () => InFolder(folder =>
        {
            SavePartial(folder);
            string old = "ArcanaArena-v0.29.10-Android-arm64.apk";
            foreach (string name in new[] { old, old + ".part", old + ".download.json", old + ".download.json.tmp",
                "ArcanaArena-v0.29.10-Android-arm64-from-v0.29.9.delta", "skip-delta-0.29.10.txt",
                Name + ".part-" + new string('a', 32) }) File.WriteAllText(Path.Combine(folder, name), "antiguo");
            File.WriteAllText(Path.Combine(folder, "ajustes.json"), "conservar");
            string backup = Path.Combine(folder, "install-recovery"); Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "backup.apk"), "conservar");
            ReleaseUpdateCache.CleanInstalled(folder, "0.29.10");
            if (ReleaseUpdateCache.SavedBytes(Plan, folder) != 8 || File.Exists(Path.Combine(folder, old)) ||
                Directory.EnumerateFiles(folder).Any(p => Path.GetFileName(p).Contains("0.29.10") || p.Contains(".part-")) ||
                !File.Exists(Path.Combine(folder, "ajustes.json")) || !File.Exists(Path.Combine(backup, "backup.apk")))
                throw new Exception("La limpieza perdió la descarga pendiente o dejó paquetes obsoletos.");
            ReleaseUpdateCache.CleanInstalled(folder, "0.29.11");
            if (Directory.EnumerateFiles(folder).Any(p => Path.GetFileName(p).StartsWith("ArcanaArena-")))
                throw new Exception("La instalación nueva conservó sus archivos de actualización.");
        }));
        test("Limpieza: mantiene una prueba futura y la elimina una vez instalada esa compilación", () => InFolder(folder =>
        {
            var preview = Plan with { BuildNumber = 100 };
            SavePartial(folder, plan: preview);
            ReleaseUpdateCache.CleanInstalled(folder, Plan.Version, 99);
            if (ReleaseUpdateCache.SavedBytes(preview, folder) != 8) throw new Exception("Se eliminó una prueba aún pendiente.");
            ReleaseUpdateCache.CleanInstalled(folder, Plan.Version, 100);
            if (Directory.EnumerateFiles(folder).Any()) throw new Exception("La prueba instalada conservó sus temporales.");
        }));
        test("Limpieza: seleccionar un destino retira descargas abandonadas de otras versiones", () => InFolder(folder =>
        {
            SavePartial(folder);
            string obsolete = Path.Combine(folder, "ArcanaArena-v0.29.12-Android-arm64.apk");
            File.WriteAllText(obsolete, "descarga descartada");
            ReleaseUpdateCache.KeepPlan(folder, Plan);
            if (File.Exists(obsolete) || ReleaseUpdateCache.SavedBytes(Plan, folder) != 8)
                throw new Exception("No se limitó la caché al destino seleccionado.");
        }));
    }
}

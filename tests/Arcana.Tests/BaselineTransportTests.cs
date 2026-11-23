using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineTransportTests
{
    public static async Task Run(Func<string, Func<Task>, Task> AsyncTest)
    {
        await DiagnosticRedactionTests.Run(AsyncTest);
        await PerformanceRegressionTests.RunTransport(AsyncTest);
        async Task ReplyOnce(object value, Func<string, Task> request)
        {
            var portPicker = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            portPicker.Start(); int port = ((System.Net.IPEndPoint)portPicker.LocalEndpoint).Port; portPicker.Stop();
            using var listener = new System.Net.HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            async Task Reply()
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Protocol.Serialize(value));
                context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, timeout.Token); context.Response.Close();
            }
            var response = Reply();
            await request($"127.0.0.1:{port}"); await response;
        }
        await AsyncTest("HTTP: crear salas valida todos sus campos y conserva una respuesta válida", async () =>
        {
            var valid = new RoomInfo("audit", "Sala", GameMode.Arena, 0, 8, 0, true, 15, 75, false);
            await ReplyOnce(valid, async address => Check(await RoomBrowser.CreateAsync(address, new()) == valid, "Se rechazó una sala válida."));
            foreach (var invalid in new[] { valid with { Name = null! }, valid with { Map = (ArenaMapId)99 },
                valid with { Mode = (GameMode)99 }, valid with { MaxPlayers = 17 }, valid with { Players = 9 },
                valid with { Bots = 1 }, valid with { ScoreLimit = 0 }, valid with { Tickets = 0 }, valid with { Mode = GameMode.Duel } })
                await ReplyOnce(invalid, async address =>
                {
                    bool rejected = false; try { await RoomBrowser.CreateAsync(address, new()); } catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "Crear sala aceptó metadatos que no cumplen sus reglas.");
                });
        });
        await AsyncTest("HTTP: el directorio valida el servidor, las reglas y los identificadores únicos", async () =>
        {
            var serverInfo = new ServerInfo("Servidor", 27015, 27016, GameMode.Warfare, 0, 8, Protocol.Version, false);
            var room = new RoomInfo("audit", "Sala", GameMode.Warfare, 0, 8, 0, true, 15, 75, false);
            var valid = new RoomDirectory(serverInfo, [room]);
            await ReplyOnce(valid, async address => Check((await RoomBrowser.ListAsync(address)).Rooms.Single() == room, "Se rechazó un directorio válido."));
            foreach (var invalid in new[] { valid with { Server = serverInfo with { Name = null! } },
                valid with { Server = serverInfo with { Players = 9 } }, valid with { Server = serverInfo with { DiscoveryPort = 0 } },
                valid with { Rooms = [room with { ScoreLimit = 201 }] }, valid with { Rooms = [room with { Tickets = 1001 }] },
                valid with { Rooms = [room, room] }, valid with { Rooms = [room with { Mode = GameMode.Duel }] } })
                await ReplyOnce(invalid, async address =>
                {
                    bool rejected = false; try { await RoomBrowser.ListAsync(address); } catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "El directorio aceptó metadatos incompletos o repetidos.");
                });
        });
        await AsyncTest("Respuestas HTTP con y sin Content-Length tienen un límite real", async () =>
        {
            using var sized = new StringContent(new string('a', 4097));
            using var streamed = new StreamContent(new UnknownLengthStream(new byte[4097]));
            Check(streamed.Headers.ContentLength == null, "La prueba requiere una respuesta sin Content-Length.");
            foreach (var content in new System.Net.Http.HttpContent[] { sized, streamed })
            {
                bool rejected = false; try { await RoomBrowser.ReadBoundedAsync(content, 4096); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Se leyó una respuesta demasiado grande.");
            }
            using var valid = new StringContent("correcta"); Check(await RoomBrowser.ReadBoundedAsync(valid, 16) == "correcta", "Se truncó una respuesta válida.");
        });
        await AsyncTest("Logs concurrentes: JSON, contraseñas ocultas, exportación y cierre repetido", async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "arcana-logs-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using var log = new DiagnosticLog(Path.Combine(directory, "logs"), "test", queueCapacity: 1024);
                string secret = "clave-privada-\"\\-123"; log.ProtectSecret(secret);
                var context = new Dictionary<string, object?> { ["room"] = "sala-1", ["password"] = secret,
                    ["endpoint"] = new Uri("https://user:clave@juego.example/play?token=abc"), ["note"] = secret };
                await Task.WhenAll(Enumerable.Range(0, 4).Select(n => Task.Run(() =>
                    { for (int i = 0; i < 25; i++) log.Write(DiagnosticLevel.Error, "test.concurrent", "fallo " + secret, new IOException("password=oculta"), context); })));
                await log.FlushAsync();
                string[] lines = Directory.GetFiles(log.DirectoryPath, "*.jsonl").SelectMany(File.ReadAllLines).ToArray();
                Check(lines.Length == 100, "Se perdieron registros sin saturar la cola.");
                foreach (string line in lines)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    Check(doc.RootElement.GetProperty("session").GetString() == log.SessionId && doc.RootElement.GetProperty("context").GetProperty("room").GetString() == "sala-1", "Falta el contexto del fallo.");
                    Check(!line.Contains("clave-privada") && !line.Contains("oculta") && !line.Contains("token=abc") && !line.Contains("user:"), "Credencial presente en el log.");
                }
                File.WriteAllText(Path.Combine(log.DirectoryPath, "launcher.cfg"), secret);
                string report = await log.ExportReportAsync(Path.Combine(directory, "reports"), context);
                string second = await log.ExportReportAsync(Path.Combine(directory, "reports"));
                Check(report != second, "Dos exportaciones colisionaron.");
                using var zip = System.IO.Compression.ZipFile.OpenRead(report);
                Check(zip.GetEntry("report.json") != null && zip.Entries.All(e => e.Name == "report.json" || e.Name.EndsWith(".jsonl")), "El ZIP incluyó archivos ajenos a los logs.");
                await Task.WhenAll(log.DisposeAsync().AsTask(), log.DisposeAsync().AsTask());
            }
            finally { Directory.Delete(directory, true); }
        });
        await AsyncTest("Rotación y retención de logs; un fallo del disco no derriba el juego", async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "arcana-rotation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                await using (var log = new DiagnosticLog(directory, "test", maxFileBytes: 2048, maxFiles: 3))
                {
                    for (int i = 0; i < 30; i++) { log.Write(DiagnosticLevel.Information, "rotation", new string('a', 400)); await log.FlushAsync(); }
                    var files = Directory.GetFiles(directory, "test-*.jsonl");
                    Check(files.Length == 3 && files.All(p => new FileInfo(p).Length <= 2048), "La retención o el tamaño no se respetaron.");
                }
                string blocked = Path.Combine(directory, "no-es-carpeta"); File.WriteAllText(blocked, "archivo");
                await using var broken = new DiagnosticLog(Path.Combine(blocked, "logs"), "broken");
                broken.Write(DiagnosticLevel.Error, "disk", "El juego continúa aunque no se pueda escribir."); await broken.FlushAsync();
                Check(broken.LastError != null, "No se comunicó el fallo del disco.");
            }
            finally { Directory.Delete(directory, true); }
        });
    }
}

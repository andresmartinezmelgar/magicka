using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselinePackageTests
{
    public static async Task Run(string? package, Func<string, Func<Task>, Task> AsyncTest)
    {
        if (package != null) await AsyncTest("Paquete autónomo: alojar, crear, entrar y detener desde LocalServerHost", async () =>
        {
            using var host = new LocalServerHost();
            string? inheritedMap = Environment.GetEnvironmentVariable("ARENA_MAP");
            try
            {
                Environment.SetEnvironmentVariable("ARENA_MAP", "Relics");
                await host.StartAsync(package, Path.Combine(Path.GetTempPath(), "arcana-package-test"), new("Paquete de prueba", 29015, 29016, "paquete-clave"));
            }
            finally { Environment.SetEnvironmentVariable("ARENA_MAP", inheritedMap); }
            Check(host.Running, "No arrancó el servidor incluido.");
            Check((await RoomBrowser.ListAsync(host.Address)).Rooms.Length == 0,
                "Alojar muestra una sala automática antes de que el usuario configure la suya.");
            var room = await RoomBrowser.CreateAsync(host.Address, new() { Name = "Sala desde paquete", Bots = 1, ServerPassword = "paquete-clave" });
            await using var player = new ArenaConnection();
            await player.ConnectAsync(host.Address, "Doble clic", -1, "paquete-clave", roomId: room.Id);
            Check(player.Latest!.Players.Length == 2, "No se pudo jugar en el servidor incluido.");
            var lan = await LanDiscovery.FindAsync(29016);
            Check(lan.Any(s => s.Info.Name == "Paquete de prueba"), "El servidor incluido no se anuncia en LAN.");
            await player.DisposeAsync(); host.Stop(); Check(!host.Running, "El servidor quedó activo tras detenerlo.");
        });

        if (package != null) await AsyncTest("Alojamiento: puerto ocupado, doble inicio, cancelación y logs conservados", async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "arcana-host-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var host = new LocalServerHost(); var options = new LocalServerOptions("Arranque de prueba", 29035, 29036, "host-regression-clave");
                var pending = host.StartAsync(package, directory, options); host.Stop();
                bool canceled = false; try { await pending; } catch (OperationCanceledException) { canceled = true; }
                Check(canceled && !host.Running, "El servidor sobrevivió a cancelar su arranque.");
                await host.StartAsync(package, directory, options);
                bool blocked = false; try { await host.StartAsync(package, directory, options); } catch (InvalidOperationException) { blocked = true; }
                Check(blocked && host.Running, "El doble inicio reemplazó al servidor activo.");
                using (var competitor = new LocalServerHost())
                {
                    blocked = false;
                    try { await competitor.StartAsync(package, directory, options); } catch (InvalidOperationException) { blocked = true; }
                    Check(blocked && host.Running && !competitor.Running, "Se admitió un puerto ocupado o se detuvo el otro servidor.");
                }
                var first = await RoomBrowser.ListAsync(host.Address); Check(!string.IsNullOrWhiteSpace(first.Server.InstanceId), "El servidor carece de identidad de arranque.");
                host.Stop();
                var oldLogs = Directory.GetFiles(Path.Combine(directory, "logs"), "server-*.jsonl");
                Check(oldLogs.Length > 0, "El servidor incluido no escribió logs persistentes.");
                await host.StartAsync(package, directory, options);
                var second = await RoomBrowser.ListAsync(host.Address); Check(first.Server.InstanceId != second.Server.InstanceId, "Se reutilizó la identidad del proceso.");
                host.Stop();
                Check(oldLogs.All(File.Exists), "Reiniciar borró el diagnóstico anterior.");
                foreach (string file in Directory.GetFiles(Path.Combine(directory, "logs"), "server-*.jsonl"))
                    Check(!File.ReadAllText(file).Contains(options.Password), "El log del servidor expuso su contraseña.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
}

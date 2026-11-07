using Arcana.Core;
using System.Text.Json;

internal static class DashboardTelemetryTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static (GameWorld World, Wizard Player) Arena()
    {
        var world = new GameWorld(new() { Mode = GameMode.Arena });
        var player = world.AddPlayer("Dashboard_Name", 0)!;
        world.Step();
        return (world, player);
    }
    public static void Run(Action<string, Action> test)
    {
        test("Dashboard: contrato web numérico para clases y anónimo anterior intacto", () =>
        {
            var (world, player) = Arena();
            var statistics = world.MatchStatistics();
            var live = DashboardRunReport.Capture(statistics, world.Snapshot(), player.Id, new(3, 120, 20, 60));
            using var json = JsonDocument.Parse(live.ToJson());
            var root = json.RootElement;
            var actor = root.GetProperty("players")[0];
            Check(actor.GetProperty("wizardType").ValueKind == JsonValueKind.Number &&
                actor.GetProperty("wizardType").GetInt32() == (int)player.WizardType &&
                actor.GetProperty("name").GetString() == player.Name, "Clase/nombre incompatibles con el dashboard web.");
            Check(root.GetProperty("mode").GetString() == "arena" && root.GetProperty("status").GetString() == "playing" &&
                root.GetProperty("ping").GetProperty("sumMs").GetDouble() == 120, "Enums o ping incompatibles con JS.");
            Check(!statistics.ToJson().Contains(player.Name) && !statistics.ToJson().Contains("players"), "El histórico anónimo ha cambiado.");
            MatchStatisticsReport.Parse(statistics.ToJson());
        });
        test("Dashboard: no mezcla el estado de una ronda con el informe de otra", () =>
        {
            var (world, player) = Arena(); var old = world.MatchStatistics();
            world.ResetForWaitingRoom(); world.Step();
            Check(world.Snapshot().StatisticsRunId != old.RunId, "El snapshot conserva la ronda antigua.");
            try { DashboardRunReport.Capture(old, world.Snapshot(), player.Id, null); }
            catch (InvalidDataException) { return; }
            throw new Exception("Mezcla métricas antiguas y jugadores nuevos.");
        });
        test("Dashboard: copias de daño independientes y nombres acotados", () =>
        {
            var (world, player) = Arena(); var snapshot = world.Snapshot();
            var health = new double[9]; health[7] = 45;
            snapshot = snapshot with { Players = [snapshot.Players[0] with { Name = new string('x', 80) + "\n", Damage = new(health, new double[9]) }] };
            var live = DashboardRunReport.Capture(world.MatchStatistics(), snapshot, player.Id, null);
            health[7] = 900;
            Check(live.Players[0].Name.Length == 64 && live.Players[0].Health[7] == 45, "Nombre sin límite o referencia mutable compartida.");
            _ = live.ToJson();
        });
        test("Dashboard: ausencia de ping local y rechazo de daños no finitos", () =>
        {
            var (world, player) = Arena(); var live = DashboardRunReport.Capture(world.MatchStatistics(), world.Snapshot(), player.Id, null);
            using var json = JsonDocument.Parse(live.ToJson());
            Check(!live.Network && json.RootElement.GetProperty("ping").ValueKind == JsonValueKind.Null, "La sesión local inventa una muestra de 0 ms.");
            live.Players[0].Health[0] = double.NaN;
            try { _ = live.ToJson(); } catch (InvalidDataException) { return; }
            throw new Exception("Se admitió daño NaN en el envío.");
        });
        test("Dashboard: una sesión invitada no puede enviar al canal privado", () =>
        {
            var (world, player) = Arena(); var live = DashboardRunReport.Capture(world.MatchStatistics(), world.Snapshot(), player.Id, null);
            using var account = new AccountClient();
            try { account.UploadDashboardTelemetry(live).GetAwaiter().GetResult(); }
            catch (UnauthorizedAccessException) { return; }
            throw new Exception("Se permitió enviar sin una sesión directa de Google.");
        });
    }
}

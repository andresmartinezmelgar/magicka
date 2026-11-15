using Arcana.Core;
using System.Text.Json;

internal static class ElementSelectionStatisticsTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Advance(GameWorld world, int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    private static bool Send(GameWorld world, Wizard player, string type, int state) => world.ApplyMessage(player.Id,
        new() { Type = type, Slot = state, Sequence = player.LastSequence + 1 });
    private static (GameWorld World, Wizard Player) Arena()
    {
        var world = new GameWorld(new() { Mode = GameMode.Arena, MaxPlayers = 3 });
        var player = world.AddPlayer("Mago", 0)!; player.Invulnerable = 10000;
        return (world, player);
    }
    private static void Usage(MatchStatisticsReport report, double manual, double automatic, double unknown)
    {
        var usage = ElementSelectionUsage.FromReport(report)!;
        Check(Math.Abs(usage.ManualSeconds - manual) < .002 && Math.Abs(usage.AutomaticSeconds - automatic) < .002 &&
            Math.Abs(usage.UnreportedSeconds - unknown) < .002, "Los tiempos de selección se mezclan o se duplican.");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException) { return; }
        throw new Exception("Se ha admitido un tiempo de selección inválido.");
    }

    public static void Run(Action<string, Action> test)
    {
        test("Selección de elementos: tiempo autoritativo manual, automático y sin modo; excluye bots, menús y muerte", () =>
        {
            var (world, player) = Arena(); var bot = world.AddPlayer("Bot", 0, bot: true)!;
            Advance(world, 60); Usage(world.MatchStatistics(), 0, 0, 1);
            Check(!Send(world, bot, "spell_selection", 1) && !Send(world, player, "spell_selection", 4), "Acepta un bot o un modo inexistente.");
            Check(Send(world, player, "spell_selection", 0), "No registra el modo manual."); Advance(world, 60);
            Check(Send(world, player, "spell_selection", 1), "No registra el modo automático."); Advance(world, 120);
            Check(Send(world, player, "spell_selection", 3), "No bloquea la selección desde la interfaz."); Advance(world, 60);
            Check(Send(world, player, "spell_selection", 1), "No reanuda el modo.");
            player.Health = 0; player.Respawn = 100; Advance(world, 60);
            player.Health = player.MaxHealth; Advance(world, 60);
            var report = MatchStatisticsReport.Parse(world.MatchStatistics(MatchRecordStatus.Left).ToJson());
            Usage(report, 1, 3, 1);
            var participant = report.Participants!.Single(p => !p.Bot).ElementSelection!;
            Check(participant == new ElementSelectionUsage(1, 3, 1) && report.Participants!.Single(p => p.Bot).ElementSelection == null,
                "El cierre no conserva el tiempo individual o lo atribuye a un bot.");
            var live = DashboardRunReport.Capture(report, world.Snapshot(), player.Id, null);
            using var json = JsonDocument.Parse(live.ToJson());
            Check(Math.Abs(json.RootElement.GetProperty("elementSelection").GetProperty("automaticSeconds").GetDouble() - 3) < .002,
                "El Dashboard pierde el tiempo agregado.");
        });
        test("Selección de elementos: no cuenta la asistencia cooperativa ni la pausa de la tienda", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
                SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 3, Seed = 37 });
            var player = world.AddPlayer("A")!; var ally = world.AddPlayer("B")!;
            player.Invulnerable = ally.Invulnerable = 10000;
            Check(Send(world, ally, "spell_selection", 2) && Send(world, player, "spell_selection", 0), "No prepara los modos.");
            Advance(world, 60);
            Check(Send(world, player, "defense_shop", 1) && !world.DefensePaused, "La tienda cooperativa pausa la partida.");
            Advance(world, 60); Usage(world.MatchStatistics(), 1, 0, 0);
            Check(Send(world, player, "spell_selection", 1) && Send(world, player, "defense_shop", 0), "No cierra la tienda.");
            Advance(world, 60); Usage(world.MatchStatistics(), 1, 1, 0);
            world.RemovePlayer(ally.Id);
            Check(Send(world, player, "defense_shop", 1) && world.DefensePaused, "No pausa sin otros humanos.");
            long tick = world.Tick;
            Check(Send(world, player, "spell_selection", 2), "La pausa impide sincronizar el modo.");
            Advance(world, 60); Check(world.Tick == tick, "La pausa avanza el reloj."); Usage(world.MatchStatistics(), 1, 1, 0);
        });
        test("Selección de elementos: reconectar conserva el participante y continuar un guardado abre otro registro", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
                SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 3, Seed = 37 });
            var player = world.AddPlayer("A")!; var ally = world.AddPlayer("B")!;
            player.Invulnerable = ally.Invulnerable = 10000;
            world.AttachDefenseAccount(player.Id, "verified-account");
            Send(world, ally, "spell_selection", 2); Send(world, player, "spell_selection", 0); Advance(world, 60);
            world.RemovePlayer(player.Id);
            var resumed = world.AddPlayer("A")!;
            Check(world.AttachDefenseAccount(resumed.Id, "verified-account"), "No recupera el defensor.");
            Send(world, resumed, "spell_selection", 1); Advance(world, 60);
            var closed = world.MatchStatistics(MatchRecordStatus.Suspended);
            Usage(closed, 1, 1, 0);
            Check(closed.Participants!.Count(p => p.ElementSelection != null) == 1 &&
                closed.Participants!.Single(p => p.ElementSelection != null).ElementSelection == new ElementSelectionUsage(1, 1, 0),
                "La reconexión reinicia el tiempo o duplica al participante.");
            world.RemovePlayer(ally.Id);
            var saved = world.SaveSurvival(resumed.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(saved);
            Check(restored.MatchStatistics().RunId != closed.RunId && ElementSelectionUsage.FromReport(restored.MatchStatistics()) == null,
                "Continuar duplica los tiempos que ya se guardaron en el cierre anterior.");
        });
        test("Selección de elementos: contrato opcional, validado y comando compacto sin duración del cliente", () =>
        {
            var (world, player) = Arena(); Send(world, player, "spell_selection", 1); Advance(world, 60);
            var report = world.MatchStatistics(MatchRecordStatus.Completed);
            var participant = report.Participants![0];
            foreach (var invalid in new[] { new ElementSelectionUsage(-1, 0, 0), new(0, double.NaN, 0), new(0, 2, 0) })
                Reject(() => (report with { Participants = [participant with { ElementSelection = invalid }] }).ToJson());
            Reject(() => (report with { Participants = [participant with { Bot = true }] }).Validate());
            var legacy = report with { Metrics = [], Participants = [participant with { ElementSelection = null }] };
            Check(!legacy.ToJson().Contains("elementSelection") && ElementSelectionUsage.FromReport(MatchStatisticsReport.Parse(legacy.ToJson())) == null,
                "Una partida antigua se clasifica como manual.");
            for (int state = 0; state < 4; state++)
            {
                byte[] message = Protocol.SerializeBinaryAction(new() { Type = "spell_selection", Slot = state, Sequence = 73 });
                var decoded = Protocol.Deserialize<ClientMessage>(message)!;
                Check(decoded.Type == "spell_selection" && decoded.Slot == state && decoded.Sequence == 73 && message.Length < 100,
                    "El comando pierde el estado o envía información redundante.");
            }
            world.ResetForWaitingRoom(); Advance(world, 60); Usage(world.MatchStatistics(), 0, 1, 0);
        });
    }
}

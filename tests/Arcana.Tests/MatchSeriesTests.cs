using Arcana.Core;

static class MatchSeriesTests
{
    public static void Run(Action<string, Action> test)
    {
        foreach (int bestOf in new[] { 5, 7, 9 })
        {
            int format = bestOf;
            test($"PvP: mejor de {format} decide campeon exactamente tras {MatchSeries.WinsNeeded(format)} victorias", () =>
            {
                var wins = new int[2];
                int needed = MatchSeries.WinsNeeded(format);
                for (int i = 0; i < needed - 1; i++)
                {
                    if (MatchSeries.RecordRound(wins, format, 0))
                        throw new Exception("La serie terminó antes de las victorias requeridas.");
                }
                for (int i = 0; i < needed - 1; i++)
                {
                    if (MatchSeries.RecordRound(wins, format, 1))
                        throw new Exception("El empate de rondas terminó la serie.");
                }
                if (!MatchSeries.RecordRound(wins, format, 1) || wins[1] != needed)
                    throw new Exception("La serie no terminó al alcanzar las victorias previstas.");
                if (!MatchSeries.RecordRound(wins, format, 0) || wins[0] != needed - 1)
                    throw new Exception("Se alteró el resultado después de haber terminado la serie.");
            });
        }

        test("PvP: ambos equipos reciben el resultado de la ronda y la cuenta atrás", () =>
        {
            var world = new GameWorld(new WorldOptions { Mode = GameMode.Duel, MaxPlayers = 2, Bots = 0 });
            var blue = world.AddPlayer("Azul", 0)!;
            var red = world.AddPlayer("Rojo", 1)!;
            var round = world.Snapshot() with
            {
                MatchEnded = true, Winner = 0, BestOf = 5,
                SeriesWins = [1, 0], RestartIn = 8
            };
            string a = RoundPresentation.Message(round, blue.Id);
            string b = RoundPresentation.Message(round, red.Id);
            if (!a.Contains("HAS GANADO LA RONDA") || !b.Contains("HAS PERDIDO LA RONDA") ||
                !a.Contains("AZUL 1 — 0 ROJO") || !b.Contains("AZUL 1 — 0 ROJO") ||
                !a.Contains("Siguiente ronda en 8 s") || !b.Contains("Siguiente ronda en 8 s"))
                throw new Exception("Un equipo no recibió el resultado y la cuenta atrás.");
            var second = round with { Winner = 1, SeriesWins = [1, 1], RestartIn = 4 };
            if (!RoundPresentation.Message(second, blue.Id).Contains("HAS PERDIDO LA RONDA") ||
                !RoundPresentation.Message(second, red.Id).Contains("HAS GANADO LA RONDA"))
                throw new Exception("El titular no cambió al ganar el equipo rojo.");
            var final = round with { SeriesWins = [3, 2] };
            if (!RoundPresentation.Message(final, blue.Id).Contains("HAS GANADO LA SERIE") ||
                !RoundPresentation.Message(final, red.Id).Contains("HAS PERDIDO LA SERIE") ||
                !RoundPresentation.Message(final, red.Id).Contains("Preparación de la siguiente serie"))
                throw new Exception("El equipo perdedor no recibió el fin de la serie.");
            if (RoundPresentation.Message(round with { MatchEnded = false }, red.Id) != "")
                throw new Exception("El aviso de fin de ronda no se oculta al reiniciar.");
            var simulatedWire = new ServerMessage { Type = "snapshot", Snapshot = round };
            var serialized = Protocol.SerializeSnapshot(simulatedWire);
            var restored = Protocol.DeserializeFrame(serialized, binary: false)?.Snapshot
                ?? throw new Exception("El estado final no llegó por el protocolo.");
            if (RoundPresentation.Message(restored, blue.Id) != a ||
                RoundPresentation.Message(restored, red.Id) != b)
                throw new Exception("Ambos clientes no reconstruyeron el mismo desenlace del servidor.");
        });
        test("Duelo LAN: dos preferencias azules ocupan equipos opuestos", () =>
        {
            var world = new GameWorld(new WorldOptions { Mode = GameMode.Duel, MaxPlayers = 2, Bots = 0 });
            var host = world.AddPlayer("Anfitrion", 0) ?? throw new Exception("No pudo entrar el anfitrión.");
            var guest = world.AddPlayer("Invitado", 0) ?? throw new Exception("Se rechazó el segundo jugador.");
            if (host.Team != 0 || guest.Team != 1)
                throw new Exception("El segundo jugador quedó en el equipo incorrecto.");
        });
        test("Premios: nunca se entrega resultado incompleto ni con actividad insuficiente", () =>
        {
            RewardMatch Match(float a, float b) => new(Guid.NewGuid().ToString("N"), "servidor", GameMode.Duel,
                ArenaMapId.Classic, DateTimeOffset.UtcNow,
                [new("u-1", 1, 0, a, true), new("u-2", 2, 1, b, false)]);
            if (RewardRules.Eligible(Match(14.9f, 20)).Length != 0)
                throw new Exception("Un duelo con participante inactivo recibió premio.");
            if (RewardRules.Eligible(Match(15, 15)).Length != 2)
                throw new Exception("No se reconoció una serie válida de dos participantes.");
        });
        test("PvP: formatos de series inválidos son rechazados", () =>
        {
            foreach (var invalid in new[] { 0, 2, 4, 6, 8, 11 })
            {
                bool rejected = false;
                try { MatchSeries.WinsNeeded(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
                if (!rejected) throw new Exception("Formato inválido aceptado.");
            }
        });
    }
}

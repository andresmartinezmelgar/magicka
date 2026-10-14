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

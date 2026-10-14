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

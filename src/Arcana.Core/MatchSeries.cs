namespace Arcana.Core;

/// <summary>Marcador de series competitivas. Cada ronda concluida suma exactamente una victoria.</summary>
public static class MatchSeries
{
    public static int WinsNeeded(int bestOf) => bestOf switch
    {
        1 => 1, 5 => 3, 7 => 4, 9 => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(bestOf))
    };

    public static bool RecordRound(int[] wins, int bestOf, int winningTeam)
    {
        if (wins is not { Length: 2 } || winningTeam is not (0 or 1))
            throw new ArgumentException("Resultado de equipos inválido.");
        int needed = WinsNeeded(bestOf);
        if (wins[0] >= needed || wins[1] >= needed) return true;
        wins[winningTeam]++;
        return wins[winningTeam] >= needed;
    }
}

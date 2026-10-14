namespace Arcana.Core;

/// <summary>
/// Mensaje de fin de ronda calculado exclusivamente del estado autoritativo.
/// Nunca depende de qué equipo aloja la sala ni de quién vio primero el resultado.
/// </summary>
public static class RoundPresentation
{
    public static string Message(WorldSnapshot state, int localPlayerId)
    {
        if (!state.MatchEnded) return "";
        var local = state.Players.FirstOrDefault(p => p.Id == localPlayerId);
        int winningTeam = state.Mode == GameMode.Arena
            ? state.Players.FirstOrDefault(p => p.Id == state.Winner)?.Team ?? -1
            : state.Winner;
        bool victory = state.Mode == GameMode.Arena ? state.Winner == localPlayerId :
            local != null && local.Team == winningTeam;
        bool known = local != null && (state.Mode == GameMode.Arena ? state.Winner > 0 : winningTeam is 0 or 1);
        bool hasSeries = state.BestOf > 1 && state.SeriesWins is { Length: >= 2 };
        int needed = hasSeries ? MatchSeries.WinsNeeded(state.BestOf) : 1;
        bool complete = hasSeries && (state.SeriesWins![0] >= needed || state.SeriesWins[1] >= needed);
        string eventName = complete ? "SERIE" : "RONDA";
        string headline = !known ? "FIN DE LA " + eventName :
            victory ? "HAS GANADO LA " + eventName : "HAS PERDIDO LA " + eventName;
        string winner = state.Mode == GameMode.Arena
            ? state.Players.FirstOrDefault(p => p.Id == state.Winner)?.Name ?? "Mago"
            : winningTeam == 0 ? "Equipo azul" : winningTeam == 1 ? "Equipo rojo" : "Sin ganador";
        string score = hasSeries
            ? $"\nSERIE AL MEJOR DE {state.BestOf}: AZUL {state.SeriesWins![0]} — {state.SeriesWins[1]} ROJO"
            : "";
        string next = complete ? "Preparación de la siguiente serie" :
            hasSeries ? "Siguiente ronda" : "Nueva partida";
        return headline + "\nGANADOR: " + winner + score +
            $"\n{next} en {Math.Ceiling(Math.Max(0, state.RestartIn))} s";
    }

    public static bool IsWinner(WorldSnapshot state, int localPlayerId)
    {
        if (!state.MatchEnded) return false;
        if (state.Mode == GameMode.Arena) return state.Winner == localPlayerId;
        var local = state.Players.FirstOrDefault(p => p.Id == localPlayerId);
        return local != null && state.Winner is 0 or 1 && local.Team == state.Winner;
    }
}

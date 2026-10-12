namespace Arcana.Core;

/// <summary>Reglas de inicio y recompensas para encuentros PvP por equipos.</summary>
public static class BalancedMatchRules
{
    public const int MaxHumansPerTeam = 4;

    public static bool CanStart(int teamOneHumans, int teamTwoHumans,
        int teamOneReady, int teamTwoReady)
        => teamOneHumans >= 1
           && teamOneHumans <= MaxHumansPerTeam
           && teamOneHumans == teamTwoHumans
           && teamOneReady == teamOneHumans
           && teamTwoReady == teamTwoHumans;

    /// <summary>Una IA en cualquiera de los equipos invalida las recompensas.</summary>
    public static bool RewardsEnabled(int teamOneHumans, int teamTwoHumans,
        int teamOneBots, int teamTwoBots)
        => teamOneBots == 0 && teamTwoBots == 0
           && teamOneHumans >= 1
           && teamOneHumans <= MaxHumansPerTeam
           && teamOneHumans == teamTwoHumans;

    public static string RewardNotice(int teamOneHumans, int teamTwoHumans,
        int teamOneBots, int teamTwoBots)
        => RewardsEnabled(teamOneHumans, teamTwoHumans, teamOneBots, teamTwoBots)
            ? "Recompensas activadas: partida equilibrada entre jugadores."
            : "Sin recompensas: se requiere el mismo número de jugadores humanos en ambos equipos y ninguna IA.";
}

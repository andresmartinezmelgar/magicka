namespace Arcana.Core;

/// <summary>Textos compartidos por la ayuda, las salas y el HUD; los límites vienen de la simulación.</summary>
public static class MatchRules
{
    public static string Objective(GameMode mode, int scoreLimit, int tickets) => mode switch
    {
        GameMode.Duel => $"Gana el primer equipo que consigue {scoreLimit} bajas enemigas.",
        GameMode.Arena => $"Gana el primer mago que alcanza {scoreLimit} bajas. Todos son rivales.",
        _ => $"Captura y conserva los puntos. Gana al reducir a 0 los tickets del rival; cada equipo empieza con {tickets}."
    };
    public static string Details(GameMode mode) => mode switch
    {
        GameMode.Duel => "Dos magos, uno por equipo. Cada baja enemiga suma un punto. Reapareces a los 2,5 segundos.",
        GameMode.Arena => "Cada baja enemiga suma uno a tu marcador. Las muertes no restan bajas. Reapareces a los 4 segundos.",
        _ => "Permanece dentro de un círculo para capturarlo. Con ambos equipos dentro, la captura se detiene. Cada segundo, la diferencia de puntos controlados resta esa cantidad de tickets al rival. Cada muerte también cuesta un ticket a tu equipo. Reapareces a los 4 segundos."
    };
}

namespace Arcana.Core;

/// <summary>Textos compartidos por la ayuda, las salas y el HUD; los límites vienen de la simulación.</summary>
public static class MatchRules
{
    public static string Objective(GameMode mode, int scoreLimit, int tickets, ArenaMapId map = ArenaMapId.Classic) => (mode switch
    {
        GameMode.Warfare when map == ArenaMapId.Frontline => "Conquista los cinco puntos en orden para ganar. Tu reaparición avanza con el terreno controlado.",
        GameMode.Duel => $"Gana el primer equipo que consigue {scoreLimit} bajas enemigas.",
        GameMode.Arena => $"Gana el primer mago que alcanza {scoreLimit} bajas. Todos son rivales.",
        GameMode.Warfare when map == ArenaMapId.Citadel => "Mantén los tres puntos sin disputa durante 15 segundos seguidos para ganar.",
        _ => $"Captura y conserva los puntos. Gana al reducir a 0 los tickets del rival; cada equipo empieza con {tickets}."
    }) + (map == ArenaMapId.Ascension
        ? "\nAscensión: +10 de vida por baja; +0,5 s al morir (máx. 10 s). Objetos aleatorios hasta morir."
        : map == ArenaMapId.Relics ? "\nRecoge reliquias: sus mejoras se acumulan y duran hasta morir." : "");
    public static string Details(GameMode mode, ArenaMapId map = ArenaMapId.Classic) => (mode switch
    {
        GameMode.Warfare when map == ArenaMapId.Frontline => "Cada equipo solo puede capturar el primer punto libre o enemigo desde su extremo. No puedes saltarte puntos, tampoco usando los portales. La reaparición se sitúa detrás del último punto conectado a tu base y retrocede si lo pierdes. Gana quien controla los cinco puntos. Las bajas y los tickets no deciden la victoria. Hay tres pares de portales; caminar sobre uno te traslada al otro. De forma aleatoria caen meteoros con aviso previo o aparecen muros de piedra que puedes destruir con hechizos; desaparecen tras 30 segundos.",
        GameMode.Duel => "Dos magos, uno por equipo. Cada baja enemiga suma un punto.",
        GameMode.Arena => "Cada baja enemiga suma uno a tu marcador. Las muertes no restan bajas. Apareces en una posición aleatoria del mapa, con un segundo de invulnerabilidad visible al entrar y al reaparecer.",
        GameMode.Warfare when map == ArenaMapId.Citadel => "Captura los tres círculos. El contador empieza cuando tu equipo controla todos. Si un enemigo disputa o neutraliza cualquiera, vuelve a cero. Hay que mantener el control durante 15 segundos consecutivos. Los tickets y las bajas no deciden la victoria.",
        _ => "Permanece dentro de un círculo para capturarlo. Con ambos equipos dentro, la captura se detiene. Cada segundo, la diferencia de puntos controlados resta esa cantidad de tickets al rival. Cada muerte también cuesta un ticket a tu equipo."
    }) + (map == ArenaMapId.Ascension
        ? " Cada baja de otro mago aumenta tu vida máxima en 10 y recupera 10 de vida si sigues vivo; también cuenta el fuego amigo. El bonus se conserva al morir y al ser reanimado. Una baja póstuma aumenta la vida máxima sin revivirte. "
          + (mode == GameMode.Duel ? "La primera reaparición tarda 3 segundos" : "La primera reaparición tarda 4,5 segundos")
          + "; cada nueva muerte añade 0,5 segundos, hasta 10. Vida y penalización se reinician en la siguiente partida. "
          + "Hay catorce objetos en posiciones aleatorias; al recogerlos, reaparecen tras 12 segundos en una posición nueva. Sus mejoras se acumulan y se pierden al morir. La vida ganada por bajas se conserva."
        : mode == GameMode.Duel ? " Reapareces a los 2,5 segundos." : " Reapareces a los 4 segundos.")
        + (map == ArenaMapId.Relics ? " Camina sobre una reliquia para recogerla. Cada objeto reaparece a los 12 segundos. Se acumulan daño, protección, vida/s, vida máxima, velocidad, energía/s y curación. Todas las mejoras desaparecen al morir, también si te reaniman." : "");
}

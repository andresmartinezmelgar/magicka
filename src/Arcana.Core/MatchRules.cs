namespace Arcana.Core;

/// <summary>Textos compartidos por la ayuda, las salas y el HUD; los límites vienen de la simulación.</summary>
public static class MatchRules
{
    public static string Objective(GameMode mode, int scoreLimit, int tickets, ArenaMapId map = ArenaMapId.Classic) => mode == GameMode.Defense ? "Sobrevive en un mapa nuevo en cada partida. Dos vidas por jugador, enemigos continuos y colosos a intervalos variables." : map == ArenaMapId.Practice
        ? "Práctica libre: estados, barreras y lanzador. Sin puntos de control ni victoria."
        : (mode switch
    {
        GameMode.Warfare when map == ArenaMapId.Frontline => "Conquista los cinco puntos en orden para ganar. Tu reaparición avanza con el terreno controlado.",
        GameMode.Duel => $"Gana el primer equipo que consigue {scoreLimit} bajas enemigas.",
        GameMode.Arena => $"Gana el primer mago que alcanza {scoreLimit} bajas. Todos son rivales.",
        GameMode.Warfare when map == ArenaMapId.Citadel => "Mantén los tres puntos sin disputa durante 15 segundos seguidos para ganar.",
        _ => $"Captura y conserva los puntos. Gana al reducir a 0 los tickets del rival; cada equipo empieza con {tickets}."
    }) + (map == ArenaMapId.Ascension
        ? "\nAscensión: +10 de vida por baja; +0,5 s al morir (máx. 10 s). Objetos aleatorios hasta morir."
        : map == ArenaMapId.Relics ? "\nRecoge reliquias: sus mejoras se acumulan y duran hasta morir." : "");
    public static string Details(GameMode mode, ArenaMapId map = ArenaMapId.Classic) => mode == GameMode.Defense
        ? "Mapa nuevo de salas, pasillos y portales en cada partida. Los enemigos te buscan por todo el mapa y aparecen al menos a 16 m de cada defensor. Algunos aceleran brevemente; los colosos tienen mucha más vida y llegan a intervalos variables.\n" +
          "Dos vidas por jugador. La partida termina cuando todos los humanos las agotan. Oro, equipo, reliquias, auras y habilidades se conservan al morir, hasta empezar otra partida. La tienda pausa si eres el único humano.\n" +
          "Premios permanentes: un objeto cada 5 minutos activos, hasta cuatro, con cuenta verificada y servidor configurado. Las entregas fallidas quedan pendientes; las partidas locales no conceden estos premios."
        : map == ArenaMapId.Practice
        ? "Los objetivos reciben daño y curación reales y reaparecen en su estación al morir. Sus estados duran hasta neutralizarlos con hechizos o restablecer la estación, que también recupera su escudo inicial.\n" +
          "Las estaciones indican sus combinaciones de barreras y minas; se reconstruyen 2 segundos después de destruirlas.\n" +
          "El lanzador apunta a la derecha: rayos, cadenas y conos continuos; rocas y fragmentos cada 2 segundos. Puedes cambiar la combinación, pausarlo y dejar tu vida al 40 % para medir curación. Reapareces en el círculo azul 1 segundo después de morir."
        : (mode switch
    {
        GameMode.Warfare when map == ArenaMapId.Frontline => "Captura el primer punto libre o enemigo desde tu extremo, sin saltarte puntos, tampoco con portales. Reapareces detrás del último punto conectado a tu base; retrocedes si lo pierdes. Gana quien controla los cinco, sin depender de bajas ni tickets.\nTres parejas de portales te trasladan al caminar sobre ellas. Aparecen meteoros con aviso previo y muros aleatorios: se destruyen con hechizos o desaparecen tras 30 segundos.",
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

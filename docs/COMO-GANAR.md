# Cómo ganar

![Ayuda de objetivos dentro del juego](images/help-win.png)

La sala muestra su objetivo antes de entrar. Durante la partida aparece bajo el marcador. **F1 → Cómo ganar** abre la explicación; **TAB** muestra las bajas y muertes.

| Modo | Condición de victoria | Cómo progresar |
|---|---|---|
| Duelo | Primer equipo que alcanza el límite de bajas de la sala. | Hay dos magos, uno por equipo. Cada baja enemiga suma un punto. |
| Todos contra todos | Primer mago que alcanza el límite de bajas. | Todos los demás magos son rivales, aunque compartan el color de la túnica. Cada baja suma uno; morir no resta bajas. |
| Guerra de puntos | Dejar al equipo rival con cero tickets. | Capturar y conservar los tres puntos, y causar bajas enemigas. |

## Guerra de puntos

- Cada equipo comienza con los tickets configurados por la sala; por defecto, **75**.
- Entra en el círculo de un punto y permanece dentro para capturarlo. Una captura desde neutral tarda unos **4,2 segundos**; cambiarlo completamente de bando tarda unos **8,3 segundos**.
- Si ambos equipos están dentro, la captura se detiene. Un punto capturado conserva su dueño al salir; el rival debe ocuparlo para neutralizarlo y capturarlo.
- Cada segundo se compara cuántos puntos controla cada equipo. La diferencia resta tickets al equipo que controla menos: con 2 puntos frente a 1, pierde **1 ticket por segundo**; con 3 frente a 0, pierde **3**.
- Cada muerte resta **1 ticket** al equipo del mago muerto, incluidas las muertes propias o por fuego amigo.

## Límites, reaparición y nueva partida

El creador elige el límite de bajas y los tickets. Por defecto, duelo y arena se juegan a **15 bajas**; el duelo de práctica se juega a **5**. Reapareces a los **2,5 segundos** en duelo y a los **4** en arena y guerra. Tras anunciar al ganador, comienza otra partida a los **9 segundos**, con los marcadores restablecidos y el mismo tipo de mago.

## Combate

Combina hasta tres elementos. Clic derecho lanza; botón central aplica la magia sobre ti; Mayús + derecho lanza en área. `E` + central activa un escudo; `W` + central cura. El escudo absorbe daño antes que la vida. Elige la afinidad en el launcher: [tipos de mago](TIPOS-DE-MAGO.md).

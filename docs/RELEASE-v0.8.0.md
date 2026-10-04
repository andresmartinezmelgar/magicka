# Arcana Arena v0.8.0

Nuevo mapa: **Coliseo de la ascensión**, con suelo en cruz, cuatro pilares simétricos y tres puntos de control. Se puede elegir en **Práctica → Mapa** y en **Alojar → Crear sala → Mapa**.

Solo en este mapa:

- Cada baja de otro mago suma **10 de vida máxima permanente durante la partida** y recupera 10 de vida si el autor sigue vivo. Cuenta también el fuego amigo y los bots. El suicidio no da bonus; una baja póstuma no revive al autor.
- La vida ganada se conserva al morir y al ser reanimado. Todas las curaciones respetan el nuevo máximo.
- Cada muerte añade **0,5 segundos** de reaparición, hasta **10 segundos**. El primer fallecimiento tarda 3 segundos en duelo y 4,5 segundos en todos contra todos y guerra.
- La siguiente partida reinicia el bonus de vida y el aumento de espera.

La guerra usa tickets; duelo y todos contra todos conservan sus límites de bajas. Arena clásica y Ciudadela mantienen sus reglas anteriores.

El HUD muestra vida actual, máxima y bonus acumulado. La ayuda explica las reglas y el contador de reaparición muestra décimas.

Extraer todo **ArcanaArena-v0.8.0-Windows-x64.zip** y abrir **ArcanaArena.exe**. El paquete incluye cliente, servidor y runtimes. Usa **protocolo 6**: actualizar juntos el juego y el servidor.

# Supervivencia — IA de aliados y chorros/conos (prueba 0.25.9)

**Rama:** `feature/supervivencia-ia-hechizos-conos`. Derivada de la rama de escudos 0.25.8, sin modificar `main`.

## IA en Supervivencia/Defensa
Los aliados bots ya no canalizan solo `Fuego + Arcano + Arcano`. `GameWorld.DefenseBots.cs` elige el hechizo según distancia y rota aproximadamente cada 2,1–2,6 s, desincronizado según ID del mago:
- Cerca (≤6 m): cono de fuego, agua, frío; rayos eléctricos, fragmentos helados, rocas imbuidas y rayo arcano.
- Media distancia (>6–9 m): descarga, fragmentos helados, roca, rayo arcano/fuego.
- Lejos (>9 m): rayos arcanos, rocas y fragmentos; **nunca** conos fuera de alcance.
- Recuperación de salud propia y de aliados sigue teniendo prioridad, y la IA no interrumpe las rocas mientras carga una ofensiva.

La selección pasa por `SpellRecipe` y `ElementBuffer`; se respetan sus incompatibilidades. Solo se modifican decisiones de bots, no daño, alcance, colisiones ni reglas.

## Conos y partículas
En `ArenaRenderer.Beams.cs`:
- La envolvente traslúcida del cono ahora es visible incluso con calidad alta y en móvil. Conserva el tamaño geométrico calculado a partir del alcance real.
- Aumenta contraste y bordes, con haces fluidos y reflejos diferentes para fuego, agua y frío.
- La representación `Force` recibe solo un refuerzo suave, independiente del estilo de los chorros.

En `CombatEffects.cs`, partículas repartidas a lo largo de la apertura del cono y formas específicas: chispas/brasas para fuego, gotas para agua y cristales para frío. Se reutiliza el sistema de objetos y límites existentes (320 partículas, emisión ≤64 por fotograma).

## Comparativa visual para decisión
La imagen compartida en la conversación es un **concepto ilustrativo**, no una captura de Godot ni un recorte exacto del sprite de mago existente. Los cambios ejecutables siguen usando los **sprites originales de mago del juego**, sin alterarlos. La lámina es una propuesta para estudiar la intensidad de los tres estilos antes de generar e integrar nuevos atlas de partículas.

## Comprobaciones
`SurvivalTests.cs` prueba que las combinaciones elegidas son válidas, hay diferentes familias de hechizos, aparecen los tres tipos de cono a corta distancia y no se seleccionan chorros fuera de alcance. CI compila y genera una publicación preliminar de Windows/Android si pasan las demás pruebas.

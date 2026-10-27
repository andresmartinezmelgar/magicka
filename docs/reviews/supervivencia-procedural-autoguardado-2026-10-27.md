# Supervivencia: mapa, equipo mítico y continuidad en Android

Revisión inicial, anterior a la autorización para compilar. La preparación de 0.29.29 y su medición están en [publicacion-supervivencia-0.29.29.md](publicacion-supervivencia-0.29.29.md).

Rama: `pruebas/supervivencia-procedural-autoguardado`. Base: `26b5f30fabdda744a83f028504d4a77ba71b96fa` (0.29.28). Cambios preparados para revisión en esta rama, sin incrementar la versión de distribución.

## Cambios

- El inventario ya tenía brillo y motas, pero no filamentos míticos; el personaje de combate no tenía el aura del equipo. Ambos comparten ahora el mismo shader. Una sola pieza mítica activa filamentos del color del equipo; los ojos siguen requiriendo las seis piezas míticas. Morir, desequipar y reaparecer actualizan el efecto. En combate usa una superficie por personaje, sin partículas adicionales ni luces dinámicas.
- Cada partida nueva o reinicio genera nueve salas de tamaños y posiciones variables, conexiones con giros y de dos a cuatro ciclos de escape. El terreno pasa de 192 × 144 a 160 × 120 m, un 30,56 % menos de superficie delimitadora. Hay una plaza central segura y dos parejas de portales. La semilla y la versión del generador se comparten entre simulación, navegación, renderizado, minimapa y guardado. La caché de mapas se limita a 16 entradas.
- Reiniciar descarta también las celdas y rutas del terreno anterior. La búsqueda de navegación deja de depender de las dimensiones antiguas.
- Android conserva la partida y la carga al perder el foco. Los eventos duplicados de foco/pausa se agrupan; recuperar el foco mientras Android sigue en pausa no reanuda la simulación. El tiempo en segundo plano no consume el plazo de carga.
- La supervivencia local se autoguarda al activarse, cada 20 s, al pasar a segundo plano y al salir. El guardado periódico también recoge cambios de equipo durante la pausa. Se reutilizan la escritura atómica y la copia anterior. Continuar conserva un punto recuperable durante toda la carga; terminar la partida invalida principal y respaldo. Las salas online mantienen su autoridad y sus reglas de reconexión del servidor.
- Protocolo 39 y guardado v2. Se leen guardados v1 con el mapa antiguo; los v2 sin descriptor o con un generador desconocido se rechazan. El formato v2 evita que una instalación antigua cargue una partida procedural sobre otro terreno.

## Verificación realizada sin compilar

- Análisis de sintaxis de los 50 archivos C# modificados: sin errores.
- Revisión de los dos shaders, su inclusión compartida y las seis entradas de equipo: referencias correctas. Esto no sustituye la compilación de shaders en Godot.
- Comprobación de los límites numéricos del generador: las salas llegan como máximo a ±77/±55 m y los pasillos a ±68/±48, dentro de ±80/±60. Salas y pasillos tienen bordes enteros; el árbol conecta todas las salas y el ancho mínimo de 8 m permite la navegación.
- Revisión de todos los consumidores de geometría y del guardado/restauración de la semilla.
- `git diff --check`: correcto. Sin cambios en bitmaps, versión de distribución ni flujos de publicación. Los flujos de compilación son manuales; el único disparador por push se limita a `main` y a modificar su archivo de publicación.

## Verificación pendiente

Se añaden nueve pruebas de continuidad: 64 semillas con salas y portales accesibles; reconstrucción tras expulsar la caché; horda y bots después de reiniciar; nuevo terreno por partida; JSON y deltas; recuperación con la misma semilla y azar; compatibilidad v1; respaldo e invalidación; apariencia de una pieza mítica en supervivencia. Están disponibles con `--survival-continuity-only` y en la suite completa. Los escenarios históricos fijan explícitamente el mapa anterior para conservar sus coordenadas y comparaciones originales.

Las comprobaciones visuales existentes se amplían para una/seis piezas míticas, cambio a Diamante, muerte, reaparición y desequipado. El smoke de guardado incluye las dos secuencias de foco/pausa de Android, una carga suspendida durante más de dos minutos, conservación de progreso y mapa, y finalización sin restaurar respaldos antiguos.

**Estas pruebas C#/Godot no se han ejecutado en esta rama.** [AGENTS.md](../../AGENTS.md) indica: «Si una comprobación necesita compilar, dejarla pendiente hasta la indicación del usuario y comunicarlo con claridad». Quedan también pendientes la prueba en Android físico y el cierre del proceso por el sistema.

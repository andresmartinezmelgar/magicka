# Prueba visual: escudos estilo B

Rama de prueba, separada de `main`. Las imágenes generadas previamente son **concept art**, no capturas obtenidas con Godot.

## Cambios ejecutables

- **Escudos personales:** el renderizador crea hasta ocho pequeños cristales flotantes y dos órbitas luminosas según los elementos de la protección. Verde regeneración (`EW`), cian frío (`ER`), violeta arcano (`ES`), y colores específicos para agua, fuego y rayo. Las cargas refuerzan el ornamento.
- **Barreras sobre el terreno:** glifos cristalinos más grandes y escalonados a lo largo de los muros existentes, con cantidad creciente según cargas. El efecto combina con las superficies elementales que ya renderiza `BarrierVisual.Elements.cs`.
- **Anillos de área:** siguen usando seis segmentos del muro, por lo que heredan el nuevo detalle de cada barrera.
- Las reglas de hechizos, daño, bloqueo, duración, hitboxes, servidor y protocolo **no se modifican**.

## Limitación del arte aprobado

Los ocho PNG aprobados están conservados en Biblioteca, `/Arcana Arena/Arte aprobado/Elementos/`, junto con ZIP y atlas; **aún no están copiados a GitHub** ni enlazados con `ElementIcon.cs`. El conector GitHub de esta sesión permite escribir texto/código, pero no recibe directamente los archivos PNG montados en el entorno. Por tanto, las mejoras de la rama son geometría 3D y materiales sobre el renderizador existente, y no una reproducción píxel a píxel de los fotomontajes.

## Validación pendiente

- Compilar/exportar cliente Godot Mono para Windows y Android en un entorno con el SDK instalado.
- Examinar in-game los escudos `E`, `EW`, `ER`, `ES`, la barrera `ED`, la barrera `EDF` y los anillos de área.
- Verificar visibilidad, superposición, framerate en móvil, integridad visual al caducar, y las pruebas `--effects-smoke` y `--mobile-smoke`.
- Valorar si el tamaño de los adornos se debe reducir antes de fusionar con `main`.

No se ha etiquetado una versión ni fusionado esta rama.

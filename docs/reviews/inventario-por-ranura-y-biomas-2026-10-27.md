# Venta por calidad, fichas y biomas de Supervivencia

Cambios posteriores a `preview-v0.29.30-r613` en `pruebas/supervivencia-procedural-autoguardado`. Esta petición modifica el código; no solicita otra compilación ni publicación.

## Causa de los objetos inferiores

`EquipmentStyle.MatchesLowerQuality` exigía la misma ranura **y familia**, por lo que un gorro Mítico de Rayo no filtraba gorros inferiores de Fuego, Vida u otros elementos. Ahora el límite pertenece a la ranura y utiliza `item.Tier < limit.Tier`. Se conserva la calidad igual o superior. La activación sigue comprobando que la pieza de referencia pertenece al jugador. El botín de Supervivencia se filtra antes de entrar en el inventario o equiparse.

Las preferencias y guardados migran de reglas v1 por familia a v2 por ranura. Varias reglas de una ranura se consolidan con el umbral más alto. En la cuenta, la migración espera un inventario actualizado, conserva las exclusiones previas y añade los objetos actuales de todos los elementos; no aplica la ampliación retrospectivamente. Favoritos y equipados siguen protegidos. Los guardados restauran sus reglas v1 y mantienen íntegros sus objetos; las reglas históricas v0 de calidad exacta se desactivan como antes. El protocolo pasa a 41 para evitar mezclar criterios entre clientes y servidores.

## Información útil en las fichas

Las cifras de anillos se mantienen calculadas por las mismas fórmulas de combate. Cambia su descripción: se suprimen las líneas de curación y daño a cero, sin anunciar capacidades ausentes. La tabla individual omite estadísticas a cero o inactivas, y los afijos antiguos que no funcionan en esa ranura no se describen como efectos. Las penalizaciones activas, recargas, duración, alcance y condiciones que afectan a los efectos reales se conservan.

## Ambientes

Diez temas elegidos mediante un hash entero de la semilla v2, sin consumir azar de terreno, horda o botín. La elección y la decoración son reproducibles al recuperar la partida. Los terrenos v0 y v1 conservan santuario; no cambian los temas de mapas competitivos.

Cada tema tiene paleta, material de suelo procedural, bordes y luz propios. Incluye hierba, adoquines, ruinas con musgo, grabados de santuario, dunas, grietas de hielo, vetas volcánicas, facetas de cristal, suelo de marisma y manchas fúngicas. La decoración usa árboles, edificios, columnas, cactus, rocas, cristales, tocones, juncos y hongos. Se crea por sectores de 24 m, fuera de todo el suelo transitable, con límites de piezas y agrupación de mallas. No se crean colisiones nuevas ni luces por objeto. Edificios y árboles usan una escala reducida en Supervivencia.

## Verificación

Se añaden regresiones para el gorro mítico eléctrico frente a todos los elementos y calidades, la migración de reglas con inventario anterior, favoritos, equipados y recuperación de Supervivencia; se actualizan las comprobaciones de fichas sin efectos nulos. Se añaden pruebas de variedad y continuidad de biomas y una simulación Godot que prepara, renderiza, comprueba y captura los diez ambientes en escritorio y móvil. El flujo manual ejecutará estas comprobaciones en la siguiente compilación autorizada.

Esta revisión solo ejecuta comprobaciones estáticas: sintaxis C#, referencias, YAML y scripts del flujo, integridad del diff y metadatos de versión. Quedan pendientes de compilación y ejecución las regresiones C#/Godot y la inspección de las capturas. `AGENTS.md` exige una indicación expresa para compilar o publicar por cada cambio; la petición anterior ya quedó completada con la preliminar r613.

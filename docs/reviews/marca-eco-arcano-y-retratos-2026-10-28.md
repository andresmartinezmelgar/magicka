# Marca arcana, Eco arcano y presentación de enemigos

Rama `pruebas/supervivencia-procedural-autoguardado`. Catálogo 11 y protocolo 44; sin compilación ni publicación.

## Combate

- Se añaden `ArcaneMark` y `ArcaneEcho` al final del enum de afijos. Cada uno tiene rango 1 en Diamante y 2 en Mítico; las copias usan el máximo y el anillo de oro conserva su probabilidad económica. Los objetos anteriores mantienen sus tiradas.
- Marca exige daño arcano efectivo a un enemigo. Al morir, aplica a cada enemigo visible en 3/4 m el daño real a vida y escudos recibido en los últimos 3/5 segundos, de todos los orígenes, incluido el golpe mortal y excluido su exceso. Se guarda el historial anterior a la primera marca. El nuevo daño es de Arcano y respeta las defensas del destinatario; no vuelve a aplicar potencia, daño arcano ni remate del lanzador.
- Una marca por víctima: mayor rango y, en empate, último autor. Sin caducidad mientras viva el objetivo; cambiar equipo conserva la marca. Reaparecer, reiniciar o salir su autor la limpia. Una explosión no crea marcas nuevas, pero puede matar a otros enemigos ya marcados. Se consume cada marca antes de encolar su muerte y se resuelve con una cola acotada, sin recursión.
- El historial agrupa daño por tick: como máximo 301 muestras por combatiente para cinco segundos inclusivos. No asigna memoria por impacto; libera el buffer al morir. Guardado v2 conserva marcas, historial y explosiones pendientes, con validación de tiempos, rangos y autores. Los puntos anteriores leen los campos nuevos vacíos.
- Eco se canaliza desde el mago hacia otro enemigo cercano y accesible, distinto del objetivo físico inicial del haz principal. Mantiene receta y carga al 25/40 %. No hereda penetración, no crea ecos adicionales ni nace en la primera víctima. Mantiene las reflexiones de escudos; se detiene al soltar, morir o congelarse. El uso se cuenta una vez por canalización efectiva.
- Ambos efectos tienen origen estadístico propio: Marca como área arcana y Eco como ataque directo con sus elementos. Se mantiene la atribución personal, incluida una explosión posterior a la muerte del autor.

## Presentación

- El indicador de Marca se replica en snapshots de magos y minions, incluidas las deltas binarias; se muestra con una runa magenta y una expansión del mismo color al explotar.
- Se oculta la etiqueta completa de nombres y clases de los magos invasores, y la etiqueta del coloso, durante Supervivencia. Se conservan vida, escudos, elementos preparados y el elemento vulnerable del prisma. Los nombres siguen disponibles en fichas y estadísticas.
- Las dos gráficas de daño por enemigo incluyen retratos bajo las barras; la amenaza media, las tablas detalladas y la guía de Supervivencia también identifican cada especie con su modelo real. Se reutiliza `BestiaryPortrait`, con renderizado una sola vez. Se reserva espacio para que imagen, nombre y valores no se solapen.

## Validación

Siete regresiones nuevas cubren daño efectivo, ventana por calidad, autoría, inmunidades y muros, explosiones encadenadas, objetivos y proporción del eco, mezcla y carga, interrupciones, escudos, guardados anteriores, explosiones pendientes, datos inválidos y marca en deltas. Se amplían las comprobaciones de catálogo, colores, reutilización de modelos, nombres ocultos, indicadores y retratos en ambas gráficas.

Revisión estática de sintaxis, referencias, contratos y diferencias. La compilación, ejecución de pruebas y revisión visual en Godot quedan pendientes de una solicitud expresa. No se modifica ningún disparador de GitHub Actions ni se genera tag o APK.

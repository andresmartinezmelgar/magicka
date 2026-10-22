# Cómo se ha optimizado la parte gráfica

Las versiones de pruebas 0.29.3 y 0.29.4 reducen la preparación y el envío de trabajo gráfico. La intención es conservar el aspecto de los hechizos y aprovechar que muchos objetos pequeños comparten forma y material. Desde 0.29.7, estas optimizaciones están integradas en main junto a las gráficas con escalas independientes, la continuación de un solo uso y la unificación de modales.

## Por qué unos gráficos sencillos pueden resultar costosos

Una escena con miles de piezas pequeñas puede requerir mucho trabajo aunque cada pieza tenga pocos polígonos. Cada nodo necesita recorrido del árbol, transformaciones y sincronización; cada material o geometría independiente puede requerir otra llamada de dibujo. Las llamadas desde C# al motor también tienen coste. Muchos efectos transparentes solapados añaden trabajo de píxel.

La CPU ejecuta simulación, IA y preparación del render. La GPU dibuja la geometría y los píxeles. RAM y VRAM almacenan datos; no tienen núcleos que se puedan activar para repartir la partida. Tener más memoria no elimina por sí solo miles de envíos pequeños ni acelera la IA. Estas optimizaciones cambian la cantidad y organización del trabajo. No se ha activado un reparto arbitrario del árbol de escena entre hilos.

## Partículas y rayos en lotes

Antes, muchas chispas tenían un `MeshInstance3D` propio. Ahora las partículas que comparten forma y material conservan su estado en C# y se dibujan mediante `MultiMesh`. Un lote comparte geometría y material y recibe un buffer con las posiciones, orientaciones, escalas y datos de sus instancias. Las trayectorias, duración, colores y facetas siguen calculándose; disminuyen los nodos y envíos individuales.

Los tramos sencillos de relámpagos, conos y empujes también comparten un cilindro unitario. Su transformación conserva ambos extremos y el grosor; los radios superior e inferior viajan como datos de instancia. Las superficies opacas y transparentes se mantienen separadas para respetar sus reglas de profundidad. Los enlaces eléctricos y las ondas conservan la representación específica que necesitan.

En la segunda pasada se agrupan los núcleos, filamentos y pulsos estáticos de los canales. El alfa viaja con cada instancia: aumentar la carga del hechizo no crea un material diferente para cada valor de transparencia. Se mantienen los radios, las UV, la emisión y el suavizado de los extremos. Los perfiles con animaciones particulares siguen separados para conservar sus shaders y tiempos.

Se retiraron subdivisiones longitudinales que no aportaban forma a los cilindros rectos o cónicos. Se conserva su contorno y número de caras alrededor del eje. Los límites siguen siendo 320 partículas, 24 ondas, 64 emisiones por fotograma y 512 tramos de canales; no se redujeron para obtener los resultados.

## Muros con menos piezas independientes

Los postes, cristales y runas compatibles de cada muro se fusionan por material, configuración de sombras y padre animado. Se reutilizan sus vértices, normales, UV y colores. No se sustituye la silueta por una geometría más pobre.

La fusión se hace dentro de cada muro: cada uno conserva su visibilidad. Las crestas que se mueven comparten su padre animado; el hielo conserva su material y desvanecimiento. Las piezas que necesitan otro shader, sprite o comportamiento permanecen separadas. Esto reduce envíos sin convertir todo el mapa en una malla que deba dibujarse entera.

## Buffers reutilizables y actualizaciones cuando cambian los datos

Los lotes empiezan reservando hasta 16 instancias y duplican su capacidad solo al necesitar más, hasta el límite original. En los fotogramas normales se reutiliza el mismo almacenamiento y se envía un buffer por lote. Al limpiar la escena se libera el exceso. Un lote con dos tramos reserva 16 matrices en vez de 512; esa relación se refiere a ese buffer, no a toda la memoria del juego.

Las barras de vida y escudo de los monstruos guardan los últimos porcentajes, colores, orientación y visibilidad. Solo escriben propiedades del motor cuando cambia el valor exacto. Los muros hacen lo mismo con transparencia, escala, destellos e integridad; también reutilizan los identificadores de parámetros de shader. Las animaciones que necesitan avanzar siguen actualizándose cada fotograma. Los nodos reutilizados conservan o restablecen el estado correcto.

Las nuevas gráficas de estadísticas también dibujan sus barras, ejes y textos en un único control. Se redibujan al cambiar su tamaño o sus datos; no ejecutan una actualización continua por barra.

## Qué se ha medido

La comparación más reciente utiliza la misma escena de carga, resolución y semilla, con 16 magos, 128 proyectiles, 40 muros, 40 campos y 64 canales. Se hicieron dos parejas de ejecuciones de 0.29.3 y 0.29.4 después de cinco segundos de calentamiento y con veinte segundos de muestras.

| Trabajo gráfico | 0.29.3 | 0.29.4 | Diferencia |
|---|---:|---:|---:|
| Llamadas de dibujo medias | 2738,7 | 1931,7 | −29,5 % |
| Nodos medios | 3860 | 2913 | −24,5 % |
| Pico de primitivas | 435282 | 435282 | Se conserva |
| Pico de partículas / ondas | 320 / 24 | 320 / 24 | Se conserva |

La primera pareja redujo la actualización visual media de 4,03 a 3,52 ms; la segunda, de 7,17 a 3,81 ms. El tiempo total y los picos variaron: el P95 de la primera pareja empeoró de 173,43 a 182,73 ms, mientras que en la segunda mejoró. No sería correcto anunciar una mejora uniforme de FPS a partir de estas pruebas.

Las medidas proceden de Linux con OpenGL por software, sin GPU física ni teléfono. Sirven para comprobar la reducción de trabajo en ese entorno. Los informes originales y todos los tiempos están en [la comparación 0.29.4](benchmarks/visual-0.29.4/README.md); [la primera pasada](benchmarks/visual-0.29.3/README.md) conserva la comparación con 0.29.2.

## Qué falta por medir en el móvil

El perfilador separa creación/simulación del estado (`sceneCpuMs`) de actualización de objetos gráficos (`renderUpdateCpuMs`). Esta última no incluye el dibujo posterior del motor o la GPU. Así se evita atribuir a los gráficos un coste que pertenece al combate o a los bots.

En la pareja diagnóstica de Supervivencia, la actualización visual ocupó 1,19/0,77 ms, frente a 120,34/115,13 ms de simulación/estado. Las partidas terminaron con el mismo número de enemigos, pero ejecutaron distintos pasos y cruces de portal; no son una comparación exacta de FPS. Sí muestran por qué hay que revisar el tiempo de simulación además del renderer en ese ensayo.

Para evaluar el teléfono se puede ejecutar **Práctica → Medir rendimiento gráfico** y **Versus IA → Medir rendimiento de Supervivencia**. El informe incluye versión, dispositivo, backend, resolución, medias y P95, y se extrae mediante Diagnóstico. Comparar ambas escenas en condiciones equivalentes permite decidir si el siguiente trabajo debe centrarse en envíos gráficos, transparencias o simulación. Sin esa medición no se garantiza un mínimo de 30 FPS en el dispositivo.

## Conservación del aspecto

Las pruebas leen los buffers reales para comprobar extremos, radios, colores y alfa, incluyendo distintas transparencias en un mismo lote, crecimiento, límites, limpieza y reutilización. Las 18 recetas de muros conservan sus vértices y se comprueba el desvanecimiento y recuperación de sus materiales. También se revisan capturas de haces, carga y escudos elementales antes y después, junto con daño, congelación, quemadura y reparación de escudos enemigos.

La calidad se contrasta por geometría, materiales, presupuestos y capturas. No se afirma identidad píxel a píxel: el rasterizado, las transparencias y el HUD producen pequeñas diferencias, y cada backend necesita su propia medición.

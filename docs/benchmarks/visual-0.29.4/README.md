# Segunda optimización visual: 0.29.3 → 0.29.4

Referencia: `adb0ca5a04ef551601e906357cd2589706b49920` (0.29.3). La compilación de referencia conserva su código original. Las dos versiones se compilan y ejecutan en directorios de proyecto independientes y se comprueba la versión del JSON, evitando intercambiar DLL dentro de un proyecto Godot.

Entorno: Godot 4.5.2 .NET, runtime 8.0.32, Linux, OpenGL Compatibility y llvmpipe LLVM 20.1.2. Viewport real de 1600 × 720, interfaz móvil, semilla 271, cinco segundos de calentamiento y veinte de muestras por ejecución. `DOTNET_PROCESSOR_COUNT=2` y `LP_NUM_THREADS=2`. Es renderizado por software, sin móvil ni GPU física.

Los archivos `before-1`/`after-1` y `before-2`/`after-2` corresponden a dos comparaciones consecutivas de la implementación final, con la misma escena de 16 magos, 128 proyectiles, 40 muros, 40 campos y 64 canales. Los límites de efectos permanecen en 320 partículas, 24 ondas y 512 tramos. Las pasadas realizadas durante la implementación no se mezclan con estas cifras.

| Trabajo por fotograma | 0.29.3 | 0.29.4 |
|---|---:|---:|
| Llamadas de dibujo, media de las dos pasadas | 2738.7 | 1931.7 |
| Nodos, media de las dos pasadas | 3860.0 | 2913.0 |
| Pico de primitivas en ambas pasadas | 435282 | 435282 |
| Pico de partículas / ondas | 320 / 24 | 320 / 24 |

Las llamadas bajan aproximadamente un 29,5 % y los nodos un 24,5 %. No se recorta la geometría ni el presupuesto de efectos. Son medias de dos ejecuciones; no se combinan sus percentiles. Los tiempos de las dos comparaciones se muestran separados para conservar su variación:

| Pasada | CPU visual media, antes → después | Fotograma medio, antes → después | P95 de fotograma, antes → después |
|---|---:|---:|---:|
| 1 | 4.03 → 3.52 ms | 165.00 → 160.79 ms | 173.43 → 182.73 ms |
| 2 | 7.17 → 3.81 ms | 244.39 → 169.13 ms | 422.64 → 199.21 ms |

Los tiempos no ofrecen una garantía uniforme: el percentil 95 puede empeorar aun con menos llamadas. No se atribuyen los picos a una causa que no se haya medido. La reducción repetible aquí es el trabajo enviado al renderer y la cantidad de nodos, junto con la menor reserva inicial de los buffers.

Se fusionan únicamente piezas de un mismo muro, material, ajuste de sombras y padre animado. Los muros conservan su visibilidad individual, las crestas su animación y el hielo su material. Las pruebas de las 18 recetas verifican que el número de vértices anterior y posterior coincide y que los materiales se desvanecen y recuperan sin reconstruir la malla.

Los núcleos y filamentos de los canales mantienen geometría, UV, radios, colores y suavizado de extremos. Su alfa viaja por instancia: cargar un canal no crea un lote por cada valor de potencia. Todos los lotes empiezan reservando hasta 16 instancias, duplican su capacidad cuando hace falta y conservan el límite original. Se reutilizan sin asignaciones por instancia/fotograma y liberan la reserva sobrante al limpiar la escena. Por ejemplo, un lote con dos tramos reserva 16 matrices en lugar de 512.

`survival-before` y `survival-after` contienen además una ejecución integral con simulación real, cuatro defensores, equipo y portales. La actualización visual ocupa 1,19/0,77 ms, frente a 120,34/115,13 ms de simulación/creación de estado en esta pareja. Terminan con 169 minions, pero han ejecutado 1255/1324 pasos y 11/12 cruces de portal. Aunque tienen la misma semilla, la evolución y los pasos simulados dependen del tiempo de fotograma; no son estados equivalentes. El coste de simulación se conserva separado del de actualizar los objetos gráficos y no se atribuye una ganancia integral de FPS a esa pareja.

La verificación nativa de escritorio y móvil lee los buffers de MultiMesh: comprueba extremos, radios, alfa distinto en un mismo lote, crecimiento 16 → 512, límite global, limpieza, reducción de reserva y reutilización. La versión headless comprueba cantidades, colores, caducidad y reutilización, junto a las 120 combinaciones y diez escenas. El bestiario ejercita daño, curación de vida, quemadura, congelación y escudos completos, dañados, agotados y reparados, incluida la reutilización de enemigos.

Se compararon capturas nativas del mismo fotograma 90, antes y después, de las galerías `beams`, `shield-strength`, `shield-elements` y `beam-charge`. Las dos últimas corresponden a la compilación final y sus medidas y hashes están en `visual-comparison.json`. Se revisaron siluetas, coronas de hielo, crestas, colores, grosores, intensidades y transparencias. El HUD de FPS y pequeñas diferencias de rasterizado/transparencia cambian algunos píxeles; no se afirma identidad píxel a píxel ni equivalencia en todos los dispositivos.

Para reproducir las mediciones, compilar e importar cada proyecto con Godot .NET y ejecutar por separado:

```bash
godot --audio-driver Dummy --path src/Arcana.Client -- --mobile \
  --profile-render --profile-map=Classic --profile-warmup=5 --profile-seconds=20 \
  --profile-environment=local-linux-software-opengl --profile-output=/ruta/stress.json

godot --audio-driver Dummy --path src/Arcana.Client -- --mobile \
  --profile-render --profile-survival --profile-warmup=5 --profile-seconds=20 \
  --profile-environment=local-linux-software-opengl --profile-output=/ruta/survival.json

godot --audio-driver Dummy --path src/Arcana.Client -- --mobile \
  --preview-spells=shield-elements --capture=/ruta/shield-elements.png
```

Las fechas UTC, resoluciones y procedencia completas se conservan en cada JSON. Los contadores que el backend no ofrece siguen en `null`. Las cifras de software no permiten prometer un porcentaje de FPS ni un mínimo de 30 FPS en un móvil físico; para eso hace falta la medición de ese dispositivo desde Práctica o Supervivencia.

# Arcana Arena v0.12.21

## Corrección de tirones

El recálculo de rutas recorría los nodos de oeste a este y comprobaba muchas líneas de colisión antes de encontrar el nodo más próximo al mago o a su destino. Ahora busca primero el más próximo por distancia; si está tapado por una pared, examina los demás por distancia e índice. Conserva los desempates y la comprobación de cada segmento contra suelo, columnas y muros. La cuadrícula estática se prepara al cargar el mundo, antes de ejecutar sus ticks, y se comparte entre las partidas del mismo mapa.

La decoración tenía cientos o miles de mallas pequeñas e independientes. Árboles, columnas, edificios y bordes opacos se agrupan por material, tipo de sombra y zonas de 12 unidades. Se conservan los vértices y sus transformaciones, normales y materiales. Las zonas se descartan fuera de cámara. El suelo con shader, las plantas MultiMesh, las transparencias y los objetos animados siguen separados.

La luz direccional usa una sola pasada ortográfica de sombras, con un alcance de 70 unidades, adecuado a la vista de la partida. Reduce las pasadas de geometría; las sombras pueden tener menor detalle que con cascadas. No se desactivan las sombras del combate.

Al cambiar de Frente de conquista al laboratorio también se limpian las referencias a los portales y respawns anteriores, evitando actualizar nodos ya liberados. Las primitivas sustituidas por las mallas agrupadas y sus arrays se liberan explícitamente para evitar una acumulación de recursos y un fallo de cierre.

## Comparación gráfica

Comparación de la base `fa95e0c` con la corrección de renderizado, en los seis mapas: Godot 4.5.2 .NET, OpenGL Compatibility, 1440×900, llvmpipe por software con dos hilos, escena de decoración sin combate, semilla 271, tres segundos de calentamiento y ocho segundos de muestras por mapa. Se cambia solo el código y se mantiene la configuración de medición.

| Mapa | Llamadas antes | Llamadas después | Reducción |
|---|---:|---:|---:|
| Arena clásica | 2557 | 685 | 73.2 % |
| Ciudadela | 2084 | 551 | 73.6 % |
| Coliseo | 2244 | 572 | 74.5 % |
| Santuario | 2181 | 486 | 77.7 % |
| Frente de conquista | 3175 | 786 | 75.2 % |
| Laboratorio | 1907 | 455 | 76.1 % |

Son contadores del motor en una escena fija de decoración; no representan el porcentaje de FPS que ganará un equipo concreto. El rendimiento de GPU, los efectos del combate, la red y el equipo del jugador requieren sus propias mediciones.

## Reproducir la revisión

Para separar el coste del mapa de los hechizos, arrancar el cliente con:

```sh
godot --path src/Arcana.Client -- --profile-render --profile-map=Frontline --profile-scenery --profile-warmup=3 --profile-seconds=8 --profile-output=artifacts/profiles/frontline-scenery.json
```

Sustituir `godot` por el ejecutable Godot 4.5.2 .NET. Omitir `--profile-scenery` conserva la escena anterior de carga máxima con 16 magos, proyectiles, barreras y efectos. El informe identifica ambas escenas por separado.

Para medir la simulación, sin GPU ni red:

```sh
dotnet run -c Release --project tests/Arcana.Tests -- --profile-maps --profile-output=artifacts/profiles/map-simulation.json
```

Recorre 3600 ticks por mapa, con siete bots y un jugador; en el laboratorio utiliza sus estaciones y lanzador reales. Mide también la copia del snapshot, separa el coste de carga, descarta 120 ticks de calentamiento y registra media, p95, máximo, ticks por encima de 16,67 ms, asignaciones y SHA-256 del estado final. Las semillas están fijadas. La huella permite comprobar que una optimización conserva el resultado de la partida; los tiempos dependen del equipo y la carga del sistema.

## Validación

**142 pruebas de reglas y 20 pruebas de red correctas**, además de las comprobaciones Godot de seis mapas, laboratorio, efectos y sprites. El recorrido de mapas termina con cierre limpio tras liberar los recursos de la decoración sustituida.

La simulación comparada mantiene exactamente el mismo SHA-256 del estado final en los seis mapas tras 3600 ticks. En Frente, la media observada de tick y copia de snapshot pasó de 0,350 a 0,106 ms y las asignaciones de 232 a 94 MB por minuto simulado; son mediciones locales con semillas fijas, no garantías de latencia en otro equipo.

La comprobación Godot de mapas recorre los seis escenarios y vuelve a Ciudadela después de Frente y Laboratorio. Comprueba la reducción de mallas, la conservación del número de vértices agrupados, portales, objetos, puntos y cámara. Una prueba de reglas cubre el anclaje de ruta cuando una pared tapa el nodo más cercano.

La prueba de muerte de Ascensión en red espera la protección y el enfriamiento de la simulación replicada, en lugar de asumir que un retraso del reloj real implica ese mismo avance del servidor.

Los metadatos Windows y Android se sincronizan con `VERSION` mediante los scripts existentes. Esta actualización publica código; los EXE, APK, tags y releases son publicaciones independientes.

# Arcana Arena v0.12.22

## Cambios de rendimiento

La simulación deja de crear filtros, ordenaciones y cierres por cada comprobación de colisión, componente elemental, captura y objetivo. Los canales reutilizan sus listas de trabajo y conservan el orden de resolución y los desempates. Las colisiones descartan candidatos cuyas cajas no se cruzan antes de ejecutar las fórmulas originales. La geometría estática del mapa se recorre mediante sus arrays internos.

Los snapshots se construyen con arrays del tamaño exacto, sin colecciones intermedias. Sus elementos, guardas, enfriamientos, mejoras, eventos y puntuaciones siguen siendo copias independientes: la red puede serializarlos fuera del bloqueo del mundo. Las descripciones y la forma del lanzador del laboratorio solo se calculan al cambiar su receta.

Cliente y servidor serializan y leen directamente UTF-8. La recepción reutiliza buffers por conexión y vacía la longitud entre mensajes; conserva los límites existentes, los mensajes fragmentados, las colas y el cierre coordinado. El contenido JSON y el protocolo 9 se mantienen.

El cliente cambia la longitud de cada rayo mediante escala, evitando reconstruir sus cilindros durante el movimiento. Actualiza radios y parámetros estáticos del shader solo cuando cambian. Los bloques de cada barrera de piedra se agrupan en tres mallas por material, con los mismos vértices, normales y sombras. Se liberan explícitamente las herramientas y mallas temporales de construcción.

Sprites, colores, textos, barras y protecciones conservan los últimos valores aplicados. El cliente evita volver a asignar atlas, materiales, textos y estilos idénticos en cada fotograma. Las animaciones y los contadores que cambian siguen actualizándose.

## Simulación comparada

Base: commit `e058a35e`, v0.12.21. Comparación local de compilaciones Release con .NET 8.0.8: tres rondas alternando la base y la optimización, con `DOTNET_TieredCompilation=0` en ambas para evitar la compilación de fondo durante las muestras. Cada ronda recorre 3600 ticks por mapa e incluye la copia del snapshot; los tiempos descartan los primeros 120 ticks y la carga se mide por separado. Las asignaciones incluyen el recorrido completo. Siete bots y un jugador en los mapas competitivos; estaciones y lanzador reales en el laboratorio. Semillas de simulación 73 y de aparición, objetos y entorno 271.

La tabla muestra las medianas de las tres rondas. MB corresponde a un millón de bytes asignados temporalmente, no a la memoria residente del proceso.

| Mapa | Media antes, ms | Media después, ms | MB asignados antes | MB asignados después | Reducción de asignaciones |
|---|---:|---:|---:|---:|---:|
| Arena clásica | 0,0557 | 0,0341 | 76,96 | 28,72 | 62,7 % |
| Ciudadela | 0,1679 | 0,0712 | 104,29 | 27,88 | 73,3 % |
| Coliseo | 0,1037 | 0,0396 | 114,38 | 31,31 | 72,6 % |
| Santuario | 0,0619 | 0,0256 | 122,32 | 33,71 | 72,4 % |
| Frente de conquista | 0,2001 | 0,0806 | 93,64 | 23,20 | 75,2 % |
| Laboratorio | 0,0607 | 0,0342 | 112,96 | 50,64 | 55,2 % |

El total de los seis recorridos pasa de 624,54 a 195,45 MB: **68,7 % menos asignaciones**. Los SHA-256 de los estados finales coinciden en los seis mapas en todas las rondas, también al repetir con la configuración habitual de compilación escalonada de .NET. Los tiempos dependen de la carga y del equipo; estas muestras separan la simulación de la GPU y la red y no garantizan una mejora concreta de FPS.

## Red y renderizado

El medidor de protocolo usa un snapshot real del laboratorio de 13 906 bytes, 100 iteraciones de calentamiento y 1000 muestras por operación. La ruta de escritura pasa de 47 440 a 19 600 bytes asignados por mensaje; la lectura pasa de 69 237 a 41 389. Son asignaciones de serialización local; el transporte queda excluido. El JSON resultante coincide byte a byte con la ruta anterior.

La escena de carga gráfica máxima conserva 16 magos, 128 proyectiles, 40 barreras, 40 campos, 64 rayos y 160 eventos. Medición con Godot 4.5.2 .NET, OpenGL Compatibility, 1440×900, llvmpipe por software con dos hilos, tres segundos de calentamiento y seis segundos de muestras por mapa:

| Mapa | Llamadas de dibujo antes | Llamadas de dibujo después | Reducción |
|---|---:|---:|---:|
| Arena clásica | 4253,7 | 3413,1 | 19,8 % |
| Frente de conquista | 4336,7 | 3496,7 | 19,4 % |
| Laboratorio | 4031,1 | 3189,1 | 20,9 % |

La agrupación elimina unas 840 llamadas y 420 nodos por fotograma en esta escena. Conserva los vértices de las barreras; la pequeña variación de primitivas entre muestras corresponde a la fase de las partículas. La GPU por software limita esta prueba, por lo que los contadores no equivalen a una promesa de FPS en un dispositivo real.

Las capturas fijas de barreras, rayos y variantes elementales, a 60 FPS y en el mismo fotograma, coinciden píxel a píxel con la base.

## Reproducción y validación

Después de compilar Release:

```sh
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build --project tests/Arcana.Tests -- --profile-maps --profile-output=artifacts/profiles/map-simulation.json
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build --project tests/Arcana.Tests -- --profile-protocol --profile-output=artifacts/profiles/protocol.json
godot --path src/Arcana.Client -- --profile-render --profile-map=Frontline --profile-warmup=3 --profile-seconds=6 --profile-output=artifacts/profiles/frontline-render.json
```

Sustituir `godot` por Godot 4.5.2 .NET. CI guarda los informes JSON de simulación y protocolo como artefactos, además de la medición gráfica existente; los tiempos son informativos y no umbrales rígidos entre máquinas.

Validación local: 146 pruebas de reglas y transporte y 20 pruebas contra el servidor real correctas, además de ocho comprobaciones Godot de arranque, móvil, interfaz, sprites, efectos, combinaciones, mapas y laboratorio. Las regresiones nuevas comparan 20 000 segmentos con las fórmulas de colisión anteriores, verifican la independencia de snapshots y alternan mensajes de red grandes, pequeños y fragmentados, incluidos caracteres UTF-8 partidos entre fragmentos.

La compilación local usa el compilador Roslyn del SDK 8.0.400 y el generador oficial Godot 4.5.2 para núcleo, servidor, pruebas y cliente Release/Debug. Los trabajos existentes de CI están configurados para ejecutar MSBuild y el empaquetado Windows/Android; no se han generado esos paquetes en esta revisión local. Los metadatos de exportación se sincronizan con `VERSION`; publicar ejecutables, tags o releases es una operación independiente de subir este código.

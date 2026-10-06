# Revisión del proyecto v0.12.12

Fecha: 6 de octubre de 2026. Base revisada: `main`, commit `55bc0022064eee8b831d95ec76b4a58252f1af71` (v0.12.11). Cambios aplicados en `main`.

## Ámbito

Simulación y combate de `Arcana.Core`; transporte WebSocket, HTTP y descubrimiento LAN; salas y servicios de `Arcana.Server`; entrada, movimiento, HUD, launcher, sprites, shaders y efectos del cliente; scripts PowerShell/Bash, exportación Windows/Android, Docker, Compose y CI. Se revisaron las pruebas existentes, los metadatos y la documentación vigente. Los binarios no forman parte de la entrega.

## Hallazgos corregidos

| Prioridad | Ubicación | Problema comprobado | Resultado |
|---|---|---|---|
| P1 | `scripts/package-android.sh` | Godot podía registrar un error con salida 0. La importación no se revisaba y la exportación escribía directamente sobre la APK anterior antes de verificar firma y contenido. | Validación del registro en ambas fases. Solo se admite el mensaje exacto de EditorSettings acompañado de su traza. APK temporal en el mismo sistema de archivos, firma/manifest/runtime comprobados y sustitución posterior. Un fallo conserva APK, checksum y presets. |
| P2 | `GameWorld.cs`, `Magick` | Input y magick se procesan antes de `Step`. Traslación calculaba la distancia con el destino nuevo, pero la dirección con `Facing` del tick anterior. | Dirección y orientación calculadas desde el último destino. Conserva la comprobación de obstáculos y la cancelación de la ruta anterior. |
| P2 | `GameWorld.cs`, `Move` | Normalizar siempre el vector convertía una inclinación pequeña del joystick en velocidad completa. | Movimiento proporcional para jugadores, con límite circular para diagonales. Las rutas del ratón y los bots conservan su comportamiento. |
| P2 | `BarrierVisual.cs` | El arco de un muro sin piedra creaba 20 cajas oscuras y 20 de color, cada una con su propio nodo de renderizado. | Los mismos segmentos se agrupan por material en dos mallas. La barrera básica pasa de 50 a 12 mallas; solo su arco pasa de 40 a 2. No cambia su colisión ni sus colores. |
| P3 | `ArenaMaps.cs`, `FindPath` | Buscar el nodo visible más próximo ordenaba todos los nodos dos veces por ruta. | Búsqueda lineal del mínimo visible, conservando el primer nodo en los empates y las comprobaciones de recorrido. Elimina las ordenaciones y sus arrays temporales. |
| P3 | `SpellCatalogue.cs` | F1 decía que reflejar un rayo consume energía. La simulación resta puntos de escudo. | Texto corregido para describir la absorción real. |
| P3 | CI y documentación | El artefacto del servidor mantenía el nombre v0.12.10; la explicación de interpolación y balance estaba desactualizada. | Nombre de artefacto sin versión fija y documentación ajustada al código actual. |

P1: puede perder o aceptar como válida una salida de compilación. P2: comportamiento incorrecto o carga gráfica evitable. P3: eficiencia localizada, claridad y mantenimiento.

## Comprobaciones

- Regresión de Traslación: input y magick consecutivos sin un tick intermedio en los cuatro mapas; parada al llegar y bloqueo por un pilar.
- Regresión del joystick: inclinaciones 0,25 y 0,5, diagonal máxima, parada y ruta del ratón a velocidad normal.
- Comprobaciones Godot de todas las combinaciones de barrera: arco completo, una superficie por material, cantidad de mallas acotada y limpieza. Se mantienen las verificaciones de volumen de piedra, elementos, protección y meteorito.
- `test-android-build-output.sh`: herramientas simuladas con errores de importación/exportación y salida 0, salida distinta de 0, firma fallida y mensaje de EditorSettings sin traza. Comprueba sustitución correcta, salida anterior, checksum, presets, rutas con espacios y limpieza. Ejecutado contra el script anterior, reproduce que se aceptaba un error de importación.
- CI: compilación .NET/Godot, reglas, HTTP, diagnóstico, red real, Docker, paquete Windows autónomo, scripts Windows PowerShell 5.1, capturas OpenGL y APK ejecutada en Android 15. Los artefactos de compilación sirven a estas verificaciones; la publicación de una Release sigue siendo manual.

## Mejoras pendientes

Estado posterior: [v0.12.13](RELEASE-v0.12.13.md) aplica el bloqueo compartido, los límites espaciales y la separación del coordinador. Añade un perfil reproducible y acceso desde móvil/ADB. La medición en un móvil físico permanece pendiente de disponer de ese dispositivo; las mediciones de CI se identifican como render por software y emulador. La tabla conserva los hallazgos originales de esta revisión.

| Prioridad | Ubicación | Hallazgo por inspección y alcance pendiente |
|---|---|---|
| P2 | `auto-compile-apk.ps1` y `package-android.sh` | Ambos modifican temporalmente `export_presets.cfg`. Dos compilaciones simultáneas en la misma carpeta pueden interferir entre arquitecturas o restaurar un preset ajeno. Falta un bloqueo de compilación compartido. No se ha reproducido una carrera en exportaciones reales. |
| P2 | `SnapshotValidation.cs` | Rechaza NaN/Infinity y entidades inválidas, pero varias coordenadas y radios aceptan cualquier valor finito. Falta acotarlos por geometría/tamaño del mapa antes de crear mallas; la simulación normal mantiene esos límites. |
| P3 | `Main.cs`, `Main.*Checks.cs`, `VisualFixture.cs` | El coordinador del juego incluye muchas escenas y verificaciones de CI. Extraer un coordinador de pruebas separado reduciría el tamaño y las responsabilidades del cliente principal. Requiere preservar las comprobaciones nativas y sus accesos al estado. |
| P3 | `ArenaRenderer`, `CombatEffects`, métricas | La reducción de nodos está comprobada estructuralmente. Falta medir CPU/GPU, draw calls y memoria en móviles reales con 16 jugadores y efectos máximos antes de fijar un objetivo de FPS o reducir más el detalle. Las capturas OpenGL y el emulador comprueban funcionamiento; no sustituyen ese perfil. |

No se ha cambiado el formato de red: protocolo 8. Las correcciones de joystick y Traslación se ejecutan en el servidor y en práctica; para usarlas en red hay que actualizar también el servidor. La revisión no demuestra ausencia de fallos ni establece cifras de FPS para hardware sin medir.

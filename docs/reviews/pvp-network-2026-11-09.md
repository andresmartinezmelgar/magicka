# Auditoría del tráfico PvP — 09/11/2026

Revisión sobre `main` en `abe2bfdcb9f4ee4a2367cfef8ce5bdbf3c426581`, versión 0.29.53 y protocolo 46. Cambios de código preparados en una rama de pruebas; no compilados, publicados ni desplegados. No se dispone de una captura de la partida del usuario.

La subida de controles no explica por sí sola varios MB/min. El mayor flujo continuo es la recepción de estados: a 20 estados/s, **1 KiB de contenido medio por estado equivale a 1,23 MB/min**, antes de bases completas, estadísticas y cabeceras. No puede atribuirse un porcentaje del consumo real a cada componente sin una medición de esa sesión. El contador del HUD mide contenido WebSocket, después de comprimir en origen; no incluye TCP/TLS/IP, retransmisiones ni HTTP/Firebase.

## Inventario de comunicaciones

| Flujo | Datos y frecuencia | Evaluación |
|---|---|---|
| Cliente → servidor: saludo/reconexión | Identidad, protocolo/versión, sala, equipo, tipo de mago, credenciales y token de recuperación al conectar | Necesario; los secretos no se añaden a los estados ni a los nuevos diagnósticos. |
| Cliente → servidor: `input` | Secuencia, ejes, apuntado y destino cuando está activo; 30 Hz, 23 B sin destino o 31 B con destino | 41.400–55.800 B/min de contenido a cadencia sostenida. Ya es binario. Se conserva la cadencia y la precisión: la simulación cancela movimiento/canalización después de 0,6 s sin entrada. |
| Cliente → servidor: combate | Elemento, magick, lanzar/soltar, área, autolanzamiento, limpiar y bastón, cuando el jugador actúa | Binario de 10–11 B. No contiene posiciones impuestas, daño ni vida. |
| Ambos sentidos: inspección | Solicitud binaria de 22 B; respuesta con equipamiento público al abrir la ficha | Bajo demanda; respuesta fiable separada de la cola descartable. |
| Ambos sentidos: latencia | `ping` como máximo una vez/s, una petición pendiente; `pong` con su identificador | El `pong` reenviaba campos vacíos de `ServerMessage`. Ahora solo lleva tipo e identificador. |
| Cliente → servidor: base/estado de sala | ACK al recibir un keyframe; listo, equipo de lobby, inicio y reinicio cuando corresponde | ACK ya compacto. Se compactan controles de lobby, reinicio y consulta de estadísticas; el saludo conserva su contrato. |
| Cliente → servidor: rendimiento | FPS y p95 aproximadamente cada 2 s de juego activo | JSON específico ya pequeño; se conserva para diagnóstico. |
| Servidor → cliente: deltas | Tick/tiempo, jugadores, posiciones, salud, escudos, estados, elementos, reutilizaciones, equipo, daño, proyectiles, barreras, áreas, haces, puntos, objetos del escenario y eventos; 20 Hz y desenlace inmediato | Solo diferencias respecto a una base confirmada. Brotli calidad 3 ya estaba implantado. Se añade compresión de mensajes entre 256 y 1023 B que antes salían en texto. |
| Servidor → cliente: bases completas | Una base nueva cada 40 estados; reintento cada 20 hasta ACK | Se conservan la recuperación, el filtro de información privada y las dos bases del receptor. |
| Servidor → cliente: estadísticas | Informe aproximadamente cada 15 s de simulación y al finalizar; recuperación del cliente tras 30 s sin informe o al terminar | La revisión anterior ya comprimía el informe y evitaba la consulta periódica duplicada. No se atribuye de nuevo ese ahorro a este cambio. |
| Servidor → cliente: avisos/errores | Cuenta/recompensas, errores, cierre, estado de carga y preparación | Eventuales; no son un flujo de posiciones. |
| HTTP: salas, conexión y cuenta | Listado/creación de salas antes de entrar, descubrimiento de servidor; refresco de perfil tras recompensas | No hay sondeo de salas continuo durante la partida. Renovación de autenticación cuando procede; comprobar su vencimiento localmente no implica una petición. |
| HTTP/Firebase: telemetría e informes | Telemetría de dashboard para cuentas autorizadas cada 15 s; informes finalizados y reintentos | Es tráfico adicional al WebSocket y al contador del HUD; no se ha medido en esta revisión. La venta automática de inventario está deshabilitada jugando. |
| Descargas de actualizaciones | Consulta/descarga por su flujo propio con aceptación del jugador | No forma parte de la replicación de combate; no se instala durante partida o servidor local activo. |

En Supervivencia se añaden enemigos, progreso, avisos y equipo temporal autorizado. Sus enemigos ya usan máscaras binarias exactas y base64 dentro del delta JSON; el mapa procedural usa semilla y versión. Los vectores `V2` ya omiten propiedades calculadas con `JsonIgnore`.

Rutas revisadas: `Protocol.cs`, `SnapshotDelta.cs`, `SnapshotStream.cs`, `MinionDeltaCodec.cs`, `GameWorld.Snapshot.cs`, `ArenaConnection*.cs`, `GameService*.cs`, `Main.cs`, `Main.Statistics.cs`, `Main.Defense.cs`, `Main.Dashboard.cs`, `Main.Accounts.cs`, `Main.InventoryAutoSell.cs` y los clientes de cuenta/autenticación. Las reglas de Firestore no cambian.

## Cambios realizados

1. **Brotli desde 256 B**, manteniendo calidad 3, ventana 22 y la condición exacta `compressedBytes + 17 < rawBytes`. Se elige texto cuando comprimir no ahorra más de 16 B incluyendo el prefijo. Los mensajes menores de 256 B siguen pasando directamente. No se redondean números ni se retiran campos de combate.
2. **Una sola descompresión por delta en el cliente.** Antes `DeserializeFrame` descomprimía para averiguar el tipo y después `Read` volvía a descomprimir para reconstruir el estado. Ahora se descomprime una vez, se lee el tipo con `Utf8JsonReader` y se comparte el árbol del delta entre selección de base y reconstrucción. Los mensajes que no son deltas no construyen un `JsonNode`. Se conserva el límite de 256 KiB y la validación de profundidad, snapshot y preparación.
3. **Controles compactos.** `pong` contiene `type`/`pingId`; consultas de estadísticas, listo/inicio de lobby y reinicio llevan `type`/`sequence`; cambio de equipo añade `team`. Sus receptores ya admitían la omisión de los demás campos. Los tipos desconocidos y el saludo siguen usando el formato completo.
4. **Diagnóstico local `network.traffic` cada 30 s.** Bytes y mensajes por categoría, totales y MB/min decimales; sin contenido de las tramas ni nuevas peticiones. `ArenaConnection.Traffic` expone los contadores. La lectura concurrente es aproximada en los límites de cada intervalo; al cesar el tráfico los acumulados son exactos para los mensajes clasificados. El contador histórico `BytesReceived` también cuenta fragmentos que no llegaron a formar un mensaje clasificable y puede diferir del desglose si la conexión falla.

No cambia el protocolo binario, el prefijo Brotli, los ACK, las frecuencias de simulación/entrada/estados ni la versión de juego. El formato es compatible con el protocolo 46; la política existente exige igualmente que coincidan las versiones de cliente y servidor. La compresión se aplica en el servidor y las mejoras de recepción/diagnóstico en el cliente, por lo que ambas partes deben incorporar sus respectivos cambios para aprovecharlos todos.

## Estudio reproducible sin compilar

```bash
python3 scripts/study-pvp-bandwidth.py --output docs/benchmarks/pvp-network-2026-11-09.json
```

El script construye JSON sintético con el esquema del protocolo, comprueba la reconstrucción de sus deltas y compara ambos umbrales usando Brotli nativo. Son 12 escenarios, con 240 deltas y seis bases por escenario: 12 s extrapolados a un minuto, ACK inmediato, sin pérdidas. Hay **5.932 comprobaciones de ida/vuelta o paso directo**, incluyendo límites, ruido y una exploración de arrays. [Resultados completos](../benchmarks/pvp-network-2026-11-09.json).

| Escenario sintético | Antes, MB/min | Después, MB/min | Ahorro |
|---|---:|---:|---:|
| Movimiento, 2 jugadores | 0,355 | 0,237 | 33,05 % |
| Movimiento, 4 jugadores | 0,560 | 0,306 | 45,38 % |
| Movimiento, 8 jugadores | 0,966 | 0,428 | 55,65 % |
| Movimiento, 16 jugadores | 0,664 | 0,664 | 0 % |
| Combate, 2 jugadores | 0,655 | 0,641 | 2,12 % |
| Combate, 4/8/16 jugadores | 0,776 / 1,009 / 1,472 | 0,776 / 1,009 / 1,472 | 0 % |

En reposo no cambia el tamaño. La diferencia entre 8 y 16 jugadores antes del cambio se debe precisamente al umbral: los paquetes de 16 ya se comprimían, mientras que los de 8 no. Los estados grandes de combate ya superaban 1024 B, por lo que este cambio no reduce su volumen salvo en los cinco deltas pequeños del combate de dos jugadores.

En esta ejecución Linux x86_64, el coste incremental de compresión de los escenarios de movimiento afectados fue aproximadamente **0,012–0,018 ms por estado y destinatario**. No representa el coste en Raspberry Pi ni en Android. El script incluye tiempo Python/ctypes, no serialización C#; la notación de números también puede diferir de System.Text.Json. No mide FPS, latencia, ancho de banda facturado, pérdidas ni el ahorro real en la partida del usuario. Las diferencias de tiempos cercanas a cero en escenarios sin cambio son ruido.

## Verificación y siguiente medición

Ejecutado sin compilar: estudio sintético completo, análisis sintáctico C# de los 11 archivos modificados/añadidos con Tree-sitter, comprobación de metadatos de versión y `git diff --check`.

Preparadas, **sin ejecutar**, regresiones C# de compresión pequeña y ruido, controles/credenciales, identificador de ping de 64 bits, clasificación de JSON, profundidad y tamaño máximo, deltas descartados y recuperación de bases, y contadores concurrentes. El perfil existente `--profile-protocol` compara ahora la ruta de recepción anterior con la nueva sobre el mismo delta comprimido de ocho jugadores, informando media, p95 y asignaciones. La compilación y las pruebas C# están pendientes porque [AGENTS.md](../../AGENTS.md) indica: «Si una comprobación necesita compilar, dejarla pendiente hasta la indicación del usuario y comunicarlo con claridad».

Tras autorizar la compilación:

```bash
dotnet build ArcanaArena.sln -c Release --nologo
dotnet run --project tests/Arcana.Tests/Arcana.Tests.csproj -c Release --no-build
dotnet run --project tests/Arcana.Tests/Arcana.Tests.csproj -c Release --no-build -- --profile-protocol --profile-output=artifacts/profiles/pvp-receive.json
```

Antes de publicar, comparar una partida de duración, mapa, modo y participantes equivalentes en Android y servidor real. En «Ver registros», buscar `network.traffic`: `deltasReceivedBytes`, `keyframesReceivedBytes`, `statisticsReceivedBytes` y `receivedMBPerMinute` permiten identificar el componente dominante. Revisar FPS/p95, ping, retrasos de simulación y desconexiones además de bytes. Un registro cada 30 s evita telemetría por fotograma. El informe no afirma que el rendimiento en dispositivos haya sido validado.

## Oportunidades que requieren esa evidencia

- Los eventos se conservan 90 ticks (1,5 s), hasta 160, y las entidades nacidas después de la base repiten sus datos hasta incorporarse a una base confirmada. Reducir esa retención o encadenar deltas puede perder efectos/entidades al descartar tramas; requiere otro diseño y pruebas de recuperación.
- Los arrays escalares con varios campos cambiados pueden ocupar menos como array completo que como índices `$array`. La exploración aislada se conserva en el JSON de resultados; no se ha adoptado porque el ahorro después de comprimir la trama y el coste C# deben medirse juntos.
- El daño detallado y el equipo/inventario en bases grandes merecen medir su contribución antes de cambiar cadencias o separar mensajes. No se ha limitado información visible ni la exactitud del informe final.
- Un códec binario adicional para jugadores/objetos o eliminar base64 puede ahorrar, pero el tamaño bruto de JSON/base64 no predice el ahorro después de Brotli. Requiere comparación del estado exacto, CPU y asignaciones en ARM64 antes de sustituir el formato.

Esta revisión aporta mejoras concretas para paquetes pequeños y elimina trabajo duplicado. Si la partida genera muchos MB/min principalmente en estados grandes, harán falta las muestras por categoría y escenarios de combate equivalentes para decidir la siguiente optimización.
